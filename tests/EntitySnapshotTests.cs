using System.Numerics;
using TREditorSharp.Storage;

namespace TREditorSharp.Tests;

public sealed class EntitySnapshotTests
{
    private readonly struct PositionTag { }

    private readonly struct WeightTag { }

    private readonly struct AddedLaterTag { }

    [Fact]
    public void CaptureAndRestore_PreservesExactHandleAndAllComponentEntries()
    {
        using HalfEdgeMesh mesh = new();
        NativeColumn<Vector3> positions = mesh.Vertices.RegisterNativeColumn<
            Vector3,
            PositionTag
        >();
        NativeColumn<int> weights = mesh.Vertices.RegisterNativeColumn<int, WeightTag>();
        VertexHandle first = mesh.Vertices.Allocate();
        VertexHandle captured = mesh.Vertices.Allocate();
        VertexHandle last = mesh.Vertices.Allocate();
        SetValues(mesh, positions, weights, first, new Vector3(1, 2, 3), 10);
        SetValues(mesh, positions, weights, captured, new Vector3(4, 5, 6), 20);
        SetValues(mesh, positions, weights, last, new Vector3(7, 8, 9), 30);

        EntitySnapshot<VertexTag> snapshot = mesh.Vertices.CaptureAndReserve(captured);

        Assert.False(mesh.Vertices.IsAlive(captured));
        Assert.True(mesh.Vertices.IsReserved(captured));
        Assert.Equal(1, mesh.Vertices.GetDenseIndex(last));
        AssertValues(mesh, positions, weights, first, new Vector3(1, 2, 3), 10);
        AssertValues(mesh, positions, weights, last, new Vector3(7, 8, 9), 30);

        mesh.Vertices.RestoreReserved(snapshot);

        Assert.True(mesh.Vertices.IsAlive(captured));
        Assert.False(mesh.Vertices.IsReserved(captured));
        Assert.Equal(2, mesh.Vertices.GetDenseIndex(captured));
        AssertValues(mesh, positions, weights, captured, new Vector3(4, 5, 6), 20);
        AssertValues(mesh, positions, weights, first, new Vector3(1, 2, 3), 10);
        AssertValues(mesh, positions, weights, last, new Vector3(7, 8, 9), 30);
    }

    [Fact]
    public void CaptureAndRestore_PreservesConnectivityEntry()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle vertex = mesh.Vertices.Allocate();
        HalfEdgeHandle outgoing = mesh.HalfEdges.Allocate();
        mesh.Vertices[vertex] = new Vertex { OutgoingHalfEdge = outgoing };
        EntitySnapshot<VertexTag> snapshot = mesh.Vertices.CaptureAndReserve(vertex);

        mesh.Vertices.RestoreReserved(snapshot);

        Assert.Equal(outgoing, mesh.Vertices[vertex].OutgoingHalfEdge);
    }

    [Fact]
    public void RestoreReserved_RejectsChangedColumnSchemaBeforeChangingStorage()
    {
        using HalfEdgeMesh mesh = new();
        mesh.Vertices.RegisterNativeColumn<int, WeightTag>();
        VertexHandle vertex = mesh.Vertices.Allocate();
        EntitySnapshot<VertexTag> snapshot = mesh.Vertices.CaptureAndReserve(vertex);
        mesh.Vertices.RegisterNativeColumn<long, AddedLaterTag>();

        Assert.Throws<InvalidOperationException>(() => mesh.Vertices.RestoreReserved(snapshot));

        Assert.True(mesh.Vertices.IsReserved(vertex));
        Assert.False(mesh.Vertices.IsAlive(vertex));
        Assert.Equal(0, mesh.Vertices.LiveCount);
    }

    [Fact]
    public void SnapshotsFromSameStorageSchemaShareDescriptor()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle first = mesh.Vertices.Allocate();
        VertexHandle second = mesh.Vertices.Allocate();

        EntitySnapshot<VertexTag> firstSnapshot = mesh.Vertices.CaptureAndReserve(first);
        EntitySnapshot<VertexTag> secondSnapshot = mesh.Vertices.CaptureAndReserve(second);

        Assert.Same(firstSnapshot.ColumnSchema, secondSnapshot.ColumnSchema);
    }

    [Fact]
    public void ReleaseReserved_PermanentlyInvalidatesHandleAndMakesSlotReusable()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle vertex = mesh.Vertices.Allocate();
        mesh.Vertices.CaptureAndReserve(vertex);

        mesh.Vertices.ReleaseReserved(vertex);
        VertexHandle replacement = mesh.Vertices.Allocate();

        Assert.Equal(vertex.Index, replacement.Index);
        Assert.NotEqual(vertex.Generation, replacement.Generation);
        Assert.False(mesh.Vertices.IsAlive(vertex));
        Assert.True(mesh.Vertices.IsAlive(replacement));
    }

    private static void SetValues(
        HalfEdgeMesh mesh,
        NativeColumn<Vector3> positions,
        NativeColumn<int> weights,
        VertexHandle vertex,
        Vector3 position,
        int weight
    )
    {
        int denseIndex = mesh.Vertices.GetDenseIndex(vertex);
        positions[denseIndex] = position;
        weights[denseIndex] = weight;
    }

    private static void AssertValues(
        HalfEdgeMesh mesh,
        NativeColumn<Vector3> positions,
        NativeColumn<int> weights,
        VertexHandle vertex,
        Vector3 position,
        int weight
    )
    {
        int denseIndex = mesh.Vertices.GetDenseIndex(vertex);
        Assert.Equal(position, positions[denseIndex]);
        Assert.Equal(weight, weights[denseIndex]);
    }
}
