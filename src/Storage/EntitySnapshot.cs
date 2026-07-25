namespace TREditorSharp.Storage;

using System.Runtime.InteropServices;

/// <summary>
/// Complete component state for one reserved topology entity. Component bytes are packed in
/// column registration order and interpreted using <see cref="ColumnSchema"/>.
/// </summary>
internal sealed class EntitySnapshot<TTag>
    where TTag : unmanaged
{
    public Handle<TTag> Handle { get; }
    public IReadOnlyList<ComponentColumnSchema> ColumnSchema => _columnSchema;
    public ReadOnlyMemory<byte> ComponentData => _componentData;

    private readonly ComponentColumnSchema[] _columnSchema;
    private readonly byte[] _componentData;

    public EntitySnapshot(
        Handle<TTag> handle,
        ComponentColumnSchema[] columnSchema,
        byte[] componentData
    )
    {
        ArgumentNullException.ThrowIfNull(columnSchema);
        ArgumentNullException.ThrowIfNull(componentData);

        Handle = handle;
        _columnSchema = columnSchema;
        _componentData = componentData;
    }

    public T GetComponent<T, TColumnTag>()
        where T : unmanaged
    {
        int offset = 0;
        for (int i = 0; i < _columnSchema.Length; i++)
        {
            ComponentColumnSchema column = _columnSchema[i];
            if (column.TagType == typeof(TColumnTag))
            {
                if (column.ElementType != typeof(T))
                {
                    throw new InvalidOperationException(
                        $"Column tagged {typeof(TColumnTag)} stores {column.ElementType}, "
                            + $"not {typeof(T)}."
                    );
                }

                return MemoryMarshal.Read<T>(
                    _componentData.AsSpan(offset, column.ElementSize)
                );
            }

            offset += column.ElementSize;
        }

        throw new KeyNotFoundException(
            $"No component column tagged {typeof(TColumnTag)} exists in the snapshot."
        );
    }

    public bool StateEquals(EntitySnapshot<TTag> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Handle != other.Handle || _columnSchema.Length != other._columnSchema.Length)
            return false;

        for (int i = 0; i < _columnSchema.Length; i++)
        {
            if (_columnSchema[i] != other._columnSchema[i])
                return false;
        }

        return _componentData.AsSpan().SequenceEqual(other._componentData);
    }

    internal static Dictionary<Handle<TTag>, EntitySnapshot<TTag>> IndexByHandle(
        IReadOnlyList<EntitySnapshot<TTag>> snapshots
    )
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        Dictionary<Handle<TTag>, EntitySnapshot<TTag>> result = new(snapshots.Count);
        for (int i = 0; i < snapshots.Count; i++)
        {
            EntitySnapshot<TTag> snapshot = snapshots[i];
            if (!result.TryAdd(snapshot.Handle, snapshot))
            {
                throw new ArgumentException(
                    $"Topology snapshot list contains duplicate handle {snapshot.Handle}."
                );
            }
        }

        return result;
    }
}
