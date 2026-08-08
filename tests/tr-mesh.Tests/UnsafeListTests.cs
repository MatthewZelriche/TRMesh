using TRMesh.Containers;

namespace tr_mesh.Tests;

public unsafe class UnsafeListTests
{
    [Fact]
    public void Constructor_WithCapacity_AllocatesEmptyList()
    {
        using var list = new UnsafeList<int>(8);

        Assert.Equal(0, list.Count);
        Assert.Equal(8, list.Capacity);
        Assert.True(list.Ptr != null);
    }

    [Fact]
    public void Constructor_ZeroCapacity_LeavesUnallocated()
    {
        using var list = new UnsafeList<int>(0);

        Assert.Equal(0, list.Count);
        Assert.Equal(0, list.Capacity);
        Assert.True(list.Ptr == null);
    }

    [Fact]
    public void Add_AppendsAndGrowsFromEmpty()
    {
        using var list = new UnsafeList<int>(0);

        list.Add(10);
        list.Add(20);
        list.Add(30);

        Assert.Equal(3, list.Count);
        Assert.Equal(4, list.Capacity);
        Assert.Equal(10, list[0]);
        Assert.Equal(20, list[1]);
        Assert.Equal(30, list[2]);
    }

    [Fact]
    public void Add_DoublesCapacityWhenFull()
    {
        using var list = new UnsafeList<int>(2);

        list.Add(1);
        list.Add(2);
        Assert.Equal(2, list.Capacity);

        list.Add(3);
        Assert.Equal(3, list.Count);
        Assert.Equal(4, list.Capacity);
        Assert.Equal(1, list[0]);
        Assert.Equal(2, list[1]);
        Assert.Equal(3, list[2]);
    }

    [Fact]
    public void Indexer_ReadsAndWritesByRef()
    {
        using var list = new UnsafeList<int>(2);
        list.Add(1);
        list.Add(2);

        list[0] = 42;
        ref int second = ref list[1];
        second = 99;

        Assert.Equal(42, list[0]);
        Assert.Equal(99, list[1]);
    }

    [Fact]
    public void Resize_GrowsAndZeroesNewElements()
    {
        using var list = new UnsafeList<int>(2);
        list.Add(7);

        list.Resize(4);

        Assert.Equal(4, list.Count);
        Assert.True(list.Capacity >= 4);
        Assert.Equal(7, list[0]);
        Assert.Equal(0, list[1]);
        Assert.Equal(0, list[2]);
        Assert.Equal(0, list[3]);
    }

    [Fact]
    public void Resize_ShrinksCountWithoutChangingCapacity()
    {
        using var list = new UnsafeList<int>(8);
        list.Add(1);
        list.Add(2);
        list.Add(3);

        list.Resize(1);

        Assert.Equal(1, list.Count);
        Assert.Equal(8, list.Capacity);
        Assert.Equal(1, list[0]);
    }

    [Fact]
    public void RemoveAt_PreservesOrder()
    {
        using var list = new UnsafeList<int>(4);
        list.Add(1);
        list.Add(2);
        list.Add(3);
        list.Add(4);

        list.RemoveAt(1);

        Assert.Equal(3, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(3, list[1]);
        Assert.Equal(4, list[2]);
    }

    [Fact]
    public void RemoveAt_LastElement_DoesNotShift()
    {
        using var list = new UnsafeList<int>(4);
        list.Add(1);
        list.Add(2);
        list.Add(3);

        list.RemoveAt(2);

        Assert.Equal(2, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(2, list[1]);
    }

    [Fact]
    public void SetCapacity_GrowsPreservingContents()
    {
        using var list = new UnsafeList<int>(2);
        list.Add(5);
        list.Add(6);

        list.SetCapacity(16);

        Assert.Equal(2, list.Count);
        Assert.Equal(16, list.Capacity);
        Assert.Equal(5, list[0]);
        Assert.Equal(6, list[1]);
    }

    [Fact]
    public void SetCapacity_SameCapacity_IsNoOp()
    {
        using var list = new UnsafeList<int>(4);
        var ptr = list.Ptr;

        list.SetCapacity(4);

        Assert.True(list.Ptr == ptr);
        Assert.Equal(4, list.Capacity);
    }

    [Fact]
    public void Enumerate_YieldsElementsInOrder()
    {
        using var list = new UnsafeList<int>(4);
        list.Add(10);
        list.Add(20);
        list.Add(30);

        var values = new List<int>();
        foreach (ref int value in list)
            values.Add(value);

        Assert.Equal(new[] { 10, 20, 30 }, values);
    }

    [Fact]
    public void Enumerate_EmptyList_YieldsNothing()
    {
        using var list = new UnsafeList<int>(4);
        var count = 0;

        foreach (ref int _ in list)
            count++;

        Assert.Equal(0, count);
    }

    [Fact]
    public void Dispose_FreesBufferAndResetsFields()
    {
        var list = new UnsafeList<int>(4);
        list.Add(1);
        list.Dispose();

        Assert.Equal(0, list.Count);
        Assert.Equal(0, list.Capacity);
        Assert.True(list.Ptr == null);
    }

    [Fact]
    public void Dispose_UnallocatedList_IsSafe()
    {
        var list = new UnsafeList<int>(0);
        list.Dispose();

        Assert.Equal(0, list.Count);
        Assert.Equal(0, list.Capacity);
        Assert.True(list.Ptr == null);
    }
}
