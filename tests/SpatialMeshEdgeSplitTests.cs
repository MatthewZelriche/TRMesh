using System.Numerics;

namespace TREditorSharp.Tests;

public sealed class SpatialMeshEdgeSplitTests
{
    [Fact]
    public void SplitEdge_InteriorEdgeAddsCornerToBothFaces()
    {
        using SpatialMesh mesh = BuildAdjacentTriangles(
            out VertexHandle a,
            out _,
            out VertexHandle c,
            out _,
            out FaceHandle first,
            out FaceHandle second
        );
        HalfEdgeHandle edge = FindEdge(mesh, a, c);

        VertexHandle inserted = mesh.SplitEdge(edge);

        Assert.Equal(5, CountVertices(mesh));
        Assert.Equal(6, CountEdges(mesh));
        Assert.Equal(2, CountFaces(mesh));
        Assert.Equal(4, CollectFaceVertices(mesh, first).Count);
        Assert.Equal(4, CollectFaceVertices(mesh, second).Count);
        Assert.Contains(inserted, CollectFaceVertices(mesh, first));
        Assert.Contains(inserted, CollectFaceVertices(mesh, second));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void SplitEdge_BoundaryEdgeAddsOneFaceCornerAndSplitsBoundaryLoop()
    {
        using SpatialMesh mesh = BuildTriangle(
            out VertexHandle a,
            out VertexHandle b,
            out _,
            out FaceHandle face
        );
        HalfEdgeHandle edge = FindEdge(mesh, a, b);

        VertexHandle inserted = mesh.SplitEdge(edge);

        Assert.Equal(4, CountVertices(mesh));
        Assert.Equal(4, CountEdges(mesh));
        Assert.Equal(4, CollectFaceVertices(mesh, face).Count);
        Assert.Contains(inserted, CollectFaceVertices(mesh, face));
        Assert.Equal(4, CountBoundaryHalfEdges(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void SplitEdge_IsolatedWireEdgeSplitsTwoHalfEdgeLoop()
    {
        using SpatialMesh mesh = BuildTriangle(
            out VertexHandle a,
            out VertexHandle b,
            out _,
            out FaceHandle face
        );
        HalfEdgeHandle edge = FindEdge(mesh, a, b);
        Assert.True(mesh.RemoveFace(face));
        foreach (HalfEdgeHandle other in UniqueEdges(mesh))
        {
            if (other != edge && mesh.GetHalfEdge(other).Twin != edge)
                Assert.True(mesh.RemoveEdge(other));
        }

        VertexHandle inserted = mesh.SplitEdge(edge);

        Assert.True(mesh.IsVertexAlive(inserted));
        Assert.Equal(2, CountEdges(mesh));
        Assert.Equal(0, CountFaces(mesh));
        mesh.ValidateConsistency();
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.25f, 1f)]
    [InlineData(1f, 4f)]
    [InlineData(1.5f, 6f)]
    public void SplitEdge_InterpolationParameterPositionsNewVertex(float t, float expectedX)
    {
        using SpatialMesh mesh = BuildTriangle(
            out VertexHandle a,
            out VertexHandle b,
            out _,
            out _
        );
        mesh.SetVertexPosition(a, Vector3.Zero);
        mesh.SetVertexPosition(b, Vector3.UnitX * 4f);

        VertexHandle inserted = mesh.SplitEdge(FindEdge(mesh, a, b), t);

        Assert.Equal(new Vector3(expectedX, 0f, 0f), mesh.GetVertexPosition(inserted));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void SplitEdge_InterpolatesInitializedUvsOnBothFaces()
    {
        using SpatialMesh mesh = BuildAdjacentTriangles(
            out VertexHandle a,
            out _,
            out VertexHandle c,
            out _,
            out FaceHandle first,
            out FaceHandle second
        );
        HalfEdgeHandle edge = FindEdge(mesh, a, c);
        HalfEdgeHandle twin = mesh.GetHalfEdge(edge).Twin;
        mesh.SetFaceCornerUv(edge, new Vector2(0f, 0f));
        mesh.SetFaceCornerUv(mesh.GetHalfEdge(edge).Next, new Vector2(4f, 2f));
        mesh.SetFaceCornerUv(twin, new Vector2(8f, 6f));
        mesh.SetFaceCornerUv(mesh.GetHalfEdge(twin).Next, new Vector2(2f, 0f));
        mesh.SetFaceUvsInitialized(first, true);
        mesh.SetFaceUvsInitialized(second, true);

        VertexHandle inserted = mesh.SplitEdge(edge, 0.25f);

        FaceCornerHandle firstCorner = FindCorner(mesh, first, inserted);
        FaceCornerHandle secondCorner = FindCorner(mesh, second, inserted);
        Assert.Equal(new Vector2(1f, 0.5f), mesh.GetFaceCornerUv(firstCorner));
        Assert.Equal(new Vector2(3.5f, 1.5f), mesh.GetFaceCornerUv(secondCorner));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void SplitEdge_DeadEdgeThrowsWithoutChangingMesh()
    {
        using SpatialMesh mesh = BuildTriangle(out _, out _, out _, out _);
        int verticesBefore = CountVertices(mesh);
        int edgesBefore = CountEdges(mesh);
        int facesBefore = CountFaces(mesh);

        Assert.Throws<ArgumentException>(() => mesh.SplitEdge(HalfEdgeHandle.Null));

        Assert.Equal(verticesBefore, CountVertices(mesh));
        Assert.Equal(edgesBefore, CountEdges(mesh));
        Assert.Equal(facesBefore, CountFaces(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void SplitEdge_DeadTwinThrowsWithoutAllocating()
    {
        using SpatialMesh mesh = BuildTriangle(
            out VertexHandle a,
            out VertexHandle b,
            out _,
            out _
        );
        HalfEdgeHandle edge = FindEdge(mesh, a, b);
        HalfEdgeHandle twin = mesh.GetHalfEdge(edge).Twin;
        mesh.HalfEdges.Free(twin);
        int verticesBefore = CountVertices(mesh);
        int halfEdgesBefore = mesh.HalfEdges.LiveCount;

        Assert.Throws<ArgumentException>(() => mesh.SplitEdge(edge));

        Assert.Equal(verticesBefore, CountVertices(mesh));
        Assert.Equal(halfEdgesBefore, mesh.HalfEdges.LiveCount);
    }

    private static SpatialMesh BuildTriangle(
        out VertexHandle a,
        out VertexHandle b,
        out VertexHandle c,
        out FaceHandle face
    )
    {
        SpatialMesh mesh = new();
        a = mesh.AddVertex(Vector3.Zero);
        b = mesh.AddVertex(Vector3.UnitX);
        c = mesh.AddVertex(Vector3.UnitY);
        face = mesh.AddFace([a, b, c]);
        return mesh;
    }

    private static SpatialMesh BuildAdjacentTriangles(
        out VertexHandle a,
        out VertexHandle b,
        out VertexHandle c,
        out VertexHandle d,
        out FaceHandle first,
        out FaceHandle second
    )
    {
        SpatialMesh mesh = new();
        a = mesh.AddVertex(Vector3.Zero);
        b = mesh.AddVertex(Vector3.UnitX);
        c = mesh.AddVertex(Vector3.One);
        d = mesh.AddVertex(Vector3.UnitY);
        first = mesh.AddFace([a, c, b]);
        second = mesh.AddFace([a, d, c]);
        return mesh;
    }

    private static HalfEdgeHandle FindEdge(
        SpatialMesh mesh,
        VertexHandle origin,
        VertexHandle destination
    )
    {
        foreach (HalfEdgeHandle edge in mesh.EnumerateLiveHalfEdges())
        {
            HalfEdge halfEdge = mesh.GetHalfEdge(edge);
            if (halfEdge.Origin == origin && mesh.GetHalfEdge(halfEdge.Twin).Origin == destination)
            {
                return edge;
            }
        }

        throw new InvalidOperationException();
    }

    private static FaceCornerHandle FindCorner(
        SpatialMesh mesh,
        FaceHandle face,
        VertexHandle vertex
    )
    {
        foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
        {
            if (mesh.GetHalfEdge(corner).Origin == vertex)
                return corner;
        }

        throw new InvalidOperationException();
    }

    private static List<HalfEdgeHandle> UniqueEdges(SpatialMesh mesh)
    {
        HashSet<HalfEdgeHandle> visited = [];
        List<HalfEdgeHandle> edges = [];
        foreach (HalfEdgeHandle edge in mesh.EnumerateLiveHalfEdges())
        {
            if (!visited.Add(edge))
                continue;

            visited.Add(mesh.GetHalfEdge(edge).Twin);
            edges.Add(edge);
        }
        return edges;
    }

    private static List<VertexHandle> CollectFaceVertices(SpatialMesh mesh, FaceHandle face)
    {
        List<VertexHandle> vertices = [];
        foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
            vertices.Add(mesh.GetHalfEdge(corner).Origin);
        return vertices;
    }

    private static int CountVertices(SpatialMesh mesh)
    {
        int count = 0;
        foreach (VertexHandle _ in mesh.EnumerateLiveVertices())
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

    private static int CountFaces(SpatialMesh mesh)
    {
        int count = 0;
        foreach (FaceHandle _ in mesh.EnumerateLiveFaces())
            count++;
        return count;
    }

    private static int CountBoundaryHalfEdges(SpatialMesh mesh)
    {
        int count = 0;
        foreach (HalfEdgeHandle edge in mesh.EnumerateLiveHalfEdges())
        {
            if (mesh.GetHalfEdge(edge).Face.IsNull)
                count++;
        }
        return count;
    }
}
