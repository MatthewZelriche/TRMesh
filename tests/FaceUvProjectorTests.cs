using System.Numerics;

namespace TREditorSharp.Tests;

public sealed class FaceUvProjectorTests
{
    public static TheoryData<Vector3, Vector3, Vector3> MajorAxisBases =>
        new()
        {
            { Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY },
            { -Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY },
            { Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ },
            { -Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ },
            { Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY },
            { -Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY },
        };

    [Theory]
    [MemberData(nameof(MajorAxisBases))]
    public void TryProject_AllMajorDirectionsUseExpectedOrientation(
        Vector3 normal,
        Vector3 uAxis,
        Vector3 vAxis
    )
    {
        using SpatialMesh mesh = BuildFace(
            Vector3.Zero,
            uAxis,
            uAxis + vAxis,
            vAxis
        );
        FaceHandle face = GetOnlyFace(mesh);
        List<ProjectedFaceCornerUv> projected = [];

        Assert.True(FaceUvProjector.TryProject(mesh, face, projected));

        Assert.Equal(4, projected.Count);
        AssertProjection(projected, Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY);
        AssertNormal(mesh, face, normal);
    }

    [Fact]
    public void TryProject_SlopedFaceUsesExactFacePlaneWithoutScaleDistortion()
    {
        float diagonal = 1f / MathF.Sqrt(2f);
        Vector3 uAxis = -Vector3.UnitZ;
        Vector3 vAxis = new(-diagonal, diagonal, 0f);
        using SpatialMesh mesh = BuildFace(
            Vector3.Zero,
            uAxis,
            uAxis + vAxis,
            vAxis
        );
        FaceHandle face = GetOnlyFace(mesh);
        List<ProjectedFaceCornerUv> projected = [];

        Assert.True(FaceUvProjector.TryProject(mesh, face, projected));

        // Both source edges are exactly one object-space unit long. Exact-normal projection keeps
        // both UV edges one unit long; nearest-axis planar projection would shorten the V edge.
        AssertProjection(projected, Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY);
    }

    [Fact]
    public void TryProject_OneUnitFaceProducesOneTextureRepeatWithoutMutatingMesh()
    {
        using SpatialMesh mesh = BuildFace(
            Vector3.Zero,
            Vector3.UnitX,
            Vector3.UnitX - Vector3.UnitZ,
            -Vector3.UnitZ
        );
        FaceHandle face = GetOnlyFace(mesh);
        List<ProjectedFaceCornerUv> projected = [];

        Assert.True(FaceUvProjector.TryProject(mesh, face, projected));

        AssertProjection(projected, Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY);
        foreach (ProjectedFaceCornerUv corner in projected)
            Assert.Equal(Vector2.Zero, mesh.GetFaceCornerUv(corner.Corner));
    }

