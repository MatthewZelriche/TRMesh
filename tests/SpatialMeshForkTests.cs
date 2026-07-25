namespace TREditorSharp.Tests;

using System.Numerics;

public sealed class SpatialMeshForkTests
{
    [Fact]
    public void ReadsAffectedStateFromDeltaAndUnaffectedStateFromBase()
    {
        using SpatialMesh mesh = new();
        VertexHandle a = mesh.AddVertex(Vector3.Zero);
        VertexHandle b = mesh.AddVertex(Vector3.UnitX);
        VertexHandle c = mesh.AddVertex(Vector3.UnitY);
        VertexHandle unaffected = mesh.AddVertex(new Vector3(10, 10, 10));
        FaceHandle face = mesh.AddFace([a, b, c]);
        FaceCornerHandle corner = FirstCorner(mesh, face);
        mesh.SetFaceCornerUv(corner, new Vector2(1, 2));
        mesh.SetFaceUvsInitialized(face, true);
        mesh.SetFaceMaterialSlot(face, 2);

        using TopologyEditScope edit = mesh.BeginTopologyEdit([a]);
        mesh.SetVertexPosition(a, Vector3.UnitZ);
        mesh.SetFaceCornerUv(corner, new Vector2(3, 4));
        mesh.SetFaceMaterialSlot(face, 7);
        using TopologyPatch patch = edit.Commit();
        patch.ApplyBefore();

        SpatialMeshFork fork = new(mesh, patch.Delta);

        Assert.True(fork.IsValid);
        Assert.Equal(mesh.Revision, fork.BaseRevision);
        Assert.Equal(Vector3.Zero, mesh.GetVertexPosition(a));
        Assert.Equal(Vector3.UnitZ, fork.GetVertexPosition(a));
        Assert.Equal(new Vector2(1, 2), mesh.GetFaceCornerUv(corner));
        Assert.Equal(new Vector2(3, 4), fork.GetFaceCornerUv(corner));
        Assert.Equal(2, mesh.GetFaceMaterialSlot(face));
        Assert.Equal(7, fork.GetFaceMaterialSlot(face));
        Assert.True(fork.AreFaceUvsInitialized(face));
        Assert.Equal(mesh.GetVertexPosition(unaffected), fork.GetVertexPosition(unaffected));
        Assert.DoesNotContain(unaffected, fork.Delta.AffectedVertices);

        List<FaceCornerHandle> triangles = [];
        Assert.True(fork.TriangulateFace(face, triangles));
        Assert.Equal(3, triangles.Count);
    }

    [Fact]
    public void PresentsCreatedAndRemovedTopologyWithoutMutatingBase()
    {
        using SpatialMesh mesh = new();
        VertexHandle a = mesh.AddVertex(Vector3.Zero);
        VertexHandle b = mesh.AddVertex(Vector3.UnitX);
        VertexHandle c = mesh.AddVertex(Vector3.One);
        VertexHandle d = mesh.AddVertex(Vector3.UnitY);
        FaceHandle original = mesh.AddFace([a, b, c, d]);
        mesh.SetFaceMaterialSlot(original, 5);
        FaceCornerHandle[] originalCorners = FaceCorners(mesh, original);

        using TopologyEditScope edit = mesh.BeginTopologyEdit([a, b, c, d]);
        (FaceHandle first, FaceHandle second) = mesh.SplitFace(
            originalCorners[0],
            originalCorners[2]
        );
        using TopologyPatch patch = edit.Commit();
        patch.ApplyBefore();
        TopologyDelta delta = patch.Delta;
        patch.Dispose();

        SpatialMeshFork fork = new(mesh, delta);

        Assert.True(mesh.IsFaceAlive(original));
        Assert.False(fork.IsFaceAlive(original));
        Assert.False(mesh.IsFaceAlive(first));
        Assert.False(mesh.IsFaceAlive(second));
        Assert.True(fork.IsFaceAlive(first));
        Assert.True(fork.IsFaceAlive(second));

        foreach (FaceHandle face in new[] { first, second })
        {
            Assert.Equal(5, fork.GetFaceMaterialSlot(face));
            List<FaceCornerHandle> triangles = [];
            Assert.True(fork.TriangulateFace(face, triangles));
            Assert.Equal(3, triangles.Count);
            Assert.NotEqual(Vector3.Zero, fork.ComputeFaceNormal(face));
        }
    }

    [Fact]
    public void RejectsUseAfterBaseMeshChanges()
    {
        using SpatialMesh mesh = new();
        VertexHandle changed = mesh.AddVertex(Vector3.Zero);
        VertexHandle unrelated = mesh.AddVertex(Vector3.One);

        using TopologyEditScope edit = mesh.BeginTopologyEdit([changed]);
        mesh.SetVertexPosition(changed, Vector3.UnitX);
        using TopologyPatch patch = edit.Commit();
        patch.ApplyBefore();
        SpatialMeshFork fork = new(mesh, patch.Delta);

        mesh.SetVertexPosition(unrelated, Vector3.UnitY);

        Assert.False(fork.IsValid);
        Assert.Throws<InvalidOperationException>(() => fork.GetVertexPosition(changed));
    }

    [Fact]
    public void RequiresBaseMeshToMatchDeltaBeforeState()
    {
        using SpatialMesh mesh = new();
        VertexHandle vertex = mesh.AddVertex(Vector3.Zero);

        using TopologyEditScope edit = mesh.BeginTopologyEdit([vertex]);
        mesh.SetVertexPosition(vertex, Vector3.One);
        using TopologyPatch patch = edit.Commit();

        Assert.Throws<InvalidOperationException>(() => new SpatialMeshFork(mesh, patch.Delta));
    }

    private static FaceCornerHandle FirstCorner(SpatialMesh mesh, FaceHandle face)
    {
        foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
            return corner;
        throw new InvalidOperationException("Expected a face corner.");
    }

    private static FaceCornerHandle[] FaceCorners(SpatialMesh mesh, FaceHandle face)
    {
        List<FaceCornerHandle> corners = [];
        foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
            corners.Add(corner);
        return corners.ToArray();
    }
}
