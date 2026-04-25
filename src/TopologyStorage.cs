using System.Runtime.CompilerServices;

namespace TREditorSharp;

/// <summary>
/// Topology storage container combining a <see cref="SlotPool{TTag}"/> with a
/// tag-keyed registry of component columns (one row of the user's mental table per
/// registered tag). The first row is always the entity's own connectivity column,
/// which is registered automatically at construction.
///
/// Columns are identified by their type and an optional tag to distinguish multiple columns of
/// the same type.
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
    private readonly Dictionary<Type, IComponentColumn> _columnsByTag = new();
    private bool _disposed;

    public TopologyStorage(int initialCapacity = 16)
    {
        _pool = new SlotPool<TTag>(initialCapacity);
        _connectivity = new NativeColumn<TConnectivity>();
        // Connectivity column is implicitly tagged by its own element type.
        _columnsByTag[typeof(TConnectivity)] = _connectivity;
        _pool.RegisterColumn(_connectivity);
    }

    /// <summary>
    /// The connectivity column, backed by 64-byte-aligned native memory.
    /// Direct pointer access for unsafe-fast bulk ops; lifetime is owned by
    /// this storage and released on <see cref="Dispose"/>.
    /// </summary>
    public NativeColumn<TConnectivity> Connectivity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _connectivity;
    }

    /// <summary>Number of currently live entities.</summary>
    public int LiveCount => _pool.LiveCount;

    /// <summary>One past the highest slot index ever issued.</summary>
    public int HighWater => _pool.HighWater;

    /// <summary>Total backing capacity across every column.</summary>
    public int Capacity => _pool.Capacity;

    /// <summary>Allocate a fresh entity. O(1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Handle<TTag> Allocate() => _pool.Allocate();

    /// <summary>Free a previously allocated entity. O(1). Always validates.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Free(Handle<TTag> handle) => _pool.Free(handle);

    /// <summary>True if the handle still refers to a live entity.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAlive(Handle<TTag> handle) => _pool.IsAlive(handle);

    /// <summary>
    /// Get a ref to the connectivity record for <paramref name="handle"/>. The handle
    /// is validated in MESH_VALIDATE builds; in release the validation is elided and
    /// callers are responsible for handle freshness.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TConnectivity GetConnectivity(Handle<TTag> handle)
    {
        _pool.ValidateLive(handle);
        return ref _connectivity.UnsafeRef(handle.Index);
    }

    /// <summary>
    /// Register a managed component column tagged by its own element type
    /// <typeparamref name="T"/>. Equivalent to <c>RegisterColumn&lt;T, T&gt;()</c>.
    /// Throws if a column already exists for the tag.
    /// </summary>
    public ManagedColumn<T> RegisterColumn<T>() => RegisterColumn<T, T>();

    /// <summary>
    /// Register a managed component column storing <typeparamref name="T"/> under
    /// the phantom tag <typeparamref name="TColumnTag"/>. The tag uniquely
    /// identifies the column and lets multiple columns of the same element type
    /// coexist (e.g. <c>RegisterColumn&lt;Vector3, Position&gt;()</c> and
    /// <c>RegisterColumn&lt;Vector3, VertexNormal&gt;()</c>). Throws if the tag
    /// is already in use.
    /// </summary>
    public ManagedColumn<T> RegisterColumn<T, TColumnTag>()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_columnsByTag.ContainsKey(typeof(TColumnTag)))
            throw new InvalidOperationException(
                $"A column with tag {typeof(TColumnTag)} is already registered.");
        var col = new ManagedColumn<T>();
        _columnsByTag[typeof(TColumnTag)] = col;
        _pool.RegisterColumn(col);
        return col;
    }

    /// <summary>
    /// Register a native (unmanaged-backed) component column tagged by its own
    /// element type <typeparamref name="T"/>. Equivalent to
    /// <c>RegisterNativeColumn&lt;T, T&gt;()</c>.
    /// </summary>
    public NativeColumn<T> RegisterNativeColumn<T>() where T : unmanaged
        => RegisterNativeColumn<T, T>();

    /// <summary>
    /// Register a native component column storing <typeparamref name="T"/> under
    /// the phantom tag <typeparamref name="TColumnTag"/>. See
    /// <see cref="RegisterColumn{T, TColumnTag}"/> for the tag mechanism.
    /// </summary>
    public NativeColumn<T> RegisterNativeColumn<T, TColumnTag>() where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_columnsByTag.ContainsKey(typeof(TColumnTag)))
            throw new InvalidOperationException(
                $"A column with tag {typeof(TColumnTag)} is already registered.");
        var col = new NativeColumn<T>();
        _columnsByTag[typeof(TColumnTag)] = col;
        _pool.RegisterColumn(col);
        return col;
    }

    /// <summary>
    /// Look up the managed column tagged by element type <typeparamref name="T"/>.
    /// Equivalent to <c>GetColumn&lt;T, T&gt;()</c>.
    /// </summary>
    public ManagedColumn<T> GetColumn<T>() => GetColumn<T, T>();

    /// <summary>
    /// Look up the managed column tagged <typeparamref name="TColumnTag"/> and
    /// storing <typeparamref name="T"/>. Cache the returned reference outside
    /// hot loops; subsequent access via the column is a single array index.
    /// </summary>
    public ManagedColumn<T> GetColumn<T, TColumnTag>()
    {
        if (!_columnsByTag.TryGetValue(typeof(TColumnTag), out var col))
            throw new KeyNotFoundException(
                $"No column with tag {typeof(TColumnTag)} is registered.");
        if (col is not ManagedColumn<T> typed)
            throw new InvalidOperationException(
                $"Column tagged {typeof(TColumnTag)} is registered as {col.GetType().Name}, "
                + $"not ManagedColumn<{typeof(T).Name}>.");
        return typed;
    }

    /// <summary>
    /// Look up the native column tagged by element type <typeparamref name="T"/>.
    /// Equivalent to <c>GetNativeColumn&lt;T, T&gt;()</c>.
    /// </summary>
    public NativeColumn<T> GetNativeColumn<T>() where T : unmanaged
        => GetNativeColumn<T, T>();

    /// <summary>
    /// Look up the native column tagged <typeparamref name="TColumnTag"/> and
    /// storing <typeparamref name="T"/>.
    /// </summary>
    public NativeColumn<T> GetNativeColumn<T, TColumnTag>() where T : unmanaged
    {
        if (!_columnsByTag.TryGetValue(typeof(TColumnTag), out var col))
            throw new KeyNotFoundException(
                $"No column with tag {typeof(TColumnTag)} is registered.");
        if (col is not NativeColumn<T> typed)
            throw new InvalidOperationException(
                $"Column tagged {typeof(TColumnTag)} is registered as {col.GetType().Name}, "
                + $"not NativeColumn<{typeof(T).Name}>.");
        return typed;
    }

    /// <summary>
    /// True if a column tagged by element type <typeparamref name="T"/> is registered.
    /// Equivalent to <c>HasColumn&lt;T&gt;()</c> in the prior API.
    /// </summary>
    public bool HasColumn<T>() => _columnsByTag.ContainsKey(typeof(T));

    /// <summary>True if a column with the given <typeparamref name="TColumnTag"/> is registered.</summary>
    public bool HasColumnTag<TColumnTag>() => _columnsByTag.ContainsKey(typeof(TColumnTag));

    /// <summary>Reset the storage to empty without releasing buffers.</summary>
    public void Clear() => _pool.Clear();

    /// <summary>Iterate live entity handles. Stack-allocated; never allocates.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LiveHandleEnumerator GetEnumerator() => new(_pool);

    public LiveHandleEnumerable Live
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(_pool);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var col in _columnsByTag.Values)
        {
            if (col is IDisposable d) d.Dispose();
        }
        _columnsByTag.Clear();
        GC.SuppressFinalize(this);
    }

    /// <summary>foreach-friendly wrapper for live-handle iteration.</summary>
    public readonly ref struct LiveHandleEnumerable
    {
        private readonly SlotPool<TTag> _pool;
        internal LiveHandleEnumerable(SlotPool<TTag> pool) { _pool = pool; }
        public LiveHandleEnumerator GetEnumerator() => new(_pool);
    }

    /// <summary>
    /// Ref-struct enumerator yielding strongly-typed handles for each live slot.
    /// Walks the alive bitset; skips dead-only words in O(1) via TZCNT.
    /// </summary>
    public ref struct LiveHandleEnumerator
    {
        private SlotPool<TTag>.LiveEnumerator _inner;
        internal LiveHandleEnumerator(SlotPool<TTag> pool) { _inner = pool.GetEnumerator(); }

        public Handle<TTag> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _inner.Current;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext() => _inner.MoveNext();
    }
}
