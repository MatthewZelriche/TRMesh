namespace TRMesh.Mesh;

struct Vertex
{
    public int OutgoingHalfEdge;
}

struct HalfEdge
{
    public int SourceVertex;
    public int NextHalfEdge;
    public int PrevHalfEdge;
    public int AdjacentFace;
}

struct Face
{
    public int AdjacentHalfEdge;
}

struct Edge
{
    public HalfEdge halfEdge;
    public HalfEdge twin;
}
