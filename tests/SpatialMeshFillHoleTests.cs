using System.Numerics;

namespace TREditorSharp.Tests;

public sealed class SpatialMeshFillHoleTests
{
    [Fact]
    public void TryFillHole_BoundaryEdgeCreatesFaceUsingCompleteLoop()
    {
        using SpatialMesh mesh = BuildTriangle(out FaceHandle original);
        HalfEdgeHandle selected = FirstFaceEdge(mesh, original);
        Assert.True(mesh.RemoveFace(original));

        Assert.True(mesh.TryFillHole(selected, out FaceHandle filled));

        Assert.True(mesh.IsFaceAlive(filled));
        Assert.Equal(SpatialMesh.UntexturedMaterialSlot, mesh.GetFaceMaterialSlot(filled));
        Assert.False(mesh.AreFaceUvsInitialized(filled));
        Assert.Equal(3, CountFaceEdges(mesh, filled));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryFillHole_InteriorTwinOfBoundaryEdgeCreatesFace()
    {
        using SpatialMesh mesh = BuildTriangle(out FaceHandle face);
        HalfEdgeHandle interior = FirstFaceEdge(mesh, face);

        Assert.True(mesh.TryFillHole(interior, out FaceHandle filled));

        Assert.True(mesh.IsFaceAlive(face));
        Assert.True(mesh.IsFaceAlive(filled));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryFillHole_ClosedInteriorEdgeReturnsFalseWithoutMutation()
    {
        using SpatialMesh mesh = BuildTriangle(out FaceHandle face);
        HalfEdgeHandle edge = FirstFaceEdge(mesh, face);
        Assert.True(mesh.TryFillHole(edge, out _));

        Assert.False(mesh.TryFillHole(edge, out FaceHandle filled));

        Assert.True(filled.IsNull);
        mesh.ValidateConsistency();
    }

    private static SpatialMesh BuildTriangle(out FaceHandle face)
    {
        SpatialMesh mesh = new();
        face = mesh.AddFace([
            mesh.AddVertex(Vector3.Zero),
            mesh.AddVertex(Vector3.UnitX),
            mesh.AddVertex(Vector3.UnitY),
        ]);
        return mesh;
    }

    private static HalfEdgeHandle FirstFaceEdge(SpatialMesh mesh, FaceHandle face)
    {
        foreach (HalfEdgeHandle edge in mesh.HalfEdgesAroundFace(face))
            return edge;
        throw new InvalidOperationException();
    }

    private static int CountFaceEdges(SpatialMesh mesh, FaceHandle face)
    {
        int count = 0;
        foreach (HalfEdgeHandle _ in mesh.HalfEdgesAroundFace(face))
            count++;
        return count;
    }
}
