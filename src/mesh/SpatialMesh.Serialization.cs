namespace TRMesh.Mesh;

public partial class SpatialMesh
{
    const uint BinaryMagic = 0x4D535254; // "TRSM" in little-endian byte order.
    const int BinaryVersion = 1;

    // Writes a versioned, compact binary snapshot of this mesh. Exact runtime slot values are NOT preserved to avoid
    // inadvertantly preserving unused slots in the underlying storage data structure.
    // Instead, runtime handles are mapped to dense handles for storage.
    public void Serialize(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // Build the three mappings between UnsafeColony runtime slots and dense handles for the file

        var vertexSlots = new int[_vertices.Count];
        var vertexIds = new Dictionary<int, int>(_vertices.Count);
        var vertexEnumerator = _vertices.GetEnumerator();
        for (int id = 0; vertexEnumerator.MoveNext(); id++)
        {
            int slot = vertexEnumerator.CurrentSlot;
            vertexSlots[id] = slot;
            vertexIds.Add(slot, id);
        }

        var edgeSlots = new int[_edges.Count];
        var edgeIds = new Dictionary<int, int>(_edges.Count);
        var edgeEnumerator = _edges.GetEnumerator();
        for (int id = 0; edgeEnumerator.MoveNext(); id++)
        {
            int slot = edgeEnumerator.CurrentSlot;
            edgeSlots[id] = slot;
            edgeIds.Add(slot, id);
        }

        var faceSlots = new int[_faces.Count];
        var faceIds = new Dictionary<int, int>(_faces.Count);
        var faceEnumerator = _faces.GetEnumerator();
        for (int id = 0; faceEnumerator.MoveNext(); id++)
        {
            int slot = faceEnumerator.CurrentSlot;
            faceSlots[id] = slot;
            faceIds.Add(slot, id);
        }

        // Construct the records with the dense handles. We fully construct all records
        // before attempting to mutate the writer to avoid half-committed mesh data in the event of a serialization error.

        var vertices = new VertexData[vertexSlots.Length];
        for (int id = 0; id < vertexSlots.Length; id++)
        {
            var source = _vertices.Get(vertexSlots[id]);
            vertices[id] = new VertexData
            {
                topology = new Vertex
                {
                    OutgoingHalfEdge = RemapHalfEdge(
                        source.topology.OutgoingHalfEdge,
                        edgeIds,
                        allowInvalid: true
                    ),
                },
                position = source.position,
            };
        }

        var edges = new EdgeData[edgeSlots.Length];
        for (int id = 0; id < edgeSlots.Length; id++)
        {
            var source = _edges.Get(edgeSlots[id]);
            edges[id] = new EdgeData
            {
                topology = new Edge
                {
                    halfEdge = RemapHalfEdgeRecord(
                        source.topology.halfEdge,
                        vertexIds,
                        edgeIds,
                        faceIds
                    ),
                    twin = RemapHalfEdgeRecord(
                        source.topology.twin,
                        vertexIds,
                        edgeIds,
                        faceIds
                    ),
                },
                halfEdgeUv = source.halfEdgeUv,
                twinUv = source.twinUv,
            };
        }

        var faces = new FaceData[faceSlots.Length];
        for (int id = 0; id < faceSlots.Length; id++)
        {
            var source = _faces.Get(faceSlots[id]);
            faces[id] = new FaceData
            {
                topology = new Face
                {
                    AdjacentHalfEdge = RemapHalfEdge(
                        source.topology.AdjacentHalfEdge,
                        edgeIds,
                        allowInvalid: false
                    ),
                },
                normal = source.normal,
            };
        }

        // All validation checks are complete, we can now mutate the writer

        writer.Write(BinaryMagic);
        writer.Write(BinaryVersion);
        writer.Write(vertices.Length);
        writer.Write(edges.Length);
        writer.Write(faces.Length);

        foreach (VertexData vertex in vertices)
        {
            writer.Write(vertex.topology.OutgoingHalfEdge);
            writer.Write(vertex.position.X);
            writer.Write(vertex.position.Y);
            writer.Write(vertex.position.Z);
        }

        foreach (EdgeData edge in edges)
        {
            WriteHalfEdge(writer, edge.topology.halfEdge, edge.halfEdgeUv);
            WriteHalfEdge(writer, edge.topology.twin, edge.twinUv);
        }

        foreach (FaceData face in faces)
        {
            writer.Write(face.topology.AdjacentHalfEdge);
            writer.Write(face.normal.X);
            writer.Write(face.normal.Y);
            writer.Write(face.normal.Z);
        }
    }

