using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TRMesh.Containers;

// Unmanaged array with geometric chunk sizes (32, 32, 64, 128, …).
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

    public UnsafeChunkedList(int initialCapacity = 0)
    {
        _chunks = default;

        if (initialCapacity > 0)
            SetCapacity(initialCapacity);
    }

    // Total zero-initialized elements addressable with currently allocated chunks.
    public int Capacity => _chunks.Count == 0 ? 0 : BaseChunkSize << (_chunks.Count - 1);

    // Auto-grows when index >= Capacity. New chunks are zero-filled on allocate.
    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Debug.Assert(index >= 0);
            if ((uint)index >= (uint)Capacity)
                SetCapacity(index + 1);
            return ref ElementRef(index);
        }
    }

    // Grow-only: ensures Capacity >= newCapacity. Never frees chunks.
    public void SetCapacity(int newCapacity)
    {
        Debug.Assert(newCapacity > 0);
        if (newCapacity <= Capacity)
            return;

        while (Capacity < newCapacity)
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
        public bool MoveNext() => ++_index < _list.Capacity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ref T ElementRef(int index)
    {
        Resolve(index, out var chunkIndex, out var localIndex);
        return ref ((T*)_chunks[chunkIndex])[localIndex];
    }

    // Maps a global index to (chunk, local) via leading-zero count on the
    // 32-element page index. page = index >> 5 covers: 0→chunk0, 1→chunk1,
    // 2..3→chunk2, 4..7→chunk3, … matching the 32/32/64/128/… layout.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Resolve(int index, out int chunkIndex, out int localIndex)
    {
        var page = (uint)index >> BaseChunkBits;
        chunkIndex = 32 - BitOperations.LeadingZeroCount(page);
        // offset(0)=0; offset(c>=1)=32<<(c-1)=1<<(c+4). Mask clears the c==0 case.
        var offset = (1 << (chunkIndex + BaseChunkBits - 1)) & -chunkIndex;
        localIndex = index - offset;
    }

    static int ChunkElements(int chunkIndex) =>
        chunkIndex == 0 ? BaseChunkSize : BaseChunkSize << (chunkIndex - 1);

    void AddChunk()
    {
        var size = ChunkElements(_chunks.Count);
        var ptr = (nint)NativeMemory.AllocZeroed((nuint)size * (nuint)sizeof(T));
        _chunks.Add(ptr);
    }
}
