using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TRMesh.Containers;

// Unmanaged array with geometric chunk sizes (32, 64, 128, 256, …), optionally
// capped so later chunks stay at MaxChunkSize (…, 65536, 65536, …).
// MaxChunkSize must be a power of two (>= 32) so capped lookup stays shift/mask-based.
// Every allocated slot is zero-initialized. Capacity is the allocated extent.
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
    {
        _chunks = default;
        _capacity = 0;

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

    // Total zero-initialized elements addressable with currently allocated chunks.
    public int Capacity => _capacity;

    // 0 when uncapped; otherwise the per-chunk element cap.
    public int MaxChunkSize => _maxChunkSize;

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
    public Enumerator GetEnumerator() => new(this);

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ref T ElementRef(int index)
    {
        Resolve(index, out var chunkIndex, out var localIndex);
        return ref ((T*)_chunks[chunkIndex])[localIndex];
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
        return (uint)chunkIndex < firstCappedChunk ? BaseChunkSize << chunkIndex : _maxChunkSize;
    }

    void AddChunk()
    {
        var size = ChunkElements(_chunks.Count);
        Debug.Assert(size > 0);
        var ptr = (nint)NativeMemory.AllocZeroed((nuint)size * (nuint)sizeof(T));
        _chunks.Add(ptr);
        _capacity += size;
    }
}