    // Reads one compact, versioned mesh snapshot. The reader and its underlying stream remain
    // owned by the caller.
    public static SpatialMesh Deserialize(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        uint magic = reader.ReadUInt32();
        if (magic != BinaryMagic)
            throw new InvalidDataException("The stream does not contain a TRMesh SpatialMesh snapshot.");

        int version = reader.ReadInt32();
        if (version != BinaryVersion)
            throw new InvalidDataException($"Unsupported SpatialMesh snapshot version {version}.");

        int vertexCount = ReadCount(reader, "vertex");
        int edgeCount = ReadCount(reader, "edge");
        int faceCount = ReadCount(reader, "face");

        var mesh = new SpatialMesh();
        try
        {
            // File-local handles are used directly as runtime handles. This relies on inserting
            // into a fresh SoA allocating dense slots in ascending order.
            // This is unfortunately relying on an undocumented internal implementation detail of our storage data structure.
            // But I'm too lazy to change it right now.
            // So for now we validate the insertion is dense as we expect and fail if it is not.
            for (int i = 0; i < vertexCount; i++)
            {
                int slot = mesh._vertices.Insert(
                    new VertexData
                    {
                        topology = new Vertex { OutgoingHalfEdge = reader.ReadInt32() },
                        position = new System.Numerics.Vector3(
                            reader.ReadSingle(),
                            reader.ReadSingle(),
                            reader.ReadSingle()
                        ),
                    }
                );
                if (slot != i)
                    throw new InvalidOperationException("Vertex records were not allocated densely.");
            }

            for (int i = 0; i < edgeCount; i++)
            {
                int slot = mesh._edges.Insert(
                    ReadEdgeData(reader)
                );
                if (slot != i)
                    throw new InvalidOperationException("Edge records were not allocated densely.");
            }

            for (int i = 0; i < faceCount; i++)
            {
                int slot = mesh._faces.Insert(
                    new FaceData
                    {
                        topology = new Face { AdjacentHalfEdge = reader.ReadInt32() },
                        normal = new System.Numerics.Vector3(
                            reader.ReadSingle(),
                            reader.ReadSingle(),
                            reader.ReadSingle()
                        ),
                    }
                );
                if (slot != i)
                    throw new InvalidOperationException("Face records were not allocated densely.");
            }

            mesh.ValidateDeserializedTopology();
            return mesh;
        }
        catch
        {
            mesh.Dispose();
            throw;
        }
    }

    static HalfEdge RemapHalfEdgeRecord(
        HalfEdge source,
        Dictionary<int, int> vertexIds,
        Dictionary<int, int> edgeIds,
        Dictionary<int, int> faceIds
    ) =>
        new()
        {
            SourceVertex = RemapHandle(
                source.SourceVertex,
                vertexIds,
                allowInvalid: false
            ),
            NextHalfEdge = RemapHalfEdge(
                source.NextHalfEdge,
                edgeIds,
                allowInvalid: false
            ),
            PrevHalfEdge = RemapHalfEdge(
                source.PrevHalfEdge,
                edgeIds,
                allowInvalid: false
            ),
            AdjacentFace = RemapHandle(
                source.AdjacentFace,
                faceIds,
                allowInvalid: true
            ),
        };

    static int RemapHalfEdge(int handle, Dictionary<int, int> edgeIds, bool allowInvalid)
    {
        if (handle == INVALID_HANDLE && allowInvalid)
            return INVALID_HANDLE;
        int edgeId = RemapHandle(handle >> 1, edgeIds, allowInvalid: false);
        return (edgeId << 1) | (handle & 1);
    }

