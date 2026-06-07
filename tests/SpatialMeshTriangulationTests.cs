using System.Numerics;
using TREditorSharp.Builders;

namespace TREditorSharp.Tests;

public sealed class SpatialMeshTriangulationTests
{
    [Fact]
    public void TriangulateFace_TriangleReturnsOriginalFaceCorners()
    {
        using SpatialMesh mesh = BuildTriangle();
        FaceHandle face = GetOnlyFace(mesh);
        List<FaceCornerHandle> originalCorners = CollectFaceCorners(mesh, face);
        List<FaceCornerHandle> triangles = [];

        Assert.True(mesh.TriangulateFace(face, triangles));

        Assert.Equal(originalCorners, triangles);
        AssertCornersBelongToFace(mesh, face, triangles);
    }

    [Fact]
    public void TriangulateFace_QuadReturnsTwoTrianglesUsingOriginalFaceCorners()
    {
        using SpatialMesh mesh = MeshBuilders.Build(
            new PlaneOptions { Width = 1.0f, Height = 1.0f }
        );
        FaceHandle face = GetOnlyFace(mesh);
        List<FaceCornerHandle> originalCorners = CollectFaceCorners(mesh, face);
        List<FaceCornerHandle> triangles = [];

        Assert.True(mesh.TriangulateFace(face, triangles));

        Assert.Equal(6, triangles.Count);
        Assert.All(triangles, corner => Assert.Contains(corner, originalCorners));
        AssertCornersBelongToFace(mesh, face, triangles);
    }

    [Fact]
    public void TriangulateFace_NgonReturnsOriginalCornersWithoutChangingTopology()
    {
        using SpatialMesh mesh = BuildPentagon();
        FaceHandle face = GetOnlyFace(mesh);
        List<FaceCornerHandle> originalCorners = CollectFaceCorners(mesh, face);
        int vertexCount = mesh.Vertices.LiveCount;
        int halfEdgeCount = mesh.HalfEdges.LiveCount;
        int faceCount = mesh.Faces.LiveCount;
        List<FaceCornerHandle> triangles = [];

        Assert.True(mesh.TriangulateFace(face, triangles));

        Assert.Equal(9, triangles.Count);
        Assert.All(triangles, corner => Assert.Contains(corner, originalCorners));
        AssertCornersBelongToFace(mesh, face, triangles);
        Assert.Equal(vertexCount, mesh.Vertices.LiveCount);
        Assert.Equal(halfEdgeCount, mesh.HalfEdges.LiveCount);
        Assert.Equal(faceCount, mesh.Faces.LiveCount);
    }

    private static SpatialMesh BuildPentagon()
    {
        SpatialMesh mesh = new();
        VertexHandle[] vertices =
        [
            mesh.AddVertex(new Vector3(-1.0f, 0.0f, 0.0f)),
            mesh.AddVertex(new Vector3(-0.25f, 0.0f, -1.0f)),
            mesh.AddVertex(new Vector3(1.0f, 0.0f, -0.5f)),
            mesh.AddVertex(new Vector3(1.0f, 0.0f, 0.5f)),
            mesh.AddVertex(new Vector3(-0.25f, 0.0f, 1.0f)),
        ];
        mesh.AddFace(vertices);
        return mesh;
    }

    private static SpatialMesh BuildTriangle()
    {
        SpatialMesh mesh = new();
        VertexHandle[] vertices =
        [
            mesh.AddVertex(new Vector3(-1.0f, 0.0f, 0.0f)),
            mesh.AddVertex(new Vector3(0.0f, 0.0f, 1.0f)),
            mesh.AddVertex(new Vector3(1.0f, 0.0f, 0.0f)),
        ];
        mesh.AddFace(vertices);
        return mesh;
    }

    private static FaceHandle GetOnlyFace(SpatialMesh mesh)
    {
        FaceHandle face = default;
        int count = 0;
        foreach (FaceHandle candidate in mesh.EnumerateLiveFaces())
        {
            face = candidate;
            count++;
        }

        Assert.Equal(1, count);
        return face;
    }

    private static List<FaceCornerHandle> CollectFaceCorners(SpatialMesh mesh, FaceHandle face)
    {
        List<FaceCornerHandle> corners = [];
        foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
        {
            corners.Add(corner);
        }

        return corners;
    }

    private static void AssertCornersBelongToFace(
        SpatialMesh mesh,
        FaceHandle face,
        IEnumerable<FaceCornerHandle> corners
    )
    {
        foreach (FaceCornerHandle corner in corners)
        {
            Assert.Equal(face, mesh.GetHalfEdge(corner).Face);
        }
    }
}
