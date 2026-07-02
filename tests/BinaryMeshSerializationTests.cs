using System.Numerics;
using TREditorSharp.Builders;
using TREditorSharp.IO;
using TREditorSharp.Storage;

namespace TREditorSharp.Tests;

public sealed class BinaryMeshSerializationTests
{
    readonly struct VertexWeightTag { }

    [Fact]
    public void SpatialMesh_RoundTripsExactTopologyAndPositions()
    {
        using var expected = MeshBuilders.Build(
            new BlockOptions { Min = new Vector3(-1, -2, -3), Max = new Vector3(4, 5, 6) }
        );
        expected.ValidateConsistency();

        using var stream = new MemoryStream();
        new BinaryMeshWriter().Write(expected, stream);
        stream.Position = 0;

        using var actual = new BinaryMeshReader().ReadSpatialMesh(stream);

        actual.ValidateConsistency();
        AssertSemanticTopology(expected, actual);
        AssertExactPositions(expected, actual);
    }

    [Fact]
    public void SpatialMesh_RoundTripsExactTextureAttributes()
    {
        using var expected = MeshBuilders.Build(
            new BlockOptions { Min = new Vector3(-1, -2, -3), Max = new Vector3(4, 5, 6) }
        );
        SetDistinctTextureAttributes(expected);

        using var stream = new MemoryStream();
        new BinaryMeshWriter().Write(expected, stream);
        stream.Position = 0;

        using var actual = new BinaryMeshReader().ReadSpatialMesh(stream);

        AssertExactTextureAttributes(expected, actual);
    }

    [Fact]
    public void SpatialMesh_OlderPositionOnlyFileLoadsDefaultTextureAttributes()
    {
        using var expected = MeshBuilders.Build(
            new BlockOptions { Min = new Vector3(-1, -2, -3), Max = new Vector3(4, 5, 6) }
        );
        var legacyOptions = new BinaryMeshSerializerOptions();
        legacyOptions.Columns.Clear();
        legacyOptions.Columns.Add(BinaryMeshColumnDescriptors.VertexPositions);

        using var stream = new MemoryStream();
        new BinaryMeshWriter().Write(expected, stream, legacyOptions);
        stream.Position = 0;

        using var actual = new BinaryMeshReader().ReadSpatialMesh(stream);

        foreach (FaceHandle face in actual.EnumerateLiveFaces())
        {
            Assert.Equal(SpatialMesh.UntexturedMaterialSlot, actual.GetFaceMaterialSlot(face));
            Assert.False(actual.AreFaceUvsInitialized(face));
            foreach (FaceCornerHandle corner in actual.HalfEdgesAroundFace(face))
            {
                Assert.Equal(Vector2.Zero, actual.GetFaceCornerUv(corner));
            }
        }
    }

    [Fact]
    public void GenericColumn_RoundTripsWhenDescriptorIsRegistered()
    {
        using var expected = new HalfEdgeMesh();
        var expectedWeights = expected.Vertices.RegisterNativeColumn<int, VertexWeightTag>();
        var v0 = expected.Vertices.Allocate();
        var v1 = expected.Vertices.Allocate();
        expectedWeights[expected.Vertices.GetDenseIndex(v0)] = 11;
        expectedWeights[expected.Vertices.GetDenseIndex(v1)] = 22;

        var options = new BinaryMeshSerializerOptions();
        options.Columns.Add(
            BinaryMeshColumnDescriptor.Create<int, VertexWeightTag>(
                BinaryMeshEntityKind.Vertex,
                "test.vertex.weight.v1"
            )
        );

        using var stream = new MemoryStream();
        new BinaryMeshWriter().Write(expected, stream, options);
        stream.Position = 0;

        using var actual = new BinaryMeshReader().Read(stream, options);

        AssertSemanticTopology(expected, actual);
        var actualWeights = actual.Vertices.GetNativeColumn<int, VertexWeightTag>();
        for (int i = 0; i < expected.Vertices.LiveCount; i++)
            Assert.Equal(expectedWeights[i], actualWeights[i]);
    }

    [Fact]
    public void SparseReuse_RoundTripAllocatesFreshRuntimeHandles()
    {
        using var expected = new HalfEdgeMesh();
        expected.Vertices.Allocate();
        var stale = expected.Vertices.Allocate();
        expected.Vertices.Allocate();
        expected.Vertices.Free(stale);
        var reused = expected.Vertices.Allocate();

        Assert.False(expected.Vertices.IsAlive(stale));
        Assert.True(expected.Vertices.IsAlive(reused));

        using var stream = new MemoryStream();
        new BinaryMeshWriter().Write(expected, stream);
        stream.Position = 0;

        using var actual = new BinaryMeshReader().Read(stream);

        AssertSemanticTopology(expected, actual);
        Assert.Equal(expected.Vertices.LiveCount, actual.Vertices.LiveCount);
        Assert.Equal(expected.HalfEdges.LiveCount, actual.HalfEdges.LiveCount);
        Assert.Equal(expected.Faces.LiveCount, actual.Faces.LiveCount);
    }

