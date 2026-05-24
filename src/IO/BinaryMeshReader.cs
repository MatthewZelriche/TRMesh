namespace TREditorSharp.IO;

public sealed class BinaryMeshReader
{
    static readonly byte[] Magic = [(byte)'T', (byte)'R', (byte)'M', (byte)'B'];
    const int Version = 1;
    const int NullRef = -1;

    public HalfEdgeMesh Read(Stream source, BinaryMeshSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var mesh = new HalfEdgeMesh();
        try
        {
            ReadInto(mesh, source, options);
            return mesh;
        }
        catch
        {
            mesh.Dispose();
            throw;
        }
    }

    public SpatialMesh ReadSpatialMesh(Stream source, BinaryMeshSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var mesh = new SpatialMesh();
        try
        {
            ReadInto(mesh, source, options);
            return mesh;
        }
        catch
        {
            mesh.Dispose();
            throw;
        }
    }

    public void ReadInto(
        HalfEdgeMesh mesh,
        Stream source,
        BinaryMeshSerializerOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(source);

        mesh.Clear();
        var opts = options ?? BinaryMeshSerializerOptions.Default;
        var descriptors = BuildDescriptorMap(opts);

        ReadHeader(source);

        var vertexSection = ReadVertexSection(mesh, source, opts, descriptors);
        var halfEdgeSection = ReadHalfEdgeSection(mesh, source, opts, descriptors);
        ReadFacesAndPatchConnectivity(
            mesh,
            source,
            opts,
            descriptors,
            vertexSection,
            halfEdgeSection
        );

        if (opts.ValidateOnRead)
            mesh.ValidateConsistency();
    }

    static void ReadHeader(Stream source)
    {
        Span<byte> magic = stackalloc byte[4];
        source.ReadExactly(magic);
        if (!magic.SequenceEqual(Magic))
            throw new FormatException("Stream is not a TRMesh binary mesh file.");

        int version = BinaryMeshPrimitives.ReadInt32(source);
        if (version != Version)
            throw new NotSupportedException(
                $"Unsupported TRMesh binary mesh version {version}; expected {Version}."
            );
    }

    static VertexSection ReadVertexSection(
        HalfEdgeMesh mesh,
        Stream source,
        BinaryMeshSerializerOptions options,
        Dictionary<string, BinaryMeshColumnDescriptor> descriptors
    )
    {
        int count = ReadSectionHeader(source, BinaryMeshEntityKind.Vertex);
        var handles = new VertexHandle[count];
        for (int i = 0; i < count; i++)
            handles[i] = mesh.Vertices.Allocate();

        var outgoingRefs = new int[count];
        for (int i = 0; i < count; i++)
            outgoingRefs[i] = ReadFileRef(source, BinaryMeshEntityKind.HalfEdge, allowNull: true);

        ReadColumns(mesh, source, options, descriptors, BinaryMeshEntityKind.Vertex, count);
        return new VertexSection(handles, outgoingRefs);
    }

    static HalfEdgeSection ReadHalfEdgeSection(
        HalfEdgeMesh mesh,
        Stream source,
        BinaryMeshSerializerOptions options,
        Dictionary<string, BinaryMeshColumnDescriptor> descriptors
    )
    {
        int count = ReadSectionHeader(source, BinaryMeshEntityKind.HalfEdge);
        var handles = new HalfEdgeHandle[count];
        for (int i = 0; i < count; i++)
            handles[i] = mesh.HalfEdges.Allocate();

        var records = new HalfEdgeFileRecord[count];
        for (int i = 0; i < count; i++)
        {
            records[i] = new HalfEdgeFileRecord(
                Origin: ReadFileRef(source, BinaryMeshEntityKind.Vertex, allowNull: false),
                Twin: ReadFileRef(source, BinaryMeshEntityKind.HalfEdge, allowNull: false),
                Next: ReadFileRef(source, BinaryMeshEntityKind.HalfEdge, allowNull: false),
                Prev: ReadFileRef(source, BinaryMeshEntityKind.HalfEdge, allowNull: false),
                Face: ReadFileRef(source, BinaryMeshEntityKind.Face, allowNull: true)
            );
        }

        ReadColumns(mesh, source, options, descriptors, BinaryMeshEntityKind.HalfEdge, count);
        return new HalfEdgeSection(handles, records);
    }

