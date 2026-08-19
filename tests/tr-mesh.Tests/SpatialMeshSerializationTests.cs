using System.Buffers.Binary;
using System.Numerics;
using System.Reflection;
using System.Text;
using TRMesh.Mesh;

namespace tr_mesh.Tests;

public class SpatialMeshSerializationTests
{
    static readonly Vector2 Uv = new(0.25f, 0.75f);

    [Fact]
    public void Serialize_EmptyMesh_WritesVersionedLittleEndianHeader()
    {
        using var mesh = new SpatialMesh();

        byte[] bytes = Serialize(mesh);

        Assert.Equal(
            new byte[]
            {
                (byte)'T',
                (byte)'R',
                (byte)'S',
                (byte)'M',
                1,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
            },
            bytes
        );
    }

    [Fact]
    public void Serialize_Triangle_WritesDocumentedRecordLayout()
    {
        using var mesh = new SpatialMesh();
        AddTriangle(mesh, Vector3.Zero);
        using var stream = new MemoryStream(Serialize(mesh), writable: false);
        using var reader = new BinaryReader(stream);

        Assert.Equal(0x4D535254u, reader.ReadUInt32());
        Assert.Equal(1, reader.ReadInt32());
        Assert.Equal(3, reader.ReadInt32());
        Assert.Equal(3, reader.ReadInt32());
        Assert.Equal(1, reader.ReadInt32());

        Assert.Equal(0, reader.ReadInt32());
        Assert.Equal(0f, reader.ReadSingle());
        Assert.Equal(0f, reader.ReadSingle());
        Assert.Equal(0f, reader.ReadSingle());

        stream.Position = 20 + (3 * 16);
        Assert.Equal(0, reader.ReadInt32());
        Assert.Equal(2, reader.ReadInt32());
        Assert.Equal(4, reader.ReadInt32());
        Assert.Equal(0, reader.ReadInt32());
        Assert.Equal(Uv.X, reader.ReadSingle());
        Assert.Equal(Uv.Y, reader.ReadSingle());
        Assert.Equal(1, reader.ReadInt32());
        Assert.Equal(5, reader.ReadInt32());
        Assert.Equal(3, reader.ReadInt32());
        Assert.Equal(SpatialMesh.INVALID_HANDLE, reader.ReadInt32());
        Assert.Equal(0f, reader.ReadSingle());
        Assert.Equal(0f, reader.ReadSingle());

        stream.Position = 20 + (3 * 16) + (3 * 48);
        Assert.Equal(0, reader.ReadInt32());
        Assert.Equal(0f, reader.ReadSingle());
        Assert.Equal(0f, reader.ReadSingle());
        Assert.Equal(1f, reader.ReadSingle());
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void Deserialize_EmptyMesh_RoundTrips()
    {
        using var source = new SpatialMesh();

        using SpatialMesh loaded = Deserialize(Serialize(source));

        Assert.Equal(0, loaded.VertexCount);
        Assert.Equal(0, loaded.EdgeCount);
        Assert.Equal(0, loaded.FaceCount);
    }

    [Fact]
    public void SerializeAndDeserialize_RejectNullArguments()
    {
        using var mesh = new SpatialMesh();

        Assert.Throws<ArgumentNullException>(() => mesh.Serialize(null!));
        Assert.Throws<ArgumentNullException>(() => SpatialMesh.Deserialize(null!));
    }

    [Fact]
    public void RoundTrip_SharedEdgeMesh_PreservesSnapshotBytesAndTopology()
    {
        using var source = new SpatialMesh();
        int v0 = source.AddVertex(new Vector3(0, 0, 0));
        int v1 = source.AddVertex(new Vector3(1, 0, 0));
        int v2 = source.AddVertex(new Vector3(0, 1, 0));
        int v3 = source.AddVertex(new Vector3(1, 1, 0));
        source.AddFace([v0, v1, v2], [Uv, new Vector2(1, 0), new Vector2(0, 1)]);
        source.AddFace([v0, v2, v3], [new Vector2(0.5f), new Vector2(0, 1), Vector2.One]);
        byte[] expected = Serialize(source);

        using SpatialMesh loaded = Deserialize(expected);

        Assert.Equal(4, loaded.VertexCount);
        Assert.Equal(5, loaded.EdgeCount);
        Assert.Equal(2, loaded.FaceCount);
        Assert.Equal(expected, Serialize(loaded));
        Assert.Equal(new Vector3(0, 0, 1), loaded.GetFaceNormal(0));
        Assert.Equal(new Vector3(0, 0, -1), loaded.GetFaceNormal(1));

        int firstStart = loaded.GetFace(0).AdjacentHalfEdge;
        Assert.Equal(0, loaded.GetHalfEdge(firstStart).AdjacentFace);
        Assert.Equal(Uv, loaded.HalfEdgeUvRef(firstStart));
        int secondStart = loaded.GetFace(1).AdjacentHalfEdge;
        Assert.Equal(1, loaded.GetHalfEdge(secondStart).AdjacentFace);
        Assert.Equal(new Vector2(0.5f), loaded.HalfEdgeUvRef(secondStart));
    }

    [Fact]
    public void Serialize_SparseRuntimeSlots_CompactsHandlesAndDoesNotRestoreHoles()
    {
        using var source = new SpatialMesh();
        AddTriangle(source, new Vector3(-10, 0, 0));
        AddTriangle(source, new Vector3(10, 0, 0));

        VertexDataSoA vertices = GetStorage<VertexDataSoA>(source, "_vertices");
        EdgeDataSoA edges = GetStorage<EdgeDataSoA>(source, "_edges");
        FaceDataSoA faces = GetStorage<FaceDataSoA>(source, "_faces");
        vertices.RemoveAt(0);
        vertices.RemoveAt(1);
        vertices.RemoveAt(2);
        edges.RemoveAt(0);
        edges.RemoveAt(1);
        edges.RemoveAt(2);
        faces.RemoveAt(0);

        byte[] compact = Serialize(source);
        using SpatialMesh loaded = Deserialize(compact);

        Assert.Equal(3, loaded.VertexCount);
        Assert.Equal(3, loaded.EdgeCount);
        Assert.Equal(1, loaded.FaceCount);
        Assert.Equal(compact, Serialize(loaded));

        for (int halfEdge = 0; halfEdge < loaded.EdgeCount * 2; halfEdge++)
        {
            ref HalfEdge topology = ref loaded.GetHalfEdge(halfEdge);
            Assert.InRange(topology.SourceVertex, 0, 2);
            Assert.InRange(topology.NextHalfEdge, 0, 5);
            Assert.InRange(topology.PrevHalfEdge, 0, 5);
            Assert.True(topology.AdjacentFace is -1 or 0);
        }

        int nextVertex = loaded.AddVertex(new Vector3(20, 0, 0));
        Assert.Equal(3, nextVertex);
    }

    [Fact]
    public void Deserialize_ConsumesOnePayloadAndLeavesReaderAndWriterUsable()
    {
        using var source = new SpatialMesh();
        AddTriangle(source, Vector3.Zero);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        source.Serialize(writer);
        writer.Write(0x12345678);
        writer.Flush();

        stream.Position = 0;
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        using SpatialMesh loaded = SpatialMesh.Deserialize(reader);

        Assert.Equal(0x12345678, reader.ReadInt32());
        Assert.True(stream.CanRead);
        Assert.True(stream.CanWrite);
    }

    [Fact]
    public void Deserialize_RejectsInvalidMagicVersionAndCounts()
    {
        using var source = new SpatialMesh();
        byte[] valid = Serialize(source);

        byte[] badMagic = (byte[])valid.Clone();
        badMagic[0] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => Deserialize(badMagic));

        byte[] badVersion = (byte[])valid.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(badVersion.AsSpan(4), 2);
        Assert.Throws<InvalidDataException>(() => Deserialize(badVersion));

        byte[] badCount = (byte[])valid.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(badCount.AsSpan(8), -1);
        Assert.Throws<InvalidDataException>(() => Deserialize(badCount));
    }

