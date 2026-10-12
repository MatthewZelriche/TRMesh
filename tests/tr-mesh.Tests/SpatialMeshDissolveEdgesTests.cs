using System.Numerics;
using TRMesh.Mesh;

namespace TRMesh.Tests;

public sealed class SpatialMeshDissolveEdgesTests
{
    [Fact]
    public void DissolveEdgeJoinsTwoFacesAndRevertsExactly()
    {
        using SpatialMesh mesh = CreateSplitQuad(out int diagonal);
        byte[] before = Serialize(mesh);

        MeshPatch patch = mesh.Record(value => value.DissolveEdge(diagonal));

        Assert.Equal(1, mesh.FaceCount);
        Assert.Equal(4, mesh.EdgeCount);
        Assert.Equal(4, mesh.VertexCount);
        Assert.False(mesh.IsHalfEdgeAlive(diagonal));
        AssertValid(mesh);

        mesh.Revert(patch);

        Assert.Equal(before, Serialize(mesh));
        AssertValid(mesh);
    }

    [Fact]
    public void DissolveEdgesRemovesACompleteInteriorFanAndItsIsolatedCenter()
    {
        using SpatialMesh mesh = CreateQuadFan(out int center, out int[] spokes);

        mesh.DissolveEdges(spokes);

        Assert.Equal(1, mesh.FaceCount);
        Assert.Equal(4, mesh.EdgeCount);
        Assert.Equal(4, mesh.VertexCount);
        Assert.False(mesh.IsVertexAlive(center));
        AssertValid(mesh);
    }

    [Fact]
    public void DissolveCubeEdgeRemovesItsEndpointsAndMakesTheNeighboringFacesTriangles()
    {
        using SpatialMesh mesh = SpatialMeshBuilder.CreateAabb(Vector3.Zero, Vector3.One);
        int edge = Enumerable.Range(0, mesh.HalfEdgeCount)
            .First(candidate => mesh.IsHalfEdgeAlive(candidate));
        byte[] before = Serialize(mesh);

        MeshPatch patch = mesh.Record(value => value.DissolveEdge(edge));

        Assert.Equal(6, mesh.VertexCount);
        Assert.Equal(9, mesh.EdgeCount);
        Assert.Equal(5, mesh.FaceCount);
        Assert.Equal([3, 3, 4, 4, 4], FaceSizes(mesh).Order().ToArray());
        AssertValid(mesh);

        mesh.Revert(patch);

        Assert.Equal(before, Serialize(mesh));
        AssertValid(mesh);
    }

    [Fact]
    public void DissolveEdgesRejectsAnUnselectedEdgeInsideTheMergedRegionWithoutMutation()
    {
        using SpatialMesh mesh = CreateQuadFan(out _, out int[] spokes);
        byte[] before = Serialize(mesh);

        Assert.Throws<ArgumentException>(() => mesh.DissolveEdges(spokes.AsSpan(0, 3)));

        Assert.Equal(before, Serialize(mesh));
        AssertValid(mesh);
    }

    [Fact]
    public void DissolveEdgesRejectsBoundaryAndDuplicateEdgesWithoutMutation()
    {
        using SpatialMesh mesh = CreateSplitQuad(out int diagonal);
        int face = Enumerable.Range(0, 2).First(mesh.IsFaceAlive);
        int boundary = FaceEdges(mesh, face).First(edge =>
            !mesh.IsFaceAlive(
                mesh.GetHalfEdgeData(mesh.GetHalfEdgeData(edge).topology.TwinHalfEdge)
                    .topology.AdjacentFace
            )
        );
        byte[] before = Serialize(mesh);

        Assert.Throws<ArgumentException>(() => mesh.DissolveEdge(boundary));
        Assert.Throws<ArgumentException>(() => mesh.DissolveEdges([diagonal, diagonal]));

        Assert.Equal(before, Serialize(mesh));
        AssertValid(mesh);
    }