    void ReadFacesAndPatchConnectivity(
        HalfEdgeMesh mesh,
        Stream source,
        BinaryMeshSerializerOptions options,
        Dictionary<string, BinaryMeshColumnDescriptor> descriptors,
        VertexSection vertexSection,
        HalfEdgeSection halfEdgeSection
    )
    {
        int count = ReadSectionHeader(source, BinaryMeshEntityKind.Face);
        var faceRefs = new FaceHandle[count];
        for (int i = 0; i < count; i++)
            faceRefs[i] = mesh.Faces.Allocate();

        var firstHalfEdgeRefs = new int[count];
        for (int i = 0; i < count; i++)
            firstHalfEdgeRefs[i] = ReadFileRef(
                source,
                BinaryMeshEntityKind.HalfEdge,
                allowNull: false
            );

        ReadColumns(mesh, source, options, descriptors, BinaryMeshEntityKind.Face, count);

        var vertexRefs = vertexSection.Handles;
        var halfEdgeRefs = halfEdgeSection.Handles;

        for (int i = 0; i < vertexRefs.Length; i++)
        {
            mesh.Vertices[vertexRefs[i]] = new Vertex
            {
                OutgoingHalfEdge = ResolveRef(vertexSection.OutgoingHalfEdgeRefs[i], halfEdgeRefs),
            };
        }

        for (int i = 0; i < halfEdgeSection.Records.Length; i++)
        {
            var record = halfEdgeSection.Records[i];
            mesh.HalfEdges[halfEdgeRefs[i]] = new HalfEdge
            {
                Origin = ResolveRef(record.Origin, vertexRefs),
                Twin = ResolveRef(record.Twin, halfEdgeRefs),
                Next = ResolveRef(record.Next, halfEdgeRefs),
                Prev = ResolveRef(record.Prev, halfEdgeRefs),
                Face = ResolveRef(record.Face, faceRefs),
            };
        }

        for (int i = 0; i < count; i++)
        {
            mesh.Faces[faceRefs[i]] = new Face
            {
                FirstHalfEdge = ResolveRef(firstHalfEdgeRefs[i], halfEdgeRefs),
            };
        }
    }

    static int ReadSectionHeader(Stream source, BinaryMeshEntityKind expectedKind)
    {
        int kind = source.ReadByte();
        if (kind < 0)
            throw new EndOfStreamException();
        if ((BinaryMeshEntityKind)kind != expectedKind)
            throw new FormatException(
                $"Expected {expectedKind} section, got {(BinaryMeshEntityKind)kind}."
            );

        int count = BinaryMeshPrimitives.ReadInt32(source);
        if (count < 0)
            throw new FormatException($"{expectedKind} live count is negative.");
        return count;
    }

    static int ReadFileRef(Stream source, BinaryMeshEntityKind kind, bool allowNull)
    {
        int fileRef = BinaryMeshPrimitives.ReadInt32(source);
        if (fileRef == NullRef && allowNull)
            return fileRef;
        if (fileRef < 0)
            throw new FormatException($"{kind} reference {fileRef} is invalid.");
        return fileRef;
    }

    static Storage.Handle<TTag> ResolveRef<TTag>(
        int fileRef,
        IReadOnlyList<Storage.Handle<TTag>> handles
    )
        where TTag : unmanaged
    {
        if (fileRef == NullRef)
            return Storage.Handle<TTag>.Null;
        if ((uint)fileRef >= (uint)handles.Count)
            throw new FormatException($"File-local reference {fileRef} is out of range.");
        return handles[fileRef];
    }

