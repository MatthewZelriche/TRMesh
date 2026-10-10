using System.Numerics;
using TRMesh.Mesh;

namespace TRMesh.Tests;

public sealed class SpatialMeshCollapseEdgesTests
{
    [Fact]
    public void CollapseEdgesContractsEachEdgeAtItsMidpoint()
    {
        using var mesh = new SpatialMesh();
        int a = mesh.AddVertex(Vector3.Zero);
        int b = mesh.AddVertex(new Vector3(2f, 0f, 0f));
        int c = mesh.AddVertex(new Vector3(2f, 2f, 0f));
        int d = mesh.AddVertex(new Vector3(0f, 2f, 0f));
        int face = mesh.AddFace(
            [a, b, c, d],
            [Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY]
        );
        int edge = mesh.GetFaceData(face).topology.AdjacentHalfEdge;
        byte[] before = Serialize(mesh);

        MeshPatch patch = mesh.Record(value => value.CollapseEdges([edge]));

        Assert.False(mesh.IsVertexAlive(a));
        Assert.True(mesh.IsVertexAlive(b));
        Assert.Equal(new Vector3(1f, 0f, 0f), mesh.GetVertexData(b).position);
        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(1, mesh.FaceCount);
        AssertValid(mesh);

        mesh.Revert(patch);
        Assert.Equal(before, Serialize(mesh));
        AssertValid(mesh);
    }

    [Fact]
    public void CollapseEdgesSkipsEdgesRemovedByAnEarlierCollapse()
    {
        using var mesh = new SpatialMesh();
        int a = mesh.AddVertex(Vector3.Zero);
        int b = mesh.AddVertex(Vector3.UnitX);
        int c = mesh.AddVertex(Vector3.UnitY);
        int face = mesh.AddFace(
            [a, b, c],
            [Vector2.Zero, Vector2.UnitX, Vector2.UnitY]
        );
        int first = mesh.GetFaceData(face).topology.AdjacentHalfEdge;
        int second = mesh.GetHalfEdgeData(first).topology.NextHalfEdge;

        mesh.CollapseEdges([first, second]);

        Assert.Equal(2, mesh.VertexCount);
        Assert.Equal(0, mesh.FaceCount);
        AssertValid(mesh);
    }

    [Fact]
    public void CollapseEdgesRejectsDuplicateOrDeadHandlesWithoutMutation()
    {
        using var mesh = new SpatialMesh();
        int a = mesh.AddVertex(Vector3.Zero);
        int b = mesh.AddVertex(Vector3.UnitX);
        int c = mesh.AddVertex(Vector3.UnitY);
        int face = mesh.AddFace(
            [a, b, c],
            [Vector2.Zero, Vector2.UnitX, Vector2.UnitY]
        );
        int edge = mesh.GetFaceData(face).topology.AdjacentHalfEdge;
        int twin = mesh.GetHalfEdgeData(edge).topology.TwinHalfEdge;
        byte[] before = Serialize(mesh);

        Assert.Throws<ArgumentException>(() => mesh.CollapseEdges([edge, twin]));
        Assert.Throws<ArgumentException>(() => mesh.CollapseEdges([-1]));
        Assert.Equal(before, Serialize(mesh));
        AssertValid(mesh);
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
        using var stream = new MemoryStream(Serialize(mesh));
        using var reader = new BinaryReader(stream);
        using SpatialMesh _ = SpatialMesh.Deserialize(reader);
    }
}
