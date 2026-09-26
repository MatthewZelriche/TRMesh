using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace TRMesh.Containers;

// A dense slot map backed entirely by native allocations. Handles are stable sparse-array indices,
// while values are packed densely and filled from the end on removal. A removed handle may refer to
// a different value after its sparse slot is reused.
//
// References into the map may be invalidated by either insertion (reallocation) or erasure
// (swap-removal).
//
// Because this struct owns its native allocations inline, it is an error to ever copy this struct.
public unsafe struct UnsafeSlotMap<T> : IDisposable
    where T : unmanaged
{
    UnsafeList<T> _denseValues;
    UnsafeList<int> _denseToSparse;

    // Active entries contain a nonnegative dense index. Vacant entries contain an encoded negative
    // link to the next sparse index in the free list.
    UnsafeList<int> _sparse;
    int _freeListHead;

    public UnsafeSlotMap()
        : this(0) { }

    public UnsafeSlotMap(int initialCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
        _denseValues = new UnsafeList<T>(initialCapacity);
        _denseToSparse = new UnsafeList<int>(initialCapacity);
        _sparse = new UnsafeList<int>(initialCapacity);
        _freeListHead = -1;
    }

    public readonly int Count => _denseValues.Count;
    public readonly int Capacity => _denseValues.Capacity;

    // Gets or replaces the value associated with handle. Invalid handles indicate a caller bug; use
    // TryGetValue or Contains when a handle may legitimately be inactive.
    public T this[int handle]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => _denseValues[ResolveDenseIndex(handle)];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _denseValues[ResolveDenseIndex(handle)] = value;
    }

    // Opt-in reference access for performance-sensitive code. The returned reference must not be
    // retained across Insert, Remove, Clear, or Dispose, since those operations may relocate or
    // release dense storage.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ref T UnsafeRef(int handle) => ref _denseValues[ResolveDenseIndex(handle)];

    // Inserts value and returns its stable sparse-array handle. Removed handles are reused.
    public int Insert(T value)
    {
        EnsureInitialized();

        int denseIndex = _denseValues.Count;
        int sparseIndex;

        if (_freeListHead >= 0)
        {
            sparseIndex = _freeListHead;
            ref int sparseEntry = ref _sparse[sparseIndex];
            Debug.Assert(sparseEntry < 0);
            _freeListHead = DecodeFreeListLink(sparseEntry);
            sparseEntry = denseIndex;
        }
        else
        {
            sparseIndex = _sparse.Count;
            _sparse.Add(denseIndex);
        }

        _denseValues.Add(value);
        _denseToSparse.Add(sparseIndex);
        Debug.Assert(_denseValues.Count == _denseToSparse.Count);
        return sparseIndex;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Contains(int handle) =>
        (uint)handle < (uint)_sparse.Count && _sparse[handle] >= 0;

    public readonly bool TryGetValue(int handle, out T value)
    {
        if (!Contains(handle))
        {
            value = default;
            return false;
        }

        value = _denseValues[_sparse[handle]];
        return true;
    }

    // Removes handle if it is active. The last dense value is moved into the resulting hole.
    public bool Remove(int handle)
    {
        if (!Contains(handle))
            return false;

        int removedDenseIndex = _sparse[handle];
        int lastDenseIndex = _denseValues.Count - 1;

        if (removedDenseIndex != lastDenseIndex)
        {
            _denseValues[removedDenseIndex] = _denseValues[lastDenseIndex];

            int movedSparseIndex = _denseToSparse[lastDenseIndex];
            _denseToSparse[removedDenseIndex] = movedSparseIndex;
            _sparse[movedSparseIndex] = removedDenseIndex;
        }

        _denseValues.Count = lastDenseIndex;
        _denseToSparse.Count = lastDenseIndex;

        _sparse[handle] = EncodeFreeListLink(_freeListHead);
        _freeListHead = handle;
        Debug.Assert(_denseValues.Count == _denseToSparse.Count);
        return true;
    }

    // Removes every value while retaining all native allocations and reusable sparse handles.
    public void Clear()
    {
        _freeListHead = -1;
        for (int sparseIndex = _sparse.Count - 1; sparseIndex >= 0; sparseIndex--)
        {
            _sparse[sparseIndex] = EncodeFreeListLink(_freeListHead);
            _freeListHead = sparseIndex;
        }

        _denseValues.Count = 0;
        _denseToSparse.Count = 0;
    }

    public void Dispose()
    {
        _denseValues.Dispose();
        _denseToSparse.Dispose();
        _sparse.Dispose();
        _freeListHead = -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Enumerator GetEnumerator() =>
        new(_denseValues.Ptr, _denseToSparse.Ptr, _denseValues.Count);

    public ref struct Enumerator
    {
        readonly T* _denseValues;
        readonly int* _denseToSparse;
        readonly int _count;
        int _denseIndex;

        internal Enumerator(T* denseValues, int* denseToSparse, int count)
        {
            _denseValues = denseValues;
            _denseToSparse = denseToSparse;
            _count = count;
            _denseIndex = -1;
        }

        public ref T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _denseValues[_denseIndex];
        }

        public readonly int CurrentSlot
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _denseToSparse[_denseIndex];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext() => ++_denseIndex < _count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    readonly int ResolveDenseIndex(int handle)
    {
        if (!Contains(handle))
            throw new KeyNotFoundException("The handle is not active in this slot map.");

        return _sparse[handle];
    }

    // The all-zero default struct has a free-list head of zero rather than the -1 sentinel.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void EnsureInitialized()
    {
        if (_sparse.Count == 0 && _freeListHead == 0)
            _freeListHead = -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int EncodeFreeListLink(int next) => -2 - next;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int DecodeFreeListLink(int encoded) => -2 - encoded;
}
