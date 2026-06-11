namespace TREditorSharp.Tests;

using System.Numerics;
using TREditorSharp.Storage;

public sealed class TopologyPatchTests
{
    private readonly struct WeightTag { }

    private readonly struct AddedLaterTag { }

    [Fact]
    public void ApplyBeforeAndAfter_RestoresAllComponentEntries()
    {
        using SpatialMesh mesh = new();
        NativeColumn<int> weights = mesh.Vertices.RegisterNativeColumn<int, WeightTag>();
        VertexHandle vertex = mesh.AddVertex(new Vector3(1, 2, 3));
        weights[mesh.Vertices.GetDenseIndex(vertex)] = 10;
        TopologyPatchState before = mesh.CaptureTopologyPatchState([vertex]);

        mesh.SetVertexPosition(vertex, new Vector3(4, 5, 6));
        weights[mesh.Vertices.GetDenseIndex(vertex)] = 20;
        TopologyPatchState after = mesh.CaptureTopologyPatchState([vertex]);
        using TopologyPatch patch = new(mesh, before, after);

        patch.ApplyBefore();

        Assert.Equal(new Vector3(1, 2, 3), mesh.GetVertexPosition(vertex));
        Assert.Equal(10, weights[mesh.Vertices.GetDenseIndex(vertex)]);

        patch.ApplyAfter();

        Assert.Equal(new Vector3(4, 5, 6), mesh.GetVertexPosition(vertex));
        Assert.Equal(20, weights[mesh.Vertices.GetDenseIndex(vertex)]);
    }

    [Fact]
    public void ApplyBeforeAndAfter_RestoresCompleteTopologyAtExactHandles()
    {
        using HalfEdgeMesh mesh = BuildTriangle(
            out VertexHandle a,
            out VertexHandle b,
            out VertexHandle c
        );
        TopologyPatchState before = mesh.CaptureTopologyPatchState([a, b, c]);
        ReserveState(mesh, before);
        TopologyPatchState after = EmptyState();
        using TopologyPatch patch = new(mesh, before, after);

        patch.ApplyBefore();

        Assert.All(
            before.Vertices,
            snapshot => Assert.True(mesh.Vertices.IsAlive(snapshot.Handle))
        );
        Assert.All(
            before.HalfEdges,
            snapshot => Assert.True(mesh.HalfEdges.IsAlive(snapshot.Handle))
        );
        Assert.All(before.Faces, snapshot => Assert.True(mesh.Faces.IsAlive(snapshot.Handle)));
        mesh.ValidateConsistency();

        patch.ApplyAfter();

        Assert.Equal(0, mesh.Vertices.LiveCount);
        Assert.Equal(0, mesh.HalfEdges.LiveCount);
        Assert.Equal(0, mesh.Faces.LiveCount);
        Assert.All(
            before.Vertices,
            snapshot => Assert.True(mesh.Vertices.IsReserved(snapshot.Handle))
        );
        Assert.All(
            before.HalfEdges,
            snapshot => Assert.True(mesh.HalfEdges.IsReserved(snapshot.Handle))
        );
        Assert.All(before.Faces, snapshot => Assert.True(mesh.Faces.IsReserved(snapshot.Handle)));
    }

    [Fact]
    public void ApplyBeforeAndAfter_RepeatsWithoutChangingHandles()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle created = mesh.Vertices.Allocate();
        TopologyPatchState before = EmptyState();
        TopologyPatchState after = mesh.CaptureTopologyPatchState([created]);
        using TopologyPatch patch = new(mesh, before, after);

