using TRMesh.Containers;

namespace tr_mesh.Tests;

public unsafe class UnsafeColonyTests
{
    [Fact]
    public void Constructor_Empty_HasNoActiveElements()
    {
        using var colony = new UnsafeColony<int>();

        Assert.Equal(0, colony.Count);
        Assert.Equal(0, colony.Capacity);
        Assert.True(colony.IsEmpty);
    }

    [Fact]
    public void Constructor_WithCapacity_Uses32768ElementChunkLimit()
    {
        using var colony = new UnsafeColony<int>(65_505);

        // Geometric prefix capacity is 65,504; the next chunk contributes 32,768.
        Assert.Equal(98_272, colony.Capacity);
        Assert.Equal(0, colony.Count);
    }

    [Fact]
    public void Insert_ReturnsStableSequentialSlots()
    {
        using var colony = new UnsafeColony<int>();

        var first = colony.Insert(10);
        var second = colony.Insert(20);
        var third = colony.Insert(30);

        Assert.Equal(0, first);
        Assert.Equal(1, second);
        Assert.Equal(2, third);
        Assert.Equal(3, colony.Count);
        Assert.False(colony.IsEmpty);
        Assert.Equal(10, colony[first]);
        Assert.Equal(20, colony[second]);
        Assert.Equal(30, colony[third]);
    }

    [Fact]
    public void Insert_ReferenceRemainsStableAcrossChunkGrowth()
    {
        using var colony = new UnsafeColony<int>(65_504);
        var first = colony.Insert(42);

        int* addressBefore;
        fixed (int* address = &colony[first])
            addressBefore = address;

        for (var i = 1; i <= 65_504; i++)
            colony.Insert(i);

        int* addressAfter;
        fixed (int* address = &colony[first])
            addressAfter = address;

        Assert.True(addressBefore == addressAfter);
        Assert.Equal(42, *addressBefore);
        Assert.Equal(65_505, colony.Count);
        Assert.Equal(98_272, colony.Capacity);
    }

    [Fact]
    public void IsActive_RejectsSlotsOutsideLogicalExtent()
    {
        using var colony = new UnsafeColony<int>(100);
        var slot = colony.Insert(7);

        Assert.True(colony.IsActive(slot));
        Assert.False(colony.IsActive(-1));
        Assert.False(colony.IsActive(slot + 1));
        Assert.False(colony.IsActive(colony.Capacity - 1));
    }

    [Fact]
    public void Clear_InvalidatesSlotsAndRetainsCapacity()
    {
        using var colony = new UnsafeColony<int>(100);
        var oldSlot = colony.Insert(7);
        var capacity = colony.Capacity;

        colony.Clear();

        Assert.Equal(0, colony.Count);
        Assert.True(colony.IsEmpty);
        Assert.Equal(capacity, colony.Capacity);
        Assert.False(colony.IsActive(oldSlot));

        var newSlot = colony.Insert(9);
        Assert.Equal(0, newSlot);
        Assert.Equal(9, colony[newSlot]);
    }

    [Fact]
    public void RemoveAt_ExtendsSkipBlockToTheRight()
    {
        using var colony = CreateColony(4);

        colony.RemoveAt(2);
        colony.RemoveAt(1);

        Assert.Equal(1, colony.Insert(10));
        Assert.Equal(2, colony.Insert(20));
        Assert.Equal(4, colony.Count);
    }

    [Fact]
    public void RemoveAt_ExtendsSkipBlockToTheLeft()
    {
        using var colony = CreateColony(4);

        colony.RemoveAt(1);
        colony.RemoveAt(2);

        Assert.Equal(1, colony.Insert(10));
        Assert.Equal(2, colony.Insert(20));
        Assert.Equal(4, colony.Count);
    }

    [Fact]
    public void RemoveAt_MergesAdjacentSkipBlocks()
    {
        using var colony = CreateColony(5);

        colony.RemoveAt(1);
        colony.RemoveAt(3);
        colony.RemoveAt(2);

        Assert.Equal(1, colony.Insert(10));
        Assert.Equal(2, colony.Insert(20));
        Assert.Equal(3, colony.Insert(30));
        Assert.Equal(5, colony.Count);
    }

