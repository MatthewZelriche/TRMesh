namespace TREditorSharp.Tests;

using System.Numerics;

public sealed class MeshRevisionTests
{
    [Fact]
    public void MutationAdvancesOnlyOwningStorageRevision()
    {
        using SpatialMesh mesh = new();
        MeshRevision beforeVertex = mesh.Revision;

        VertexHandle vertex = mesh.AddVertex(Vector3.Zero);

        Assert.NotEqual(beforeVertex.Vertices, mesh.Revision.Vertices);
        Assert.Equal(beforeVertex.HalfEdges, mesh.Revision.HalfEdges);
        Assert.Equal(beforeVertex.Faces, mesh.Revision.Faces);

        VertexHandle b = mesh.AddVertex(Vector3.UnitX);
        VertexHandle c = mesh.AddVertex(Vector3.UnitY);
        FaceHandle face = mesh.AddFace([vertex, b, c]);
        FaceCornerHandle corner = FirstCorner(mesh, face);
        MeshRevision beforeUv = mesh.Revision;

        mesh.SetFaceCornerUv(corner, Vector2.One);

        Assert.Equal(beforeUv.Vertices, mesh.Revision.Vertices);
        Assert.NotEqual(beforeUv.HalfEdges, mesh.Revision.HalfEdges);
        Assert.Equal(beforeUv.Faces, mesh.Revision.Faces);

        MeshRevision beforeMaterial = mesh.Revision;
        mesh.SetFaceMaterialSlot(face, 1);

        Assert.Equal(beforeMaterial.Vertices, mesh.Revision.Vertices);
        Assert.Equal(beforeMaterial.HalfEdges, mesh.Revision.HalfEdges);
        Assert.NotEqual(beforeMaterial.Faces, mesh.Revision.Faces);
    }

    [Fact]
    public void SupportedSpatialMeshMutationsAdvanceRevision()
    {
        using SpatialMesh mesh = new();
        MeshRevision revision = mesh.Revision;

        VertexHandle a = mesh.AddVertex(Vector3.Zero);
        AssertAdvanced(mesh, ref revision);
        VertexHandle b = mesh.AddVertex(Vector3.UnitX);
        AssertAdvanced(mesh, ref revision);
        VertexHandle c = mesh.AddVertex(Vector3.UnitY);
        AssertAdvanced(mesh, ref revision);

        mesh.SetVertexPosition(a, Vector3.UnitZ);
        AssertAdvanced(mesh, ref revision);

        FaceHandle face = mesh.AddFace([a, b, c]);
        AssertAdvanced(mesh, ref revision);

        FaceCornerHandle corner = FirstCorner(mesh, face);
        mesh.SetFaceCornerUv(corner, Vector2.One);
        AssertAdvanced(mesh, ref revision);
        mesh.SetFaceMaterialSlot(face, 3);
        AssertAdvanced(mesh, ref revision);
        mesh.SetFaceUvsInitialized(face, true);
        AssertAdvanced(mesh, ref revision);

        static void AssertAdvanced(SpatialMesh mesh, ref MeshRevision previous)
        {
            Assert.NotEqual(previous, mesh.Revision);
            previous = mesh.Revision;
        }
    }

    [Fact]
    public void ApplyingPatchAdvancesRevisionButReapplyingCurrentSideDoesNot()
    {
        using SpatialMesh mesh = new();
        VertexHandle vertex = mesh.AddVertex(Vector3.Zero);
        using TopologyEditScope edit = mesh.BeginTopologyEdit([vertex]);
        mesh.SetVertexPosition(vertex, Vector3.One);
        using TopologyPatch patch = edit.Commit();
        MeshRevision afterEdit = mesh.Revision;

        patch.ApplyBefore();
        Assert.NotEqual(afterEdit, mesh.Revision);
        MeshRevision beforeApplied = mesh.Revision;

        patch.ApplyBefore();
        Assert.Equal(beforeApplied, mesh.Revision);

        patch.ApplyAfter();
        Assert.NotEqual(beforeApplied, mesh.Revision);
    }

    private static FaceCornerHandle FirstCorner(SpatialMesh mesh, FaceHandle face)
    {
        foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
            return corner;
        throw new InvalidOperationException("Expected a face corner.");
    }
}
