using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace TRMesh.Containers;

// A dense slot map backed entirely by native allocations. Handles are stable sparse-array indices,
// while values are packed densely. A removed handle may refer to a different value after its
// sparse slot is reused.
//
// References into the map may be invalidated by either insertion (reallocation) or erasure
// (swap-removal).
//
// Because this struct owns its native allocations inline, it is an error to ever copy this struct.
public unsafe struct UnsafeSlotMap<T> : IDisposable
    where T : unmanaged
{
    // Stores the actual values themselves.
    UnsafeList<T> _denseValues;

    // Book-keeping metadata
    // D = Number of currently stored dense elements (the actual payload)
    // S = Number of allocated sparse handle slots.
    // Usually, but not always, S is equal to the historical maximum count of D.
    // This can be untrue in the case of InsertAt(), however. S >= D
    //
    // At any point, the sizes of the lists are:
    // _denseValues.Count = D
    // _denseToSparse.Count = D
    // _freeHandles.Count = S - D
    // _sparse = S
    // Logical metadata: (D + (S - D) + S) * sizeof(int) = 8 * S bytes
    // (not including unused Capacity of the lists)

    // Stores an inverse mapping - necessary to update _sparse when we perform a swap-and-pop for O(1) erasure
    UnsafeList<int> _denseToSparse;
    // Stores an unordered list of free handle slots. We can't get away with an embedded linked list here
    // because of InsertAt() being needed for Undo/Restore functionality, and there being no O(1) method to implement
    // InsertAt() with a singly-linked freelist.
    UnsafeList<int> _freeHandles;
    // Active entries contain a nonnegative dense index. Vacant entries contain the bitwise
    // complement of their index in _freeHandles.
    UnsafeList<int> _sparse;

    public UnsafeSlotMap()
        : this(0) { }

    public UnsafeSlotMap(int initialCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
        _denseValues = new UnsafeList<T>(initialCapacity);
        _denseToSparse = new UnsafeList<int>(initialCapacity);
        _sparse = new UnsafeList<int>(initialCapacity);
        _freeHandles = new UnsafeList<int>(0);
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
        int denseIndex = _denseValues.Count;
        int sparseIndex;

        if (_freeHandles.Count != 0)
        {
            int freeIndex = _freeHandles.Count - 1;
            sparseIndex = _freeHandles[freeIndex];
            _freeHandles.Count = freeIndex;
            Debug.Assert(_sparse[sparseIndex] == ~freeIndex);
            _sparse[sparseIndex] = denseIndex;
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

    // Activates a specific sparse handle and appends value to dense storage. Missing intermediate
    // handles are created as vacant slots. This is primarily intended for restoring externally
    // visible handle identities, such as when applying an undo/redo patch.
    public void InsertAt(int handle, T value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(handle);

        while (_sparse.Count <= handle)
        {
            int newHandle = _sparse.Count;
            int freeIndex = _freeHandles.Count;
            _sparse.Add(~freeIndex);
            _freeHandles.Add(newHandle);
        }

        int encodedFreeIndex = _sparse[handle];
        if (encodedFreeIndex >= 0)
            throw new InvalidOperationException($"Handle {handle} is already active.");

        int freeIndexToRemove = ~encodedFreeIndex;
        int lastFreeIndex = _freeHandles.Count - 1;
        Debug.Assert((uint)freeIndexToRemove <= (uint)lastFreeIndex);
        Debug.Assert(_freeHandles[freeIndexToRemove] == handle);

        int movedFreeHandle = _freeHandles[lastFreeIndex];
        _freeHandles[freeIndexToRemove] = movedFreeHandle;
        _sparse[movedFreeHandle] = ~freeIndexToRemove;
        _freeHandles.Count = lastFreeIndex;

        int denseIndex = _denseValues.Count;
        _sparse[handle] = denseIndex;
        _denseValues.Add(value);
        _denseToSparse.Add(handle);
        Debug.Assert(_denseValues.Count == _denseToSparse.Count);
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

        int freeIndex = _freeHandles.Count;
        _freeHandles.Add(handle);

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

        _sparse[handle] = ~freeIndex;
        Debug.Assert(_denseValues.Count == _denseToSparse.Count);
        return true;
    }

    // Removes every value while retaining all native allocations and reusable sparse handles.
    public void Clear()
    {
        _freeHandles.Count = 0;
        if (_freeHandles.Capacity < _sparse.Count)
            _freeHandles.SetCapacity(_sparse.Count);

        for (int sparseIndex = _sparse.Count - 1; sparseIndex >= 0; sparseIndex--)
        {
            int freeIndex = _freeHandles.Count;
            _freeHandles.Add(sparseIndex);
            _sparse[sparseIndex] = ~freeIndex;
        }

        _denseValues.Count = 0;
        _denseToSparse.Count = 0;
    }

    public void Dispose()
    {
        _denseValues.Dispose();
        _denseToSparse.Dispose();
        _sparse.Dispose();
        _freeHandles.Dispose();
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
}
