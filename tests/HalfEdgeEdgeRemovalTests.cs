using System.Numerics;
using TREditorSharp.Storage;

namespace TREditorSharp.Tests;

public sealed class HalfEdgeEdgeRemovalTests
{
    private readonly struct WeightTag { }

    [Fact]
    public void RemoveEdge_AfterAdjacentFacesRemovedPreservesVerticesAndConsistency()
    {
        using HalfEdgeMesh mesh = BuildAdjacentTriangles(
            out FaceHandle first,
            out FaceHandle second
        );
        HalfEdgeHandle edge = FindEdge(mesh, VertexAt(mesh, 0), VertexAt(mesh, 2));

        Assert.True(mesh.RemoveFace(first));
        Assert.True(mesh.RemoveFace(second));
        Assert.True(mesh.RemoveEdge(edge));

        Assert.Equal(4, mesh.Vertices.LiveCount);
        Assert.Equal(8, mesh.HalfEdges.LiveCount);
        mesh.ValidateConsistency();
    }

    [Fact]
    public void RemoveEdge_WithLiveAdjacentFaceReturnsFalse()
    {
        using HalfEdgeMesh mesh = BuildAdjacentTriangles(out _, out _);
        HalfEdgeHandle edge = FindEdge(mesh, VertexAt(mesh, 0), VertexAt(mesh, 2));

        Assert.False(mesh.RemoveEdge(edge));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void RemoveEveryEdge_AfterFaceRemovedLeavesIsolatedVertices()
    {
        using HalfEdgeMesh mesh = BuildTriangle(out FaceHandle face);
        List<HalfEdgeHandle> edges = UniqueEdges(mesh);

        Assert.True(mesh.RemoveFace(face));
        foreach (HalfEdgeHandle edge in edges)
            Assert.True(mesh.RemoveEdge(edge));

        Assert.Equal(0, mesh.HalfEdges.LiveCount);
        Assert.Equal(3, mesh.Vertices.LiveCount);
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TopologyPatch_RemoveEveryEdgeRestoresExactHandlesAndComponentValues()
    {
        using HalfEdgeMesh mesh = BuildTriangle(out FaceHandle face);
        NativeColumn<int> weights = mesh.HalfEdges.RegisterNativeColumn<int, WeightTag>();
        List<HalfEdgeHandle> originalHalfEdges = CollectHalfEdges(mesh);
        List<HalfEdgeHandle> edges = UniqueEdges(mesh);
        VertexHandle[] vertices = CollectVertices(mesh);
        foreach (HalfEdgeHandle halfEdge in originalHalfEdges)
            weights[mesh.HalfEdges.GetDenseIndex(halfEdge)] = halfEdge.Index + 100;

        using TopologyEditScope edit = mesh.BeginTopologyEdit(vertices);
        Assert.True(mesh.RemoveFace(face));
        foreach (HalfEdgeHandle edge in edges)
            Assert.True(mesh.RemoveEdge(edge));
        using TopologyPatch patch = edit.Commit();

        Assert.Equal(0, mesh.HalfEdges.LiveCount);
        patch.ApplyBefore();

        Assert.True(mesh.Faces.IsAlive(face));
        Assert.All(originalHalfEdges, halfEdge => Assert.True(mesh.HalfEdges.IsAlive(halfEdge)));
        Assert.All(
            originalHalfEdges,
            halfEdge =>
                Assert.Equal(halfEdge.Index + 100, weights[mesh.HalfEdges.GetDenseIndex(halfEdge)])
        );
        mesh.ValidateConsistency();

        patch.ApplyAfter();

        Assert.Equal(0, mesh.Faces.LiveCount);
        Assert.Equal(0, mesh.HalfEdges.LiveCount);
        Assert.Equal(3, mesh.Vertices.LiveCount);
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TopologyPatch_RemoveAdjacentEdgesSupportsRepeatedUndoRedo()
    {
        using HalfEdgeMesh mesh = BuildTriangle(out FaceHandle face);
        List<HalfEdgeHandle> edges = UniqueEdges(mesh);
        VertexHandle[] vertices = CollectVertices(mesh);

        using TopologyEditScope edit = mesh.BeginTopologyEdit(vertices);
        Assert.True(mesh.RemoveFace(face));
        Assert.True(mesh.RemoveEdge(edges[0]));
        Assert.True(mesh.RemoveEdge(edges[1]));
        using TopologyPatch patch = edit.Commit();

        for (int i = 0; i < 3; i++)
        {
            patch.ApplyBefore();
            mesh.ValidateConsistency();
            patch.ApplyAfter();
            mesh.ValidateConsistency();
        }
    }

    private static HalfEdgeMesh BuildAdjacentTriangles(out FaceHandle first, out FaceHandle second)
    {
        HalfEdgeMesh mesh = new();
        VertexHandle a = mesh.Vertices.Allocate();
        VertexHandle b = mesh.Vertices.Allocate();
        VertexHandle c = mesh.Vertices.Allocate();
        VertexHandle d = mesh.Vertices.Allocate();
        first = mesh.AddFace([a, c, b]);
        second = mesh.AddFace([a, d, c]);
        return mesh;
    }

    private static HalfEdgeMesh BuildTriangle(out FaceHandle face)
    {
        HalfEdgeMesh mesh = new();
        face = mesh.AddFace([
            mesh.Vertices.Allocate(),
            mesh.Vertices.Allocate(),
            mesh.Vertices.Allocate(),
        ]);
        return mesh;
    }

    private static VertexHandle VertexAt(HalfEdgeMesh mesh, int index)
    {
        int current = 0;
        foreach (VertexHandle vertex in mesh.EnumerateLiveVertices())
        {
            if (current++ == index)
                return vertex;
        }
        throw new InvalidOperationException();
    }

    private static HalfEdgeHandle FindEdge(
        HalfEdgeMesh mesh,
        VertexHandle origin,
        VertexHandle destination
    )
    {
        foreach (HalfEdgeHandle edge in mesh.EnumerateLiveHalfEdges())
        {
            HalfEdge halfEdge = mesh.GetHalfEdge(edge);
            if (halfEdge.Origin == origin && mesh.GetHalfEdge(halfEdge.Twin).Origin == destination)
            {
                return edge;
            }
        }
        throw new InvalidOperationException();
    }

    private static List<HalfEdgeHandle> UniqueEdges(HalfEdgeMesh mesh)
    {
        HashSet<HalfEdgeHandle> visited = [];
        List<HalfEdgeHandle> edges = [];
        foreach (HalfEdgeHandle edge in mesh.EnumerateLiveHalfEdges())
        {
            if (!visited.Add(edge))
                continue;
            visited.Add(mesh.GetHalfEdge(edge).Twin);
            edges.Add(edge);
        }
        return edges;
    }

    private static List<HalfEdgeHandle> CollectHalfEdges(HalfEdgeMesh mesh)
    {
        List<HalfEdgeHandle> halfEdges = [];
        foreach (HalfEdgeHandle halfEdge in mesh.EnumerateLiveHalfEdges())
            halfEdges.Add(halfEdge);
        return halfEdges;
    }

    private static VertexHandle[] CollectVertices(HalfEdgeMesh mesh)
    {
        List<VertexHandle> vertices = [];
        foreach (VertexHandle vertex in mesh.EnumerateLiveVertices())
            vertices.Add(vertex);
        return vertices.ToArray();
    }
}
