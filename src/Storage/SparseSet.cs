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
        // Live dense indices are non-negative. Every non-live state is encoded as a
        // negative sentinel: free entries encode their next freelist index, reserved
        // entries use ReservedDenseIndex, and retired entries use RetiredDenseIndex.
        public int DenseIdx;
        public int Version;
    }

    private readonly List<SparseEntry> _sparse = [];
    private readonly List<int> _dense = [];
    private int _freeHead = FreeListEnd;

    // C# uses int for counts in List<T>, so we are forced to use int as well.
    private const int FreeListEnd = -1;
    private const int ReservedDenseIndex = -2;
    private const int RetiredDenseIndex = -3;
    private const int EncodedFreeListEnd = -4;
    private const int FreeListEncodingOffset = 5;
    private const int MaxSparseSize = int.MaxValue - FreeListEncodingOffset;

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

    internal static int SparseEntrySize => Unsafe.SizeOf<SparseEntry>();

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
        if (!Reserve(handle))
            return false;

        return ReleaseReserved(handle);
    }

    /// <summary>
    /// Remove a live handle from dense storage without changing its generation or making its
    /// sparse slot available for reuse.
    /// </summary>
    internal bool Reserve(Handle<TTag> handle)
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

        var entry = _sparse[handle.Index];
        entry.DenseIdx = ReservedDenseIndex;
        _sparse[handle.Index] = entry;
        return true;
    }

    /// <summary>
    /// Restore a reserved handle to live dense storage with its original generation.
    /// </summary>
    internal bool RestoreReserved(Handle<TTag> handle)
    {
        if (!IsReserved(handle))
            return false;

        var entry = _sparse[handle.Index];
        entry.DenseIdx = _dense.Count;
        _sparse[handle.Index] = entry;
        _dense.Add(handle.Index);
        return true;
    }

    /// <summary>
    /// Permanently release a reserved handle, invalidating its generation and making its sparse
    /// slot reusable unless generation exhaustion retires it.
    /// </summary>
    internal bool ReleaseReserved(Handle<TTag> handle)
    {
        if (!IsReserved(handle))
            return false;

        FreeSparseEntry(handle.Index);
        return true;
    }

    internal bool IsReserved(Handle<TTag> handle)
    {
        if (!MatchesGeneration(handle))
            return false;
        return _sparse[handle.Index].DenseIdx == ReservedDenseIndex;
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
        SparseEntry entry = _sparse[handle.Index];
        return entry.DenseIdx >= 0 && entry.Version == handle.Generation;
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

        if (entry.Version == DisabledVersion)
        {
            entry.DenseIdx = RetiredDenseIndex;
        }
        else
        {
            // Keep free entries distinguishable from live dense indices by encoding the next
            // freelist index as a negative value.
            entry.DenseIdx = EncodeFreeListNext(_freeHead);
            _freeHead = sparseIdx;
        }

        _sparse[sparseIdx] = entry;
    }

    private int FreelistPop()
    {
        int oldHead = _freeHead;
        if (oldHead == FreeListEnd)
            return FreeListEnd;
        SparseEntry entry = _sparse[oldHead];
        _freeHead = DecodeFreeListNext(entry.DenseIdx);
        return oldHead;
    }

    private static int EncodeFreeListNext(int next) =>
        next == FreeListEnd ? EncodedFreeListEnd : -(next + FreeListEncodingOffset);

    private static int DecodeFreeListNext(int encoded) =>
        encoded == EncodedFreeListEnd ? FreeListEnd : -encoded - FreeListEncodingOffset;

    private bool MatchesGeneration(Handle<TTag> handle)
    {
        if (handle.IsNull || (uint)handle.Index >= (uint)_sparse.Count)
            return false;
        return _sparse[handle.Index].Version == handle.Generation;
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
