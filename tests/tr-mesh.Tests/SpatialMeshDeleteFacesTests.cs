using System.Numerics;
using TRMesh.Mesh;

namespace TRMesh.Tests;

public sealed class SpatialMeshDeleteFacesTests
{
    [Fact]
    public void DeleteFaces_RemovesALoneTriangleCompletely()
    {
        using var mesh = new SpatialMesh();
        int a = mesh.AddVertex(Vector3.Zero);
        int b = mesh.AddVertex(Vector3.UnitX);
        int c = mesh.AddVertex(Vector3.UnitY);
        int face = mesh.AddFace(
            [a, b, c],
            [Vector2.Zero, Vector2.UnitX, Vector2.UnitY]
        );

        mesh.DeleteFaces([face]);

        Assert.Equal(0, mesh.FaceCount);
        Assert.Equal(0, mesh.EdgeCount);
        Assert.Equal(0, mesh.VertexCount);
        AssertValid(mesh);
    }

    [Fact]
    public void DeleteFaces_RemovesOneTriangleFromAPairAndLeavesAHoleBoundary()
    {
        using var mesh = new SpatialMesh();
        int v0 = mesh.AddVertex(Vector3.Zero);
        int v1 = mesh.AddVertex(Vector3.UnitX);
        int v2 = mesh.AddVertex(Vector3.UnitY);
        int v3 = mesh.AddVertex(new Vector3(1f, 1f, 0f));
        int first = mesh.AddFace(
            [v0, v1, v2],
            [Vector2.Zero, Vector2.UnitX, Vector2.UnitY]
        );
        int second = mesh.AddFace(
            [v0, v2, v3],
            [Vector2.Zero, Vector2.UnitX, Vector2.UnitY]
        );
        byte[] before = Serialize(mesh);

        MeshPatch patch = mesh.Record(value => value.DeleteFaces([first]));

        Assert.False(mesh.IsFaceAlive(first));
        Assert.True(mesh.IsFaceAlive(second));
        Assert.Equal(1, mesh.FaceCount);
        Assert.Equal(3, mesh.EdgeCount);
        Assert.Equal(3, mesh.VertexCount);
        Assert.False(mesh.IsVertexAlive(v1));
        AssertValid(mesh);

        mesh.Revert(patch);
        Assert.Equal(before, Serialize(mesh));
        AssertValid(mesh);
    }

    [Fact]
    public void DeleteFaces_RemovesAnAabbFaceAndKeepsTheShell()
    {
        using SpatialMesh mesh = SpatialMeshBuilder.CreateAabb(Vector3.Zero, Vector3.One);
        int face = 0;
        Assert.True(mesh.IsFaceAlive(face));
        int faceCount = mesh.FaceCount;
        int vertexCount = mesh.VertexCount;

        mesh.DeleteFaces([face]);

        Assert.False(mesh.IsFaceAlive(face));
        Assert.Equal(faceCount - 1, mesh.FaceCount);
        Assert.Equal(vertexCount, mesh.VertexCount);
        AssertValid(mesh);
    }

    [Fact]
    public void DeleteFaces_RejectsStaleHandlesWithoutMutation()
    {
        using var mesh = new SpatialMesh();
        int a = mesh.AddVertex(Vector3.Zero);
        int b = mesh.AddVertex(Vector3.UnitX);
        int c = mesh.AddVertex(Vector3.UnitY);
        int face = mesh.AddFace(
            [a, b, c],
            [Vector2.Zero, Vector2.UnitX, Vector2.UnitY]
        );
        byte[] before = Serialize(mesh);

        Assert.Throws<ArgumentException>(() => mesh.DeleteFaces([face, 99]));

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
