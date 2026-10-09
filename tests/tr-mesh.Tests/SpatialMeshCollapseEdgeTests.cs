using System.Numerics;
using TRMesh.Mesh;

namespace TRMesh.Tests;

public sealed class SpatialMeshCollapseEdgeTests
{
    [Fact]
    public void CollapsePreservesTheTargetAndTurnsAQuadIntoATriangle()
    {
        using var mesh = new SpatialMesh();
        int source = mesh.AddVertex(Vector3.Zero);
        int target = mesh.AddVertex(new Vector3(2f, 0f, 0f));
        int c = mesh.AddVertex(new Vector3(2f, 2f, 0f));
        int d = mesh.AddVertex(new Vector3(0f, 2f, 0f));
        int face = mesh.AddFace(
            [source, target, c, d],
            [Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY]
        );
        int edge = mesh.GetFaceData(face).topology.AdjacentHalfEdge;
        int[] preservedHalfEdges = Enumerable
            .Range(0, mesh.HalfEdgeCount)
            .Where(halfEdge => halfEdge != edge && halfEdge != mesh.GetHalfEdgeData(edge).topology.TwinHalfEdge)
            .ToArray();
        byte[] before = Serialize(mesh);
        var midpoint = new Vector3(1f, 0f, 0f);

        MeshPatch patch = mesh.Record(value => value.CollapseEdge(edge, target, midpoint));

        Assert.False(mesh.IsVertexAlive(source));
        Assert.True(mesh.IsVertexAlive(target));
        Assert.Equal(midpoint, mesh.GetVertexData(target).position);
        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(1, mesh.FaceCount);
        Assert.Equal(3, mesh.EdgeCount);
        Assert.True(mesh.IsFaceAlive(face));
        Assert.All(preservedHalfEdges, halfEdge => Assert.True(mesh.IsHalfEdgeAlive(halfEdge)));
        AssertValid(mesh);

        mesh.Revert(patch);

        Assert.Equal(before, Serialize(mesh));
        AssertValid(mesh);
    }

    [Fact]
    public void CollapseRemovesATriangleThatBecomesDegenerate()
    {
        using var mesh = new SpatialMesh();
        int source = mesh.AddVertex(Vector3.Zero);
        int target = mesh.AddVertex(Vector3.UnitX);
        int third = mesh.AddVertex(Vector3.UnitY);
        int face = mesh.AddFace(
            [source, target, third],
            [Vector2.Zero, Vector2.UnitX, Vector2.UnitY]
        );
        int edge = mesh.GetFaceData(face).topology.AdjacentHalfEdge;

        mesh.CollapseEdge(edge, target, new Vector3(0.5f, 0f, 0f));

        Assert.Equal(2, mesh.VertexCount);
        Assert.Equal(0, mesh.EdgeCount);
        Assert.Equal(0, mesh.FaceCount);
        AssertValid(mesh);
    }

    [Fact]
    public void CollapseStitchesATriangleEdgeIntoItsNeighboringFace()
    {
        using var mesh = new SpatialMesh();
        int source = mesh.AddVertex(Vector3.Zero);
        int target = mesh.AddVertex(Vector3.UnitX);
        int c = mesh.AddVertex(Vector3.One);
        int d = mesh.AddVertex(Vector3.UnitY);
        int removedFace = mesh.AddFace(
            [source, target, c],
            [Vector2.Zero, Vector2.UnitX, Vector2.One]
        );
        int preservedFace = mesh.AddFace(
            [source, c, d],
            [Vector2.Zero, Vector2.One, Vector2.UnitY]
        );
        int edge = mesh.GetFaceData(removedFace).topology.AdjacentHalfEdge;
        int preservedCorner = mesh.GetFaceData(preservedFace).topology.AdjacentHalfEdge;

        mesh.CollapseEdge(edge, target, new Vector3(0.5f, 0f, 0f));

        Assert.False(mesh.IsFaceAlive(removedFace));
        Assert.True(mesh.IsFaceAlive(preservedFace));
        Assert.True(mesh.IsHalfEdgeAlive(preservedCorner));
        Assert.Equal(target, mesh.GetHalfEdgeData(preservedCorner).topology.SourceVertex);
        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(3, mesh.EdgeCount);
        Assert.Equal(1, mesh.FaceCount);
        AssertValid(mesh);
    }

    [Fact]
    public void CollapsePreservesUnrelatedHandlesOnAClosedMesh()
    {
        using SpatialMesh mesh = SpatialMeshBuilder.CreateAabb(Vector3.Zero, Vector3.One);
        int edge = mesh.GetFaceData(0).topology.AdjacentHalfEdge;
        int source = mesh.GetHalfEdgeData(edge).topology.SourceVertex;
        int twin = mesh.GetHalfEdgeData(edge).topology.TwinHalfEdge;
        int target = mesh.GetHalfEdgeData(twin).topology.SourceVertex;
        int[] preservedHalfEdges = Enumerable
            .Range(0, mesh.HalfEdgeCount)
            .Where(halfEdge => halfEdge != edge && halfEdge != twin)
            .ToArray();
        int[] preservedFaces = Enumerable.Range(0, mesh.FaceCount).ToArray();

        mesh.CollapseEdge(edge, target, new Vector3(0.5f, 0f, 0f));

        Assert.False(mesh.IsVertexAlive(source));
        Assert.All(preservedHalfEdges, halfEdge => Assert.True(mesh.IsHalfEdgeAlive(halfEdge)));
        Assert.All(preservedFaces, face => Assert.True(mesh.IsFaceAlive(face)));
        Assert.Equal(7, mesh.VertexCount);
        Assert.Equal(11, mesh.EdgeCount);
        Assert.Equal(6, mesh.FaceCount);
        AssertValid(mesh);
    }

    [Fact]
    public void CollapseRejectsATargetThatIsNotAnEndpointWithoutMutation()
    {
        using var mesh = new SpatialMesh();
        int a = mesh.AddVertex(Vector3.Zero);
        int b = mesh.AddVertex(Vector3.UnitX);
        int other = mesh.AddVertex(Vector3.UnitY);
        int face = mesh.AddFace(
            [a, b, other],
            [Vector2.Zero, Vector2.UnitX, Vector2.UnitY]
        );
        int edge = mesh.GetFaceData(face).topology.AdjacentHalfEdge;
        byte[] before = Serialize(mesh);

        Assert.Throws<ArgumentException>(() => mesh.CollapseEdge(edge, other, Vector3.Zero));

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
