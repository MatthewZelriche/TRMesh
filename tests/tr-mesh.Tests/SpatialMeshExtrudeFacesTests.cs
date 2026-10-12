using System.Numerics;
using TRMesh.Mesh;

namespace tr_mesh.Tests;

public sealed class SpatialMeshExtrudeFacesTests
{
    private static readonly Vector2[] QuadUvs =
    [
        Vector2.Zero,
        Vector2.UnitX,
        Vector2.One,
        Vector2.UnitY,
    ];

    [Fact]
    public void ExtrudeFaces_ExtrudesAStandaloneFaceAndRevertsExactly()
    {
        using SpatialMesh mesh = CreateQuad(out int face);

        MeshPatch patch = mesh.Record(value =>
        {
            FaceExtrusion result = value.ExtrudeFaces([face], Vector3.UnitZ * 2.0f);
            Assert.Single(result.Faces);
            Assert.Equal(4, result.Vertices.Length);
        });

        Assert.Equal(8, mesh.VertexCount);
        Assert.Equal(5, mesh.FaceCount);
        Assert.Equal(12, mesh.EdgeCount);
        AssertValid(mesh);
        mesh.Revert(patch);
        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(1, mesh.FaceCount);
        Assert.Equal(4, mesh.EdgeCount);
        AssertValid(mesh);
    }

    [Fact]
    public void ExtrudeFaces_AdjacentFacesShareCopiedVerticesAndOnlyBuildTheOuterRim()
    {
        using SpatialMesh mesh = SpatialMeshBuilder.CreateAabb(Vector3.Zero, Vector3.One);
        int first = 0;
        int second = AdjacentFace(mesh, first);

        FaceExtrusion result = mesh.ExtrudeFaces([first, second], Vector3.UnitY);

        Assert.Equal(2, result.Faces.Length);
        Assert.Equal(6, result.Vertices.Length);
        Assert.Equal(12, mesh.FaceCount);
        Assert.Equal(14, mesh.VertexCount);
        AssertValid(mesh);
    }

    [Fact]
    public void ExtrudeFaces_AllFacesCreatesOneTranslatedClosedShell()
    {
        using SpatialMesh mesh = SpatialMeshBuilder.CreateAabb(Vector3.Zero, Vector3.One);
        int[] faces = Enumerable.Range(0, 6).ToArray();

        FaceExtrusion result = mesh.ExtrudeFaces(faces, new Vector3(3.0f, 0.0f, 0.0f));

        Assert.Equal(6, result.Faces.Length);
        Assert.Equal(8, result.Vertices.Length);
        Assert.Equal(8, mesh.VertexCount);
        Assert.Equal(6, mesh.FaceCount);
        Assert.Equal(12, mesh.EdgeCount);
        AssertValid(mesh);
    }

    private static SpatialMesh CreateQuad(out int face)
    {
        var mesh = new SpatialMesh();
        int a = mesh.AddVertex(Vector3.Zero);
        int b = mesh.AddVertex(Vector3.UnitX);
        int c = mesh.AddVertex(Vector3.One);
        int d = mesh.AddVertex(Vector3.UnitY);
        face = mesh.AddFace([a, b, c, d], QuadUvs);
        return mesh;
    }

    private static int AdjacentFace(SpatialMesh mesh, int face)
    {
        int start = mesh.GetFaceData(face).topology.AdjacentHalfEdge;
        int current = start;
        do
        {
            HalfEdge edge = mesh.GetHalfEdgeData(current).topology;
            int adjacent = mesh.GetHalfEdgeData(edge.TwinHalfEdge).topology.AdjacentFace;
            if (mesh.IsFaceAlive(adjacent))
                return adjacent;
            current = edge.NextHalfEdge;
        } while (current != start);
        throw new InvalidOperationException("The face has no neighbor.");
    }

    private static void AssertValid(SpatialMesh mesh)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            mesh.Serialize(writer);
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        using SpatialMesh _ = SpatialMesh.Deserialize(reader);
    }
}
