namespace TREditorSharp;

/// <summary>
/// Phantom marker for vertex slots at the type level. Zero-size <c>struct</c>; used only
/// as a generic type argument (e.g. <c>Handle&lt;VertexTag&gt;</c>,
/// <see cref="TopologyStorage{TTag, TConnectivity}"/>).
/// </summary>
public readonly struct VertexTag { }

/// <summary>Phantom marker for half-edge slots. See <see cref="VertexTag"/>.</summary>
public readonly struct HalfEdgeTag { }

/// <summary>Phantom marker for face slots. See <see cref="VertexTag"/>.</summary>
public readonly struct FaceTag { }

/// <summary>
/// Phantom marker for a vertex-position component column (e.g. <c>Vector3</c> positions)
/// stored in <see cref="TopologyStorage{TTag, TConnectivity}"/>. This tag exists so a mesh
/// can have multiple <c>Vector3</c> columns (position, normal, etc.) without ambiguity.
/// </summary>
public readonly struct VertexPositionTag { }

/// <summary>
/// Phantom marker for a face-corner UV component column stored on half-edges.
/// </summary>
public readonly struct FaceCornerUvTag { }

/// <summary>
/// Phantom marker for a packed texture-state component column stored on faces.
/// </summary>
public readonly struct FaceTextureStateTag { }
