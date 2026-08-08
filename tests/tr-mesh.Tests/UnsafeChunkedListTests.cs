using TRMesh.Containers;

namespace tr_mesh.Tests;

public unsafe class UnsafeChunkedListTests
{
    [Fact]
    public void Constructor_ZeroCapacity_LeavesUnallocated()
    {
        using var list = new UnsafeChunkedList<int>();

        Assert.Equal(0, list.Capacity);
    }

    [Fact]
    public void Constructor_WithCapacity_AllocatesGeometricChunks()
    {
        using var list = new UnsafeChunkedList<int>(40);

        // Chunks are 32, 64, 128, …; 40 requires the first two.
        Assert.Equal(96, list.Capacity);
    }

    [Fact]
    public void Constructor_ExactChunkBoundary_DoesNotOverAllocate()
    {
        using var list = new UnsafeChunkedList<int>(32);

        Assert.Equal(32, list.Capacity);
    }

    [Fact]
    public void Indexer_AutoGrowsFromEmpty_AndZeroInitializes()
    {
        using var list = new UnsafeChunkedList<int>();

        Assert.Equal(0, list[0]);
        Assert.Equal(32, list.Capacity);

        for (var i = 0; i < list.Capacity; i++)
            Assert.Equal(0, list[i]);
    }

    [Fact]
    public void Indexer_WritesAndReadsBack()
    {
        using var list = new UnsafeChunkedList<int>();

        list[0] = 11;
        list[15] = 22;
        list[31] = 33;

        Assert.Equal(11, list[0]);
        Assert.Equal(22, list[15]);
        Assert.Equal(33, list[31]);
        Assert.Equal(32, list.Capacity);
    }

    [Fact]
    public void Indexer_GrowsAcrossChunkBoundaries()
    {
        using var list = new UnsafeChunkedList<int>();

        list[31] = 31;
        Assert.Equal(32, list.Capacity);

        list[32] = 32;
        Assert.Equal(96, list.Capacity);

        list[63] = 63;
        Assert.Equal(96, list.Capacity);

        list[64] = 64;
        Assert.Equal(96, list.Capacity);

        list[128] = 128;
        Assert.Equal(224, list.Capacity);

        list[256] = 256;
        Assert.Equal(480, list.Capacity);

        Assert.Equal(31, list[31]);
        Assert.Equal(32, list[32]);
        Assert.Equal(63, list[63]);
        Assert.Equal(64, list[64]);
        Assert.Equal(128, list[128]);
        Assert.Equal(256, list[256]);
    }

    [Fact]
    public void Indexer_NewSlotsAreZeroInitialized_WhenGrowing()
    {
        using var list = new UnsafeChunkedList<int>();
        list[0] = 7;

        _ = list[100];

        Assert.Equal(224, list.Capacity);
        Assert.Equal(7, list[0]);
        for (var i = 1; i < list.Capacity; i++)
            Assert.Equal(0, list[i]);
    }

    [Fact]
    public void Indexer_RefWrite_MutatesInPlace()
    {
        using var list = new UnsafeChunkedList<int>();
        list[5] = 1;

        ref var value = ref list[5];
        value = 99;

        Assert.Equal(99, list[5]);
    }

    [Fact]
    public void SetCapacity_GrowsToCoverRequestedSize()
    {
        using var list = new UnsafeChunkedList<int>();

        list.SetCapacity(1);
        Assert.Equal(32, list.Capacity);

        list.SetCapacity(33);
        Assert.Equal(96, list.Capacity);

        list.SetCapacity(200);
        Assert.Equal(224, list.Capacity);
    }

    [Fact]
    public void SetCapacity_WhenAlreadyLargeEnough_IsNoOp()
    {
        using var list = new UnsafeChunkedList<int>(100);
        var capacity = list.Capacity;

        list.SetCapacity(50);
        list.SetCapacity(capacity);

        Assert.Equal(capacity, list.Capacity);
    }

    [Fact]
    public void SetCapacity_DoesNotClearExistingValues()
    {
        using var list = new UnsafeChunkedList<int>();
        list[10] = 42;

        list.SetCapacity(100);

        Assert.Equal(42, list[10]);
        Assert.True(list.Capacity >= 100);
    }

    [Fact]
    public void SetCapacity_PreservesValues_AcrossLaterGrowth()
    {
        using var list = new UnsafeChunkedList<int>();

        // Fill the first two chunks.
        for (var i = 0; i < 96; i++)
            list[i] = i;

        list.SetCapacity(200);

        for (var i = 0; i < 96; i++)
            Assert.Equal(i, list[i]);
        for (var i = 96; i < list.Capacity; i++)
            Assert.Equal(0, list[i]);
    }

    [Fact]
    public void Enumerate_YieldsAllCapacitySlots()
    {
        using var list = new UnsafeChunkedList<int>(40);
        for (var i = 0; i < list.Capacity; i++)
            list[i] = i;

        var values = new List<int>();
        foreach (ref int value in list)
            values.Add(value);

        Assert.Equal(list.Capacity, values.Count);
        Assert.Equal(Enumerable.Range(0, list.Capacity), values);
    }

    [Fact]
    public void Enumerate_Empty_YieldsNothing()
    {
        using var list = new UnsafeChunkedList<int>();
        var count = 0;

        foreach (ref int _ in list)
            count++;

        Assert.Equal(0, count);
    }

    [Fact]
    public void Dispose_IsSafeOnUnallocated()
    {
        var list = new UnsafeChunkedList<int>();
        list.Dispose();
        Assert.Equal(0, list.Capacity);
    }

    [Fact]
    public void Dispose_ReleasesAllocatedChunks()
    {
        var list = new UnsafeChunkedList<int>();
        _ = list[100];
        Assert.True(list.Capacity > 0);

        list.Dispose();
        Assert.Equal(0, list.Capacity);
    }

