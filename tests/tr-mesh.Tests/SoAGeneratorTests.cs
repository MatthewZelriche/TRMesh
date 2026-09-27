using TRMesh;
using TRMesh.Containers;
using System.Reflection;

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
    public void IsAlive_TracksInsertedAndRemovedSlots()
    {
        using var soa = new TestEntitySoA();

        Assert.False(soa.IsAlive(0));
        Assert.False(soa.IsAlive(-1));

        var slot = soa.Insert();
        Assert.True(soa.IsAlive(slot));

        soa.RemoveAt(slot);
        Assert.False(soa.IsAlive(slot));
    }

    [Fact]
    public void InsertWithoutValue_InitializesStandaloneFieldsToDefault()
    {
        using var soa = new TestEntitySoA();

        var slot = soa.Insert();

        Assert.Equal(0, soa.Get(slot).Id);
        Assert.Equal(0f, soa.Get(slot).Health);
        Assert.Equal(0, soa.Get(slot).Flags);
    }

    [Fact]
    public void Insert_GetSet_UsesValueSemantics()
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
        soa.Set(slot, view);

        var again = soa.Get(slot);
        Assert.Equal(42, again.Id);
        Assert.Equal(55f, again.Health);
        Assert.Equal(1, again.Flags);
    }

    [Fact]
    public void GetPointers_ProvidesExplicitPointerAccess()
    {
        using var soa = new TestEntitySoA();
        int slot = soa.Insert(new TestEntity { Id = 1, Health = 2f, Flags = 3 });

        soa.GetPointers(slot, out _, out float* health, out _);
        *health = 9f;

        Assert.Equal(9f, soa.Get(slot).Health);
    }

    [Fact]
    public void Get_ReturnsSnapshotThatSurvivesStructuralModification()
    {
        using var soa = new TestEntitySoA();
        int slot = soa.Insert(new TestEntity { Id = 1, Health = 2f, Flags = 3 });
        TestEntity snapshot = soa.Get(slot);

        for (int i = 0; i < 300; i++)
            soa.Insert(new TestEntity { Id = i + 10 });
        soa.RemoveAt(slot);

        Assert.Equal(1, snapshot.Id);
        Assert.Equal(2f, snapshot.Health);
        Assert.Equal(3, snapshot.Flags);
    }

    [Fact]
    public void MutableColumnCallback_ProvidesDenseBulkAccess()
    {
        using var soa = new TestEntitySoA();
        int first = soa.Insert(new TestEntity { Id = 1, Health = 2f, Flags = 3 });
        int second = soa.Insert(new TestEntity { Id = 4, Health = 5f, Flags = 6 });

        soa.WithMutableColumns(
            (slots, ids, health, flags) =>
            {
                Assert.Equal(new[] { first, second }, slots.ToArray());
                for (int i = 0; i < health.Length; i++)
                    health[i] *= 2f;
            }
        );

        Assert.Equal(4f, soa.Get(first).Health);
        Assert.Equal(10f, soa.Get(second).Health);
    }

    [Fact]
    public void ColumnCallback_RejectsStructuralModification()
    {
        using var soa = new TestEntitySoA();
        soa.Insert();

        Assert.Throws<InvalidOperationException>(() =>
            soa.WithReadOnlyColumns((slots, ids, health, flags) => soa.Insert())
        );
        Assert.Equal(1, soa.Insert());
    }

    [Fact]
    public void Enumerator_SkipsRemovedSlots_AndReportsStableSlots()
    {
        using var soa = new TestEntitySoA();

        var first = soa.Insert(new TestEntity { Id = 1, Health = 1f, Flags = 1 });
        var second = soa.Insert(new TestEntity { Id = 2, Health = 2f, Flags = 2 });
        var third = soa.Insert(new TestEntity { Id = 3, Health = 3f, Flags = 3 });

        soa.RemoveAt(second);

        var seen = new List<int>();
        var seenSlots = new List<int>();
        var enumerator = soa.GetEnumerator();
        while (enumerator.MoveNext())
        {
            seen.Add(enumerator.Current.Id);
            seenSlots.Add(enumerator.CurrentSlot);
        }

        Assert.Equal(new[] { 1, 3 }, seen);
        Assert.Equal(new[] { first, third }, seenSlots);
        Assert.Equal(2, soa.Count);

        // Keep first/third alive for clarity in failure messages.
        Assert.Equal(0, first);
        Assert.Equal(2, third);
    }

    [Fact]
    public void ReadOnlyColumnCallback_ExposesGeneratedFieldsAndStableSlots()
    {
        using var soa = new TestEntitySoA();
        int first = soa.Insert(new TestEntity { Id = 1, Health = 2f, Flags = 3 });
        int removed = soa.Insert(new TestEntity { Id = 4, Health = 5f, Flags = 6 });
        int third = soa.Insert(new TestEntity { Id = 7, Health = 8f, Flags = 9 });
        soa.RemoveAt(removed);

        soa.WithReadOnlyColumns((slots, ids, health, flags) =>
        {
            Assert.Equal([first, third], slots.ToArray());
            Assert.Equal([1, 7], ids.ToArray());
            Assert.Equal([2f, 8f], health.ToArray());
            Assert.Equal<byte>([3, 9], flags.ToArray());
        });
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
    public void DenseColumns_PreserveValuesAcrossStorageGrowth()
    {
        using var soa = new TestEntitySoA();
        var slot = soa.Insert(new TestEntity { Id = 1, Health = 1f, Flags = 1 });

        // Cross several UnsafeList reallocations in every dense column.
        for (var i = 2; i <= 300; i++)
            soa.Insert(new TestEntity { Id = i, Health = i, Flags = (byte)i });

        var view = soa.Get(slot);
        view.Health = 123f;
        view.Flags = 45;
        soa.Set(slot, view);

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

    [Fact]
    public void TargetedDerivedSoA_CombinesBaseAndDerivedFieldsInOneSlotDomain()
    {
        using var mesh = new TargetSpatialMesh();
        var first = mesh.Insert(
            new TopologyInfo { Vertex = 10, Next = 11 },
            new SpatialInfo { Position = 12f, UV = 13f }
        );
        var second = mesh.Insert(
            new TopologyInfo { Vertex = 20, Next = 21 },
            new SpatialInfo { Position = 22f, UV = 23f }
        );

        Assert.Equal(2, mesh.Count);
        Assert.Equal(10, mesh.Get(first).Vertex);
        Assert.Equal(11, mesh.Get(first).Next);
        Assert.Equal(12f, mesh.Get(first).Position);
        Assert.Equal(13f, mesh.Get(first).UV);

        TargetMesh topology = mesh;
        Assert.Equal(20, topology.Get(second).Vertex);
        Assert.Equal(21, topology.Get(second).Next);
    }

    [Fact]
    public void TargetedDerivedSoA_InheritedInsertResetsDerivedColumnsOnReusedSlot()
    {
        using var mesh = new TargetSpatialMesh();
        var removed = mesh.Insert(
            new TopologyInfo { Vertex = 1, Next = 2 },
            new SpatialInfo { Position = 3f, UV = 4f }
        );
        mesh.RemoveAt(removed);

        var reused = mesh.Insert(new TopologyInfo { Vertex = 5, Next = 6 });

        Assert.Equal(removed, reused);
        Assert.Equal(5, mesh.Get(reused).Vertex);
        Assert.Equal(0f, mesh.Get(reused).Position);
        Assert.Equal(0f, mesh.Get(reused).UV);
    }

    [Fact]
    public void TargetedDerivedSoA_InsertWithoutValuesDefaultsEveryColumn()
    {
        using var mesh = new TargetSpatialMesh();
        var removed = mesh.Insert(
            new TopologyInfo { Vertex = 1, Next = 2 },
            new SpatialInfo { Position = 3f, UV = 4f }
        );
        mesh.RemoveAt(removed);

        var reused = mesh.Insert();
        var view = mesh.Get(reused);

        Assert.Equal(removed, reused);
        Assert.Equal(0, view.Vertex);
        Assert.Equal(0, view.Next);
        Assert.Equal(0f, view.Position);
        Assert.Equal(0f, view.UV);
    }

    [Fact]
    public void TargetedDerivedSoA_EnumerationUsesRootLivenessAndCombinedView()
    {
        using var mesh = new TargetSpatialMesh();
        mesh.Insert(
            new TopologyInfo { Vertex = 1, Next = 2 },
            new SpatialInfo { Position = 3f, UV = 4f }
        );
        var removed = mesh.Insert(
            new TopologyInfo { Vertex = 5, Next = 6 },
            new SpatialInfo { Position = 7f, UV = 8f }
        );
        mesh.Insert(
            new TopologyInfo { Vertex = 9, Next = 10 },
            new SpatialInfo { Position = 11f, UV = 12f }
        );
        mesh.RemoveAt(removed);

        var values = new List<(int Vertex, float Position)>();
        foreach (TargetSpatialMesh.View view in mesh)
            values.Add((view.Vertex, view.Position));

        Assert.Equal(new[] { (1, 3f), (9, 11f) }, values);
    }

    [Fact]
    public void TargetedDerivedSoA_ProvidesPointerAndBulkApis()
    {
        using var mesh = new TargetSpatialMesh();
        int slot = mesh.Insert(
            new TopologyInfo { Vertex = 1, Next = 2 },
            new SpatialInfo { Position = 3f, UV = 4f }
        );

        mesh.GetPointers(slot, out int* vertex, out _, out _, out float* uv);
        *vertex = 10;
        *uv = 40f;
        mesh.WithMutableColumns(
            (slots, vertices, next, positions, uvs) => positions[0] = 30f
        );

        TargetSpatialMesh.View value = mesh.Get(slot);
        Assert.Equal(10, value.Vertex);
        Assert.Equal(30f, value.Position);
        Assert.Equal(40f, value.UV);
    }

    [Fact]
    public void TargetedSoA_UnionsMultipleComponentsOnOneClass()
    {
        using var target = new MultiComponentTarget();
        var slot = target.Insert(new AlphaInfo { Alpha = 31 }, new BetaInfo { Beta = 32f });

        Assert.Equal(31, target.Get(slot).Alpha);
        Assert.Equal(32f, target.Get(slot).Beta);
    }

    [Fact]
    public void GeneratedSoA_UsesOneRootSlotMapAndNoColonies()
    {
        var baseOwnsSlotMap = typeof(TargetMesh)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Any(IsUnsafeSlotMapField);
        var derivedOwnsSlotMap = typeof(TargetSpatialMesh)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Any(IsUnsafeSlotMapField);
        var generatedFields = new[]
        {
            typeof(TestEntitySoA),
            typeof(TargetMesh),
            typeof(TargetSpatialMesh),
        }
            .SelectMany(type =>
                type.GetFields(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                )
            )
            .ToArray();

        Assert.True(baseOwnsSlotMap);
        Assert.False(derivedOwnsSlotMap);
        Assert.DoesNotContain(generatedFields, IsUnsafeColonyField);
        Assert.All(
            generatedFields.Where(field => !IsUnsafeSlotMapField(field)),
            field => Assert.True(IsUnsafeListField(field) || field.FieldType == typeof(int))
        );
    }

    static List<int> EnumerateIds(TestEntitySoA soa)
    {
        var ids = new List<int>();
        foreach (var view in soa)
            ids.Add(view.Id);
        return ids;
    }

    static bool IsUnsafeColonyField(FieldInfo field) =>
        field.FieldType.IsGenericType
        && field.FieldType.GetGenericTypeDefinition() == typeof(UnsafeColony<>);

    static bool IsUnsafeSlotMapField(FieldInfo field) =>
        field.FieldType.IsGenericType
        && field.FieldType.GetGenericTypeDefinition() == typeof(UnsafeSlotMap<>);

    static bool IsUnsafeListField(FieldInfo field) =>
        field.FieldType.IsGenericType
        && field.FieldType.GetGenericTypeDefinition() == typeof(UnsafeList<>);
}
