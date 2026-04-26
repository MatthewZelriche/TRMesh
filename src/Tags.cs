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
