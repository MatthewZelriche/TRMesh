namespace TREditorSharp.Storage;

/// <summary>
/// Identifies one registered component column independently of its concrete implementation.
/// Registration order is significant because future entity snapshots will store entries in
/// the same order.
/// </summary>
internal readonly record struct ComponentColumnSchema(
    int RegistrationIndex,
    Type TagType,
    Type ElementType,
    int ElementSize
);
