namespace TREditorSharp.Tests;

using System.Collections;
using System.Reflection;
using TREditorSharp.Storage;

public class SparseSetTests
{
    [Fact]
    public void NewSetIsEmpty()
    {
        var set = new SparseSet<VertexTag>();
        Assert.Equal(0, set.Count);
        Assert.True(set.IsEmpty);
    }

    [Fact]
    public void SparseEntryRemainsTwoIntegersWide()
    {
        Assert.Equal(sizeof(int) * 2, SparseSet<VertexTag>.SparseEntrySize);
    }

    [Fact]
    public void InsertReturnsNonNullHandleAndIncrementsCount()
    {
        var set = new SparseSet<VertexTag>();
        var h = set.Insert();

        Assert.False(h.IsNull);
        Assert.Equal(1, set.Count);
        Assert.False(set.IsEmpty);
        Assert.True(set.Contains(h));
    }

    [Fact]
    public void DefaultHandleIsNotContainedEvenWhenSetIsEmpty()
    {
        var set = new SparseSet<VertexTag>();
        Assert.False(set.Contains(default));
    }

    [Fact]
    public void DefaultHandleIsNotContainedAfterInsertions()
    {
        var set = new SparseSet<VertexTag>();
        set.Insert();
        set.Insert();
        set.Insert();
        Assert.False(set.Contains(default));
    }

    [Fact]
    public void GetDenseIndexReturnsInsertionOrderInitially()
    {
        var set = new SparseSet<VertexTag>();
        var h0 = set.Insert();
        var h1 = set.Insert();
        var h2 = set.Insert();

        Assert.Equal(0, set.GetDenseIndex(h0));
        Assert.Equal(1, set.GetDenseIndex(h1));
        Assert.Equal(2, set.GetDenseIndex(h2));
    }

    [Fact]
    public void GetDenseIndexThrowsForUncontainedHandle()
    {
        var set = new SparseSet<VertexTag>();
        Assert.Throws<KeyNotFoundException>(() => set.GetDenseIndex(default));
    }

    [Fact]
    public void TryGetDenseIndexReturnsFalseForUncontainedHandle()
    {
        var set = new SparseSet<VertexTag>();
        Assert.False(set.TryGetDenseIndex(default, out int idx));
        Assert.Equal(-1, idx);
    }

    [Fact]
    public void TryGetDenseIndexReturnsTrueAndCorrectIndexForLiveHandle()
    {
        var set = new SparseSet<VertexTag>();
        var h = set.Insert();
        set.Insert();
        Assert.True(set.TryGetDenseIndex(h, out int idx));
        Assert.Equal(0, idx);
    }

    [Fact]
    public void EraseRemovesHandleAndDecrementsCount()
    {
        var set = new SparseSet<VertexTag>();
        var h = set.Insert();
        Assert.True(set.Erase(h));
        Assert.Equal(0, set.Count);
        Assert.True(set.IsEmpty);
        Assert.False(set.Contains(h));
    }

    [Fact]
    public void EraseReturnsFalseForUncontainedHandle()
    {
        var set = new SparseSet<VertexTag>();
        Assert.False(set.Erase(default));

        var h = set.Insert();
        set.Erase(h);
        Assert.False(set.Erase(h));
    }

    [Fact]
    public void ReserveMakesHandleNonLiveWithoutChangingGeneration()
    {
        var set = new SparseSet<VertexTag>();
        VertexHandle handle = set.Insert();

        Assert.True(set.Reserve(handle));

        Assert.False(set.Contains(handle));
        Assert.True(set.IsReserved(handle));
        Assert.Equal(0, set.Count);
        Assert.False(set.TryGetDenseIndex(handle, out _));
    }

    [Fact]
    public void ReservedSlotIsNotReusedByInsert()
    {
        var set = new SparseSet<VertexTag>();
        VertexHandle reserved = set.Insert();
        Assert.True(set.Reserve(reserved));

        VertexHandle inserted = set.Insert();

        Assert.NotEqual(reserved.Index, inserted.Index);
        Assert.True(set.IsReserved(reserved));
        Assert.True(set.Contains(inserted));
    }

    [Fact]
    public void RestoreReservedResurrectsExactHandleAtNewDenseIndex()
    {
        var set = new SparseSet<VertexTag>();
        VertexHandle first = set.Insert();
        VertexHandle reserved = set.Insert();
        VertexHandle last = set.Insert();
        Assert.True(set.Reserve(reserved));
        Assert.Equal(1, set.GetDenseIndex(last));

        Assert.True(set.RestoreReserved(reserved));

        Assert.True(set.Contains(reserved));
        Assert.False(set.IsReserved(reserved));
        Assert.Equal(2, set.GetDenseIndex(reserved));
        Assert.Equal(first, set.HandleAtDense(0));
        Assert.Equal(last, set.HandleAtDense(1));
        Assert.Equal(reserved, set.HandleAtDense(2));
    }