    [Fact]
    public void FreeList_RelocatesANonHeadBlock()
    {
        using var colony = CreateColony(8);

        colony.RemoveAt(5);
        colony.RemoveAt(1);
        colony.RemoveAt(4);

        Assert.Equal(1, colony.Insert(10));
        Assert.Equal(4, colony.Insert(20));
        Assert.Equal(5, colony.Insert(30));
    }

    [Fact]
    public void FourByteFreeNodes_SupportByteElements()
    {
        using var colony = new UnsafeColony<byte>();
        for (var i = 0; i < 40; i++)
            colony.Insert((byte)i);

        colony.RemoveAt(5);
        colony.RemoveAt(1);
        colony.RemoveAt(4);
        colony.RemoveAt(32);

        Assert.Equal(32, colony.Insert(100));
        Assert.Equal(1, colony.Insert(101));
        Assert.Equal(4, colony.Insert(102));
        Assert.Equal(5, colony.Insert(103));
        Assert.Equal((byte)100, colony[32]);
        Assert.Equal((byte)101, colony[1]);
        Assert.Equal((byte)102, colony[4]);
        Assert.Equal((byte)103, colony[5]);
    }

    [Fact]
    public void Clear_DiscardsSkipBlocksAndRestartsDenseInsertion()
    {
        using var colony = CreateColony(10);
        colony.RemoveAt(3);
        colony.RemoveAt(7);

        colony.Clear();

        Assert.Equal(0, colony.Insert(100));
        Assert.Equal(1, colony.Insert(200));
        Assert.Equal(2, colony.Count);
    }

    [Fact]
    public void IntrusiveFreeLists_RemainConsistentUnderRandomReuse()
    {
        using var colony = new UnsafeColony<int>();
        var random = new Random(12_345);
        var active = new Dictionary<int, int>();
        var greatestSlot = -1;

        for (var step = 0; step < 10_000; step++)
        {
            if (active.Count != 0 && (active.Count > 300 || random.Next(2) == 0))
            {
                var pair = active.ElementAt(random.Next(active.Count));
                colony.RemoveAt(pair.Key);
                active.Remove(pair.Key);
            }
            else
            {
                var slot = colony.Insert(step);
                Assert.DoesNotContain(slot, active.Keys);
                active.Add(slot, step);
                greatestSlot = Math.Max(greatestSlot, slot);
            }

            if (step % 100 != 0)
                continue;

            Assert.Equal(active.Count, colony.Count);
            for (var slot = 0; slot <= greatestSlot; slot++)
            {
                Assert.Equal(active.ContainsKey(slot), colony.IsActive(slot));
                if (active.TryGetValue(slot, out var expected))
                    Assert.Equal(expected, colony[slot]);
            }

            var enumeratedValues = new List<int>();
            foreach (ref int value in colony)
                enumeratedValues.Add(value);

            Assert.Equal(active.OrderBy(pair => pair.Key).Select(pair => pair.Value), enumeratedValues);
        }
    }

    [Fact]
    public void Enumerate_YieldsActiveElementsInSlotOrder()
    {
        using var colony = CreateColony(6);
        colony.RemoveAt(1);
        colony.RemoveAt(2);
        colony.RemoveAt(4);

        var values = new List<int>();
        foreach (ref int value in colony)
            values.Add(value);

        Assert.Equal(new[] { 0, 3, 5 }, values);
    }

    [Fact]
    public void Enumerator_CurrentSlotTracksCurrentElementAcrossErasedRanges()
    {
        using var colony = CreateColony(40);
        for (var slot = 1; slot < 32; slot++)
            colony.RemoveAt(slot);
        for (var slot = 33; slot < 39; slot++)
            colony.RemoveAt(slot);

        var entries = new List<(int Slot, int Value)>();
        var enumerator = colony.GetEnumerator();
        while (enumerator.MoveNext())
            entries.Add((enumerator.CurrentSlot, enumerator.Current));

        Assert.Equal(new[] { (0, 0), (32, 32), (39, 39) }, entries);
    }

    [Fact]
    public void Enumerate_JumpsOverErasedBlockWithinCurrentChunk()
    {
        using var colony = CreateColony(10);
        colony.RemoveAt(0);
        for (var slot = 2; slot <= 6; slot++)
            colony.RemoveAt(slot);
        colony.RemoveAt(8);
        colony.RemoveAt(9);

        var values = new List<int>();
        foreach (ref int value in colony)
            values.Add(value);

        Assert.Equal(new[] { 1, 7 }, values);
    }

