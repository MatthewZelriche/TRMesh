using System.Runtime.CompilerServices;

namespace TREditorSharp.Storage;

/// <summary>
/// Topology storage container combining a <see cref="SlotPool{TTag}"/> with a
/// registry of component columns. The first row is always the entity's own connectivity column,
/// which is registered automatically at construction.
///
/// Storage is dense and swap-packed: every registered column has the live entities'
/// data laid out contiguously in <c>[0, LiveCount)</c>, in the dense order tracked
/// by the underlying <see cref="SparseSet{TTag}"/>. Use array indexer to access connectivity
/// by value. You may also use <see cref="GetUnsafeRef{T, TColumnTag}"/> to access component data
/// without copying, but you must be careful with the lifetime of the reference.
///
/// Extra columns are <see cref="NativeColumn{T}"/> instances, keyed by an optional tag type
/// to distinguish multiple columns of the same element type. Implementations of
/// <see cref="IComponentColumn"/> can be added later for other storage backends.
///
/// Type parameters:
///   <typeparamref name="TTag"/>          phantom marker for entity-kind identity; also
///                                        the type argument of <see cref="Handle{TTag}"/>
///   <typeparamref name="TConnectivity"/> POD struct holding connectivity fields
/// </summary>
public class TopologyStorage<TTag, TConnectivity> : IDisposable
    where TTag : unmanaged
    where TConnectivity : unmanaged
{
    private readonly SlotPool<TTag> _pool;
    private readonly NativeColumn<TConnectivity> _connectivity;
    private readonly Dictionary<Type, IComponentColumn> _columnsByTag = [];
    private ComponentColumnSchema[] _columnSchema = [];
    private bool _disposed;

    public TopologyStorage()
    {
        _pool = new SlotPool<TTag>();
        _connectivity = new NativeColumn<TConnectivity>();
        // Connectivity column is implicitly tagged by its own element type.
        _columnsByTag[typeof(TConnectivity)] = _connectivity;
        AddColumnSchema(typeof(TConnectivity), _connectivity);
        _pool.RegisterColumn(_connectivity);
    }

    /// <summary>Number of currently live entities.</summary>
    public int LiveCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pool.LiveCount;
    }

    /// <summary>Allocate a fresh entity. O(1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Handle<TTag> Allocate() => _pool.Allocate();

    /// <summary>Free a previously allocated entity. O(1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Free(Handle<TTag> handle) => _pool.Free(handle);

    internal EntitySnapshot<TTag> CaptureAndReserve(Handle<TTag> handle) =>
        _pool.CaptureAndReserve(handle, _columnSchema);

    internal void RestoreReserved(EntitySnapshot<TTag> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSchema(snapshot.ColumnSchema);
        _pool.RestoreReserved(snapshot);
    }

    internal void ReleaseReserved(Handle<TTag> handle) => _pool.ReleaseReserved(handle);

    internal bool IsReserved(Handle<TTag> handle) => _pool.IsReserved(handle);

    /// <summary>True if the handle still refers to a live entity.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAlive(Handle<TTag> handle) => _pool.IsAlive(handle);

    /// <summary>
    /// Translate <paramref name="handle"/> to the dense index used by every
    /// registered column. The result is only valid until the next
    /// <see cref="Free"/> call on this storage.
    /// </summary>
    /// <exception cref="KeyNotFoundException">If the handle is not live.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetDenseIndex(Handle<TTag> handle) => _pool.GetDenseIndex(handle);

    /// <summary>
    /// Connectivity accessor by handle.
    /// </summary>
    public TConnectivity this[Handle<TTag> handle]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            _pool.ValidateLive(handle);
            return _connectivity[_pool.GetDenseIndex(handle)];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _pool.ValidateLive(handle);
            _connectivity[_pool.GetDenseIndex(handle)] = value;
        }
    }

    /// <summary>
    /// <para>
    /// Zero-copy <see cref="ref"/> into the native column that stores <typeparamref name="T"/>
    /// under tag <typeparamref name="TColumnTag"/> (see <see cref="GetNativeColumn{T, TColumnTag}"/>).
    /// Prefer the handle indexer or <see cref="GetComponent{T, TColumnTag}"/> when by-value access is acceptable.
    /// </para>
    /// <para>
    /// <b>Unsafe lifetime:</b> the returned reference aliases native storage. It is valid only while
    /// no operation on this storage adds, removes, or reorders live entities in a way that touches
    /// this column — specifically: do not call <see cref="Allocate"/>, <see cref="Free"/>,
    /// or <see cref="Clear"/> on this <see cref="TopologyStorage{TTag,TConnectivity}"/> (or on any
    /// facade that forwards to it) until you are finished using the reference. Do not
    /// <see cref="IDisposable.Dispose"/> the storage while the reference is in use.
    /// </para>
    /// <para>
    /// For column-local operations that also invalidate refs (buffer moves, swap-remove, etc.),
    /// see <see cref="NativeColumn{T}.UnsafeRef"/>.
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetUnsafeRef<T, TColumnTag>(Handle<TTag> handle)
        where T : unmanaged
    {
        _pool.ValidateLive(handle);
        return ref GetNativeColumn<T, TColumnTag>().UnsafeRef(_pool.GetDenseIndex(handle));
    }

    /// <summary>
    /// Read the component of element type <typeparamref name="T"/> for
    /// <paramref name="handle"/> from the column tagged by its own element type.
    /// Equivalent to <c>GetComponent&lt;T, T&gt;(handle)</c>. For writes, assign through
    /// <see cref="GetNativeColumn{T}"/>()[<see cref="GetDenseIndex"/>(handle)].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetComponent<T>(Handle<TTag> handle)
        where T : unmanaged => GetComponent<T, T>(handle);

    /// <summary>
    /// Write the component of element type <typeparamref name="T"/> for
    /// <paramref name="handle"/> into the column tagged by its own element type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetComponent<T>(Handle<TTag> handle, T value)
        where T : unmanaged => SetComponent<T, T>(handle, value);

    /// <summary>
    /// Read the component of element type <typeparamref name="T"/> from the column
    /// tagged <typeparamref name="TColumnTag"/>. For writes, assign through
    /// <see cref="GetNativeColumn{T, TColumnTag}"/>()[<see cref="GetDenseIndex"/>(handle)].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetComponent<T, TColumnTag>(Handle<TTag> handle)
        where T : unmanaged
    {
        _pool.ValidateLive(handle);
        return GetNativeColumn<T, TColumnTag>()[_pool.GetDenseIndex(handle)];
    }

    /// <summary>
    /// Write the component of element type <typeparamref name="T"/> for <paramref name="handle"/>
    /// into the column tagged <typeparamref name="TColumnTag"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetComponent<T, TColumnTag>(Handle<TTag> handle, T value)
        where T : unmanaged
    {
        _pool.ValidateLive(handle);
        GetNativeColumn<T, TColumnTag>()[_pool.GetDenseIndex(handle)] = value;
    }

    /// <summary>
    /// Register a native (unmanaged-backed) component column tagged by its own
    /// element type <typeparamref name="T"/>. Equivalent to
    /// <c>RegisterNativeColumn&lt;T, T&gt;()</c>.
    /// </summary>
    public NativeColumn<T> RegisterNativeColumn<T>()
        where T : unmanaged => RegisterNativeColumn<T, T>();

    /// <summary>
    /// Register a native component column storing <typeparamref name="T"/> under
    /// the phantom tag <typeparamref name="TColumnTag"/>. The tag uniquely
    /// identifies the column and lets multiple columns of the same element type. Throws if the tag
    /// is already in use.
    ///
    /// Late registration is supported: the new column is brought up to the current
    /// live count with default-initialized entries via <see cref="IComponentColumn.EnsureCount"/>
    /// before being added to the pool's column list.
    /// </summary>
    public NativeColumn<T> RegisterNativeColumn<T, TColumnTag>()
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_columnsByTag.ContainsKey(typeof(TColumnTag)))
            throw new InvalidOperationException(
                $"A column with tag {typeof(TColumnTag)} is already registered."
            );
        var col = new NativeColumn<T>();
        _columnsByTag[typeof(TColumnTag)] = col;
        AddColumnSchema(typeof(TColumnTag), col);
        _pool.RegisterColumn(col);
        return col;
    }

    /// <summary>
    /// Look up the native column tagged by element type <typeparamref name="T"/>.
    /// </summary>
    public NativeColumn<T> GetNativeColumn<T>()
        where T : unmanaged => GetNativeColumn<T, T>();

    /// <summary>
    /// Look up the native column tagged <typeparamref name="TColumnTag"/> and
    /// storing <typeparamref name="T"/>.
    /// </summary>
    public NativeColumn<T> GetNativeColumn<T, TColumnTag>()
        where T : unmanaged
    {
        if (!_columnsByTag.TryGetValue(typeof(TColumnTag), out var col))
            throw new KeyNotFoundException(
                $"No column with tag {typeof(TColumnTag)} is registered."
            );
        if (col is not NativeColumn<T> typed)
            throw new InvalidOperationException(
                $"Column tagged {typeof(TColumnTag)} is registered as {col.GetType().Name}, "
                    + $"not NativeColumn<{typeof(T).Name}>."
            );
        return typed;
    }

    /// <summary>
    /// True if a column registered with default tag <c>typeof(T)</c> exists.
    /// </summary>
    public bool HasColumn<T>() => _columnsByTag.ContainsKey(typeof(T));

    /// <summary>True if a column with the given <typeparamref name="TColumnTag"/> is registered.</summary>
    public bool HasColumnTag<TColumnTag>() => _columnsByTag.ContainsKey(typeof(TColumnTag));

    /// <summary>Reset the storage to empty without releasing buffers.</summary>
    public void Clear() => _pool.Clear();

    internal Span<TConnectivity> ConnectivitySpan => _connectivity.AsSpan();

    internal IReadOnlyList<ComponentColumnSchema> ColumnSchema => _columnSchema;

    /// <summary>Iterate live entity handles in unspecified order. Stack-allocated; never allocates.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LiveHandleEnumerator GetEnumerator() => new(_pool);

    public LiveHandleEnumerable Live
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(_pool);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var col in _columnsByTag.Values)
        {
            if (col is IDisposable d)
                d.Dispose();
        }
        _columnsByTag.Clear();
        _columnSchema = [];
        GC.SuppressFinalize(this);
    }

    private void AddColumnSchema(Type tagType, IComponentColumn column)
    {
        ComponentColumnSchema[] updated = new ComponentColumnSchema[_columnSchema.Length + 1];
        _columnSchema.CopyTo(updated, 0);
        updated[^1] = new ComponentColumnSchema(
            _columnSchema.Length,
            tagType,
            column.ElementType,
            column.ElementSize
        );
        _columnSchema = updated;
    }

    private void ValidateSchema(IReadOnlyList<ComponentColumnSchema> schema)
    {
        if (schema.Count != _columnSchema.Length)
            ThrowSchemaMismatch();

        for (int i = 0; i < schema.Count; i++)
        {
            if (schema[i] != _columnSchema[i])
                ThrowSchemaMismatch();
        }
    }

    private static void ThrowSchemaMismatch() =>
        throw new InvalidOperationException(
            "Entity snapshot column schema does not match the topology storage schema."
        );

    /// <summary>foreach-friendly wrapper for live-handle iteration.</summary>
    public readonly ref struct LiveHandleEnumerable
    {
        private readonly SlotPool<TTag> _pool;

        internal LiveHandleEnumerable(SlotPool<TTag> pool)
        {
            _pool = pool;
        }

        public LiveHandleEnumerator GetEnumerator() => new(_pool);
    }

    /// <summary>
    /// Ref-struct enumerator yielding strongly-typed handles for each live slot
    /// in unspecified order. Stack-only; never allocates.
    /// </summary>
    public ref struct LiveHandleEnumerator
    {
        private SlotPool<TTag>.LiveEnumerator _inner;

        internal LiveHandleEnumerator(SlotPool<TTag> pool)
        {
            _inner = pool.GetEnumerator();
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
