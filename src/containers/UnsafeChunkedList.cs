using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TRMesh.Containers;

// Unmanaged array with geometric chunk sizes (32, 64, 128, 256, …), optionally
// capped so later chunks stay at MaxChunkSize (…, 65536, 65536, …).
// MaxChunkSize must be a power of two (>= 32) so capped lookup stays shift/mask-based.
// Every allocated slot is zero-initialized. Capacity is the stable logical extent;
// internal owning containers may temporarily release individual chunk storage.
// Owns native chunk buffers via fields stored directly on the struct — do not copy;
// dispose only once.
public unsafe struct UnsafeChunkedList<T> : IDisposable
    where T : unmanaged
{
    const int BaseChunkSize = 32;
    const int BaseChunkBits = 5; // log2(32)

    // Each element is a T* stored as nint (pointer types cannot be generic args).
    UnsafeList<nint> _chunks;
    int _capacity;
    int _elementStride;

    // 0 = uncapped; otherwise power-of-two per-chunk cap.
    int _maxChunkSize;
    int _maxChunkBits;

    // Index into _chunks of the first chunk whose size is _maxChunkSize, plus one.
    // Zero denotes an uncapped list in order to support default-init structs.
    uint _firstCappedChunkPlusOne;

    // One plus the transformed-index value at which capped chunks begin. Zero
    // denotes an uncapped/default list. Subtracting one as uint maps that state
    // to uint.MaxValue, allowing Resolve to use one boundary comparison.
    uint _cappedStartPlusOne;

    // maxChunkSize: 0 = uncapped geometric growth. Otherwise a power of two
    // >= 32; chunks grow geometrically until that size, then stay there.
    public UnsafeChunkedList(int initialCapacity = 0, int maxChunkSize = 0)
        : this(initialCapacity, maxChunkSize, 0) { }

    // A nonzero minimumElementStride reserves and aligns storage for containers
    // which overlay metadata while a slot is unused.
    internal UnsafeChunkedList(int initialCapacity, int maxChunkSize, int minimumElementStride)
    {
        Debug.Assert(minimumElementStride >= 0);
        _chunks = default;
        _capacity = 0;
        _elementStride =
            minimumElementStride == 0
                ? sizeof(T)
                : (Math.Max(sizeof(T), minimumElementStride) + minimumElementStride - 1)
                    / minimumElementStride
                    * minimumElementStride;

        if (maxChunkSize <= 0)
        {
            _maxChunkSize = 0;
            _maxChunkBits = 0;
            _firstCappedChunkPlusOne = 0;
            _cappedStartPlusOne = 0;
        }
        else
        {
            Debug.Assert(maxChunkSize >= BaseChunkSize);
            Debug.Assert(BitOperations.IsPow2(maxChunkSize));
            _maxChunkSize = maxChunkSize;
            _maxChunkBits = BitOperations.Log2((uint)maxChunkSize);
            _firstCappedChunkPlusOne = (uint)(_maxChunkBits - BaseChunkBits + 1);
            _cappedStartPlusOne = (uint)maxChunkSize + 1;
        }

        if (initialCapacity > 0)
            SetCapacity(initialCapacity);
    }

    // Total elements represented by the stable chunk topology.
    public readonly int Capacity => _capacity;

    // 0 when uncapped; otherwise the per-chunk element cap.
    public readonly int MaxChunkSize => _maxChunkSize;

    // Auto-grows when index >= Capacity. New chunks are zero-filled on allocate.
    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Debug.Assert(index >= 0);
            if ((uint)index >= (uint)_capacity)
                SetCapacity(index + 1);
            return ref ElementRef(index);
        }
    }

    // Grow-only: ensures Capacity >= newCapacity. Never frees chunks.
    public void SetCapacity(int newCapacity)
    {
        Debug.Assert(newCapacity > 0);
        if (newCapacity <= _capacity)
            return;

        while (_capacity < newCapacity)
            AddChunk();
    }

    public void Dispose()
    {
        for (var i = 0; i < _chunks.Count; i++)
        {
            NativeMemory.Free((void*)_chunks[i]);
            _chunks[i] = 0;
        }

        _chunks.Dispose();
        _capacity = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Enumerator GetEnumerator() => new(this);

    public ref struct Enumerator
    {
        UnsafeChunkedList<T> _list;
        int _index;

        internal Enumerator(UnsafeChunkedList<T> list)
        {
            _list = list;
            _index = -1;
        }

        public ref T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _list.ElementRef(_index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext() => ++_index < _list._capacity;
    }

    // Non-growing access to the element at a given index.
    // Must be a valid index! Otherwise use the this[] indexer.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly ref T ElementRef(int index)
    {
        Debug.Assert((uint)index < (uint)_capacity);
        Resolve(index, out var chunkIndex, out var localIndex);
        return ref ElementRef(chunkIndex, localIndex);
    }

    // Byte stride between consecutive elements in a chunk. May exceed sizeof(T)
    // when an owning container overlays larger unused-slot metadata.
    internal readonly int ElementStride => _elementStride;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly byte* ChunkBase(int chunkIndex)
    {
        Debug.Assert((uint)chunkIndex < (uint)_chunks.Count);
        return (byte*)_chunks.Ptr[chunkIndex];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly ref T ElementRef(int chunkIndex, int localIndex)
    {
        Debug.Assert((uint)chunkIndex < (uint)_chunks.Count);
        return ref ElementRef(ChunkBase(chunkIndex), localIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly ref T ElementRef(byte* chunkBase, int localIndex)
    {
        // Common case: no metadata padding; scale folds into the address mode.
        if (_elementStride == sizeof(T))
            return ref *((T*)chunkBase + localIndex);

        return ref *(T*)(chunkBase + (nuint)localIndex * (nuint)_elementStride);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    readonly void Resolve(int index, out int chunkIndex, out int localIndex)
    {
        var x = (uint)index + BaseChunkSize;
        var cappedStart = _cappedStartPlusOne - 1;
        if (x < cappedStart)
        {
            var msb = 31 - BitOperations.LeadingZeroCount(x);
            chunkIndex = msb - BaseChunkBits;
            localIndex = (int)(x ^ (1u << msb));
        }
        else
        {
            var firstCappedChunk = _firstCappedChunkPlusOne - 1;
            chunkIndex = (int)(firstCappedChunk + (x >> _maxChunkBits) - 1);
            localIndex = (int)(x & (uint)(_maxChunkSize - 1));
        }
    }

    readonly int ChunkElements(int chunkIndex)
    {
        var firstCappedChunk = _firstCappedChunkPlusOne - 1;
        // Likely compiles down to a branchless cmp + cmov
        return (uint)chunkIndex < firstCappedChunk ? BaseChunkSize << chunkIndex : _maxChunkSize;
    }

    void AddChunk()
    {
        var size = ChunkElements(_chunks.Count);
        Debug.Assert(size > 0);
        if (_elementStride == 0)
            _elementStride = sizeof(T);

        var ptr = (nint)NativeMemory.AllocZeroed((nuint)size * (nuint)_elementStride);
        _chunks.Add(ptr);
        _capacity += size;
    }

    // Read-only chunk topology for containers which coordinate metadata or
    // parallel storage by this list's stable global indices.
    internal readonly int ChunkCount => _chunks.Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly int getChunkBaseIndex(int chunkIndex)
    {
        Debug.Assert((uint)chunkIndex < (uint)_chunks.Count);
        var firstCappedChunk = _firstCappedChunkPlusOne - 1;
        if ((uint)chunkIndex < firstCappedChunk)
        {
            // Geometric chunk
            return (BaseChunkSize << chunkIndex) - BaseChunkSize;
        }
        else
        {
            // Capped chunk
            return (int)(
                ((uint)(chunkIndex - (int)firstCappedChunk + 1) << _maxChunkBits) - BaseChunkSize
            );
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly int GetChunkCapacity(int chunkIndex)
    {
        Debug.Assert((uint)chunkIndex < (uint)_chunks.Count);
        return ChunkElements(chunkIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly void ResolveIndex(int index, out int chunkIndex, out int localIndex)
    {
        Debug.Assert((uint)index < (uint)_capacity);
        Resolve(index, out chunkIndex, out localIndex);
    }
}
