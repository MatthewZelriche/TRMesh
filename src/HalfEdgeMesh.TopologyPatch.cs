namespace TREditorSharp;

using TREditorSharp.Storage;

public partial class HalfEdgeMesh
{
    private TopologyEditScope? _activeTopologyEdit;

    /// <summary>
    /// Begin tracking a topology edit around a conservative set of affected vertices.
    /// Dispose the returned scope without committing to restore the initial state.
    /// </summary>
    public TopologyEditScope BeginTopologyEdit(IEnumerable<VertexHandle> affectedVertices)
    {
        if (_activeTopologyEdit is not null)
            throw new InvalidOperationException(
                "Nested topology edits on the same mesh are not supported."
            );

        TopologyEditScope edit = new(this, CaptureTopologyPatchState(affectedVertices));
        _activeTopologyEdit = edit;
        return edit;
    }

    internal void CompleteTopologyEdit(TopologyEditScope edit)
    {
        if (!ReferenceEquals(_activeTopologyEdit, edit))
            throw new InvalidOperationException("The topology edit is not active on this mesh.");
        _activeTopologyEdit = null;
    }

    /// <summary>
    /// Capture the complete currently live state of a conservative one-ring around
    /// <paramref name="affectedVertices"/> without mutating the mesh.
    /// </summary>
    internal TopologyPatchState CaptureTopologyPatchState(
        IEnumerable<VertexHandle> affectedVertices
    )
    {
        ArgumentNullException.ThrowIfNull(affectedVertices);

        HashSet<VertexHandle> vertices = [];
        HashSet<HalfEdgeHandle> halfEdges = [];
        HashSet<FaceHandle> faces = [];

        foreach (VertexHandle vertex in affectedVertices)
        {
            if (!Vertices.IsAlive(vertex))
            {
                throw new ArgumentException(
                    $"Affected vertex {vertex} does not refer to a live vertex.",
                    nameof(affectedVertices)
                );
            }

            vertices.Add(vertex);
        }

        // Capture every edge pair incident to a supplied vertex. Include both endpoints so
        // wire-only neighborhoods remain complete even when no face expands the domain.
        foreach (VertexHandle vertex in vertices.ToArray())
        {
            foreach (HalfEdgeHandle halfEdgeHandle in HalfEdgesAroundVertex(vertex))
            {
                HalfEdge halfEdge = HalfEdges[halfEdgeHandle];
                HalfEdge twin = HalfEdges[halfEdge.Twin];
                halfEdges.Add(halfEdgeHandle);
                halfEdges.Add(halfEdge.Twin);
                vertices.Add(twin.Origin);
                AddFace(halfEdge.Face);
                AddFace(twin.Face);
            }
        }

        // Incident faces expand the one-ring to every polygon vertex and face-owned half-edge.
        // The opposite twins of non-incident polygon edges remain outside the patch domain.
        foreach (FaceHandle face in faces.ToArray())
        {
            foreach (HalfEdgeHandle halfEdgeHandle in HalfEdgesAroundFace(face))
            {
                halfEdges.Add(halfEdgeHandle);
                vertices.Add(HalfEdges[halfEdgeHandle].Origin);
            }
        }

        return new TopologyPatchState(
            CaptureInStorageOrder(Vertices, vertices),
            CaptureInStorageOrder(HalfEdges, halfEdges),
            CaptureInStorageOrder(Faces, faces)
        );

        void AddFace(FaceHandle face)
        {
            if (Faces.IsAlive(face))
                faces.Add(face);
        }
    }

    private static EntitySnapshot<TTag>[] CaptureInStorageOrder<TTag, TConnectivity>(
        TopologyStorage<TTag, TConnectivity> storage,
        HashSet<Handle<TTag>> included
    )
        where TTag : unmanaged
        where TConnectivity : unmanaged
    {
        List<EntitySnapshot<TTag>> snapshots = [];
        foreach (Handle<TTag> handle in storage)
        {
            if (included.Contains(handle))
                snapshots.Add(storage.Capture(handle));
        }
        return snapshots.ToArray();
    }
}