    [Fact]
    public void Enumerate_JumpsFromTrailingErasedBlockIntoNextActiveChunk()
    {
        using var colony = CreateColony(40);
        for (var slot = 0; slot <= 27; slot++)
            colony.RemoveAt(slot);
        for (var slot = 29; slot <= 31; slot++)
            colony.RemoveAt(slot);
        for (var slot = 33; slot < 40; slot++)
            colony.RemoveAt(slot);

        var values = new List<int>();
        foreach (ref int value in colony)
            values.Add(value);

        // The first chunk ends at slot 32; slot 32 starts the next chunk.
        Assert.Equal(new[] { 28, 32 }, values);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Enumerate_JumpsPastFullyErasedChunksIntoNextActiveChunk(
        int fullyErasedChunkCount
    )
    {
        var nextActiveSlot = 32 * ((1 << (fullyErasedChunkCount + 1)) - 1);
        using var colony = CreateColony(nextActiveSlot + 1);
        for (var slot = 0; slot <= 27; slot++)
            colony.RemoveAt(slot);
        for (var slot = 29; slot < nextActiveSlot; slot++)
            colony.RemoveAt(slot);

        var values = new List<int>();
        foreach (ref int value in colony)
            values.Add(value);

        Assert.Equal(new[] { 28, nextActiveSlot }, values);
    }

    [Fact]
    public void Enumerate_ExposesElementsByReference()
    {
        using var colony = CreateColony(4);
        colony.RemoveAt(1);

        foreach (ref int value in colony)
            value *= 10;

        Assert.Equal(0, colony[0]);
        Assert.Equal(20, colony[2]);
        Assert.Equal(30, colony[3]);
    }

    [Fact]
    public void Enumerate_EmptyColony_YieldsNothing()
    {
        using var colony = new UnsafeColony<int>();
        var count = 0;

        foreach (ref int _ in colony)
            count++;

        Assert.Equal(0, count);
    }

    [Fact]
    public void Enumerate_AllElementsRemovedAcrossChunks_YieldsNothing()
    {
        using var colony = CreateColony(96);
        for (var slot = 0; slot < 96; slot++)
            colony.RemoveAt(slot);

        var count = 0;
        foreach (ref int _ in colony)
            count++;

        Assert.Equal(0, count);
    }

    [Fact]
    public void Enumerate_SkipsConsecutiveFullyErasedChunks()
    {
        using var colony = CreateColony(97);
        for (var slot = 0; slot < 96; slot++)
            colony.RemoveAt(slot);

        var values = new List<int>();
        foreach (ref int value in colony)
            values.Add(value);

        Assert.Equal(new[] { 96 }, values);
    }

    [Fact]
    public void Enumerator_TerminalSentinelSupportsExactCapacityAndRepeatedEndChecks()
    {
        using var colony = CreateColony(32);
        var enumerator = colony.GetEnumerator();

        var count = 0;
        while (enumerator.MoveNext())
            count++;

        Assert.Equal(32, count);
        Assert.False(enumerator.MoveNext());
    }

    [Fact]
    public void Enumerate_AfterClearWithErasedFirstSlot_YieldsNothing()
    {
        using var colony = CreateColony(4);
        colony.RemoveAt(0);

        colony.Clear();

        var count = 0;
        foreach (ref int _ in colony)
            count++;

        Assert.Equal(0, count);
    }

    [Fact]
    public void DefaultValue_CanInsertAndDispose()
    {
        UnsafeColony<int> colony = default;
        try
        {
            var slot = colony.Insert(123);

            Assert.Equal(0, slot);
            Assert.Equal(123, colony[slot]);
            Assert.True(colony.IsActive(slot));
        }
        finally
        {
            colony.Dispose();
        }
    }

    [Fact]
    public void Dispose_ResetsVisibleState()
    {
        var colony = new UnsafeColony<int>();
        colony.Insert(1);

        colony.Dispose();

        Assert.Equal(0, colony.Count);
        Assert.Equal(0, colony.Capacity);
        Assert.True(colony.IsEmpty);
    }

    static UnsafeColony<int> CreateColony(int count)
    {
        var colony = new UnsafeColony<int>(count);
        for (var i = 0; i < count; i++)
            colony.Insert(i);
        return colony;
    }
}
