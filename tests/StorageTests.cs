using System.Numerics;

namespace TREditorSharp.Tests;

public class StorageTests
{
    [Fact]
    public void AllocateReturnsNonNullHandle()
    {
        using var mesh = new HalfEdgeMesh();
        var v = mesh.Vertices.Allocate();
        Assert.False(v.IsNull);
        Assert.True(mesh.Vertices.IsAlive(v));
        Assert.Equal(1, mesh.Vertices.LiveCount);
    }

    [Fact]
    public void FreeMakesHandleNotAlive()
    {
        using var mesh = new HalfEdgeMesh();
        var v = mesh.Vertices.Allocate();
        mesh.Vertices.Free(v);
        Assert.False(mesh.Vertices.IsAlive(v));
        Assert.Equal(0, mesh.Vertices.LiveCount);
    }

    [Fact]
    public void ReusedSlotHasIncrementedGeneration()
    {
        using var mesh = new HalfEdgeMesh();
        var v0 = mesh.Vertices.Allocate();
        mesh.Vertices.Free(v0);
        var v1 = mesh.Vertices.Allocate();

        Assert.Equal(v0.Index, v1.Index);
        Assert.NotEqual(v0.Generation, v1.Generation);
        Assert.False(mesh.Vertices.IsAlive(v0));
        Assert.True(mesh.Vertices.IsAlive(v1));
    }

    [Fact]
    public void StaleHandleIsDetected()
    {
        using var mesh = new HalfEdgeMesh();
        var v = mesh.Vertices.Allocate();
        mesh.Vertices.Free(v);
        mesh.Vertices.Allocate(); // reuse same slot

        Assert.False(mesh.Vertices.IsAlive(v));
    }

    [Fact]
    public void DoubleFreeThrows()
    {
        using var mesh = new HalfEdgeMesh();
        var v = mesh.Vertices.Allocate();
        mesh.Vertices.Free(v);
        Assert.ThrowsAny<Exception>(() => mesh.Vertices.Free(v));
    }

    [Fact]
    public void FreeNullHandleThrows()
    {
        using var mesh = new HalfEdgeMesh();
        Assert.ThrowsAny<Exception>(() => mesh.Vertices.Free(VertexHandle.Null));
    }

    [Fact]
    public void ManyAllocFreeCyclesPreserveCorrectness()
    {
        using var mesh = new HalfEdgeMesh(initialVertexCapacity: 4);
        var live = new HashSet<VertexHandle>();
        var rng = new Random(42);

        for (int iter = 0; iter < 5_000; iter++)
        {
            if (live.Count == 0 || rng.NextDouble() < 0.6)
            {
                var v = mesh.Vertices.Allocate();
                Assert.True(live.Add(v));
                Assert.True(mesh.Vertices.IsAlive(v));
            }
            else
            {
                var pick = live.ElementAt(rng.Next(live.Count));
                live.Remove(pick);
                mesh.Vertices.Free(pick);
                Assert.False(mesh.Vertices.IsAlive(pick));
            }
        }

        Assert.Equal(live.Count, mesh.Vertices.LiveCount);
        foreach (var h in live)
        {
            Assert.True(mesh.Vertices.IsAlive(h));
        }
    }

    [Fact]
    public void ConnectivityIsReadWrite()
    {
        using var mesh = new HalfEdgeMesh();
        var v = mesh.Vertices.Allocate();
        var he = mesh.HalfEdges.Allocate();
        ref var vData = ref mesh.Vertices.GetConnectivity(v);
        vData.OutgoingHalfEdge = he;

        Assert.Equal(he, mesh.Vertices.GetConnectivity(v).OutgoingHalfEdge);
    }

    [Fact]
    public void ColumnRegisteredBeforeAllocateGrowsAutomatically()
    {
        using var mesh = new HalfEdgeMesh(initialVertexCapacity: 2);
        var pos = mesh.Vertices.RegisterColumn<Vector3>();

        var handles = new VertexHandle[20];
        for (int i = 0; i < handles.Length; i++)
        {
            handles[i] = mesh.Vertices.Allocate();
            pos[handles[i].Index] = new Vector3(i, i * 2, i * 3);
        }

        Assert.True(pos.Capacity >= 20);
        for (int i = 0; i < handles.Length; i++)
        {
            Assert.Equal(new Vector3(i, i * 2, i * 3), pos[handles[i].Index]);
        }
    }

