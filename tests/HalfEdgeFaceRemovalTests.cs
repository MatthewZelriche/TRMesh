using System.Numerics;
using TREditorSharp.Builders;

namespace TREditorSharp.Tests;

public sealed class HalfEdgeFaceRemovalTests
{
    [Fact]
    public void RemoveFace_PreservesBoundaryEdgesAndVertices()
    {
        using SpatialMesh mesh = BuildQuad();
        FaceHandle face = GetOnlyFace(mesh);
        List<HalfEdgeHandle> originalCorners = CollectCorners(mesh, face);

        Assert.True(mesh.RemoveFace(face));

        Assert.Equal(0, mesh.Faces.LiveCount);
        Assert.Equal(4, mesh.Vertices.LiveCount);
        Assert.Equal(8, mesh.HalfEdges.LiveCount);
        Assert.All(originalCorners, corner => Assert.True(mesh.GetHalfEdge(corner).Face.IsNull));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void RemoveFace_ThenAddFaceRestoresPolygonUsingOriginalCorners()
    {
        using SpatialMesh mesh = BuildQuad();
        FaceHandle originalFace = GetOnlyFace(mesh);
        List<HalfEdgeHandle> originalCorners = CollectCorners(mesh, originalFace);
        VertexHandle[] vertices = CollectVertices(mesh, originalFace);

        Assert.True(mesh.RemoveFace(originalFace));
        FaceHandle restoredFace = mesh.AddFace(vertices);

        Assert.Equal(originalCorners, CollectCorners(mesh, restoredFace));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void RemoveAdjacentFaces_ThenRestoreInReverseMaintainsConsistency()
    {
        using SpatialMesh mesh = MeshBuilders.Build(
            new BlockOptions { Min = Vector3.Zero, Max = Vector3.One }
        );
        List<FaceHandle> faceList = [];
        foreach (FaceHandle face in mesh.EnumerateLiveFaces())
        {
            faceList.Add(face);
            if (faceList.Count == 2)
                break;
        }
        FaceHandle[] faces = faceList.ToArray();
        VertexHandle[][] vertices = faces.Select(face => CollectVertices(mesh, face)).ToArray();

        Assert.True(mesh.RemoveFace(faces[0]));
        Assert.True(mesh.RemoveFace(faces[1]));
        mesh.ValidateConsistency();

        mesh.AddFace(vertices[1]);
        mesh.AddFace(vertices[0]);

        Assert.Equal(6, mesh.Faces.LiveCount);
        mesh.ValidateConsistency();
    }

    private static SpatialMesh BuildQuad()
    {
        SpatialMesh mesh = new();
        VertexHandle[] vertices =
        [
            mesh.AddVertex(Vector3.Zero),
            mesh.AddVertex(Vector3.UnitX),
            mesh.AddVertex(Vector3.One),
            mesh.AddVertex(Vector3.UnitY),
        ];
        mesh.AddFace(vertices);
        return mesh;
    }

    private static FaceHandle GetOnlyFace(SpatialMesh mesh)
    {
        List<FaceHandle> faces = [];
        foreach (FaceHandle face in mesh.EnumerateLiveFaces())
            faces.Add(face);
        return Assert.Single(faces);
    }

    private static List<HalfEdgeHandle> CollectCorners(SpatialMesh mesh, FaceHandle face)
    {
        List<HalfEdgeHandle> corners = [];
        foreach (HalfEdgeHandle corner in mesh.HalfEdgesAroundFace(face))
            corners.Add(corner);
        return corners;
    }

    private static VertexHandle[] CollectVertices(SpatialMesh mesh, FaceHandle face)
    {
        List<VertexHandle> vertices = [];
        foreach (HalfEdgeHandle corner in mesh.HalfEdgesAroundFace(face))
            vertices.Add(mesh.GetHalfEdge(corner).Origin);
        return vertices.ToArray();
    }
}
