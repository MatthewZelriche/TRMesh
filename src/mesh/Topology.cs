namespace TRMesh.Mesh;

public struct Vertex
{
    public int OutgoingHalfEdge;
}

public struct HalfEdge
{
    public int SourceVertex;
    public int TwinHalfEdge;
    public int NextHalfEdge;
    public int PrevHalfEdge;
    public int AdjacentFace;
}

public struct Face
{
    public int AdjacentHalfEdge;
}