    static void ReadColumns(
        HalfEdgeMesh mesh,
        Stream source,
        BinaryMeshSerializerOptions options,
        Dictionary<string, BinaryMeshColumnDescriptor> descriptors,
        BinaryMeshEntityKind kind,
        int count
    )
    {
        int columnCount = BinaryMeshPrimitives.ReadInt32(source);
        if (columnCount < 0)
            throw new FormatException($"{kind} column count is negative.");

        var seenColumns = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < columnCount; i++)
        {
            string columnId = BinaryMeshPrimitives.ReadString(source);
            if (!seenColumns.Add(columnId))
                throw new FormatException($"Duplicate column '{columnId}' in {kind} section.");

            int elementSize = BinaryMeshPrimitives.ReadInt32(source);
            long payloadBytes = BinaryMeshPrimitives.ReadInt64(source);
            long expectedBytes = checked((long)elementSize * count);
            if (elementSize <= 0)
                throw new FormatException($"Column '{columnId}' has invalid element size.");
            if (payloadBytes != expectedBytes)
                throw new FormatException(
                    $"Column '{columnId}' payload length {payloadBytes} does not match expected {expectedBytes}."
                );

            if (!descriptors.TryGetValue(columnId, out var descriptor))
            {
                if (!options.IgnoreUnknownColumns)
                    throw new FormatException($"Unknown binary mesh column '{columnId}'.");
                SkipBytes(source, payloadBytes);
                continue;
            }

            if (descriptor.EntityKind != kind)
                throw new FormatException(
                    $"Column '{columnId}' appears in {kind}, but descriptor expects {descriptor.EntityKind}."
                );
            if (descriptor.ElementSize != elementSize)
                throw new FormatException(
                    $"Column '{columnId}' element size {elementSize} does not match descriptor size {descriptor.ElementSize}."
                );

            descriptor.ReadPayload(mesh, source, count);
        }

        foreach (var descriptor in descriptors.Values)
        {
            if (
                descriptor.EntityKind == kind
                && descriptor.IsRequired
                && !seenColumns.Contains(descriptor.ColumnId)
            )
            {
                throw new FormatException(
                    $"Required column '{descriptor.ColumnId}' is missing from {kind} section."
                );
            }
        }
    }

    static Dictionary<string, BinaryMeshColumnDescriptor> BuildDescriptorMap(
        BinaryMeshSerializerOptions options
    )
    {
        var descriptors = new Dictionary<string, BinaryMeshColumnDescriptor>(
            StringComparer.Ordinal
        );
        foreach (var descriptor in options.Columns)
        {
            if (!descriptors.TryAdd(descriptor.ColumnId, descriptor))
                throw new InvalidOperationException(
                    $"Duplicate binary mesh column descriptor '{descriptor.ColumnId}'."
                );
        }
        return descriptors;
    }

    static void SkipBytes(Stream source, long byteCount)
    {
        if (byteCount < 0)
            throw new FormatException("Cannot skip a negative byte count.");
        if (source.CanSeek)
        {
            if (source.Position + byteCount > source.Length)
                throw new EndOfStreamException();
            source.Seek(byteCount, SeekOrigin.Current);
            return;
        }

        byte[] buffer = new byte[Math.Min(byteCount, 65_536)];
        long remaining = byteCount;
        while (remaining > 0)
        {
            int take = (int)Math.Min(buffer.Length, remaining);
            source.ReadExactly(buffer.AsSpan(0, take));
            remaining -= take;
        }
    }

    readonly record struct HalfEdgeFileRecord(int Origin, int Twin, int Next, int Prev, int Face);

    readonly record struct VertexSection(VertexHandle[] Handles, int[] OutgoingHalfEdgeRefs);

    readonly record struct HalfEdgeSection(HalfEdgeHandle[] Handles, HalfEdgeFileRecord[] Records);
}