    [Fact]
    public void Deserialize_RejectsTruncatedPayload()
    {
        using var source = new SpatialMesh();
        AddTriangle(source, Vector3.Zero);
        byte[] bytes = Serialize(source);

        Assert.Throws<EndOfStreamException>(() => Deserialize(bytes[..^1]));
    }

    [Fact]
    public void Deserialize_RejectsOutOfRangeAndNonReciprocalTopology()
    {
        using var source = new SpatialMesh();
        AddTriangle(source, Vector3.Zero);
        byte[] valid = Serialize(source);
        const int firstHalfEdgeOffset = 20 + (3 * 16);

        byte[] outOfRange = (byte[])valid.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(outOfRange.AsSpan(firstHalfEdgeOffset + 4), 99);
        Assert.Throws<InvalidDataException>(() => Deserialize(outOfRange));

        byte[] nonReciprocal = (byte[])valid.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(nonReciprocal.AsSpan(firstHalfEdgeOffset + 4), 4);
        Assert.Throws<InvalidDataException>(() => Deserialize(nonReciprocal));
    }

    static void AddTriangle(SpatialMesh mesh, Vector3 offset)
    {
        int v0 = mesh.AddVertex(offset);
        int v1 = mesh.AddVertex(offset + Vector3.UnitX);
        int v2 = mesh.AddVertex(offset + Vector3.UnitY);
        mesh.AddFace([v0, v1, v2], [Uv, Uv, Uv]);
    }

    static T GetStorage<T>(SpatialMesh mesh, string fieldName)
        where T : class
    {
        FieldInfo field = typeof(SpatialMesh).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        return (T)field.GetValue(mesh)!;
    }

    static byte[] Serialize(SpatialMesh mesh)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        mesh.Serialize(writer);
        writer.Flush();
        return stream.ToArray();
    }

    static SpatialMesh Deserialize(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        return SpatialMesh.Deserialize(reader);
    }
}
