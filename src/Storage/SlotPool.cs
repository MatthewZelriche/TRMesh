using System.Runtime.CompilerServices;

namespace TREditorSharp.Storage;

/// <summary>
/// Generic slot allocator providing O(1) Allocate/Free with stable, generationally-versioned
/// keys. Internally delegates to a <see cref="SparseSet{TTag}"/> for handle lifetime
/// management while coordinating registered <see cref="IComponentColumn"/> instances
/// to keep their dense storage in lockstep with the live entities.
///
/// The <typeparamref name="TTag"/> phantom parameter makes <see cref="SlotPool{TTag}"/>
/// instantiations distinct types so that pools for different entity kinds cannot be mixed
/// up at the type system level. Returned handles are <see cref="Handle{TTag}"/>.
/// </summary>
internal sealed class SlotPool<TTag>
    where TTag : unmanaged
{
    private readonly SparseSet<TTag> _set = new();
    private readonly List<IComponentColumn> _columns = [];

    /// <summary>Number of currently live slots. Equal to every registered column's <c>Count</c>.</summary>
    public int LiveCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _set.Count;
    }

    /// <summary>
    /// Register a component column with this pool. The column is brought up to the
    /// pool's current live count via <see cref="IComponentColumn.EnsureCount"/> so that
    /// late registration is consistent. From this point on the pool drives the column's
    /// lifecycle through <see cref="IComponentColumn.Add"/> on Allocate and
    /// <see cref="IComponentColumn.SwapRemoveAt"/> on Free.
    /// </summary>
    public void RegisterColumn(IComponentColumn column)
    {
        column.EnsureCount(_set.Count);
        _columns.Add(column);
    }

    /// <summary>Allocate a fresh slot. O(1)</summary>
    public Handle<TTag> Allocate()
    {
        var handle = _set.Insert();
        var cols = _columns;
        for (int i = 0; i < cols.Count; i++)
            cols[i].Add();
        return handle;
    }

    /// <summary>
    /// Free a previously allocated slot. The handle's generation must match the
    /// current slot generation.
    ///
    /// All registered columns are swap-popped at the freed entity's dense index in
    /// registration order. The handle for the entity that previously occupied
    /// <c>LiveCount - 1</c> remains valid; only its dense index changes.
    /// </summary>
    public void Free(Handle<TTag> handle)
    {
        if (!_set.Contains(handle))
            ThrowInvalid(handle);

        int dense = _set.GetDenseIndex(handle);
        var cols = _columns;
        for (int i = 0; i < cols.Count; i++)
            cols[i].SwapRemoveAt(dense);
        _set.Erase(handle);
    }

    /// <summary>
    /// Returns true iff <paramref name="handle"/> currently refers to a live slot. O(1).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAlive(Handle<TTag> handle) => _set.Contains(handle);

    /// <summary>
    /// Return the dense index for <paramref name="handle"/>. The dense index is the
    /// position of the entity in every registered column's live region. The result is
    /// only stable until the next <see cref="Free"/> of any handle.
    /// </summary>
    /// <exception cref="KeyNotFoundException">If the handle is not live.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetDenseIndex(Handle<TTag> handle) => _set.GetDenseIndex(handle);

    /// <summary>
    /// Validate the handle. In a MESH_VALIDATE build (Debug by default) throws if the
    /// handle is null, out of range, stale, or refers to a freed slot. Compiled out in
    /// release builds for max throughput.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ValidateLive(Handle<TTag> handle)
    {
#if MESH_VALIDATE
        if (!_set.Contains(handle))
            ThrowInvalid(handle);
#endif
    }

    /// <summary>Reset the pool to empty without releasing any column buffers.</summary>
    public void Clear()
    {
        _set.Clear();
        var cols = _columns;
        for (int i = 0; i < cols.Count; i++)
            cols[i].Clear();
    }

    /// <summary>Iterate live handles in unspecified order. Never allocates.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LiveEnumerator GetEnumerator() => new(_set.GetEnumerator());

    /// <summary>foreach-friendly wrapper around <see cref="GetEnumerator"/>.</summary>
    public LiveEnumerable Live
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(_set);
    }

    private static void ThrowInvalid(Handle<TTag> handle)
    {
        if (handle.IsNull)
            throw new ArgumentException("Null handle is not valid.");
        throw new InvalidOperationException(
            $"Handle (Index={handle.Index}, Generation={handle.Generation}) does not refer to a live slot."
        );
    }

    /// <summary>
    /// Lightweight foreach-target wrapping a <see cref="SparseSet{TTag}.LiveEnumerator"/>.
    /// </summary>
    public readonly ref struct LiveEnumerable
    {
        private readonly SparseSet<TTag> _set;

        internal LiveEnumerable(SparseSet<TTag> set)
        {
            _set = set;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LiveEnumerator GetEnumerator() => new(_set.GetEnumerator());
    }

    /// <summary>
    /// Ref-struct enumerator over live slots in unspecified order. Stack-only; never allocates.
    /// </summary>
    public ref struct LiveEnumerator
    {
        private SparseSet<TTag>.LiveEnumerator _inner;

        internal LiveEnumerator(SparseSet<TTag>.LiveEnumerator inner)
        {
            _inner = inner;
        }

        public Handle<TTag> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _inner.Current;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext() => _inner.MoveNext();
    }
}