    [Fact]
    public void Read_RejectsBadMagic()
    {
        using var stream = new MemoryStream([0, 1, 2, 3, 1, 0, 0, 0]);

        Assert.Throws<FormatException>(() => new BinaryMeshReader().Read(stream));
    }

    [Fact]
    public void Read_RejectsEntityCountAboveConfiguredLimit()
    {
        var options = new BinaryMeshSerializerOptions { MaximumEntityCount = 1 };
        using MemoryStream stream = WritePartialFile(writer =>
        {
            writer.Write((byte)BinaryMeshEntityKind.Vertex);
            writer.Write(2);
        });

        Assert.Throws<FormatException>(() => new BinaryMeshReader().Read(stream, options));
    }

    [Fact]
    public void Read_RejectsTruncatedEntityRecordsBeforeAllocatingTopology()
    {
        using MemoryStream stream = WritePartialFile(writer =>
        {
            writer.Write((byte)BinaryMeshEntityKind.Vertex);
            writer.Write(1);
        });

        Assert.Throws<EndOfStreamException>(() => new BinaryMeshReader().Read(stream));
    }

    [Fact]
    public void Read_RejectsColumnCountAboveConfiguredLimit()
    {
        var options = new BinaryMeshSerializerOptions { MaximumColumnCount = 1 };
        using MemoryStream stream = WritePartialFile(writer =>
        {
            writer.Write((byte)BinaryMeshEntityKind.Vertex);
            writer.Write(0);
            writer.Write(2);
        });

        Assert.Throws<FormatException>(() => new BinaryMeshReader().Read(stream, options));
    }

    [Fact]
    public void Read_RejectsColumnIdentifierAboveConfiguredLimit()
    {
        var options = new BinaryMeshSerializerOptions { MaximumColumnIdBytes = 0 };
        using MemoryStream stream = WritePartialFile(writer =>
        {
            writer.Write((byte)BinaryMeshEntityKind.Vertex);
            writer.Write(0);
            writer.Write(1);
            writer.Write(1);
            writer.Write((byte)'x');
        });

        Assert.Throws<FormatException>(() => new BinaryMeshReader().Read(stream, options));
    }

    [Fact]
    public void Read_RejectsColumnPayloadAboveConfiguredLimit()
    {
        var options = new BinaryMeshSerializerOptions { MaximumColumnPayloadBytes = 0 };
        using MemoryStream stream = WritePartialFile(writer =>
        {
            writer.Write((byte)BinaryMeshEntityKind.Vertex);
            writer.Write(0);
            writer.Write(1);
            writer.Write(1);
            writer.Write((byte)'x');
            writer.Write(1);
            writer.Write((long)1);
        });

        Assert.Throws<FormatException>(() => new BinaryMeshReader().Read(stream, options));
    }

    [Fact]
    public void Read_RejectsTrailingData()
    {
        using var mesh = new HalfEdgeMesh();
        using MemoryStream stream = new();
        new BinaryMeshWriter().Write(mesh, stream);
        stream.WriteByte(1);
        stream.Position = 0;

        Assert.Throws<FormatException>(() => new BinaryMeshReader().Read(stream));
    }

