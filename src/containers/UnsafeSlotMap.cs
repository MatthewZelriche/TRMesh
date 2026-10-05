using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace TRMesh.Containers;

// A dense slot map backed entirely by native allocations. Handles are stable sparse-array indices,
// while values are packed densely. A removed handle may refer to a different value after its
// sparse slot is reused.
//
// Pointers into the map may be invalidated by either insertion (reallocation) or erasure
// (swap-removal).
//
// The map is a reference type so its native allocations cannot be accidentally copied and freed
// through multiple owners.
public sealed unsafe class UnsafeSlotMap<T> : IDisposable
    where T : unmanaged
{
    // Stores the actual values themselves.
    UnsafeList<T> _denseValues;

    // Book-keeping metadata
    // D = Number of currently stored dense elements (the actual payload)
    // S = Number of allocated sparse handle slots, equal to the historical maximum count of D. S >= D
    //
    // At any point, the sizes of the lists are:
    // _denseValues.Count = D
    // _denseToSparse.Count = D
    // _sparse = S
    // Logical metadata: (D + S) * sizeof(int) bytes
    // (not including unused Capacity of the lists)

    // Stores an inverse mapping - necessary to update _sparse when we perform a swap-and-pop for O(1) erasure
    UnsafeList<int> _denseToSparse;
    // Active entries contain a nonnegative dense index. Vacant entries form a free stack: each
    // stores FreeLink(next vacant handle), and _freeHead is the most recently freed handle.
    UnsafeList<int> _sparse;
    int _freeHead = -1;
    int _version;

    public UnsafeSlotMap()
        : this(0) { }

    public UnsafeSlotMap(int initialCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
        _denseValues = new UnsafeList<T>(initialCapacity);
        _denseToSparse = new UnsafeList<int>(initialCapacity);
        _sparse = new UnsafeList<int>(initialCapacity);
        _version = 0;
    }

    public int Count => _denseValues.Count;
    public int Capacity => _denseValues.Capacity;
    public int SparseCount => _sparse.Count;

    // Gets or replaces the value associated with handle. Invalid handles indicate a caller bug; use
    // TryGetValue or Contains when a handle may legitimately be inactive.
    public T this[int handle]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Get(handle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Set(handle, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Get(int handle) => _denseValues[ResolveDenseIndex(handle)];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Get(int handle, out int denseIndex)
    {
        denseIndex = ResolveDenseIndex(handle);
        return _denseValues[denseIndex];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(int handle, T value) => _denseValues[ResolveDenseIndex(handle)] = value;

    // Opt-in pointer access for performance-sensitive code. The returned pointer must not be
    // retained across Insert, Remove, UndoInsert, UndoRemove, Clear, or Dispose.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T* GetPointer(int handle) => _denseValues.Ptr + ResolveDenseIndex(handle);

    // Returns the dense row associated with handle. The returned index is intended for owners of
    // parallel dense storage and must not be retained across structural modifications.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetDenseIndex(int handle) => ResolveDenseIndex(handle);

    // Resolves handle once while exposing both its value and dense row to owners of parallel
    // storage. The returned pointer and dense index have the same lifetime restrictions as
    // GetPointer(handle).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T* GetPointer(int handle, out int denseIndex)
    {
        denseIndex = ResolveDenseIndex(handle);
        return _denseValues.Ptr + denseIndex;
    }

    // Dense pointer access supports generated bulk-processing APIs. Dense indices and both
    // pointers are invalidated by structural modification.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetDensePointers(out T* values, out int* slots, out int count)
    {
        values = _denseValues.Ptr;
        slots = _denseToSparse.Ptr;
        count = _denseValues.Count;
    }

    // Inserts value and returns its stable sparse-array handle. Removed handles are reused.
    public int Insert(T value)
    {
        _version++;
        int denseIndex = _denseValues.Count;
        int sparseIndex;

        if (_freeHead != -1)
        {
            sparseIndex = PopFree();
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

    // Exactly reverses the most recent structural change, which must have been an Insert. grew
    // reports whether that Insert extended the sparse array rather than reusing a free handle.
    public void UndoInsert(bool grew)
    {
        _version++;
        int lastDenseIndex = _denseValues.Count - 1;
        int handle = _denseToSparse[lastDenseIndex];
        _denseValues.Count = lastDenseIndex;
        _denseToSparse.Count = lastDenseIndex;

        if (grew)
        {
            Debug.Assert(handle == _sparse.Count - 1);
            _sparse.Count = handle;
        }
        else
        {
            PushFree(handle);
        }
    }

    // Exactly reverses the most recent structural change, which must have been a Remove that
    // vacated denseIndex. The removed handle is reactivated holding value.
    public void UndoRemove(T value, int denseIndex)
    {
        _version++;
        int handle = PopFree();

        int denseCount = _denseValues.Count;
        Debug.Assert((uint)denseIndex <= (uint)denseCount);
        if (denseIndex == denseCount)
        {
            _denseValues.Add(value);
            _denseToSparse.Add(handle);
        }
        else
        {
            int movedHandle = _denseToSparse[denseIndex];
            _denseValues.Add(_denseValues[denseIndex]);
            _denseToSparse.Add(movedHandle);
            _sparse[movedHandle] = denseCount;
            _denseValues[denseIndex] = value;
            _denseToSparse[denseIndex] = handle;
        }

        _sparse[handle] = denseIndex;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(int handle) =>
        (uint)handle < (uint)_sparse.Count && _sparse[handle] >= 0;

    public bool TryGetValue(int handle, out T value)
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

        _version++;
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

        PushFree(handle);
        Debug.Assert(_denseValues.Count == _denseToSparse.Count);
        return true;
    }

    // Removes every value while retaining all native allocations and reusable sparse handles.
    public void Clear()
    {
        _version++;
        _freeHead = -1;
        for (int sparseIndex = _sparse.Count - 1; sparseIndex >= 0; sparseIndex--)
            PushFree(sparseIndex);

        _denseValues.Count = 0;
        _denseToSparse.Count = 0;
    }

    public void Dispose()
    {
        _version++;
        _denseValues.Dispose();
        _denseToSparse.Dispose();
        _sparse.Dispose();
        _freeHead = -1;
    }

    // For testing purposes only. Returns the free head, then the sparse entries, which encode the
    // free stack.
    internal int[] CopyAllocatorState()
    {
        var state = new List<int> { _freeHead };
        for (int i = 0; i < _sparse.Count; i++)
            state.Add(_sparse[i]);
        return state.ToArray();
    }

    // Encodes a free-list link so every vacant entry is negative, including the end of the list
    // (-1). The mapping is its own inverse.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int FreeLink(int value) => -2 - value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void PushFree(int handle)
    {
        _sparse[handle] = FreeLink(_freeHead);
        _freeHead = handle;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    int PopFree()
    {
        int handle = _freeHead;
        Debug.Assert(handle != -1);
        _freeHead = FreeLink(_sparse[handle]);
        return handle;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator
    {
        readonly UnsafeSlotMap<T> _map;
        readonly int _version;
        readonly int _count;
        int _denseIndex;

        internal Enumerator(UnsafeSlotMap<T> map)
        {
            _map = map;
            _version = map._version;
            _count = map._denseValues.Count;
            _denseIndex = -1;
        }

        public T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ValidateCurrent();
                return _map._denseValues[_denseIndex];
            }
        }

        public int CurrentSlot
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ValidateCurrent();
                return _map._denseToSparse[_denseIndex];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            ValidateVersion();
            return ++_denseIndex < _count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ValidateCurrent()
        {
            ValidateVersion();
            if ((uint)_denseIndex >= (uint)_count)
                throw new InvalidOperationException("The enumerator is not positioned on an element.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ValidateVersion()
        {
            if (_version != _map._version)
                throw new InvalidOperationException("The slot map was structurally modified during enumeration.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    int ResolveDenseIndex(int handle)
    {
        if (!Contains(handle))
            throw new KeyNotFoundException("The handle is not active in this slot map.");

        return _sparse[handle];
    }
}
