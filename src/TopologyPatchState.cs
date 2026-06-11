namespace TREditorSharp;

using TREditorSharp.Storage;

/// <summary>
/// Complete component snapshots for the currently live entities in one conservative local
/// topology domain.
/// </summary>
internal sealed class TopologyPatchState
{
    public IReadOnlyList<EntitySnapshot<VertexTag>> Vertices { get; }
    public IReadOnlyList<EntitySnapshot<HalfEdgeTag>> HalfEdges { get; }
    public IReadOnlyList<EntitySnapshot<FaceTag>> Faces { get; }

    public TopologyPatchState(
        EntitySnapshot<VertexTag>[] vertices,
        EntitySnapshot<HalfEdgeTag>[] halfEdges,
        EntitySnapshot<FaceTag>[] faces
    )
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(halfEdges);
        ArgumentNullException.ThrowIfNull(faces);

        Vertices = Array.AsReadOnly(vertices);
        HalfEdges = Array.AsReadOnly(halfEdges);
        Faces = Array.AsReadOnly(faces);
    }
}
