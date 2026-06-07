using System.Numerics;

namespace TREditorSharp.IO;

/// <summary>
/// Entity section that a binary mesh component column belongs to.
/// </summary>
public enum BinaryMeshEntityKind : byte
{
    Vertex = 1,
    HalfEdge = 2,
    Face = 3,
}

/// <summary>
/// Controls which component columns are written and how strictly binary mesh files are read.
/// </summary>
public sealed class BinaryMeshSerializerOptions
{
    readonly List<BinaryMeshColumnDescriptor> _columns = [];

    /// <summary>
    /// Creates options with the built-in spatial-mesh descriptors registered.
    /// </summary>
    /// <remarks>
    /// Each descriptor is only written when the mesh actually has its matching column, such as
    /// the position, face-corner UV, and face texture-state columns on <see cref="SpatialMesh"/>.
    /// </remarks>
    public BinaryMeshSerializerOptions()
    {
        _columns.Add(BinaryMeshColumnDescriptors.VertexPositions);
        _columns.Add(BinaryMeshColumnDescriptors.FaceCornerUvs);
        _columns.Add(BinaryMeshColumnDescriptors.FaceTextureStates);
    }

    /// <summary>
    /// Column descriptors that define optional or required component payloads in the file.
    /// </summary>
    /// <remarks>
    /// On write, descriptors are considered in list order. A descriptor writes its payload only if
    /// the matching column is registered on the mesh, unless <see cref="BinaryMeshColumnDescriptor.IsRequired"/>
    /// is true, in which case a missing column is an error. On read, descriptors identify known
    /// column ids, create the matching column if needed, and decode the payload into dense entity order.
    /// Column ids must be unique within this list.
    /// </remarks>
    public IList<BinaryMeshColumnDescriptor> Columns => _columns;

    /// <summary>
    /// Allows a reader to skip column payloads whose ids are not present in <see cref="Columns"/>.
    /// </summary>
    /// <remarks>
    /// Leave this false for strict project files where unknown data probably means a version or
    /// schema mismatch. Set it true for forward-compatible readers that should preserve topology
    /// and known columns even when newer writers added extra optional columns.
    /// </remarks>
    public bool IgnoreUnknownColumns { get; set; }

    /// <summary>
    /// Runs mesh consistency validation after a successful read.
    /// </summary>
    /// <remarks>
    /// Validation catches corrupt or inconsistent topology after file-local references are remapped
    /// to fresh runtime handles. It is enabled by default; disabling it can be useful for trusted
    /// bulk loads where callers validate separately.
    /// </remarks>
    public bool ValidateOnRead { get; set; } = true;

    internal static BinaryMeshSerializerOptions Default { get; } = new();
}

/// <summary>
/// Encodes one unmanaged column element into its binary payload representation.
/// </summary>
public delegate void BinaryMeshWriteElement<T>(in T value, Span<byte> destination)
    where T : unmanaged;

/// <summary>
/// Decodes one unmanaged column element from its binary payload representation.
/// </summary>
public delegate T BinaryMeshReadElement<T>(ReadOnlySpan<byte> source)
    where T : unmanaged;

/// <summary>
/// Built-in binary column descriptors understood by the default serializer options.
/// </summary>
public static class BinaryMeshColumnDescriptors
{
    /// <summary>
    /// Vertex position column used by <see cref="SpatialMesh"/>, encoded as three little-endian
    /// 32-bit floats.
    /// </summary>
    public static BinaryMeshColumnDescriptor VertexPositions { get; } =
        BinaryMeshColumnDescriptor.Create<Vector3, VertexPositionTag>(
            BinaryMeshEntityKind.Vertex,
            "trmesh.vertex.position.v1",
            12,
            static (in Vector3 value, Span<byte> destination) =>
            {
                BinaryMeshPrimitives.WriteSingle(destination[..4], value.X);
                BinaryMeshPrimitives.WriteSingle(destination.Slice(4, 4), value.Y);
                BinaryMeshPrimitives.WriteSingle(destination.Slice(8, 4), value.Z);
            },
            static source => new Vector3(
                BinaryMeshPrimitives.ReadSingle(source[..4]),
                BinaryMeshPrimitives.ReadSingle(source.Slice(4, 4)),
                BinaryMeshPrimitives.ReadSingle(source.Slice(8, 4))
            )
        );

    /// <summary>
    /// Face-corner UV column used by <see cref="SpatialMesh"/>, encoded as two little-endian
    /// 32-bit floats.
    /// </summary>
    public static BinaryMeshColumnDescriptor FaceCornerUvs { get; } =
        BinaryMeshColumnDescriptor.Create<Vector2, FaceCornerUvTag>(
            BinaryMeshEntityKind.HalfEdge,
            "trmesh.half-edge.face-corner-uv.v1",
            8,
            static (in Vector2 value, Span<byte> destination) =>
            {
                BinaryMeshPrimitives.WriteSingle(destination[..4], value.X);
                BinaryMeshPrimitives.WriteSingle(destination.Slice(4, 4), value.Y);
            },
            static source => new Vector2(
                BinaryMeshPrimitives.ReadSingle(source[..4]),
                BinaryMeshPrimitives.ReadSingle(source.Slice(4, 4))
            )
        );

    /// <summary>
    /// Packed face texture-state column used by <see cref="SpatialMesh"/>, encoded as one
    /// little-endian 32-bit unsigned integer.
    /// </summary>
    public static BinaryMeshColumnDescriptor FaceTextureStates { get; } =
        BinaryMeshColumnDescriptor.Create<uint, FaceTextureStateTag>(
            BinaryMeshEntityKind.Face,
            "trmesh.face.texture-state.v1",
            4,
            static (in uint value, Span<byte> destination) =>
                BinaryMeshPrimitives.WriteUInt32(destination, value),
            static source => BinaryMeshPrimitives.ReadUInt32(source)
        );
}