    static int RemapHandle(int handle, Dictionary<int, int> ids, bool allowInvalid)
    {
        if (handle == INVALID_HANDLE && allowInvalid)
            return INVALID_HANDLE;
        if (handle < 0 || !ids.TryGetValue(handle, out int id))
            throw new InvalidOperationException($"The mesh contains an invalid reference ({handle}).");
        return id;
    }

    static void WriteHalfEdge(
        BinaryWriter writer,
        HalfEdge halfEdge,
        System.Numerics.Vector2 uv
    )
    {
        writer.Write(halfEdge.SourceVertex);
        writer.Write(halfEdge.NextHalfEdge);
        writer.Write(halfEdge.PrevHalfEdge);
        writer.Write(halfEdge.AdjacentFace);
        writer.Write(uv.X);
        writer.Write(uv.Y);
    }

    static EdgeData ReadEdgeData(BinaryReader reader)
    {
        HalfEdge halfEdge = ReadHalfEdge(reader);
        var halfEdgeUv = new System.Numerics.Vector2(reader.ReadSingle(), reader.ReadSingle());
        HalfEdge twin = ReadHalfEdge(reader);
        var twinUv = new System.Numerics.Vector2(reader.ReadSingle(), reader.ReadSingle());
        return new EdgeData
        {
            topology = new Edge { halfEdge = halfEdge, twin = twin },
            halfEdgeUv = halfEdgeUv,
            twinUv = twinUv,
        };
    }

    static HalfEdge ReadHalfEdge(BinaryReader reader) =>
        new()
        {
            SourceVertex = reader.ReadInt32(),
            NextHalfEdge = reader.ReadInt32(),
            PrevHalfEdge = reader.ReadInt32(),
            AdjacentFace = reader.ReadInt32(),
        };

    static int ReadCount(BinaryReader reader, string kind)
    {
        int count = reader.ReadInt32();
        if (count < 0)
            throw new InvalidDataException($"The {kind} count cannot be negative.");
        return count;
    }

