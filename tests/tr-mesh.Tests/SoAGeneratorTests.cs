using TRMesh;
using TRMesh.Containers;

namespace tr_mesh.Tests;

[SoA]
public struct TestEntity
{
    public int Id;
    public float Health;
    public byte Flags;
}

public unsafe class SoAGeneratorTests
{
    [Fact]
    public void Insert_Get_ExposesMutableFieldRefs()
    {
        using var soa = new TestEntitySoA();

        var slot = soa.Insert(
            new TestEntity
            {
                Id = 42,
                Health = 100f,
                Flags = 7,
            }
        );

        Assert.Equal(0, slot);
        Assert.Equal(1, soa.Count);

        var view = soa.Get(slot);
        Assert.Equal(42, view.Id);
        Assert.Equal(100f, view.Health);
        Assert.Equal(7, view.Flags);

        view.Health = 55f;
        view.Flags = 1;

        var again = soa.Get(slot);
        Assert.Equal(42, again.Id);
        Assert.Equal(55f, again.Health);
        Assert.Equal(1, again.Flags);
    }

    [Fact]
    public void Enumerator_SkipsRemovedSlots_AndReturnsSameViewType()
    {
        using var soa = new TestEntitySoA();

        var first = soa.Insert(new TestEntity { Id = 1, Health = 1f, Flags = 1 });
        var second = soa.Insert(new TestEntity { Id = 2, Health = 2f, Flags = 2 });
        var third = soa.Insert(new TestEntity { Id = 3, Health = 3f, Flags = 3 });

        soa.RemoveAt(second);

        var seen = new List<int>();
        foreach (TestEntitySoA.View view in soa)
            seen.Add(view.Id);

        Assert.Equal(new[] { 1, 3 }, seen);
        Assert.Equal(2, soa.Count);

        // Keep first/third alive for clarity in failure messages.
        Assert.Equal(0, first);
        Assert.Equal(2, third);
    }

    [Fact]
    public void Clear_ResetsCount()
    {
        using var soa = new TestEntitySoA();
        soa.Insert(new TestEntity { Id = 1, Health = 1f, Flags = 1 });
        soa.Insert(new TestEntity { Id = 2, Health = 2f, Flags = 2 });

        soa.Clear();

        Assert.Equal(0, soa.Count);
        Assert.Empty(EnumerateIds(soa));
    }

    [Fact]
    public void Insert_ReusesHole_AndOverwritesParallelFields()
    {
        using var soa = new TestEntitySoA();
        var first = soa.Insert(new TestEntity { Id = 1, Health = 1f, Flags = 1 });
        var second = soa.Insert(new TestEntity { Id = 2, Health = 2f, Flags = 2 });
        soa.RemoveAt(first);

        var reused = soa.Insert(new TestEntity { Id = 9, Health = 9f, Flags = 9 });

        Assert.Equal(first, reused);
        Assert.Equal(2, soa.Count);
        Assert.Equal(9, soa.Get(reused).Id);
        Assert.Equal(9f, soa.Get(reused).Health);
        Assert.Equal(9, soa.Get(reused).Flags);
        Assert.Equal(2, soa.Get(second).Id);
    }

    [Fact]
    public void View_RemainsValidAcrossParallelStorageGrowth()
    {
        using var soa = new TestEntitySoA();
        var slot = soa.Insert(new TestEntity { Id = 1, Health = 1f, Flags = 1 });
        var view = soa.Get(slot);

        // Cross several UnsafeChunkedList growth boundaries while retaining the view.
        for (var i = 2; i <= 300; i++)
            soa.Insert(new TestEntity { Id = i, Health = i, Flags = (byte)i });

        view.Health = 123f;
        view.Flags = 45;

        var again = soa.Get(slot);
        Assert.Equal(1, again.Id);
        Assert.Equal(123f, again.Health);
        Assert.Equal(45, again.Flags);
    }

    [Fact]
    public void SameStructName_InDifferentNamespaces_GeneratesBothSoATypes()
    {
        using var first = new SourceHintCollisionA.DuplicateEntitySoA();
        using var second = new SourceHintCollisionB.DuplicateEntitySoA();

        var firstSlot = first.Insert(new SourceHintCollisionA.DuplicateEntity { Value = 11 });
        var secondSlot = second.Insert(new SourceHintCollisionB.DuplicateEntity { Value = 22 });

        Assert.Equal(11, first.Get(firstSlot).Value);
        Assert.Equal(22, second.Get(secondSlot).Value);
    }

    [Fact]
    public void InternalField_PreservesInternalVisibility()
    {
        using var soa = new InternalFieldEntitySoA();
        var slot = soa.Insert(
            new InternalFieldEntity
            {
                Payload = new InternalFieldPayload { Value = 17 },
                Visible = 23,
            }
        );

        Assert.Equal(17, soa.Get(slot).Payload.Value);
        Assert.Equal(23, soa.Get(slot).Visible);
    }

    [Fact]
    public void KeywordAndCollidingFieldNames_GenerateDistinctStorageAndValidIdentifiers()
    {
        using var soa = new IdentifierEntitySoA();
        var slot = soa.Insert(
            new IdentifierEntity
            {
                Foo = 1,
                foo = 2,
                @event = 3,
                _soa = 4,
                _slot = 5,
                soa = 6,
                slot = 7,
                View = 8,
            }
        );

        var view = soa.Get(slot);
        Assert.Equal(1, view.Foo);
        Assert.Equal(2, view.foo);
        Assert.Equal(3, view.@event);
        Assert.Equal(4, view._soa);
        Assert.Equal(5, view._slot);
        Assert.Equal(6, view.soa);
        Assert.Equal(7, view.slot);
        Assert.Equal(8, view.View);
    }

    static List<int> EnumerateIds(TestEntitySoA soa)
    {
        var ids = new List<int>();
        foreach (var view in soa)
            ids.Add(view.Id);
        return ids;
    }
}
