using TRMesh.Containers;

namespace tr_mesh.Tests;

public unsafe class UnsafeSlotMapTests
{
    readonly record struct TestValue(int Field);

    [Fact]
    public void Constructor_Empty_HasNoActiveValues()
    {
        using var map = new UnsafeSlotMap<int>();

        Assert.Equal(0, map.Count);
        Assert.Equal(0, map.Capacity);
    }

    [Fact]
    public void Insert_StoresValuesAndReturnsHandles()
    {
        using var map = new UnsafeSlotMap<int>();

        var first = map.Insert(10);
        var second = map.Insert(20);

        Assert.Equal(0, first);
        Assert.Equal(1, second);
        Assert.NotEqual(first, second);
        Assert.Equal(10, map[first]);
        Assert.Equal(20, map[second]);
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void Remove_InvalidatesHandleUntilItsSparseSlotIsReused()
    {
        using var map = new UnsafeSlotMap<int>();
        var oldHandle = map.Insert(10);

        Assert.True(map.Remove(oldHandle));
        Assert.False(map.Remove(oldHandle));
        Assert.False(map.Contains(oldHandle));
        Assert.False(map.TryGetValue(oldHandle, out _));

        var newHandle = map.Insert(20);

        Assert.Equal(oldHandle, newHandle);
        Assert.True(map.Contains(oldHandle));
        Assert.Equal(20, map[oldHandle]);
    }

    [Fact]
    public void Map_IsAReferenceTypeWithOneNativeOwner()
    {
        using var map = new UnsafeSlotMap<int>();
        UnsafeSlotMap<int> alias = map;

        var key = alias.Insert(42);

        Assert.Same(map, alias);
        Assert.Equal(42, map[key]);
    }

    [Fact]
    public void UndoInsert_ThatGrewSparseArray_RestoresExactState()
    {
        using var map = new UnsafeSlotMap<int>();
        map.Insert(10);
        int[] before = State(map);

        map.Insert(20);
        map.UndoInsert(grew: true);

        Assert.Equal(before, State(map));
    }

    [Fact]
    public void UndoInsert_ThatReusedFreeHandle_RestoresExactState()
    {
        using var map = new UnsafeSlotMap<int>();
        map.Insert(10);
        int freed = map.Insert(20);
        map.Insert(30);
        map.Remove(freed);
        int[] before = State(map);

        Assert.Equal(freed, map.Insert(40));
        map.UndoInsert(grew: false);

        Assert.Equal(before, State(map));
    }

    [Fact]
    public void UndoRemove_RestoresDenseOrderAndFreeStack()
    {
        using var map = new UnsafeSlotMap<int>();
        int first = map.Insert(10);
        map.Insert(20);
        int third = map.Insert(30);
        map.Remove(third);
        int[] before = State(map);

        int removedDenseIndex = map.GetDenseIndex(first);
        map.Remove(first);
        map.UndoRemove(10, removedDenseIndex);

        Assert.Equal(before, State(map));
        Assert.Equal(third, map.Insert(40));
    }

    [Fact]
    public void UndoRemove_OfLastDenseValue_RestoresExactState()
    {
        using var map = new UnsafeSlotMap<int>();
        map.Insert(10);
        int last = map.Insert(20);
        int[] before = State(map);

        int removedDenseIndex = map.GetDenseIndex(last);
        map.Remove(last);
        map.UndoRemove(20, removedDenseIndex);

        Assert.Equal(before, State(map));
    }

    static int[] State(UnsafeSlotMap<int> map)
    {
        var values = new List<int>();
        var enumerator = map.GetEnumerator();
        while (enumerator.MoveNext())
        {
            values.Add(enumerator.CurrentSlot);
            values.Add(enumerator.Current);
        }
        return [.. map.CopyAllocatorState(), values.Count, .. values];
    }

    [Fact]
    public void ByValueIndexer_SupportsWithExpressionUpdate()
    {
        var map = new UnsafeSlotMap<TestValue>();
        try
        {
            var key = map.Insert(new TestValue(1));

            map[key] = map[key] with { Field = 5 };

            Assert.Equal(5, map[key].Field);
        }
        finally
        {
            map.Dispose();
        }
    }

    [Fact]
    public void GetPointer_CanMutateValueInPlace()
    {
        using var map = new UnsafeSlotMap<int>();
        var key = map.Insert(1);

        int* value = map.GetPointer(key);
        *value = 5;

        Assert.Equal(5, map[key]);
    }

    [Fact]
    public void Clear_InvalidatesKeysRetainsCapacityAndReusesSlotsInOrder()
    {
        using var map = new UnsafeSlotMap<int>(100);
        var first = map.Insert(1);
        var second = map.Insert(2);
        int capacity = map.Capacity;

        map.Clear();

        Assert.Equal(0, map.Count);
        Assert.Equal(capacity, map.Capacity);
        Assert.False(map.Contains(first));
        Assert.False(map.Contains(second));

        var replacement = map.Insert(3);
        Assert.Equal(first, replacement);
        Assert.Equal(3, map[first]);
    }

    [Fact]
    public void Remove_MovesLastDenseValueAndUpdatesItsSparseEntry()
    {
        using var map = new UnsafeSlotMap<int>();
        var first = map.Insert(10);
        var second = map.Insert(20);
        var last = map.Insert(30);

        int* before = map.GetPointer(last);

        Assert.True(map.Remove(first));

        int* after = map.GetPointer(last);

        Assert.True(before != after);
        Assert.Equal(30, *after);
        Assert.Equal(20, map[second]);
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void Enumerator_VisitsDenseValuesAndReportsCurrentSlot()
    {
        using var map = new UnsafeSlotMap<int>();
        var first = map.Insert(10);
        var removed = map.Insert(20);
        var third = map.Insert(30);
        map.Remove(removed);

        var handles = new List<int>();
        var values = new List<int>();
        var enumerator = map.GetEnumerator();
        while (enumerator.MoveNext())
        {
            handles.Add(enumerator.CurrentSlot);
            values.Add(enumerator.Current);
        }

        Assert.Equal(new[] { first, third }, handles);
        Assert.Equal(new[] { 10, 30 }, values);
    }

    [Fact]
    public void Enumerator_FailsFastAfterStructuralModification()
    {
        using var map = new UnsafeSlotMap<int>();
        map.Insert(10);
        var enumerator = map.GetEnumerator();
        Assert.True(enumerator.MoveNext());

        map.Insert(20);

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void ByteValues_CanReuseSparseSlots()
    {
        using var map = new UnsafeSlotMap<byte>();
        var keys = new int[100];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = map.Insert((byte)i);

        for (int i = 0; i < keys.Length; i += 2)
            Assert.True(map.Remove(keys[i]));

        for (int i = 0; i < keys.Length / 2; i++)
            map.Insert((byte)(200 + i));

        Assert.Equal(100, map.Count);
        for (int i = 1; i < keys.Length; i += 2)
            Assert.Equal((byte)i, map[keys[i]]);
    }
}
