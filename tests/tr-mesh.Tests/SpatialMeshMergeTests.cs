using System.Numerics;
using TRMesh.Mesh;

namespace TRMesh.Tests;

public sealed class SpatialMeshMergeTests
{
    private static readonly Vector2[] QuadUvs =
        [Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY];

    [Fact]
    public void ConnectedVerticesMergeIntoTheTargetAndUndoExactly()
    {
        using var mesh = new SpatialMesh();
        int a = mesh.AddVertex(Vector3.Zero);
        int target = mesh.AddVertex(Vector3.UnitX);
        int c = mesh.AddVertex(Vector3.One);
        int d = mesh.AddVertex(Vector3.UnitY);
        mesh.AddFace([a, target, c, d], QuadUvs);
        byte[] before = Serialize(mesh);
        var position = new Vector3(2f, 3f, 4f);

        MeshPatch patch = mesh.Record(value => value.MergeVertices([a, target], target, position));

        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(1, mesh.FaceCount);
        Assert.False(mesh.IsVertexAlive(a));
        Assert.Equal(position, mesh.GetVertexData(target).position);
        AssertValid(mesh);

        mesh.Revert(patch);

        Assert.Equal(before, Serialize(mesh));
        AssertValid(mesh);
    }

    [Fact]
    public void DisconnectedVerticesOnOneFaceAreSplitBeforeTheyMerge()
    {
        using var mesh = new SpatialMesh();
        int a = mesh.AddVertex(new Vector3(0f, 0f, 0f));
        int b = mesh.AddVertex(new Vector3(1f, -1f, 0f));
        int target = mesh.AddVertex(new Vector3(2f, 0f, 0f));
        int d = mesh.AddVertex(new Vector3(1.5f, 1f, 0f));
        int e = mesh.AddVertex(new Vector3(0.5f, 1f, 0f));
        mesh.AddFace(
            [a, b, target, d, e],
            [Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, new Vector2(0.5f, 1f)]
        );

        mesh.MergeVertices([a, target], target, mesh.GetVertexData(target).position);

        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(1, mesh.FaceCount);
        Assert.False(mesh.IsVertexAlive(a));
        AssertValid(mesh);
    }

    [Fact]
    public void FaceBridgesCanGrowTheTargetComponentAcrossMultipleIslands()
    {
        using var mesh = new SpatialMesh();
        int a = mesh.AddVertex(new Vector3(-2f, 0f, 0f));
        int x = mesh.AddVertex(new Vector3(-1f, -1f, 0f));
        int b = mesh.AddVertex(Vector3.Zero);
        int shared = mesh.AddVertex(new Vector3(-1f, 1f, 0f));
        int z = mesh.AddVertex(new Vector3(1f, -1f, 0f));
        int target = mesh.AddVertex(new Vector3(2f, 0f, 0f));
        int w = mesh.AddVertex(new Vector3(1f, 1f, 0f));
        mesh.AddFace([a, x, b, shared], QuadUvs);
        mesh.AddFace(
            [shared, b, z, target, w],
            [Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, new Vector2(0.5f, 1f)]
        );

        mesh.MergeVertices([a, b, target], target, Vector3.Zero);

        Assert.Equal(5, mesh.VertexCount);
        Assert.Equal(1, mesh.FaceCount);
        Assert.False(mesh.IsVertexAlive(a));
        Assert.False(mesh.IsVertexAlive(b));
        AssertValid(mesh);
    }

    [Fact]
    public void UnbridgeableIslandsAreRejectedWithoutMutation()
    {
        using var mesh = new SpatialMesh();
        int a = mesh.AddVertex(Vector3.Zero);
        int b = mesh.AddVertex(Vector3.UnitX);
        int c = mesh.AddVertex(Vector3.UnitY);
        int target = mesh.AddVertex(new Vector3(3f, 0f, 0f));
        int e = mesh.AddVertex(new Vector3(4f, 0f, 0f));
        int f = mesh.AddVertex(new Vector3(3f, 1f, 0f));
        mesh.AddFace([a, b, c], [Vector2.Zero, Vector2.UnitX, Vector2.UnitY]);
        mesh.AddFace([target, e, f], [Vector2.Zero, Vector2.UnitX, Vector2.UnitY]);
        byte[] before = Serialize(mesh);

        Assert.Throws<ArgumentException>(() =>
            mesh.MergeVertices([a, target], target, mesh.GetVertexData(target).position)
        );

        Assert.Equal(before, Serialize(mesh));
        AssertValid(mesh);
    }

    [Fact]
    public void RecordRollsBackAnEarlierBridgeWhenALaterIslandCannotBeConnected()
    {
        using var mesh = new SpatialMesh();
        int a = mesh.AddVertex(new Vector3(0f, 0f, 0f));
        int x = mesh.AddVertex(new Vector3(1f, -1f, 0f));
        int target = mesh.AddVertex(new Vector3(2f, 0f, 0f));
        int y = mesh.AddVertex(new Vector3(1.5f, 1f, 0f));
        int z = mesh.AddVertex(new Vector3(0.5f, 1f, 0f));
        int isolated = mesh.AddVertex(new Vector3(4f, 0f, 0f));
        int isolatedB = mesh.AddVertex(new Vector3(5f, 0f, 0f));
        int isolatedC = mesh.AddVertex(new Vector3(4f, 1f, 0f));
        mesh.AddFace(
            [a, x, target, y, z],
            [Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY, new Vector2(0.5f, 1f)]
        );
        mesh.AddFace(
            [isolated, isolatedB, isolatedC],
            [Vector2.Zero, Vector2.UnitX, Vector2.UnitY]
        );
        byte[] before = Serialize(mesh);

        Assert.Throws<ArgumentException>(() =>
            mesh.Record(value =>
                value.MergeVertices(
                    [a, isolated, target],
                    target,
                    value.GetVertexData(target).position
                )
            )
        );

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
