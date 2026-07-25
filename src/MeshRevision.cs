namespace TREditorSharp;

/// <summary>
/// Identifies a version of a mesh by the revisions of its three topology storages.
/// Compare values for equality; the individual counters are not globally ordered.
/// </summary>
public readonly record struct MeshRevision(ulong Vertices, ulong HalfEdges, ulong Faces);
