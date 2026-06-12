using System.Numerics;

namespace TREditorSharp.Tests;

public sealed class SpatialMeshGeometryTests
{
    [Fact]
    public void ComputeFaceNormal_ReturnsUnitNormalInWindingDirection()
    {
        using SpatialMesh mesh = BuildQuad(out FaceHandle face);

        Vector3 normal = mesh.ComputeFaceNormal(face);

        AssertVectorApproximately(Vector3.UnitY, normal);
        Assert.InRange(MathF.Abs(normal.Length() - 1f), 0f, 1e-5f);
    }

    [Fact]
    public void ComputeFaceNormal_DegenerateFaceReturnsZero()
    {
        using SpatialMesh mesh = new();
        FaceHandle face = mesh.AddFace([
            mesh.AddVertex(Vector3.Zero),
            mesh.AddVertex(Vector3.UnitX),
            mesh.AddVertex(Vector3.UnitX * 2f),
        ]);

        Assert.Equal(Vector3.Zero, mesh.ComputeFaceNormal(face));
    }

    [Fact]
    public void ComputeFaceCentroid_ReturnsAverageVertexPosition()
    {
        using SpatialMesh mesh = BuildQuad(out FaceHandle face);

        Vector3 centroid = mesh.ComputeFaceCentroid(face);

        AssertVectorApproximately(new Vector3(1.5f, 0f, 1f), centroid);
    }

    [Fact]
    public void GeometryHelpers_DeadFaceThrowArgumentException()
    {
        using SpatialMesh mesh = BuildQuad(out FaceHandle face);
        Assert.True(mesh.RemoveFace(face));

        Assert.Throws<ArgumentException>(() => mesh.ComputeFaceNormal(face));
        Assert.Throws<ArgumentException>(() => mesh.ComputeFaceCentroid(face));
    }

    private static SpatialMesh BuildQuad(out FaceHandle face)
    {
        SpatialMesh mesh = new();
        face = mesh.AddFace([
            mesh.AddVertex(new Vector3(0f, 0f, 0f)),
            mesh.AddVertex(new Vector3(0f, 0f, 2f)),
            mesh.AddVertex(new Vector3(3f, 0f, 2f)),
            mesh.AddVertex(new Vector3(3f, 0f, 0f)),
        ]);
        return mesh;
    }

    private static void AssertVectorApproximately(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0f, 1e-5f);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0f, 1e-5f);
        Assert.InRange(MathF.Abs(expected.Z - actual.Z), 0f, 1e-5f);
    }
}
