using System.Numerics;
using TREditorSharp.Storage;

namespace TREditorSharp.Tests;

public sealed class ComponentEntrySnapshotTests
{
    private readonly struct PositionTag { }

    private readonly struct WeightTag { }

    [Fact]
    public void TypeErasedEntrySnapshot_RestoresIntAtDifferentDenseIndex()
    {
        using NativeColumn<int> typedColumn = new();
        typedColumn.Add();
        typedColumn.Add();
        typedColumn[0] = 123456;
        typedColumn[1] = -1;
        IComponentColumn column = typedColumn;
        byte[] snapshot = new byte[column.ElementSize];

        column.CopyEntryTo(0, snapshot);
        column.RestoreEntryFrom(1, snapshot);

        Assert.Equal(sizeof(int), column.ElementSize);
        Assert.Equal(123456, typedColumn[1]);
    }

    [Fact]
    public void TypeErasedEntrySnapshot_RestoresVectorAtDifferentDenseIndex()
    {
        using NativeColumn<Vector3> typedColumn = new();
        typedColumn.Add();
        typedColumn.Add();
        Vector3 expected = new(1.25f, -2.5f, 10f);
        typedColumn[0] = expected;
        IComponentColumn column = typedColumn;
        byte[] snapshot = new byte[column.ElementSize];

        column.CopyEntryTo(0, snapshot);
        column.RestoreEntryFrom(1, snapshot);

        Assert.Equal(sizeof(float) * 3, column.ElementSize);
        Assert.Equal(expected, typedColumn[1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void TypeErasedEntrySnapshot_RejectsIncorrectBufferSize(int size)
    {
        using NativeColumn<int> typedColumn = new();
        typedColumn.Add();
        IComponentColumn column = typedColumn;
        byte[] buffer = new byte[size];

        Assert.Throws<ArgumentException>(() => column.CopyEntryTo(0, buffer));
        Assert.Throws<ArgumentException>(() => column.RestoreEntryFrom(0, buffer));
    }

    [Fact]
    public void TopologyStorageColumnSchema_PreservesRegistrationOrderAndIdentity()
    {
        using HalfEdgeMesh mesh = new();
        mesh.Vertices.RegisterNativeColumn<Vector3, PositionTag>();
        mesh.Vertices.RegisterNativeColumn<int, WeightTag>();

        IReadOnlyList<ComponentColumnSchema> schema = mesh.Vertices.ColumnSchema;

        Assert.Collection(
            schema,
            connectivity => AssertSchema<Vertex>(connectivity, 0, typeof(Vertex)),
            position => AssertSchema<Vector3>(position, 1, typeof(PositionTag)),
            weight => AssertSchema<int>(weight, 2, typeof(WeightTag))
        );
    }

    private static void AssertSchema<T>(
        ComponentColumnSchema schema,
        int registrationIndex,
        Type tagType
    )
        where T : unmanaged
    {
        Assert.Equal(registrationIndex, schema.RegistrationIndex);
        Assert.Equal(tagType, schema.TagType);
        Assert.Equal(typeof(T), schema.ElementType);
        Assert.Equal(System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), schema.ElementSize);
    }
}