        for (int i = 0; i < 3; i++)
        {
            patch.ApplyBefore();
            patch.ApplyBefore();
            Assert.True(mesh.Vertices.IsReserved(created));

            patch.ApplyAfter();
            patch.ApplyAfter();
            Assert.True(mesh.Vertices.IsAlive(created));
        }
    }

    [Fact]
    public void Apply_RejectsCurrentStateMismatchBeforeMutation()
    {
        using SpatialMesh mesh = new();
        VertexHandle vertex = mesh.AddVertex(new Vector3(1, 2, 3));
        TopologyPatchState before = mesh.CaptureTopologyPatchState([vertex]);
        mesh.SetVertexPosition(vertex, new Vector3(4, 5, 6));
        TopologyPatchState after = mesh.CaptureTopologyPatchState([vertex]);
        using TopologyPatch patch = new(mesh, before, after);
        Vector3 unexpected = new(7, 8, 9);
        mesh.SetVertexPosition(vertex, unexpected);

        Assert.Throws<InvalidOperationException>(patch.ApplyBefore);

        Assert.Equal(unexpected, mesh.GetVertexPosition(vertex));
        Assert.True(mesh.Vertices.IsAlive(vertex));
    }

    [Fact]
    public void Apply_RejectsChangedColumnSchemaBeforeMutation()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle vertex = mesh.Vertices.Allocate();
        TopologyPatchState state = mesh.CaptureTopologyPatchState([vertex]);
        using TopologyPatch patch = new(mesh, state, state);
        mesh.Vertices.RegisterNativeColumn<int, AddedLaterTag>();

        Assert.Throws<InvalidOperationException>(patch.ApplyBefore);

        Assert.True(mesh.Vertices.IsAlive(vertex));
    }

    [Fact]
    public void Dispose_PermanentlyReleasesCurrentlyReservedHandles()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle removed = mesh.Vertices.Allocate();
        TopologyPatchState before = mesh.CaptureTopologyPatchState([removed]);
        mesh.Vertices.CaptureAndReserve(removed);
        TopologyPatchState after = EmptyState();
        TopologyPatch patch = new(mesh, before, after);

        patch.Dispose();
        patch.Dispose();
        VertexHandle replacement = mesh.Vertices.Allocate();

        Assert.False(mesh.Vertices.IsReserved(removed));
        Assert.Equal(removed.Index, replacement.Index);
        Assert.NotEqual(removed.Generation, replacement.Generation);
        Assert.Throws<ObjectDisposedException>(patch.ApplyBefore);
    }

    [Fact]
    public void Dispose_AfterApplyingBeforeReleasesAfterOnlyReservation()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle created = mesh.Vertices.Allocate();
        TopologyPatchState before = EmptyState();
        TopologyPatchState after = mesh.CaptureTopologyPatchState([created]);
        TopologyPatch patch = new(mesh, before, after);
        patch.ApplyBefore();

        patch.Dispose();
        VertexHandle replacement = mesh.Vertices.Allocate();

        Assert.False(mesh.Vertices.IsReserved(created));
        Assert.Equal(created.Index, replacement.Index);
        Assert.NotEqual(created.Generation, replacement.Generation);
    }

    private static HalfEdgeMesh BuildTriangle(
        out VertexHandle a,
        out VertexHandle b,
        out VertexHandle c
    )
    {
        HalfEdgeMesh mesh = new();
        a = mesh.Vertices.Allocate();
        b = mesh.Vertices.Allocate();
        c = mesh.Vertices.Allocate();
        mesh.AddFace([a, b, c]);
        return mesh;
    }

    private static void ReserveState(HalfEdgeMesh mesh, TopologyPatchState state)
    {
        foreach (EntitySnapshot<FaceTag> snapshot in state.Faces)
            mesh.Faces.CaptureAndReserve(snapshot.Handle);
        foreach (EntitySnapshot<HalfEdgeTag> snapshot in state.HalfEdges)
            mesh.HalfEdges.CaptureAndReserve(snapshot.Handle);
        foreach (EntitySnapshot<VertexTag> snapshot in state.Vertices)
            mesh.Vertices.CaptureAndReserve(snapshot.Handle);
    }

    private static TopologyPatchState EmptyState() => new([], [], []);
}
