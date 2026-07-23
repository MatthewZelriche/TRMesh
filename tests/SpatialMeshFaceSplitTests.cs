using System.Numerics;

namespace TREditorSharp.Tests;

public sealed class SpatialMeshFaceSplitTests
{
    [Fact]
    public void SplitFace_InitializedNgonPreservesMaterialAndUvsThroughVirtualDispatch()
    {
        using SpatialMesh mesh = BuildPolygon(6, out FaceHandle source);
        List<FaceCornerHandle> corners = CollectCorners(mesh, source);
        Dictionary<VertexHandle, Vector2> expectedUvs = [];
        for (int i = 0; i < corners.Count; i++)
        {
            Vector2 uv = new(i + 0.25f, i * 2f - 0.5f);
            mesh.SetFaceCornerUv(corners[i], uv);
            expectedUvs.Add(mesh.GetHalfEdge(corners[i]).Origin, uv);
        }
        mesh.SetFaceMaterialSlot(source, 17);
        mesh.SetFaceUvsInitialized(source, true);
        HalfEdgeMesh topology = mesh;

        (FaceHandle first, FaceHandle second) = topology.SplitFace(corners[0], corners[2]);

        Assert.False(mesh.IsFaceAlive(source));
        Assert.Equal(3, CollectCorners(mesh, first).Count);
        Assert.Equal(5, CollectCorners(mesh, second).Count);
        AssertFaceAttributes(mesh, first, 17, expectedUvs);
        AssertFaceAttributes(mesh, second, 17, expectedUvs);
        mesh.ValidateConsistency();
    }

    [Fact]
    public void SplitFace_UninitializedSourceCopiesMaterialAndLeavesUvsUninitialized()
    {
        using SpatialMesh mesh = BuildPolygon(4, out FaceHandle source);
        List<FaceCornerHandle> corners = CollectCorners(mesh, source);
        mesh.SetFaceMaterialSlot(source, 23);

        (FaceHandle first, FaceHandle second) = mesh.SplitFace(corners[0], corners[2]);

        Assert.Equal(23, mesh.GetFaceMaterialSlot(first));
        Assert.Equal(23, mesh.GetFaceMaterialSlot(second));
        Assert.False(mesh.AreFaceUvsInitialized(first));
        Assert.False(mesh.AreFaceUvsInitialized(second));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void SplitFace_PreservesNeighborAttributes()
    {
        using SpatialMesh mesh = BuildQuadWithNeighbor(
            out FaceHandle source,
            out FaceHandle neighbor
        );
        List<FaceCornerHandle> sourceCorners = CollectCorners(mesh, source);
        List<FaceCornerHandle> neighborCorners = CollectCorners(mesh, neighbor);
        Dictionary<FaceCornerHandle, Vector2> neighborUvs = [];
        for (int i = 0; i < neighborCorners.Count; i++)
        {
            Vector2 uv = new(10f + i, 20f - i);
            mesh.SetFaceCornerUv(neighborCorners[i], uv);
            neighborUvs.Add(neighborCorners[i], uv);
        }
        mesh.SetFaceMaterialSlot(neighbor, 29);
        mesh.SetFaceUvsInitialized(neighbor, true);

        mesh.SplitFace(sourceCorners[0], sourceCorners[2]);

        Assert.True(mesh.IsFaceAlive(neighbor));
        Assert.Equal(29, mesh.GetFaceMaterialSlot(neighbor));
        Assert.True(mesh.AreFaceUvsInitialized(neighbor));
        foreach ((FaceCornerHandle corner, Vector2 uv) in neighborUvs)
            Assert.Equal(uv, mesh.GetFaceCornerUv(corner));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void SplitFace_InvalidSplitPreservesSourceAttributesAndTopology()
    {
        using SpatialMesh mesh = BuildPolygon(4, out FaceHandle source);
        List<FaceCornerHandle> corners = CollectCorners(mesh, source);
        Dictionary<FaceCornerHandle, Vector2> expectedUvs = [];
        for (int i = 0; i < corners.Count; i++)
        {
            Vector2 uv = new(i, -i);
            mesh.SetFaceCornerUv(corners[i], uv);
            expectedUvs.Add(corners[i], uv);
        }
        mesh.SetFaceMaterialSlot(source, 31);
        mesh.SetFaceUvsInitialized(source, true);
        int edgesBefore = CountEdges(mesh);

        Assert.Throws<ArgumentException>(() => mesh.SplitFace(corners[0], corners[1]));

        Assert.True(mesh.IsFaceAlive(source));
        Assert.Equal(1, CountFaces(mesh));
        Assert.Equal(edgesBefore, CountEdges(mesh));
        Assert.Equal(31, mesh.GetFaceMaterialSlot(source));
        Assert.True(mesh.AreFaceUvsInitialized(source));
        foreach ((FaceCornerHandle corner, Vector2 uv) in expectedUvs)
            Assert.Equal(uv, mesh.GetFaceCornerUv(corner));
        mesh.ValidateConsistency();
    }

    private static SpatialMesh BuildPolygon(int count, out FaceHandle face)
    {
        SpatialMesh mesh = new();
        VertexHandle[] vertices = new VertexHandle[count];
        for (int i = 0; i < count; i++)
        {
            float angle = MathF.Tau * i / count;
            vertices[i] = mesh.AddVertex(new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f));
        }
        face = mesh.AddFace(vertices);
        return mesh;
    }

    private static SpatialMesh BuildQuadWithNeighbor(
        out FaceHandle source,
        out FaceHandle neighbor
    )
    {
        SpatialMesh mesh = new();
        VertexHandle a = mesh.AddVertex(Vector3.Zero);
        VertexHandle b = mesh.AddVertex(Vector3.UnitX);
        VertexHandle c = mesh.AddVertex(Vector3.One);
        VertexHandle d = mesh.AddVertex(Vector3.UnitY);
        VertexHandle e = mesh.AddVertex(-Vector3.UnitY);
        VertexHandle f = mesh.AddVertex(Vector3.UnitX - Vector3.UnitY);
        source = mesh.AddFace([a, b, c, d]);
        neighbor = mesh.AddFace([b, a, e, f]);
        return mesh;
    }

    private static void AssertFaceAttributes(
        SpatialMesh mesh,
        FaceHandle face,
        int materialSlot,
        IReadOnlyDictionary<VertexHandle, Vector2> expectedUvs
    )
    {
        Assert.Equal(materialSlot, mesh.GetFaceMaterialSlot(face));
        Assert.True(mesh.AreFaceUvsInitialized(face));
        foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
        {
            VertexHandle vertex = mesh.GetHalfEdge(corner).Origin;
            Assert.Equal(expectedUvs[vertex], mesh.GetFaceCornerUv(corner));
        }
    }

    private static List<FaceCornerHandle> CollectCorners(SpatialMesh mesh, FaceHandle face)
    {
        List<FaceCornerHandle> corners = [];
        foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
            corners.Add(corner);
        return corners;
    }

    private static int CountFaces(SpatialMesh mesh)
    {
        int count = 0;
        foreach (FaceHandle _ in mesh.EnumerateLiveFaces())
            count++;
        return count;
    }

    private static int CountEdges(SpatialMesh mesh)
    {
        int count = 0;
        foreach (HalfEdgeHandle _ in mesh.EnumerateLiveHalfEdges())
            count++;
        return count / 2;
    }
}