    [Fact]
    public void ReleaseReservedInvalidatesHandleAndMakesSlotReusable()
    {
        var set = new SparseSet<VertexTag>();
        VertexHandle reserved = set.Insert();
        Assert.True(set.Reserve(reserved));

        Assert.True(set.ReleaseReserved(reserved));
        VertexHandle replacement = set.Insert();

        Assert.Equal(reserved.Index, replacement.Index);
        Assert.NotEqual(reserved.Generation, replacement.Generation);
        Assert.False(set.Contains(reserved));
        Assert.False(set.IsReserved(reserved));
        Assert.True(set.Contains(replacement));
    }

    [Fact]
    public void ReleasedSlotCannotAppearLiveBeforeReuse()
    {
        var set = new SparseSet<VertexTag>();
        VertexHandle reserved = set.Insert();
        Assert.True(set.Reserve(reserved));
        Assert.True(set.ReleaseReserved(reserved));
        VertexHandle forgedCurrentGeneration = new(reserved.Index, reserved.Generation + 1);

        Assert.False(set.Contains(forgedCurrentGeneration));
    }

    [Fact]
    public void ReservedLifecycleRejectsInvalidStateTransitions()
    {
        var set = new SparseSet<VertexTag>();
        VertexHandle live = set.Insert();

        Assert.False(set.RestoreReserved(live));
        Assert.False(set.ReleaseReserved(live));
        Assert.True(set.Reserve(live));
        Assert.False(set.Reserve(live));
        Assert.True(set.RestoreReserved(live));
        Assert.False(set.RestoreReserved(live));
        Assert.False(set.ReleaseReserved(live));
    }

    [Fact]
    public void ReleaseReservedAtGenerationLimitRetiresSlot()
    {
        var set = new SparseSet<VertexTag>();
        VertexHandle original = set.Insert();
        VertexHandle finalGeneration = SetGeneration(set, original, int.MaxValue - 1);
        Assert.True(set.Reserve(finalGeneration));

        Assert.True(set.ReleaseReserved(finalGeneration));
        VertexHandle inserted = set.Insert();

        Assert.NotEqual(finalGeneration.Index, inserted.Index);
        Assert.False(set.Contains(finalGeneration));
        Assert.False(set.IsReserved(finalGeneration));
    }

    [Fact]
    public void EraseMiddleSwapAndPopUpdatesDenseIndices()
    {
        var set = new SparseSet<VertexTag>();
        var h0 = set.Insert();
        var h1 = set.Insert();
        var h2 = set.Insert();

        Assert.True(set.Erase(h1));

        Assert.Equal(2, set.Count);
        Assert.True(set.Contains(h0));
        Assert.False(set.Contains(h1));
        Assert.True(set.Contains(h2));

        Assert.Equal(0, set.GetDenseIndex(h0));
        Assert.Equal(1, set.GetDenseIndex(h2));
        Assert.Equal(h2, set.HandleAtDense(1));
    }

    [Fact]
    public void EraseLastDoesNotSwap()
    {
        var set = new SparseSet<VertexTag>();
        var h0 = set.Insert();
        var h1 = set.Insert();

        Assert.True(set.Erase(h1));

        Assert.Equal(1, set.Count);
        Assert.True(set.Contains(h0));
        Assert.False(set.Contains(h1));
        Assert.Equal(0, set.GetDenseIndex(h0));
        Assert.Equal(h0, set.HandleAtDense(0));
    }

    [Fact]
    public void HandleAtDenseRoundTripsWithGetDenseIndex()
    {
        var set = new SparseSet<VertexTag>();
        for (int i = 0; i < 32; i++)
            set.Insert();

        for (int i = 0; i < set.Count; i++)
        {
            var h = set.HandleAtDense(i);
            Assert.Equal(i, set.GetDenseIndex(h));
        }
    }