    [Theory]
    [InlineData(0, 32)]
    [InlineData(31, 32)]
    [InlineData(32, 96)]
    [InlineData(95, 96)]
    [InlineData(96, 224)]
    [InlineData(223, 224)]
    [InlineData(224, 480)]
    [InlineData(479, 480)]
    [InlineData(480, 992)]
    [InlineData(991, 992)]
    [InlineData(992, 2016)]
    public void Indexer_CapacityFollowsGeometricTable(int index, int expectedCapacity)
    {
        using var list = new UnsafeChunkedList<int>();

        list[index] = index;

        Assert.Equal(expectedCapacity, list.Capacity);
        Assert.Equal(index, list[index]);
    }

    [Fact]
    public void Indexer_RoundTrip_DenseAcrossSeveralChunks()
    {
        using var list = new UnsafeChunkedList<int>();
        const int n = 300;

        for (var i = 0; i < n; i++)
            list[i] = i * 3;

        Assert.Equal(480, list.Capacity);

        for (var i = 0; i < n; i++)
            Assert.Equal(i * 3, list[i]);
        for (var i = n; i < list.Capacity; i++)
            Assert.Equal(0, list[i]);
    }

    [Fact]
    public void Constructor_Default_MaxChunkSizeIsUncapped()
    {
        using var list = new UnsafeChunkedList<int>();

        Assert.Equal(0, list.MaxChunkSize);
    }

    [Fact]
    public void DefaultValue_UsesUncappedResolution()
    {
        UnsafeChunkedList<int> list = default;
        try
        {
            list[0] = 10;
            list[32] = 20;
            list[64] = 30;

            Assert.Equal(10, list[0]);
            Assert.Equal(20, list[32]);
            Assert.Equal(30, list[64]);
            Assert.Equal(96, list.Capacity);
        }
        finally
        {
            list.Dispose();
        }
    }

    [Fact]
    public void Constructor_WithMaxChunkSize_ExposesCap()
    {
        using var list = new UnsafeChunkedList<int>(maxChunkSize: 64);

        Assert.Equal(64, list.MaxChunkSize);
    }

    [Fact]
    public void MaxChunkSize_CapsGeometricGrowth()
    {
        using var list = new UnsafeChunkedList<int>(maxChunkSize: 64);

        // Capped chunks: 32, 64, 64, 64, …
        // Cumulative capacities: 32, 96, 160, 224, …
        list[0] = 0;
        Assert.Equal(32, list.Capacity);

        list[32] = 32;
        Assert.Equal(96, list.Capacity);

        list[64] = 64;
        Assert.Equal(96, list.Capacity);

        list[128] = 128;
        Assert.Equal(160, list.Capacity);

        list[192] = 192;
        Assert.Equal(224, list.Capacity);

        Assert.Equal(0, list[0]);
        Assert.Equal(32, list[32]);
        Assert.Equal(64, list[64]);
        Assert.Equal(128, list[128]);
        Assert.Equal(192, list[192]);
    }

    [Fact]
    public void MaxChunkSize_EqualToBase_AllChunksAreBaseSize()
    {
        using var list = new UnsafeChunkedList<int>(maxChunkSize: 32);

        list[0] = 1;
        Assert.Equal(32, list.Capacity);

        list[32] = 2;
        Assert.Equal(64, list.Capacity);

        list[64] = 3;
        Assert.Equal(96, list.Capacity);

        list[95] = 4;
        Assert.Equal(96, list.Capacity);

        Assert.Equal(1, list[0]);
        Assert.Equal(2, list[32]);
        Assert.Equal(3, list[64]);
        Assert.Equal(4, list[95]);
    }

    [Fact]
    public void MaxChunkSize_PowerOfTwo_LargerCap_FollowsGeometricThenFlats()
    {
        using var list = new UnsafeChunkedList<int>(maxChunkSize: 128);

        // Chunks are 32, 64, 128, then flat 128s.
        list.SetCapacity(200);
        Assert.Equal(224, list.Capacity);

        list.SetCapacity(225);
        Assert.Equal(352, list.Capacity);

        list.SetCapacity(353);
        Assert.Equal(480, list.Capacity);

        list[400] = 400;
        Assert.Equal(400, list[400]);
        Assert.Equal(0, list[401]);
    }

    [Fact]
    public void MaxChunkSize_PreservesValues_AcrossCappedChunks()
    {
        using var list = new UnsafeChunkedList<int>(maxChunkSize: 64);
        const int n = 250;

        for (var i = 0; i < n; i++)
            list[i] = i * 7;

        Assert.Equal(288, list.Capacity);

        for (var i = 0; i < n; i++)
            Assert.Equal(i * 7, list[i]);
        for (var i = n; i < list.Capacity; i++)
            Assert.Equal(0, list[i]);
    }

    [Fact]
    public void MaxChunkSize_WithInitialCapacity_AllocatesCappedLayout()
    {
        using var list = new UnsafeChunkedList<int>(150, maxChunkSize: 64);

        // 32 + 64 + 64 = 160
        Assert.Equal(160, list.Capacity);
        Assert.Equal(64, list.MaxChunkSize);
    }

    [Fact]
    public void MaxChunkSize_Enumerate_YieldsAllCapacitySlots()
    {
        using var list = new UnsafeChunkedList<int>(100, maxChunkSize: 64);
        for (var i = 0; i < list.Capacity; i++)
            list[i] = i;

        var values = new List<int>();
        foreach (ref int value in list)
            values.Add(value);

        Assert.Equal(list.Capacity, values.Count);
        Assert.Equal(Enumerable.Range(0, list.Capacity), values);
    }
}