    // TODO: We will want to extract this out to a more generic ValidateMesh() method that performs robust debug validation
    void ValidateDeserializedTopology()
    {
        int vertexCount = _vertices.Count;
        int halfEdgeCount = checked(_edges.Count * 2);
        int faceCount = _faces.Count;
        var vertexDegrees = new int[vertexCount];

        for (int vertex = 0; vertex < vertexCount; vertex++)
        {
            int outgoing = _vertices.Get(vertex).topology.OutgoingHalfEdge;
            if (outgoing != INVALID_HANDLE && !IsHandleInRange(outgoing, halfEdgeCount))
                InvalidTopology($"Vertex {vertex} has an out-of-range outgoing half-edge.");
        }

        for (int halfEdge = 0; halfEdge < halfEdgeCount; halfEdge++)
        {
            ref HalfEdge topology = ref HalfEdgeRef(halfEdge);
            if (!IsHandleInRange(topology.SourceVertex, vertexCount))
                InvalidTopology($"Half-edge {halfEdge} has an out-of-range source vertex.");
            if (!IsHandleInRange(topology.NextHalfEdge, halfEdgeCount))
                InvalidTopology($"Half-edge {halfEdge} has an out-of-range next half-edge.");
            if (!IsHandleInRange(topology.PrevHalfEdge, halfEdgeCount))
                InvalidTopology($"Half-edge {halfEdge} has an out-of-range previous half-edge.");
            if (
                topology.AdjacentFace != INVALID_HANDLE
                && !IsHandleInRange(topology.AdjacentFace, faceCount)
            )
            {
                InvalidTopology($"Half-edge {halfEdge} has an out-of-range adjacent face.");
            }
            vertexDegrees[topology.SourceVertex]++;
        }

        for (int face = 0; face < faceCount; face++)
        {
            int adjacent = _faces.Get(face).topology.AdjacentHalfEdge;
            if (!IsHandleInRange(adjacent, halfEdgeCount))
                InvalidTopology($"Face {face} has an out-of-range adjacent half-edge.");
        }

        for (int halfEdge = 0; halfEdge < halfEdgeCount; halfEdge++)
        {
            ref HalfEdge topology = ref HalfEdgeRef(halfEdge);
            if (HalfEdgeRef(topology.NextHalfEdge).PrevHalfEdge != halfEdge)
                InvalidTopology($"Half-edge {halfEdge} has a non-reciprocal next link.");
            if (HalfEdgeRef(topology.PrevHalfEdge).NextHalfEdge != halfEdge)
                InvalidTopology($"Half-edge {halfEdge} has a non-reciprocal previous link.");
            if (
                HalfEdgeRef(topology.NextHalfEdge).SourceVertex
                != HalfEdgeRef(Twin(halfEdge)).SourceVertex
            )
            {
                InvalidTopology($"Half-edge {halfEdge} does not continue from its target vertex.");
            }
            if (HalfEdgeRef(topology.NextHalfEdge).AdjacentFace != topology.AdjacentFace)
                InvalidTopology($"Half-edge {halfEdge} crosses an adjacent-face boundary.");
        }

        for (int edge = 0; edge < _edges.Count; edge++)
        {
            Edge topology = _edges.Get(edge).topology;
            if (
                topology.halfEdge.AdjacentFace == INVALID_HANDLE
                && topology.twin.AdjacentFace == INVALID_HANDLE
            )
            {
                InvalidTopology($"Edge {edge} is not adjacent to any face.");
            }
        }

        var vertexVisited = new bool[halfEdgeCount];
        for (int vertex = 0; vertex < vertexCount; vertex++)
        {
            int outgoing = _vertices.Get(vertex).topology.OutgoingHalfEdge;
            if (vertexDegrees[vertex] == 0)
            {
                if (outgoing != INVALID_HANDLE)
                    InvalidTopology($"Isolated vertex {vertex} has an outgoing half-edge.");
                continue;
            }
            if (outgoing == INVALID_HANDLE || HalfEdgeRef(outgoing).SourceVertex != vertex)
                InvalidTopology($"Vertex {vertex} does not reference one of its outgoing half-edges.");

            int current = outgoing;
            int visitedCount = 0;
            do
            {
                if (HalfEdgeRef(current).SourceVertex != vertex || vertexVisited[current])
                    InvalidTopology($"Vertex {vertex} has an invalid or disconnected half-edge fan.");
                vertexVisited[current] = true;
                visitedCount++;
                current = HalfEdgeRef(Twin(current)).NextHalfEdge;
            } while (current != outgoing && visitedCount <= vertexDegrees[vertex]);

            if (current != outgoing || visitedCount != vertexDegrees[vertex])
                InvalidTopology($"Vertex {vertex} has more than one disconnected half-edge fan.");
        }

        for (int halfEdge = 0; halfEdge < halfEdgeCount; halfEdge++)
        {
            if (!vertexVisited[halfEdge])
                InvalidTopology($"Half-edge {halfEdge} is not reachable from its source vertex.");
        }

        var faceVisited = new bool[halfEdgeCount];
        for (int face = 0; face < faceCount; face++)
        {
            int start = _faces.Get(face).topology.AdjacentHalfEdge;
            int current = start;
            int edgeCount = 0;
            var vertices = new HashSet<int>();
            do
            {
                ref HalfEdge topology = ref HalfEdgeRef(current);
                if (topology.AdjacentFace != face || faceVisited[current])
                    InvalidTopology($"Face {face} does not contain exactly one closed boundary cycle.");
                if (!vertices.Add(topology.SourceVertex))
                    InvalidTopology($"Face {face} contains a repeated vertex.");
                faceVisited[current] = true;
                edgeCount++;
                current = topology.NextHalfEdge;
            } while (current != start && edgeCount <= halfEdgeCount);

            if (current != start || edgeCount < 3)
                InvalidTopology($"Face {face} does not contain a valid closed boundary cycle.");
        }

        for (int halfEdge = 0; halfEdge < halfEdgeCount; halfEdge++)
        {
            if (HalfEdgeRef(halfEdge).AdjacentFace != INVALID_HANDLE && !faceVisited[halfEdge])
                InvalidTopology($"Half-edge {halfEdge} is not reachable from its adjacent face.");
        }
    }

    static bool IsHandleInRange(int handle, int count) => (uint)handle < (uint)count;

    static void InvalidTopology(string message) => throw new InvalidDataException(message);
}
