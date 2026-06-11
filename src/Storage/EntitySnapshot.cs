namespace TREditorSharp.Storage;

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
}
