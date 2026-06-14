using System.Numerics;

namespace TREditorSharp.Tests;

public sealed class SpatialMeshInsetTests
{
    [Fact]
    public void InsetFace_QuadCreatesConstantDepthRingAndCap()
    {
        using SpatialMesh mesh = new();
        VertexHandle[] vertices =
        [
            mesh.AddVertex(new Vector3(0, 0, 0)),
            mesh.AddVertex(new Vector3(0, 0, 4)),
            mesh.AddVertex(new Vector3(6, 0, 4)),
            mesh.AddVertex(new Vector3(6, 0, 0)),
        ];
        FaceHandle face = mesh.AddFace(vertices);

        SpatialMesh.InsetFaceResult result = mesh.InsetFace(face, 1f);

        Vector3[] expected = [new(1, 0, 1), new(1, 0, 3), new(5, 0, 3), new(5, 0, 1)];
        Assert.Equal(4, result.RingFaces.Length);
        Assert.Equal(expected.Length, result.NewVertices.Length);
        for (int i = 0; i < expected.Length; i++)
            AssertVectorApproximately(expected[i], mesh.GetVertexPosition(result.NewVertices[i]));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void InsetFace_RingAndCapKeepSourceMaterial()
    {
        using SpatialMesh mesh = new();
        VertexHandle[] vertices =
        [
            mesh.AddVertex(Vector3.Zero),
            mesh.AddVertex(Vector3.UnitZ),
            mesh.AddVertex(Vector3.UnitX + Vector3.UnitZ),
            mesh.AddVertex(Vector3.UnitX),
        ];
        FaceHandle face = mesh.AddFace(vertices);
        mesh.SetFaceMaterialSlot(face, 12);

        SpatialMesh.InsetFaceResult result = mesh.InsetFace(face, 0.25f);

        Assert.All(
            result.RingFaces.Append(result.CapFace),
            generated => Assert.Equal(12, mesh.GetFaceMaterialSlot(generated))
        );
    }

    [Fact]
    public void ComputeMaximumInsetDepth_RectangleStopsBeforeCapCollapses()
    {
        using SpatialMesh mesh = BuildRectangle(6f, 4f, out FaceHandle face);

        float maximumDepth = mesh.ComputeMaximumInsetDepth(face);

        Assert.InRange(maximumDepth, 1.999f, 2f);
        mesh.InsetFace(face, maximumDepth);
    }

    [Fact]
    public void InsetFace_DepthBeyondMaximumThrowsWithoutMutation()
    {
        using SpatialMesh mesh = BuildRectangle(6f, 4f, out FaceHandle face);

        Assert.Throws<ArgumentOutOfRangeException>(() => mesh.InsetFace(face, 2.01f));

        Assert.True(mesh.IsFaceAlive(face));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void ComputeMaximumInsetDepth_ScalesWithSmallFaces()
    {
        using SpatialMesh mesh = BuildRectangle(0.006f, 0.004f, out FaceHandle face);

        float maximumDepth = mesh.ComputeMaximumInsetDepth(face);

        Assert.InRange(maximumDepth, 0.0019f, 0.002f);
    }

    private static SpatialMesh BuildRectangle(float width, float height, out FaceHandle face)
    {
        SpatialMesh mesh = new();
        face = mesh.AddFace([
            mesh.AddVertex(Vector3.Zero),
            mesh.AddVertex(new Vector3(0, 0, height)),
            mesh.AddVertex(new Vector3(width, 0, height)),
            mesh.AddVertex(new Vector3(width, 0, 0)),
        ]);
        return mesh;
    }

    private static void AssertVectorApproximately(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(Vector3.Distance(expected, actual), 0f, 1e-5f);
    }
}