    [Fact]
    public void ColumnRegisteredAfterAllocationsCatchesUp()
    {
        using var mesh = new HalfEdgeMesh(initialVertexCapacity: 2);
        for (int i = 0; i < 10; i++) mesh.Vertices.Allocate();

        var col = mesh.Vertices.RegisterColumn<int>();
        Assert.True(col.Capacity >= mesh.Vertices.Capacity);
    }

    [Fact]
    public void RegisterColumnTwiceForSameTypeThrows()
    {
        using var mesh = new HalfEdgeMesh();
        mesh.Vertices.RegisterColumn<Vector3>();
        Assert.Throws<InvalidOperationException>(() => mesh.Vertices.RegisterColumn<Vector3>());
    }

    [Fact]
    public void GetColumnForUnregisteredTypeThrows()
    {
        using var mesh = new HalfEdgeMesh();
        Assert.Throws<KeyNotFoundException>(() => mesh.Vertices.GetColumn<Vector3>());
    }

    [Fact]
    public void ManagedColumnClearsReferenceTypesOnFree()
    {
        using var mesh = new HalfEdgeMesh();
        var labels = mesh.Vertices.RegisterColumn<string>();
        var v = mesh.Vertices.Allocate();
        labels[v.Index] = "hello";

        mesh.Vertices.Free(v);

        Assert.Null(labels.Data[v.Index]);
    }

    [Fact]
    public void LiveIterationYieldsAllAllocatedHandlesInOrder()
    {
        using var mesh = new HalfEdgeMesh();
        var allocated = new List<VertexHandle>();
        for (int i = 0; i < 100; i++) allocated.Add(mesh.Vertices.Allocate());

        var visited = new List<VertexHandle>();
        foreach (var h in mesh.Vertices) visited.Add(h);

        Assert.Equal(allocated.Count, visited.Count);
        Assert.Equal(allocated, visited);
    }

    [Fact]
    public void LiveIterationSkipsDeadSlots()
    {
        using var mesh = new HalfEdgeMesh();
        var handles = new VertexHandle[200];
        for (int i = 0; i < handles.Length; i++) handles[i] = mesh.Vertices.Allocate();

        // Free every other slot, plus a contiguous block.
        for (int i = 0; i < handles.Length; i += 2) mesh.Vertices.Free(handles[i]);
        for (int i = 50; i < 100; i++)
        {
            if (mesh.Vertices.IsAlive(handles[i])) mesh.Vertices.Free(handles[i]);
        }

        var visited = new List<VertexHandle>();
        foreach (var h in mesh.Vertices) visited.Add(h);

        Assert.Equal(mesh.Vertices.LiveCount, visited.Count);
        Assert.All(visited, h => Assert.True(mesh.Vertices.IsAlive(h)));

        var alive = handles.Where(mesh.Vertices.IsAlive).ToList();
        Assert.Equal(alive, visited);
    }

    [Fact]
    public void LiveIterationOnEmptyPoolYieldsNothing()
    {
        using var mesh = new HalfEdgeMesh();
        int count = 0;
        foreach (var _ in mesh.Vertices) count++;
        Assert.Equal(0, count);
    }

    [Fact]
    public void ClearInvalidatesHandles()
    {
        using var mesh = new HalfEdgeMesh();
        var v = mesh.Vertices.Allocate();
        mesh.Vertices.Clear();
        Assert.Equal(0, mesh.Vertices.LiveCount);
        Assert.False(mesh.Vertices.IsAlive(v));
    }

    [Fact]
    public void HandlesForDifferentEntityKindsAreDistinctTypes()
    {
        using var mesh = new HalfEdgeMesh();
        VertexHandle v = mesh.Vertices.Allocate();
        HalfEdgeHandle h = mesh.HalfEdges.Allocate();
        FaceHandle f = mesh.Faces.Allocate();
        Assert.True(v.Index >= 0 && h.Index >= 0 && f.Index >= 0);
    }
}
