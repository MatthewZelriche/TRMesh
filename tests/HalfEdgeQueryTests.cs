namespace TREditorSharp.Tests;

public sealed class HalfEdgeQueryTests
{
    [Fact]
    public void FindHalfEdge_ReturnsEachDirectedMemberOfConnectedPair()
    {
        using HalfEdgeMesh mesh = BuildTriangle(
            out VertexHandle a,
            out VertexHandle b,
            out VertexHandle c
        );

        HalfEdgeHandle ab = mesh.FindHalfEdge(a, b);
        HalfEdgeHandle ba = mesh.FindHalfEdge(b, a);

        Assert.False(ab.IsNull);
        Assert.Equal(ba, mesh.GetHalfEdge(ab).Twin);
        Assert.True(mesh.FindHalfEdge(a, c) != HalfEdgeHandle.Null);
    }

    [Fact]
    public void FindHalfEdge_ReturnsNullForDisconnectedVertices()
    {
        using HalfEdgeMesh mesh = BuildTriangle(out VertexHandle a, out _, out _);
        VertexHandle disconnected = mesh.Vertices.Allocate();

        Assert.True(mesh.FindHalfEdge(a, disconnected).IsNull);
    }

    [Fact]
    public void FindHalfEdge_RejectsDeadVertices()
    {
        using HalfEdgeMesh mesh = BuildTriangle(out VertexHandle a, out _, out _);
        VertexHandle dead = mesh.Vertices.Allocate();
        mesh.Vertices.Free(dead);

        Assert.Throws<ArgumentException>(() => mesh.FindHalfEdge(a, dead));
    }

    [Fact]
    public void GetCanonicalEdge_ReturnsSameHandleForBothDirections()
    {
        using HalfEdgeMesh mesh = BuildTriangle(out VertexHandle a, out VertexHandle b, out _);
        HalfEdgeHandle ab = mesh.FindHalfEdge(a, b);
        HalfEdgeHandle ba = mesh.GetHalfEdge(ab).Twin;

        HalfEdgeHandle canonical = mesh.GetCanonicalEdge(ab);

        Assert.Equal(canonical, mesh.GetCanonicalEdge(ba));
        Assert.Equal(Math.Min(ab.Index, ba.Index), canonical.Index);
    }

    [Fact]
    public void EnumerateLiveEdges_YieldsEachTwinPairExactlyOnce()
    {
        using HalfEdgeMesh mesh = BuildTriangle(out _, out _, out _);
        List<HalfEdgeHandle> edges = [];

        foreach (HalfEdgeHandle edge in mesh.EnumerateLiveEdges())
            edges.Add(edge);

        Assert.Equal(3, edges.Count);
        Assert.Equal(3, edges.Distinct().Count());
        Assert.All(edges, edge => Assert.Equal(edge, mesh.GetCanonicalEdge(edge)));
    }

    private static HalfEdgeMesh BuildTriangle(
        out VertexHandle a,
        out VertexHandle b,
        out VertexHandle c
    )
    {
        HalfEdgeMesh mesh = new();
        a = mesh.Vertices.Allocate();
        b = mesh.Vertices.Allocate();
        c = mesh.Vertices.Allocate();
        mesh.AddFace([a, b, c]);
        return mesh;
    }
}
