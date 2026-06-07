using System.Numerics;
using TREditorSharp.Builders;

namespace TREditorSharp.Tests;

public sealed class SpatialMeshTextureAttributeTests
{
    [Fact]
    public void NewFacesDefaultToUntexturedWithUninitializedUvs()
    {
        using SpatialMesh mesh = BuildTwoTrianglesSharingVertices();

        foreach (FaceHandle face in mesh.EnumerateLiveFaces())
        {
            Assert.Equal(SpatialMesh.UntexturedMaterialSlot, mesh.GetFaceMaterialSlot(face));
            Assert.False(mesh.AreFaceUvsInitialized(face));

            foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
            {
                Assert.Equal(Vector2.Zero, mesh.GetFaceCornerUv(corner));
            }
        }
    }

    [Fact]
    public void TextureAttributesRoundTripThroughPublicApis()
    {
        using SpatialMesh mesh = BuildTwoTrianglesSharingVertices();
        FaceHandle face = GetFaces(mesh)[0];
        FaceCornerHandle corner = GetCorners(mesh, face)[0];
        Vector2 uv = new(1.25f, -0.5f);

        mesh.SetFaceCornerUv(corner, uv);
        mesh.SetFaceMaterialSlot(face, 7);
        mesh.SetFaceUvsInitialized(face, true);

        Assert.Equal(uv, mesh.GetFaceCornerUv(corner));
        Assert.Equal(7, mesh.GetFaceMaterialSlot(face));
        Assert.True(mesh.AreFaceUvsInitialized(face));
    }

    [Fact]
    public void SharedGeometricVertexCanHaveDifferentUvsOnAdjacentFaces()
    {
        using SpatialMesh mesh = BuildTwoTrianglesSharingVertices();
        List<FaceHandle> faces = GetFaces(mesh);
        FaceCornerHandle cornerA = FindCorner(mesh, faces[0], new Vector3(0.0f, 0.0f, 0.0f));
        FaceCornerHandle cornerB = FindCorner(mesh, faces[1], new Vector3(0.0f, 0.0f, 0.0f));
        Vector2 uvA = new(0.0f, 0.0f);
        Vector2 uvB = new(5.0f, 3.0f);

        Assert.Equal(mesh.GetHalfEdge(cornerA).Origin, mesh.GetHalfEdge(cornerB).Origin);

        mesh.SetFaceCornerUv(cornerA, uvA);
        mesh.SetFaceCornerUv(cornerB, uvB);

        Assert.Equal(uvA, mesh.GetFaceCornerUv(cornerA));
        Assert.Equal(uvB, mesh.GetFaceCornerUv(cornerB));
    }

    [Fact]
    public void SetFaceMaterialSlot_RejectsNegativeSlot()
    {
        using SpatialMesh mesh = BuildTwoTrianglesSharingVertices();
        FaceHandle face = GetFaces(mesh)[0];

        Assert.Throws<ArgumentOutOfRangeException>(() => mesh.SetFaceMaterialSlot(face, -1));
    }

    [Fact]
    public void SetFaceMaterialSlot_PreservesUvInitializedState()
    {
        using SpatialMesh mesh = BuildTwoTrianglesSharingVertices();
        FaceHandle face = GetFaces(mesh)[0];
        mesh.SetFaceUvsInitialized(face, true);

        mesh.SetFaceMaterialSlot(face, 17);

        Assert.Equal(17, mesh.GetFaceMaterialSlot(face));
        Assert.True(mesh.AreFaceUvsInitialized(face));
    }

    [Fact]
    public void SetFaceUvsInitialized_PreservesMaterialSlot()
    {
        using SpatialMesh mesh = BuildTwoTrianglesSharingVertices();
        FaceHandle face = GetFaces(mesh)[0];
        mesh.SetFaceMaterialSlot(face, 23);

        mesh.SetFaceUvsInitialized(face, true);
        Assert.Equal(23, mesh.GetFaceMaterialSlot(face));

        mesh.SetFaceUvsInitialized(face, false);
        Assert.Equal(23, mesh.GetFaceMaterialSlot(face));
    }

    [Fact]
    public void FaceCornerUvApis_RejectBoundaryHalfEdge()
    {
        using SpatialMesh mesh = BuildTwoTrianglesSharingVertices();
        HalfEdgeHandle boundary = default;
        foreach (HalfEdgeHandle halfEdge in mesh.EnumerateLiveHalfEdges())
        {
            if (mesh.GetHalfEdge(halfEdge).Face.IsNull)
            {
                boundary = halfEdge;
                break;
            }
        }

        Assert.False(boundary.IsNull);
        Assert.Throws<ArgumentException>(() => mesh.GetFaceCornerUv(boundary));
        Assert.Throws<ArgumentException>(() => mesh.SetFaceCornerUv(boundary, Vector2.One));
    }

    private static SpatialMesh BuildTwoTrianglesSharingVertices()
    {
        SpatialMesh mesh = new();
        VertexHandle a = mesh.AddVertex(new Vector3(0.0f, 0.0f, 0.0f));
        VertexHandle b = mesh.AddVertex(new Vector3(1.0f, 0.0f, 0.0f));
        VertexHandle c = mesh.AddVertex(new Vector3(0.0f, 0.0f, 1.0f));
        VertexHandle d = mesh.AddVertex(new Vector3(-1.0f, 0.0f, 0.0f));

        mesh.AddFace([a, c, b]);
        mesh.AddFace([a, d, c]);
        return mesh;
    }

    private static List<FaceHandle> GetFaces(SpatialMesh mesh)
    {
        List<FaceHandle> faces = [];
        foreach (FaceHandle face in mesh.EnumerateLiveFaces())
        {
            faces.Add(face);
        }

        return faces;
    }

    private static List<FaceCornerHandle> GetCorners(SpatialMesh mesh, FaceHandle face)
    {
        List<FaceCornerHandle> corners = [];
        foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
        {
            corners.Add(corner);
        }

        return corners;
    }

    private static FaceCornerHandle FindCorner(SpatialMesh mesh, FaceHandle face, Vector3 position)
    {
        foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
        {
            VertexHandle vertex = mesh.GetHalfEdge(corner).Origin;
            if (mesh.GetVertexPosition(vertex) == position)
            {
                return corner;
            }
        }

        throw new InvalidOperationException($"Face {face} has no corner at {position}.");
    }
}
