namespace TRMesh.Mesh;

public partial class SpatialMesh
{
    public VertexDataSoA.ReadOnlyEnumerable Vertices => _vertices.AsReadOnly();
    public HalfEdgeDataSoA.ReadOnlyEnumerable HalfEdges => _halfEdges.AsReadOnly();
    public FaceDataSoA.ReadOnlyEnumerable Faces => _faces.AsReadOnly();

    public VertexDataSoA.ReadOnlyView GetVertexData(int vertex)
    {
        if (!_vertices.IsAlive(vertex))
            throw new ArgumentOutOfRangeException(nameof(vertex), "The vertex handle is not live.");

        return _vertices.GetReadOnly(vertex);
    }

    public HalfEdgeDataSoA.ReadOnlyView GetHalfEdgeData(int halfEdge)
    {
        if (!_halfEdges.IsAlive(halfEdge))
        {
            throw new ArgumentOutOfRangeException(
                nameof(halfEdge),
                "The half-edge handle is not live."
            );
        }

        return _halfEdges.GetReadOnly(halfEdge);
    }

    public FaceDataSoA.ReadOnlyView GetFaceData(int face)
    {
        if (!_faces.IsAlive(face))
            throw new ArgumentOutOfRangeException(nameof(face), "The face handle is not live.");

        return _faces.GetReadOnly(face);
    }
}