    [Fact]
    public void TryProject_NgonProjectsEveryOriginalFaceCorner()
    {
        Vector2[] expected =
        [
            new(0f, 0f),
            new(2f, 0f),
            new(3f, 1f),
            new(1.5f, 2f),
            new(0f, 1f),
        ];
        using SpatialMesh mesh = BuildFace(
            expected.Select(uv => new Vector3(uv.X, 0f, -uv.Y)).ToArray()
        );
        FaceHandle face = GetOnlyFace(mesh);
        List<ProjectedFaceCornerUv> projected = [];

        Assert.True(FaceUvProjector.TryProject(mesh, face, projected));

        Assert.Equal(expected.Length, projected.Count);
        List<FaceCornerHandle> originalCorners = CollectFaceCorners(mesh, face);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(originalCorners[i], projected[i].Corner);
            AssertVectorApproximately(expected[i], projected[i].Uv);
        }
    }

    [Fact]
    public void ReprojectInitializedFacesAroundVertices_UpdatesFaceAfterGeometryEdit()
    {
        using SpatialMesh mesh = BuildFace(
            Vector3.Zero,
            new Vector3(4f, 0f, 0f),
            new Vector3(4f, 2f, 0f),
            new Vector3(0f, 2f, 0f)
        );
        FaceHandle face = GetOnlyFace(mesh);
        List<FaceCornerHandle> corners = CollectFaceCorners(mesh, face);
        List<ProjectedFaceCornerUv> initialProjection = [];
        Assert.True(FaceUvProjector.TryProject(mesh, face, initialProjection));
        foreach (ProjectedFaceCornerUv corner in initialProjection)
            mesh.SetFaceCornerUv(corner.Corner, corner.Uv);
        mesh.SetFaceUvsInitialized(face, true);

        VertexHandle movedVertex = mesh.GetHalfEdge(corners[2]).Origin;
        mesh.SetVertexPosition(movedVertex, new Vector3(3f, 2f, 0f));

        Assert.Equal(
            1,
            FaceUvProjector.ReprojectInitializedFacesAroundVertices(mesh, [movedVertex])
        );
        foreach (FaceCornerHandle corner in corners)
        {
            Vector3 position = mesh.GetVertexPosition(mesh.GetHalfEdge(corner).Origin);
            AssertVectorApproximately(
                new Vector2(position.X, position.Y),
                mesh.GetFaceCornerUv(corner)
            );
        }
    }

    [Fact]
    public void ReprojectInitializedFacesAroundVertices_LeavesUninitializedFaceUnchanged()
    {
        using SpatialMesh mesh = BuildFace(
            Vector3.Zero,
            Vector3.UnitX,
            new Vector3(1f, 1f, 0f),
            Vector3.UnitY
        );
        FaceHandle face = GetOnlyFace(mesh);
        FaceCornerHandle movedCorner = CollectFaceCorners(mesh, face)[2];
        VertexHandle movedVertex = mesh.GetHalfEdge(movedCorner).Origin;
        mesh.SetVertexPosition(movedVertex, new Vector3(2f, 2f, 0f));

        Assert.Equal(
            0,
            FaceUvProjector.ReprojectInitializedFacesAroundVertices(mesh, [movedVertex])
        );
        foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
            Assert.Equal(Vector2.Zero, mesh.GetFaceCornerUv(corner));
    }

    [Fact]
    public void TryProject_DegenerateFaceLeavesOutputUnchanged()
    {
        using SpatialMesh mesh = BuildFace(Vector3.Zero, Vector3.UnitX, Vector3.UnitX * 2f);
        FaceHandle face = GetOnlyFace(mesh);
        var original = new ProjectedFaceCornerUv(default, new Vector2(7f, 11f));
        List<ProjectedFaceCornerUv> projected = [original];

        Assert.False(FaceUvProjector.TryProject(mesh, face, projected));

        Assert.Equal([original], projected);
    }

    private static SpatialMesh BuildFace(params Vector3[] positions)
    {
        SpatialMesh mesh = new();
        VertexHandle[] vertices = positions.Select(mesh.AddVertex).ToArray();
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
            corners.Add(corner);
        return corners;
    }

    private static void AssertProjection(
        IReadOnlyList<ProjectedFaceCornerUv> projected,
        params Vector2[] expected
    )
    {
        Assert.Equal(expected.Length, projected.Count);
        for (int i = 0; i < expected.Length; i++)
            AssertVectorApproximately(expected[i], projected[i].Uv);
    }

    private static void AssertNormal(SpatialMesh mesh, FaceHandle face, Vector3 expected)
    {
        List<FaceCornerHandle> corners = CollectFaceCorners(mesh, face);

        Vector3 a = mesh.GetVertexPosition(mesh.GetHalfEdge(corners[0]).Origin);
        Vector3 b = mesh.GetVertexPosition(mesh.GetHalfEdge(corners[1]).Origin);
        Vector3 c = mesh.GetVertexPosition(mesh.GetHalfEdge(corners[2]).Origin);
        AssertVectorApproximately(expected, Vector3.Normalize(Vector3.Cross(b - a, c - a)));
    }

    private static void AssertVectorApproximately(Vector2 expected, Vector2 actual)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0f, 1e-5f);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0f, 1e-5f);
    }

    private static void AssertVectorApproximately(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0f, 1e-5f);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0f, 1e-5f);
        Assert.InRange(MathF.Abs(expected.Z - actual.Z), 0f, 1e-5f);
    }
}