    [Fact]
    public void HandleAtDenseThrowsForOutOfRange()
    {
        var set = new SparseSet<VertexTag>();
        Assert.Throws<ArgumentOutOfRangeException>(() => set.HandleAtDense(0));

        set.Insert();
        Assert.Throws<ArgumentOutOfRangeException>(() => set.HandleAtDense(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => set.HandleAtDense(-1));
    }

    [Fact]
    public void StaleHandleAfterReinsertIsNotContained()
    {
        var set = new SparseSet<VertexTag>();
        var h0 = set.Insert();
        Assert.True(set.Erase(h0));

        var h1 = set.Insert();
        Assert.Equal(h0.Index, h1.Index);
        Assert.NotEqual(h0.Generation, h1.Generation);
        Assert.True(h1.Generation > h0.Generation);

        Assert.False(set.Contains(h0));
        Assert.True(set.Contains(h1));
    }

    [Fact]
    public void ClearEmptiesSetAndInvalidatesAllHandles()
    {
        var set = new SparseSet<VertexTag>();
        var handles = new List<VertexHandle>();
        for (int i = 0; i < 10; i++)
            handles.Add(set.Insert());

        set.Clear();

        Assert.Equal(0, set.Count);
        Assert.True(set.IsEmpty);
        foreach (var h in handles)
            Assert.False(set.Contains(h));
    }

    [Fact]
    public void ForeachYieldsLiveHandlesInDenseOrder()
    {
        var set = new SparseSet<VertexTag>();
        var inserted = new List<VertexHandle>();
        for (int i = 0; i < 16; i++)
            inserted.Add(set.Insert());

        var visited = new List<VertexHandle>();
        foreach (var h in set)
            visited.Add(h);

        Assert.Equal(inserted.Count, visited.Count);
        for (int i = 0; i < visited.Count; i++)
        {
            Assert.Equal(set.HandleAtDense(i), visited[i]);
        }
    }

    [Fact]
    public void LivePropertyYieldsSameOrderAsGetEnumerator()
    {
        var set = new SparseSet<VertexTag>();
        for (int i = 0; i < 8; i++)
            set.Insert();

        var direct = new List<VertexHandle>();
        foreach (var h in set)
            direct.Add(h);

        var live = new List<VertexHandle>();
        foreach (var h in set.Live)
            live.Add(h);

        Assert.Equal(direct, live);
    }

    [Fact]
    public void ForeachYieldsRemainingHandlesAfterErases()
    {
        var set = new SparseSet<VertexTag>();
        var h0 = set.Insert();
        var h1 = set.Insert();
        var h2 = set.Insert();
        var h3 = set.Insert();

        set.Erase(h1);

        var visited = new List<VertexHandle>();
        foreach (var h in set)
            visited.Add(h);

        Assert.Equal(3, visited.Count);
        Assert.Contains(h0, visited);
        Assert.Contains(h2, visited);
        Assert.Contains(h3, visited);
        Assert.DoesNotContain(h1, visited);

        for (int i = 0; i < visited.Count; i++)
            Assert.Equal(i, set.GetDenseIndex(visited[i]));
    }

    [Fact]
    public void ForeachOnEmptySetYieldsNothing()
    {
        var set = new SparseSet<VertexTag>();
        int count = 0;
        foreach (var _ in set)
            count++;
        Assert.Equal(0, count);
    }

    [Fact]
    public void ManyRandomInsertEraseCyclesPreserveInvariants()
    {
        var set = new SparseSet<VertexTag>();
        var live = new HashSet<VertexHandle>();
        var rng = new Random(1234);

        for (int iter = 0; iter < 5_000; iter++)
        {
            if (live.Count == 0 || rng.NextDouble() < 0.6)
            {
                var h = set.Insert();
                Assert.True(live.Add(h));
                Assert.True(set.Contains(h));
            }
            else
            {
                var pick = live.ElementAt(rng.Next(live.Count));
                live.Remove(pick);
                Assert.True(set.Erase(pick));
                Assert.False(set.Contains(pick));
            }

            Assert.Equal(live.Count, set.Count);

            // Round-trip invariant: HandleAtDense(i) -> GetDenseIndex == i for every dense slot.
            for (int i = 0; i < set.Count; i++)
            {
                var h = set.HandleAtDense(i);
                Assert.Equal(i, set.GetDenseIndex(h));
                Assert.Contains(h, live);
            }
        }

        // Final pass: foreach order matches dense indexing.
        var visited = new List<VertexHandle>();
        foreach (var h in set)
            visited.Add(h);

        Assert.Equal(live.Count, visited.Count);
        for (int i = 0; i < visited.Count; i++)
            Assert.Equal(i, set.GetDenseIndex(visited[i]));
    }

    private static VertexHandle SetGeneration(
        SparseSet<VertexTag> set,
        VertexHandle handle,
        int generation
    )
    {
        FieldInfo sparseField =
            typeof(SparseSet<VertexTag>).GetField(
                "_sparse",
                BindingFlags.Instance | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException("SparseSet._sparse was not found.");
        IList sparse = (IList)(
            sparseField.GetValue(set)
            ?? throw new InvalidOperationException("SparseSet._sparse was null.")
        );
        object entry =
            sparse[handle.Index]
            ?? throw new InvalidOperationException("SparseSet entry was null.");
        FieldInfo versionField =
            entry.GetType().GetField("Version") ?? throw new InvalidOperationException(
                "SparseEntry.Version was not found."
            );
        versionField.SetValue(entry, generation);
        sparse[handle.Index] = entry;
        return new VertexHandle(handle.Index, generation);
    }
}
