using System.Runtime.InteropServices;

namespace TREditorSharp;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    public HalfEdgeHandle OutgoingHalfEdge;
}

[StructLayout(LayoutKind.Sequential)]
public struct HalfEdge
{
    public VertexHandle Origin;
    public HalfEdgeHandle Twin;
    public HalfEdgeHandle Next;
    public HalfEdgeHandle Prev;
    public FaceHandle Face;
}

[StructLayout(LayoutKind.Sequential)]
public struct Face
{
    public HalfEdgeHandle FirstHalfEdge;
}
