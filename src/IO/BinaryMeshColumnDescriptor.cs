using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TREditorSharp.Storage;

namespace TREditorSharp.IO;

/// <summary>
/// Describes one typed component column that can be encoded in a binary mesh file.
/// </summary>
/// <remarks>
/// A descriptor is the schema bridge between a TRMesh storage column and an on-disk column payload.
/// It identifies which entity section owns the column, the stable column id written to the file,
/// the encoded byte size of each element, and whether the column is required when writing or reading.
///
/// The serializer stores topology separately from component payloads. For each descriptor in
/// <see cref="BinaryMeshSerializerOptions.Columns"/>, the writer emits the column only when the
/// matching storage column exists on the mesh, unless <see cref="IsRequired"/> is true. The reader
/// uses the file's column id to find the matching descriptor, creates the storage column when
/// needed, and decodes elements in the section's dense file order.
///
/// Use <see cref="Create{TEntityTag, TValue, TColumnTag}(BinaryMeshEntityKind, string, bool)"/> for
/// unmanaged values that can be written as their native in-memory bytes. Use the overload with
/// explicit read/write delegates when the file representation should be stable across runtimes,
/// platforms, or future struct layout changes.
/// </remarks>
public abstract class BinaryMeshColumnDescriptor
{
    protected BinaryMeshColumnDescriptor(
        BinaryMeshEntityKind entityKind,
        string columnId,
        int elementSize,
        bool isRequired
    )
    {
        if (string.IsNullOrWhiteSpace(columnId))
            throw new ArgumentException("Column id must not be empty.", nameof(columnId));
        if (elementSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(elementSize));

        EntityKind = entityKind;
        ColumnId = columnId;
        ElementSize = elementSize;
        IsRequired = isRequired;
    }

    public BinaryMeshEntityKind EntityKind { get; }

    public string ColumnId { get; }

    public int ElementSize { get; }

    public bool IsRequired { get; }

    public static BinaryMeshColumnDescriptor Create<TEntityTag, TValue, TColumnTag>(
        BinaryMeshEntityKind entityKind,
        string columnId,
        bool isRequired = false
    )
        where TEntityTag : unmanaged
        where TValue : unmanaged =>
        new NativeBinaryMeshColumnDescriptor<TEntityTag, TValue, TColumnTag>(
            entityKind,
            columnId,
            Unsafe.SizeOf<TValue>(),
            isRequired,
            writeElement: null,
            readElement: null
        );

    public static BinaryMeshColumnDescriptor Create<TEntityTag, TValue, TColumnTag>(
        BinaryMeshEntityKind entityKind,
        string columnId,
        int elementSize,
        BinaryMeshWriteElement<TValue> writeElement,
        BinaryMeshReadElement<TValue> readElement,
        bool isRequired = false
    )
        where TEntityTag : unmanaged
        where TValue : unmanaged =>
        new NativeBinaryMeshColumnDescriptor<TEntityTag, TValue, TColumnTag>(
            entityKind,
            columnId,
            elementSize,
            isRequired,
            writeElement,
            readElement
        );

    internal abstract bool IsAvailable(HalfEdgeMesh mesh);

    internal abstract void EnsureColumn(HalfEdgeMesh mesh);

    internal abstract void WritePayload(HalfEdgeMesh mesh, Stream destination);

    internal abstract void ReadPayload(HalfEdgeMesh mesh, Stream source, int count);
}

sealed class NativeBinaryMeshColumnDescriptor<TEntityTag, TValue, TColumnTag>
    : BinaryMeshColumnDescriptor
    where TEntityTag : unmanaged
    where TValue : unmanaged
{
    readonly BinaryMeshWriteElement<TValue>? _writeElement;
    readonly BinaryMeshReadElement<TValue>? _readElement;

    public NativeBinaryMeshColumnDescriptor(
        BinaryMeshEntityKind entityKind,
        string columnId,
        int elementSize,
        bool isRequired,
        BinaryMeshWriteElement<TValue>? writeElement,
        BinaryMeshReadElement<TValue>? readElement
    )
        : base(entityKind, columnId, elementSize, isRequired)
    {
        _writeElement = writeElement;
        _readElement = readElement;
    }

    internal override bool IsAvailable(HalfEdgeMesh mesh)
    {
        return EntityKind switch
        {
            BinaryMeshEntityKind.Vertex => mesh.Vertices.HasColumnTag<TColumnTag>(),
            BinaryMeshEntityKind.HalfEdge => mesh.HalfEdges.HasColumnTag<TColumnTag>(),
            BinaryMeshEntityKind.Face => mesh.Faces.HasColumnTag<TColumnTag>(),
            _ => false,
        };
    }

    internal override void EnsureColumn(HalfEdgeMesh mesh)
    {
        if (IsAvailable(mesh))
            return;

        _ = EntityKind switch
        {
            BinaryMeshEntityKind.Vertex => mesh.Vertices.RegisterNativeColumn<TValue, TColumnTag>(),
            BinaryMeshEntityKind.HalfEdge => mesh.HalfEdges.RegisterNativeColumn<
                TValue,
                TColumnTag
            >(),
            BinaryMeshEntityKind.Face => mesh.Faces.RegisterNativeColumn<TValue, TColumnTag>(),
            _ => throw new InvalidOperationException($"Unsupported entity kind {EntityKind}."),
        };
    }

    internal override void WritePayload(HalfEdgeMesh mesh, Stream destination)
    {
        var column = GetColumn(mesh).AsSpan();
        if (_writeElement == null)
        {
            destination.Write(MemoryMarshal.AsBytes(column));
            return;
        }

        var elementBuffer = new byte[ElementSize];
        for (int i = 0; i < column.Length; i++)
        {
            _writeElement(in column[i], elementBuffer);
            destination.Write(elementBuffer);
        }
    }

    internal override void ReadPayload(HalfEdgeMesh mesh, Stream source, int count)
    {
        EnsureColumn(mesh);
        var column = GetColumn(mesh).AsSpan();
        if (column.Length != count)
            throw new FormatException(
                $"Column '{ColumnId}' expected {count} entries, got {column.Length}."
            );

        if (_readElement == null)
        {
            source.ReadExactly(MemoryMarshal.AsBytes(column));
            return;
        }

        var elementBuffer = new byte[ElementSize];
        for (int i = 0; i < count; i++)
        {
            source.ReadExactly(elementBuffer);
            column[i] = _readElement(elementBuffer);
        }
    }

    NativeColumn<TValue> GetColumn(HalfEdgeMesh mesh)
    {
        return EntityKind switch
        {
            BinaryMeshEntityKind.Vertex => mesh.Vertices.GetNativeColumn<TValue, TColumnTag>(),
            BinaryMeshEntityKind.HalfEdge => mesh.HalfEdges.GetNativeColumn<TValue, TColumnTag>(),
            BinaryMeshEntityKind.Face => mesh.Faces.GetNativeColumn<TValue, TColumnTag>(),
            _ => throw new InvalidOperationException($"Unsupported entity kind {EntityKind}."),
        };
    }
}
