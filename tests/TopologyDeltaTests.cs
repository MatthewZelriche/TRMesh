namespace TREditorSharp.Tests;

using System.Numerics;

public sealed class TopologyDeltaTests
{
    [Fact]
    public void HandleCollectionsDescribeBothStatesAndTheirSortedUnion()
    {
        using SpatialMesh mesh = new();
        VertexHandle retained = mesh.AddVertex(Vector3.Zero);
        VertexHandle removed = mesh.AddVertex(Vector3.UnitX);
        TopologyPatchState before = mesh.CaptureTopologyPatchState([removed, retained]);

        mesh.Vertices.Free(removed);
        VertexHandle created = mesh.AddVertex(Vector3.UnitY);
        TopologyPatchState after = mesh.CaptureTopologyPatchState([created, retained]);

        TopologyDelta delta = new(before, after);

        Assert.Equal([retained, removed], delta.BeforeVertices);
        Assert.Equal([retained, created], delta.AfterVertices);
        Assert.Equal(
            new[] { retained, removed, created }
                .OrderBy(handle => handle.Index)
                .ThenBy(handle => handle.Generation),
            delta.AffectedVertices
        );
        Assert.Empty(delta.AffectedHalfEdges);
        Assert.Empty(delta.AffectedFaces);
    }

    [Fact]
    public void CommittedPatchExposesItsDetachedDelta()
    {
        using SpatialMesh mesh = new();
        VertexHandle vertex = mesh.AddVertex(Vector3.Zero);

        using TopologyEditScope edit = mesh.BeginTopologyEdit([vertex]);
        mesh.SetVertexPosition(vertex, Vector3.One);
        using TopologyPatch patch = edit.Commit();

        Assert.Equal([vertex], patch.Delta.BeforeVertices);
        Assert.Equal([vertex], patch.Delta.AfterVertices);
        Assert.Equal([vertex], patch.Delta.AffectedVertices);
    }

    [Fact]
    public void RemovedFaceAppearsOnlyBeforeAndRemainsAffected()
    {
        using SpatialMesh mesh = new();
        VertexHandle a = mesh.AddVertex(Vector3.Zero);
        VertexHandle b = mesh.AddVertex(Vector3.UnitX);
        VertexHandle c = mesh.AddVertex(Vector3.UnitY);
        FaceHandle face = mesh.AddFace([a, b, c]);

        using TopologyEditScope edit = mesh.BeginTopologyEdit([a, b, c]);
        Assert.True(mesh.RemoveFace(face));
        using TopologyPatch patch = edit.Commit();

        Assert.Contains(face, patch.Delta.BeforeFaces);
        Assert.DoesNotContain(face, patch.Delta.AfterFaces);
        Assert.Contains(face, patch.Delta.AffectedFaces);
    }
}
