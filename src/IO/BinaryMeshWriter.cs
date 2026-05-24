namespace TREditorSharp.IO;

public sealed class BinaryMeshWriter
{
    static readonly byte[] Magic = [(byte)'T', (byte)'R', (byte)'M', (byte)'B'];
    const int Version = 1;
    const int NullRef = -1;

    public string FileExtension => "trmb";

    public void Write(
        HalfEdgeMesh mesh,
        Stream destination,
        BinaryMeshSerializerOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(destination);

        var opts = options ?? BinaryMeshSerializerOptions.Default;
        ValidateDescriptors(opts);

        var vertexRefs = BuildReferenceTable(mesh.Vertices);
        var halfEdgeRefs = BuildReferenceTable(mesh.HalfEdges);
        var faceRefs = BuildReferenceTable(mesh.Faces);

        destination.Write(Magic);
        BinaryMeshPrimitives.WriteInt32(destination, Version);

        WriteVertexSection(mesh, destination, opts, vertexRefs, halfEdgeRefs);
        WriteHalfEdgeSection(mesh, destination, opts, vertexRefs, halfEdgeRefs, faceRefs);
        WriteFaceSection(mesh, destination, opts, halfEdgeRefs, faceRefs);
    }

    static void WriteVertexSection(
        HalfEdgeMesh mesh,
        Stream destination,
        BinaryMeshSerializerOptions options,
        ReferenceTable<VertexHandle> vertexRefs,
        ReferenceTable<HalfEdgeHandle> halfEdgeRefs
    )
    {
        WriteSectionHeader(destination, BinaryMeshEntityKind.Vertex, vertexRefs.Handles.Count);
        foreach (var vertex in vertexRefs.Handles)
            BinaryMeshPrimitives.WriteInt32(
                destination,
                EncodeRef(mesh.Vertices[vertex].OutgoingHalfEdge, halfEdgeRefs)
            );
        WriteColumns(
            mesh,
            destination,
            options,
            BinaryMeshEntityKind.Vertex,
            vertexRefs.Handles.Count
        );
    }

    static void WriteHalfEdgeSection(
        HalfEdgeMesh mesh,
        Stream destination,
        BinaryMeshSerializerOptions options,
        ReferenceTable<VertexHandle> vertexRefs,
        ReferenceTable<HalfEdgeHandle> halfEdgeRefs,
        ReferenceTable<FaceHandle> faceRefs
    )
    {
        WriteSectionHeader(destination, BinaryMeshEntityKind.HalfEdge, halfEdgeRefs.Handles.Count);
        foreach (var handle in halfEdgeRefs.Handles)
        {
            var halfEdge = mesh.HalfEdges[handle];
            BinaryMeshPrimitives.WriteInt32(destination, EncodeRef(halfEdge.Origin, vertexRefs));
            BinaryMeshPrimitives.WriteInt32(destination, EncodeRef(halfEdge.Twin, halfEdgeRefs));
            BinaryMeshPrimitives.WriteInt32(destination, EncodeRef(halfEdge.Next, halfEdgeRefs));
            BinaryMeshPrimitives.WriteInt32(destination, EncodeRef(halfEdge.Prev, halfEdgeRefs));
            BinaryMeshPrimitives.WriteInt32(destination, EncodeRef(halfEdge.Face, faceRefs));
        }
        WriteColumns(
            mesh,
            destination,
            options,
            BinaryMeshEntityKind.HalfEdge,
            halfEdgeRefs.Handles.Count
        );
    }

    static void WriteFaceSection(
        HalfEdgeMesh mesh,
        Stream destination,
        BinaryMeshSerializerOptions options,
        ReferenceTable<HalfEdgeHandle> halfEdgeRefs,
        ReferenceTable<FaceHandle> faceRefs
    )
    {
        WriteSectionHeader(destination, BinaryMeshEntityKind.Face, faceRefs.Handles.Count);
        foreach (var face in faceRefs.Handles)
            BinaryMeshPrimitives.WriteInt32(
                destination,
                EncodeRef(mesh.Faces[face].FirstHalfEdge, halfEdgeRefs)
            );
        WriteColumns(mesh, destination, options, BinaryMeshEntityKind.Face, faceRefs.Handles.Count);
    }

    static ReferenceTable<Storage.Handle<TTag>> BuildReferenceTable<TTag, TConnectivity>(
        Storage.TopologyStorage<TTag, TConnectivity> storage
    )
        where TTag : unmanaged
        where TConnectivity : unmanaged
    {
        var refs = new Dictionary<Storage.Handle<TTag>, int>(storage.LiveCount);
        var handles = new List<Storage.Handle<TTag>>(storage.LiveCount);
        foreach (var handle in storage.Live)
        {
            refs[handle] = refs.Count;
            handles.Add(handle);
        }
        return new ReferenceTable<Storage.Handle<TTag>>(handles, refs);
    }

    static int EncodeRef<TTag>(
        Storage.Handle<TTag> handle,
        ReferenceTable<Storage.Handle<TTag>> refs
    )
        where TTag : unmanaged
    {
        if (handle.IsNull)
            return NullRef;
        if (!refs.Map.TryGetValue(handle, out var fileRef))
            throw new InvalidOperationException(
                $"Cannot serialize reference to non-live handle {handle}."
            );
        return fileRef;
    }

    static void WriteSectionHeader(Stream destination, BinaryMeshEntityKind kind, int liveCount)
    {
        destination.WriteByte((byte)kind);
        BinaryMeshPrimitives.WriteInt32(destination, liveCount);
    }

    static void WriteColumns(
        HalfEdgeMesh mesh,
        Stream destination,
        BinaryMeshSerializerOptions options,
        BinaryMeshEntityKind kind,
        int liveCount
    )
    {
        foreach (
            var required in options.Columns.Where(column =>
                column.EntityKind == kind && column.IsRequired
            )
        )
        {
            if (!required.IsAvailable(mesh))
                throw new InvalidOperationException(
                    $"Required binary mesh column '{required.ColumnId}' is not registered on {kind} storage."
                );
        }

        var columns = options
            .Columns.Where(column => column.EntityKind == kind && column.IsAvailable(mesh))
            .ToArray();
        BinaryMeshPrimitives.WriteInt32(destination, columns.Length);
        foreach (var column in columns)
        {
            BinaryMeshPrimitives.WriteString(destination, column.ColumnId);
            BinaryMeshPrimitives.WriteInt32(destination, column.ElementSize);
            BinaryMeshPrimitives.WriteInt64(destination, (long)column.ElementSize * liveCount);
            column.WritePayload(mesh, destination);
        }
    }

    static void ValidateDescriptors(BinaryMeshSerializerOptions options)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in options.Columns)
        {
            if (!seen.Add(column.ColumnId))
                throw new InvalidOperationException(
                    $"Duplicate binary mesh column descriptor '{column.ColumnId}'."
                );
        }
    }

    readonly record struct ReferenceTable<THandle>(
        IReadOnlyList<THandle> Handles,
        IReadOnlyDictionary<THandle, int> Map
    )
        where THandle : notnull;
}
