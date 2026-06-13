using System.Numerics;

namespace TREditorSharp.Tests;

public sealed class SpatialMeshEdgeCollapseTests
{
    [Fact]
    public void TryCollapseEdge_InteriorEdgeBetweenTrianglesRemovesFacesAndDestination()
    {
        using SpatialMesh mesh = BuildAdjacentTriangles(
            out VertexHandle a,
            out _,
            out VertexHandle c,
            out _,
            out _,
            out _
        );
        HalfEdgeHandle edge = FindEdge(mesh, a, c);

        Assert.True(mesh.TryCollapseEdge(edge, out VertexHandle survivor));

        Assert.Equal(a, survivor);
        Assert.True(mesh.IsVertexAlive(a));
        Assert.False(mesh.IsVertexAlive(c));
        Assert.Equal(3, CountVertices(mesh));
        Assert.Equal(2, CountEdges(mesh));
        Assert.Equal(0, CountFaces(mesh));
        mesh.ValidateConsistency();
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.25f, 1f)]
    [InlineData(1f, 4f)]
    [InlineData(1.5f, 6f)]
    public void TryCollapseEdge_InterpolationParameterPositionsSurvivor(float t, float expectedX)
    {
        using SpatialMesh mesh = BuildTriangle(
            out VertexHandle a,
            out VertexHandle b,
            out _,
            out _
        );
        mesh.SetVertexPosition(a, Vector3.Zero);
        mesh.SetVertexPosition(b, Vector3.UnitX * 4f);

        Assert.True(mesh.TryCollapseEdge(FindEdge(mesh, a, b), out VertexHandle survivor, t));

        Assert.Equal(a, survivor);
        Assert.Equal(new Vector3(expectedX, 0f, 0f), mesh.GetVertexPosition(survivor));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryCollapseEdge_BoundaryEdgeRemovesDegenerateTriangle()
    {
        using SpatialMesh mesh = BuildTriangle(
            out VertexHandle a,
            out VertexHandle b,
            out VertexHandle c,
            out FaceHandle face
        );

        Assert.True(mesh.TryCollapseEdge(FindEdge(mesh, a, b), out VertexHandle survivor));

        Assert.Equal(a, survivor);
        Assert.False(mesh.IsVertexAlive(b));
        Assert.False(mesh.IsFaceAlive(face));
        Assert.True(HasEdge(mesh, a, c));
        Assert.Equal(1, CountEdges(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryCollapseEdge_NonTriangleFaceLosesOneCorner()
    {
        using SpatialMesh mesh = BuildQuad(
            out VertexHandle a,
            out VertexHandle b,
            out _,
            out _,
            out FaceHandle face
        );

        Assert.True(mesh.TryCollapseEdge(FindEdge(mesh, a, b), out VertexHandle survivor));

        Assert.Equal(a, survivor);
        Assert.False(mesh.IsVertexAlive(b));
        Assert.True(mesh.IsFaceAlive(face));
        Assert.Equal(3, CountFaceCorners(mesh, face));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryCollapseEdge_InteriorEdgeBetweenQuadsShrinksBothFaces()
    {
        using SpatialMesh mesh = BuildAdjacentQuads(
            out VertexHandle a,
            out VertexHandle b,
            out FaceHandle first,
            out FaceHandle second
        );

        Assert.True(mesh.TryCollapseEdge(FindEdge(mesh, a, b), out VertexHandle survivor));

        Assert.Equal(a, survivor);
        Assert.False(mesh.IsVertexAlive(b));
        Assert.Equal(3, CountFaceCorners(mesh, first));
        Assert.Equal(3, CountFaceCorners(mesh, second));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryCollapseEdge_IsolatedWireEdgeLeavesSurvivorIsolated()
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

        Assert.True(mesh.TryCollapseEdge(edge, out VertexHandle survivor));

        Assert.Equal(a, survivor);
        Assert.False(mesh.IsVertexAlive(b));
        Assert.Equal(2, CountVertices(mesh));
        Assert.Equal(0, CountEdges(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryCollapseEdge_SharedNeighborOutsideAdjacentTrianglesReturnsFalseWithoutMutation()
    {
        using SpatialMesh mesh = BuildLinkConditionViolation(
            out VertexHandle a,
            out VertexHandle b
        );
        HalfEdgeHandle edge = FindEdge(mesh, a, b);
        Vector3 originalPosition = mesh.GetVertexPosition(a);
        int verticesBefore = CountVertices(mesh);
        int edgesBefore = CountEdges(mesh);
        int facesBefore = CountFaces(mesh);

        Assert.False(mesh.TryCollapseEdge(edge, out VertexHandle survivor));

        Assert.True(survivor.IsNull);
        Assert.Equal(originalPosition, mesh.GetVertexPosition(a));
        Assert.True(mesh.IsVertexAlive(b));
        Assert.Equal(verticesBefore, CountVertices(mesh));
        Assert.Equal(edgesBefore, CountEdges(mesh));
        Assert.Equal(facesBefore, CountFaces(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryCollapseEdge_DeadEdgeReturnsFalseWithoutMutation()
    {
        using SpatialMesh mesh = BuildTriangle(out _, out _, out _, out _);
        int verticesBefore = CountVertices(mesh);

        Assert.False(mesh.TryCollapseEdge(HalfEdgeHandle.Null, out VertexHandle survivor));

        Assert.True(survivor.IsNull);
        Assert.Equal(verticesBefore, CountVertices(mesh));
        mesh.ValidateConsistency();
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

    private static SpatialMesh BuildQuad(
        out VertexHandle a,
        out VertexHandle b,
        out VertexHandle c,
        out VertexHandle d,
        out FaceHandle face
    )
    {
        SpatialMesh mesh = new();
        a = mesh.AddVertex(Vector3.Zero);
        b = mesh.AddVertex(Vector3.UnitX);
        c = mesh.AddVertex(Vector3.One);
        d = mesh.AddVertex(Vector3.UnitY);
        face = mesh.AddFace([a, b, c, d]);
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

    private static SpatialMesh BuildAdjacentQuads(
        out VertexHandle a,
        out VertexHandle b,
        out FaceHandle first,
        out FaceHandle second
    )
    {
        SpatialMesh mesh = new();
        a = mesh.AddVertex(Vector3.Zero);
        b = mesh.AddVertex(Vector3.UnitX);
        VertexHandle c = mesh.AddVertex(new Vector3(1f, -1f, 0f));
        VertexHandle d = mesh.AddVertex(new Vector3(0f, -1f, 0f));
        VertexHandle e = mesh.AddVertex(Vector3.UnitY);
        VertexHandle f = mesh.AddVertex(Vector3.One);
        first = mesh.AddFace([a, b, c, d]);
        second = mesh.AddFace([b, a, e, f]);
        return mesh;
    }

    private static SpatialMesh BuildLinkConditionViolation(out VertexHandle a, out VertexHandle b)
    {
        SpatialMesh mesh = new();
        a = mesh.AddVertex(Vector3.Zero);
        b = mesh.AddVertex(Vector3.UnitX);
        VertexHandle c = mesh.AddVertex(Vector3.UnitY);
        VertexHandle shared = mesh.AddVertex(Vector3.One);
        VertexHandle x = mesh.AddVertex(Vector3.UnitZ);
        VertexHandle y = mesh.AddVertex(Vector3.UnitX + Vector3.UnitZ);
        mesh.AddFace([a, b, c]);
        mesh.AddFace([a, shared, x]);
        mesh.AddFace([b, y, shared]);
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
                return edge;
        }

        throw new InvalidOperationException();
    }

    private static bool HasEdge(SpatialMesh mesh, VertexHandle first, VertexHandle second)
    {
        foreach (HalfEdgeHandle edge in mesh.EnumerateLiveHalfEdges())
        {
            HalfEdge halfEdge = mesh.GetHalfEdge(edge);
            VertexHandle destination = mesh.GetHalfEdge(halfEdge.Twin).Origin;
            if (
                (halfEdge.Origin == first && destination == second)
                || (halfEdge.Origin == second && destination == first)
            )
            {
                return true;
            }
        }

        return false;
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

    private static int CountFaceCorners(SpatialMesh mesh, FaceHandle face)
    {
        int count = 0;
        foreach (FaceCornerHandle _ in mesh.HalfEdgesAroundFace(face))
            count++;
        return count;
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
}
