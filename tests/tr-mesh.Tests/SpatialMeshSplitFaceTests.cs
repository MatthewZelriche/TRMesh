using System.Numerics;
using TRMesh.Mesh;

namespace TRMesh.Tests;

public sealed class SpatialMeshSplitFaceTests
{
    [Fact]
    public void SplitAddsOneFaceAndOneEdgeWhilePreservingExistingHandles()
    {
        using var mesh = new SpatialMesh();
        int a = mesh.AddVertex(new Vector3(0f, 0f, 0f));
        int b = mesh.AddVertex(new Vector3(1f, -1f, 0f));
        int c = mesh.AddVertex(new Vector3(2f, 0f, 0f));
        int d = mesh.AddVertex(new Vector3(1.5f, 1f, 0f));
        int e = mesh.AddVertex(new Vector3(0.5f, 1f, 0f));
        int face = mesh.AddFace(
            [a, b, c, d, e],
            [Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, new Vector2(0.5f, 1f)]
        );
        int[] existingHalfEdges = Enumerable.Range(0, mesh.HalfEdgeCount).ToArray();
        byte[] before = Serialize(mesh);

        MeshPatch patch = mesh.Record(value => value.SplitFace(face, a, c));

        Assert.Equal(2, mesh.FaceCount);
        Assert.Equal(6, mesh.EdgeCount);
        Assert.True(mesh.IsFaceAlive(face));
        Assert.All(existingHalfEdges, halfEdge => Assert.True(mesh.IsHalfEdgeAlive(halfEdge)));
        AssertValid(mesh);

        mesh.Revert(patch);

        Assert.Equal(before, Serialize(mesh));
        AssertValid(mesh);
    }

    [Fact]
    public void ReturnedDiagonalRunsFromFirstToSecondVertex()
    {
        using var mesh = CreateQuad(out int face, out int a, out _, out int c, out _);

        int diagonal = mesh.SplitFace(face, a, c);

        Assert.Equal(a, mesh.GetHalfEdgeData(diagonal).topology.SourceVertex);
        int twin = mesh.GetHalfEdgeData(diagonal).topology.TwinHalfEdge;
        Assert.Equal(c, mesh.GetHalfEdgeData(twin).topology.SourceVertex);
        AssertValid(mesh);
    }

    [Fact]
    public void SplitRejectsAdjacentVerticesWithoutMutation()
    {
        using var mesh = CreateQuad(out int face, out int a, out int b, out _, out _);
        byte[] before = Serialize(mesh);

        Assert.Throws<ArgumentException>(() => mesh.SplitFace(face, a, b));

        Assert.Equal(before, Serialize(mesh));
    }

    private static SpatialMesh CreateQuad(
        out int face,
        out int a,
        out int b,
        out int c,
        out int d
    )
    {
        var mesh = new SpatialMesh();
        a = mesh.AddVertex(Vector3.Zero);
        b = mesh.AddVertex(Vector3.UnitX);
        c = mesh.AddVertex(Vector3.One);
        d = mesh.AddVertex(Vector3.UnitY);
        face = mesh.AddFace(
            [a, b, c, d],
            [Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY]
        );
        return mesh;
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