    private static SpatialMesh CreateSplitQuad(out int diagonal)
    {
        var mesh = new SpatialMesh();
        int a = mesh.AddVertex(Vector3.Zero);
        int b = mesh.AddVertex(Vector3.UnitX);
        int c = mesh.AddVertex(Vector3.One);
        int d = mesh.AddVertex(Vector3.UnitY);
        int face = mesh.AddFace(
            [a, b, c, d],
            [Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY]
        );
        diagonal = mesh.SplitFace(face, a, c);
        return mesh;
    }

    private static SpatialMesh CreateQuadFan(out int center, out int[] spokes)
    {
        var mesh = new SpatialMesh();
        int a = mesh.AddVertex(Vector3.Zero);
        int b = mesh.AddVertex(Vector3.UnitX);
        int c = mesh.AddVertex(Vector3.One);
        int d = mesh.AddVertex(Vector3.UnitY);
        center = mesh.AddVertex(new Vector3(0.5f, 0.5f, 0.0f));
        mesh.AddFace([a, b, center], [Vector2.Zero, Vector2.UnitX, Vector2.One]);
        mesh.AddFace([b, c, center], [Vector2.Zero, Vector2.UnitX, Vector2.One]);
        mesh.AddFace([c, d, center], [Vector2.Zero, Vector2.UnitX, Vector2.One]);
        mesh.AddFace([d, a, center], [Vector2.Zero, Vector2.UnitX, Vector2.One]);

        int centerVertex = center;
        var found = new List<int>();
        mesh.WithHalfEdges((slots, topology, _) =>
        {
            for (int index = 0; index < slots.Length; index++)
            {
                int edge = slots[index];
                if (edge > topology[index].TwinHalfEdge)
                    continue;
                int source = topology[index].SourceVertex;
                int target = mesh.GetHalfEdgeData(topology[index].TwinHalfEdge).topology.SourceVertex;
                if (source == centerVertex || target == centerVertex)
                    found.Add(edge);
            }
        });
        spokes = [.. found];
        Assert.Equal(4, spokes.Length);
        return mesh;
    }

    private static int[] FaceEdges(SpatialMesh mesh, int face)
    {
        var edges = new List<int>();
        int start = mesh.GetFaceData(face).topology.AdjacentHalfEdge;
        int current = start;
        do
        {
            edges.Add(current);
            current = mesh.GetHalfEdgeData(current).topology.NextHalfEdge;
        } while (current != start);
        return [.. edges];
    }

    private static int[] FaceSizes(SpatialMesh mesh)
    {
        var sizes = new List<int>();
        mesh.WithFaces((slots, _, _) =>
        {
            foreach (int face in slots)
                sizes.Add(FaceEdges(mesh, face).Length);
        });
        return [.. sizes];
    }

    private static byte[] Serialize(SpatialMesh mesh)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        mesh.Serialize(writer);
        return stream.ToArray();
    }

    private static void AssertValid(SpatialMesh mesh)
    {
        mesh.WithHalfEdges((slots, topology, _) =>
        {
            for (int index = 0; index < slots.Length; index++)
            {
                int edge = slots[index];
                HalfEdge value = topology[index];
                Assert.True(mesh.IsVertexAlive(value.SourceVertex));
                Assert.True(mesh.IsHalfEdgeAlive(value.TwinHalfEdge));
                Assert.True(mesh.IsHalfEdgeAlive(value.NextHalfEdge));
                Assert.True(mesh.IsHalfEdgeAlive(value.PrevHalfEdge));
                Assert.Equal(edge, mesh.GetHalfEdgeData(value.TwinHalfEdge).topology.TwinHalfEdge);
                Assert.Equal(edge, mesh.GetHalfEdgeData(value.NextHalfEdge).topology.PrevHalfEdge);
                Assert.Equal(edge, mesh.GetHalfEdgeData(value.PrevHalfEdge).topology.NextHalfEdge);
                if (value.AdjacentFace != SpatialMesh.INVALID_HANDLE)
                    Assert.True(mesh.IsFaceAlive(value.AdjacentFace));
            }
        });
    }
}
