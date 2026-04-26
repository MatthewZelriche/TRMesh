using System.Runtime.CompilerServices;

namespace TREditorSharp.Storage;

/// <summary>
/// Sparse set implementation. Each call to <see cref="Insert"/> returns a generationally-versioned
/// <see cref="Handle{TTag}"/> that is valid until the entry is erased. The Handle refers to
/// a position in the dense backing storage. The dense index may change, but the handle is
/// always updated to reflect the new dense index. Primarily used to associate a stable handle
/// with a position in one or more complementary dense arrays such as <see cref="NativeColumn{T}"/>.
///
///
/// The <typeparamref name="TTag"/> phantom marker allows strongly typed handles to avoid
/// mixing up handles for different SparseSets.
/// </summary>
public sealed class SparseSet<TTag>
    where TTag : unmanaged
{
    private struct SparseEntry
    {
        // While the slot is live, this is the slot's index in _dense.
        // While the slot is on the freelist, this is the next free sparse index
        // (or FreeListEnd for the tail). The two readings can never
        // collide because liveness is implied by version equality plus the
        // up-front IsNull check in Contains.
        public int DenseIdx;
        public int Version;
    }

    private readonly List<SparseEntry> _sparse = [];
    private readonly List<int> _dense = [];
    private int _freeHead = FreeListEnd;

    // C# uses int for counts in List<T>, so we are forced to use int as well.
    private const int FreeListEnd = -1;
    private const int MaxSparseSize = int.MaxValue - 1;

    // When a slot's bumped version reaches this value the slot is retired
    // permanently (never returned to the freelist).
    private const int DisabledVersion = int.MaxValue;

    /// <summary>Number of currently live entries.</summary>
    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _dense.Count;
    }

    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _dense.Count == 0;
    }

    /// <summary>
    /// Insert a new entry and return a fresh handle. O(1).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the sparse array would exceed <see cref="MaxSparseSize"/>.
    /// </exception>
    public Handle<TTag> Insert()
    {
        int denseIdx = _dense.Count;
        int sparseIdx = AllocateSparseEntry(denseIdx);
        _dense.Add(sparseIdx);

        // Bump on insert so the first issued handle has Generation = 1, keeping
        // default(Handle<TTag>) (Generation == 0) a true null sentinel.
        var entry = _sparse[sparseIdx];
        entry.Version++;
        _sparse[sparseIdx] = entry;

        return new Handle<TTag>(sparseIdx, entry.Version);
    }

    /// <summary>
    /// Return the dense index currently associated with <paramref name="handle"/>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">If the handle is not in the set.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetDenseIndex(Handle<TTag> handle)
    {
        if (!Contains(handle))
            throw new KeyNotFoundException("SparseSet does not contain the given handle.");
        return _sparse[handle.Index].DenseIdx;
    }

    /// <summary>
    /// Try to return the dense index for <paramref name="handle"/> without
    /// throwing. Returns false (and a sentinel <c>-1</c> in
    /// <paramref name="denseIndex"/>) if the handle is not contained.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetDenseIndex(Handle<TTag> handle, out int denseIndex)
    {
        if (!Contains(handle))
        {
            denseIndex = -1;
            return false;
        }
        denseIndex = _sparse[handle.Index].DenseIdx;
        return true;
    }

    /// <summary>
    /// Remove the entry referenced by <paramref name="handle"/> by swapping the
    /// last dense element into its slot and popping. The handle for the
    /// previously-last entry remains valid; only its dense index changes (callers
    /// keeping parallel data arrays must mirror the swap-and-pop). O(1).
    /// Returns true if the entry was removed; false if the handle was already
    /// stale, freed, null, or out of range.
    /// </summary>
    public bool Erase(Handle<TTag> handle)
    {
        if (!Contains(handle))
            return false;

        int denseIdx = _sparse[handle.Index].DenseIdx;
        int last = _dense.Count - 1;
        if (denseIdx != last)
        {
            int swappedSparseIdx = _dense[last];
            _dense[denseIdx] = swappedSparseIdx;
            var swapped = _sparse[swappedSparseIdx];
            swapped.DenseIdx = denseIdx;
            _sparse[swappedSparseIdx] = swapped;
        }
        _dense.RemoveAt(last);

        FreeSparseEntry(handle.Index);
        return true;
    }

    /// <summary>
    /// True iff <paramref name="handle"/> is in the set.
    /// O(1) and never throws.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Handle<TTag> handle)
    {
        if (handle.IsNull)
            return false;
        if ((uint)handle.Index >= (uint)_sparse.Count)
            return false;
        return _sparse[handle.Index].Version == handle.Generation;
    }

    /// <summary>
    /// Return the handle for the entry at the given <paramref name="denseIndex"/>.
    /// Indices are <c>0 &lt;= denseIndex &lt; Count</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="denseIndex"/> is out of range.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Handle<TTag> HandleAtDense(int denseIndex)
    {
        if ((uint)denseIndex >= (uint)_dense.Count)
            throw new ArgumentOutOfRangeException(
                nameof(denseIndex),
                denseIndex,
                $"Dense index out of range (Count = {_dense.Count})."
            );
        int sparseIdx = _dense[denseIndex];
        return new Handle<TTag>(sparseIdx, _sparse[sparseIdx].Version);
    }

    /// <summary>
    /// Reset the set to empty. All previously issued handles fail
    /// <see cref="Contains"/> after this call (their slots no longer exist).
    /// Releases the underlying buffers' contents but not their capacity.
    /// </summary>
    public void Clear()
    {
        _dense.Clear();
        _sparse.Clear();
        _freeHead = FreeListEnd;
    }

    /// <summary>Iterate live handles in unspecified order.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LiveEnumerator GetEnumerator() => new(this);

    /// <summary>foreach-friendly wrapper for handle iteration.</summary>
    public LiveEnumerable Live
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(this);
    }

    private int AllocateSparseEntry(int denseIdx)
    {
        int idx = FreelistPop();
        if (idx != FreeListEnd)
        {
            var entry = _sparse[idx];
            entry.DenseIdx = denseIdx;
            _sparse[idx] = entry;
            return idx;
        }

        idx = _sparse.Count;
        if (idx >= MaxSparseSize)
            throw new InvalidOperationException(
                $"SparseSet capacity exhausted (max sparse size = {MaxSparseSize})."
            );
        _sparse.Add(new SparseEntry { DenseIdx = denseIdx, Version = 0 });
        return idx;
    }

    private void FreeSparseEntry(int sparseIdx)
    {
        var entry = _sparse[sparseIdx];
        entry.Version++;

        if (entry.Version != DisabledVersion)
        {
            // Push the (now dead) slot onto the freelist by reusing DenseIdx as next-pointer.
            entry.DenseIdx = _freeHead;
            _freeHead = sparseIdx;
        }
        // else: slot retired permanently and is dropped from the freelist.

        _sparse[sparseIdx] = entry;
    }

    private int FreelistPop()
    {
        int oldHead = _freeHead;
        if (oldHead == FreeListEnd)
            return FreeListEnd;
        _freeHead = _sparse[oldHead].DenseIdx;
        return oldHead;
    }

    /// <summary>
    /// Lightweight foreach-target wrapper around the enumerator.
    /// </summary>
    public readonly ref struct LiveEnumerable
    {
        private readonly SparseSet<TTag> _set;

        internal LiveEnumerable(SparseSet<TTag> set)
        {
            _set = set;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LiveEnumerator GetEnumerator() => new(_set);
    }

    /// <summary>
    /// Ref-struct enumerator yielding live handles in unspecified order.
    /// </summary>
    public ref struct LiveEnumerator
    {
        private readonly SparseSet<TTag> _set;
        private int _denseIdx;
        private Handle<TTag> _current;

        internal LiveEnumerator(SparseSet<TTag> set)
        {
            _set = set;
            _denseIdx = -1;
            _current = default;
        }

        public Handle<TTag> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _current;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            int next = _denseIdx + 1;
            if ((uint)next >= (uint)_set._dense.Count)
                return false;
            _denseIdx = next;
            int sparseIdx = _set._dense[next];
            _current = new Handle<TTag>(sparseIdx, _set._sparse[sparseIdx].Version);
            return true;
        }
    }
}
