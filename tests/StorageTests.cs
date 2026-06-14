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
        using var mesh = new HalfEdgeMesh();
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
        var vData = mesh.Vertices[v];
        vData.OutgoingHalfEdge = he;
        mesh.Vertices[v] = vData;

        Assert.Equal(he, mesh.Vertices[v].OutgoingHalfEdge);
    }

    [Fact]
    public void ColumnRegisteredBeforeAllocateGrowsAutomatically()
    {
        using var mesh = new HalfEdgeMesh();
        var pos = mesh.Vertices.RegisterNativeColumn<Vector3>();

        var handles = new VertexHandle[20];
        for (int i = 0; i < handles.Length; i++)
        {
            handles[i] = mesh.Vertices.Allocate();
            pos[mesh.Vertices.GetDenseIndex(handles[i])] = new Vector3(i, i * 2, i * 3);
        }

        Assert.Equal(handles.Length, pos.Count);
        Assert.True(pos.Capacity >= handles.Length);
        for (int i = 0; i < handles.Length; i++)
        {
            Assert.Equal(
                new Vector3(i, i * 2, i * 3),
                pos[mesh.Vertices.GetDenseIndex(handles[i])]
            );
        }
    }

    [Fact]
    public void ColumnRegisteredAfterAllocationsCatchesUp()
    {
        using var mesh = new HalfEdgeMesh();
        for (int i = 0; i < 10; i++)
            mesh.Vertices.Allocate();

        var col = mesh.Vertices.RegisterNativeColumn<int>();
        Assert.Equal(mesh.Vertices.LiveCount, col.Count);
        // Late-registered entries should be zero-initialised.
        foreach (var h in mesh.Vertices)
            Assert.Equal(0, col[mesh.Vertices.GetDenseIndex(h)]);
    }

    [Fact]
    public void RegisterNativeColumnTwiceForSameTypeThrows()
    {
        using var mesh = new HalfEdgeMesh();
        mesh.Vertices.RegisterNativeColumn<Vector3>();
        Assert.Throws<InvalidOperationException>(
            () => mesh.Vertices.RegisterNativeColumn<Vector3>()
        );
    }

    [Fact]
    public void GetNativeColumnForUnregisteredTypeThrows()
    {
        using var mesh = new HalfEdgeMesh();
        Assert.Throws<KeyNotFoundException>(() => mesh.Vertices.GetNativeColumn<Vector3>());
    }

    [Fact]
    public void LiveIterationYieldsAllAllocatedHandlesInOrder()
    {
        using var mesh = new HalfEdgeMesh();
        var allocated = new List<VertexHandle>();
        for (int i = 0; i < 100; i++)
            allocated.Add(mesh.Vertices.Allocate());

        var visited = new List<VertexHandle>();
        foreach (var h in mesh.Vertices)
            visited.Add(h);

        Assert.Equal(allocated.Count, visited.Count);
        Assert.Equal(allocated, visited);
    }

    [Fact]
    public void LiveIterationVisitsExactlyTheLiveSet()
    {
        // Note: iteration order in a sparse-set-backed pool is dense order, which
        // is not the order of original allocation once any swap-and-pop has occurred.
        // We assert set equality + cardinality, not list equality.
        using var mesh = new HalfEdgeMesh();
        var handles = new VertexHandle[200];
        for (int i = 0; i < handles.Length; i++)
            handles[i] = mesh.Vertices.Allocate();

        // Free every other slot, plus a contiguous block.
        for (int i = 0; i < handles.Length; i += 2)
            mesh.Vertices.Free(handles[i]);
        for (int i = 50; i < 100; i++)
        {
            if (mesh.Vertices.IsAlive(handles[i]))
                mesh.Vertices.Free(handles[i]);
        }

        var visited = new List<VertexHandle>();
        foreach (var h in mesh.Vertices)
            visited.Add(h);

        Assert.Equal(mesh.Vertices.LiveCount, visited.Count);
        Assert.All(visited, h => Assert.True(mesh.Vertices.IsAlive(h)));

        var aliveSet = handles.Where(mesh.Vertices.IsAlive).ToHashSet();
        Assert.Equal(aliveSet, visited.ToHashSet());
    }

    [Fact]
    public void LiveIterationOnEmptyPoolYieldsNothing()
    {
        using var mesh = new HalfEdgeMesh();
        int count = 0;
        foreach (var _ in mesh.Vertices)
            count++;
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

    // --- Dense-storage / SwapRemove invariant tests --------------------------------

    [Fact]
    public void EraseMiddleSwapPopsLastEntryIntoFreedSlot()
    {
        using var mesh = new HalfEdgeMesh();
        var col = mesh.Vertices.RegisterNativeColumn<int>();

        var handles = new VertexHandle[10];
        for (int i = 0; i < handles.Length; i++)
        {
            handles[i] = mesh.Vertices.Allocate();
            col[mesh.Vertices.GetDenseIndex(handles[i])] = i + 1;
        }

        // Erase the middle entry; the entry that used to be last (value == 10)
        // must now occupy the freed dense slot.
        int middleDense = mesh.Vertices.GetDenseIndex(handles[4]);
        var lastHandle = handles[9];
        mesh.Vertices.Free(handles[4]);

        Assert.Equal(9, mesh.Vertices.LiveCount);
        Assert.Equal(9, col.Count);
        Assert.True(mesh.Vertices.IsAlive(lastHandle));
        Assert.Equal(middleDense, mesh.Vertices.GetDenseIndex(lastHandle));
        Assert.Equal(10, col[middleDense]);

        // Every still-live handle still reads the value last written via that handle.
        for (int i = 0; i < handles.Length; i++)
        {
            if (i == 4)
                continue;
            Assert.Equal(i + 1, col[mesh.Vertices.GetDenseIndex(handles[i])]);
        }
    }

    [Fact]
    public void EraseLastDoesNotMoveAnyOtherEntry()
    {
        using var mesh = new HalfEdgeMesh();
        var col = mesh.Vertices.RegisterNativeColumn<int>();

        var handles = new VertexHandle[6];
        var denseBefore = new int[handles.Length];
        for (int i = 0; i < handles.Length; i++)
        {
            handles[i] = mesh.Vertices.Allocate();
            col[mesh.Vertices.GetDenseIndex(handles[i])] = i + 1;
        }
        for (int i = 0; i < handles.Length; i++)
            denseBefore[i] = mesh.Vertices.GetDenseIndex(handles[i]);

        mesh.Vertices.Free(handles[^1]);

        for (int i = 0; i < handles.Length - 1; i++)
        {
            Assert.Equal(denseBefore[i], mesh.Vertices.GetDenseIndex(handles[i]));
            Assert.Equal(i + 1, col[mesh.Vertices.GetDenseIndex(handles[i])]);
        }
    }

    [Fact]
    public void RandomAllocFreePreservesPerHandleValueInvariant()
    {
        using var mesh = new HalfEdgeMesh();
        var col = mesh.Vertices.RegisterNativeColumn<long>();

        var rng = new Random(0xBEEF);
        var live = new Dictionary<VertexHandle, long>();
        long stamp = 1;

        for (int iter = 0; iter < 5_000; iter++)
        {
            // Bias slightly toward inserts so the pool keeps growing on average.
            if (live.Count == 0 || rng.NextDouble() < 0.55)
            {
                var v = mesh.Vertices.Allocate();
                long value = stamp++;
                col[mesh.Vertices.GetDenseIndex(v)] = value;
                live[v] = value;
            }
            else if (rng.NextDouble() < 0.5)
            {
                // Erase a random live handle.
                var pick = live.Keys.ElementAt(rng.Next(live.Count));
                live.Remove(pick);
                mesh.Vertices.Free(pick);
            }
            else
            {
                // Rewrite a random live handle's value.
                var pick = live.Keys.ElementAt(rng.Next(live.Count));
                long value = stamp++;
                col[mesh.Vertices.GetDenseIndex(pick)] = value;
                live[pick] = value;
            }

            // Invariant: every live handle still reads the value last written via it.
            // (Spot-check rather than full sweep every iteration for cost reasons.)
            if ((iter & 0xFF) == 0)
            {
                foreach (var (h, v) in live)
                    Assert.Equal(v, col[mesh.Vertices.GetDenseIndex(h)]);
            }
        }

        Assert.Equal(live.Count, mesh.Vertices.LiveCount);
        Assert.Equal(live.Count, col.Count);
        foreach (var (h, v) in live)
            Assert.Equal(v, col[mesh.Vertices.GetDenseIndex(h)]);
    }

    [Fact]
    public void LateRegisterColumnHasCountEqualToLiveCountAndZeroedEntries()
    {
        using var mesh = new HalfEdgeMesh();

        for (int i = 0; i < 13; i++)
            mesh.Vertices.Allocate();

        var col = mesh.Vertices.RegisterNativeColumn<long>();
        Assert.Equal(mesh.Vertices.LiveCount, col.Count);
        for (int i = 0; i < col.Count; i++)
            Assert.Equal(0L, col[i]);
    }

    [Fact]
    public void GetComponentReadAndWriteRoundTrip()
    {
        using var mesh = new HalfEdgeMesh();
        var col = mesh.Vertices.RegisterNativeColumn<Vector3>();

        var v = mesh.Vertices.Allocate();
        col[mesh.Vertices.GetDenseIndex(v)] = new Vector3(1, 2, 3);
        Assert.Equal(new Vector3(1, 2, 3), mesh.Vertices.GetComponent<Vector3>(v));
    }
}
