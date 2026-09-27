namespace TRMesh.Mesh;

public partial class SpatialMesh
{
    public void WithVertices(VertexDataSoA.ReadOnlyColumnsAction callback) =>
        _vertices.WithReadOnlyColumns(callback);

    public void WithHalfEdges(HalfEdgeDataSoA.ReadOnlyColumnsAction callback) =>
        _halfEdges.WithReadOnlyColumns(callback);

    public void WithFaces(FaceDataSoA.ReadOnlyColumnsAction callback) =>
        _faces.WithReadOnlyColumns(callback);

    public VertexData GetVertexData(int vertex)
    {
        if (!_vertices.IsAlive(vertex))
            throw new ArgumentOutOfRangeException(nameof(vertex), "The vertex handle is not live.");

        return _vertices.Get(vertex);
    }

    public HalfEdgeData GetHalfEdgeData(int halfEdge)
    {
        if (!_halfEdges.IsAlive(halfEdge))
        {
            throw new ArgumentOutOfRangeException(
                nameof(halfEdge),
                "The half-edge handle is not live."
            );
        }

        return _halfEdges.Get(halfEdge);
    }

    public FaceData GetFaceData(int face)
    {
        if (!_faces.IsAlive(face))
            throw new ArgumentOutOfRangeException(nameof(face), "The face handle is not live.");

        return _faces.Get(face);
    }
}