    [Fact]
    public void Write_RejectsMissingRequiredColumn()
    {
        using var mesh = new HalfEdgeMesh();
        var options = new BinaryMeshSerializerOptions();
        options.Columns.Add(
            BinaryMeshColumnDescriptor.Create<int, VertexWeightTag>(
                BinaryMeshEntityKind.Face,
                "test.face.required-weight.v1",
                isRequired: true
            )
        );

        using var stream = new MemoryStream();

        Assert.Throws<InvalidOperationException>(
            () => new BinaryMeshWriter().Write(mesh, stream, options)
        );
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void Write_RejectsInvalidTopologyBeforeWriting()
    {
        using var mesh = new HalfEdgeMesh();
        mesh.HalfEdges.Allocate();
        using var stream = new MemoryStream();

        Assert.Throws<InvalidOperationException>(() => new BinaryMeshWriter().Write(mesh, stream));
        Assert.Equal(0, stream.Length);
    }

    static void AssertSemanticTopology(HalfEdgeMesh expected, HalfEdgeMesh actual)
    {
        Assert.Equal(expected.Vertices.LiveCount, actual.Vertices.LiveCount);
        Assert.Equal(expected.HalfEdges.LiveCount, actual.HalfEdges.LiveCount);
        Assert.Equal(expected.Faces.LiveCount, actual.Faces.LiveCount);

        var expectedVertices = CollectHandles(expected.Vertices);
        var actualVertices = CollectHandles(actual.Vertices);
        var expectedHalfEdges = CollectHandles(expected.HalfEdges);
        var actualHalfEdges = CollectHandles(actual.HalfEdges);
        var expectedFaces = CollectHandles(expected.Faces);
        var actualFaces = CollectHandles(actual.Faces);

        for (int i = 0; i < expectedVertices.Count; i++)
        {
            var expectedVertex = expected.Vertices[expectedVertices[i]];
            var actualVertex = actual.Vertices[actualVertices[i]];
            Assert.Equal(
                IndexOf(expectedHalfEdges, expectedVertex.OutgoingHalfEdge),
                IndexOf(actualHalfEdges, actualVertex.OutgoingHalfEdge)
            );
        }

        for (int i = 0; i < expectedHalfEdges.Count; i++)
        {
            var expectedHalfEdge = expected.HalfEdges[expectedHalfEdges[i]];
            var actualHalfEdge = actual.HalfEdges[actualHalfEdges[i]];

            Assert.Equal(
                IndexOf(expectedVertices, expectedHalfEdge.Origin),
                IndexOf(actualVertices, actualHalfEdge.Origin)
            );
            Assert.Equal(
                IndexOf(expectedHalfEdges, expectedHalfEdge.Twin),
                IndexOf(actualHalfEdges, actualHalfEdge.Twin)
            );
            Assert.Equal(
                IndexOf(expectedHalfEdges, expectedHalfEdge.Next),
                IndexOf(actualHalfEdges, actualHalfEdge.Next)
            );
            Assert.Equal(
                IndexOf(expectedHalfEdges, expectedHalfEdge.Prev),
                IndexOf(actualHalfEdges, actualHalfEdge.Prev)
            );
            Assert.Equal(
                IndexOf(expectedFaces, expectedHalfEdge.Face),
                IndexOf(actualFaces, actualHalfEdge.Face)
            );
        }

        for (int i = 0; i < expectedFaces.Count; i++)
        {
            var expectedFace = expected.Faces[expectedFaces[i]];
            var actualFace = actual.Faces[actualFaces[i]];
            Assert.Equal(
                IndexOf(expectedHalfEdges, expectedFace.FirstHalfEdge),
                IndexOf(actualHalfEdges, actualFace.FirstHalfEdge)
            );
        }
    }

    static MemoryStream WritePartialFile(Action<BinaryWriter> writeBody)
    {
        MemoryStream stream = new();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("TRMB"u8);
            writer.Write(1);
            writeBody(writer);
        }
        stream.Position = 0;
        return stream;
    }

    static List<Storage.Handle<TTag>> CollectHandles<TTag, TConnectivity>(
        Storage.TopologyStorage<TTag, TConnectivity> storage
    )
        where TTag : unmanaged
        where TConnectivity : unmanaged
    {
        var handles = new List<Storage.Handle<TTag>>(storage.LiveCount);
        foreach (var handle in storage.Live)
            handles.Add(handle);
        return handles;
    }

    static int IndexOf<TTag>(
        IReadOnlyList<Storage.Handle<TTag>> handles,
        Storage.Handle<TTag> handle
    )
        where TTag : unmanaged
    {
        if (handle.IsNull)
            return -1;
        for (int i = 0; i < handles.Count; i++)
        {
            if (handles[i] == handle)
                return i;
        }
        throw new InvalidOperationException($"Handle {handle} is not in the expected live set.");
    }

    static void AssertExactPositions(SpatialMesh expected, SpatialMesh actual)
    {
        var expectedPositions = expected.Vertices.GetNativeColumn<Vector3, VertexPositionTag>();
        var actualPositions = actual.Vertices.GetNativeColumn<Vector3, VertexPositionTag>();
        Assert.Equal(expectedPositions.Count, actualPositions.Count);
        for (int i = 0; i < expectedPositions.Count; i++)
            Assert.Equal(expectedPositions[i], actualPositions[i]);
    }

    static void SetDistinctTextureAttributes(SpatialMesh mesh)
    {
        int faceIndex = 0;
        foreach (FaceHandle face in mesh.EnumerateLiveFaces())
        {
            mesh.SetFaceMaterialSlot(face, faceIndex + 1);
            mesh.SetFaceUvsInitialized(face, faceIndex % 2 == 0);

            int cornerIndex = 0;
            foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
            {
                mesh.SetFaceCornerUv(corner, new Vector2(faceIndex + 0.25f, cornerIndex + 0.5f));
                cornerIndex++;
            }
            faceIndex++;
        }
    }

    static void AssertExactTextureAttributes(SpatialMesh expected, SpatialMesh actual)
    {
        var expectedFaces = CollectHandles(expected.Faces);
        var actualFaces = CollectHandles(actual.Faces);
        for (int i = 0; i < expectedFaces.Count; i++)
        {
            Assert.Equal(
                expected.GetFaceMaterialSlot(expectedFaces[i]),
                actual.GetFaceMaterialSlot(actualFaces[i])
            );
            Assert.Equal(
                expected.AreFaceUvsInitialized(expectedFaces[i]),
                actual.AreFaceUvsInitialized(actualFaces[i])
            );
        }

        var expectedCorners = CollectHandles(expected.HalfEdges);
        var actualCorners = CollectHandles(actual.HalfEdges);
        for (int i = 0; i < expectedCorners.Count; i++)
        {
            if (expected.GetHalfEdge(expectedCorners[i]).Face.IsNull)
                continue;
            Assert.Equal(
                expected.GetFaceCornerUv(expectedCorners[i]),
                actual.GetFaceCornerUv(actualCorners[i])
            );
        }
    }
}
