namespace TREditorSharp.Tests;

public sealed class HalfEdgeEdgeFlipTests
{
    [Fact]
    public void FlipEdge_TwoTriangleQuadRotatesDiagonalAndPreservesHandles()
    {
        using HalfEdgeMesh mesh = BuildTwoTriangleQuad(
            out VertexHandle a,
            out VertexHandle b,
            out VertexHandle c,
            out VertexHandle d,
            out FaceHandle first,
            out FaceHandle second
        );
        HalfEdgeHandle edge = FindEdge(mesh, a, c);
        HalfEdgeHandle twin = mesh.GetHalfEdge(edge).Twin;
        int verticesBefore = CountVertices(mesh);
        int edgesBefore = CountEdges(mesh);
        int facesBefore = CountFaces(mesh);

        Assert.True(mesh.FlipEdge(edge));

        Assert.True(mesh.IsHalfEdgeAlive(edge));
        Assert.True(mesh.IsHalfEdgeAlive(twin));
        Assert.True(mesh.IsFaceAlive(first));
        Assert.True(mesh.IsFaceAlive(second));
        Assert.False(HasEdge(mesh, a, c));
        Assert.True(HasEdge(mesh, b, d));
        Assert.Equal(3, CountFaceCorners(mesh, first));
        Assert.Equal(3, CountFaceCorners(mesh, second));
        Assert.Equal(verticesBefore, CountVertices(mesh));
        Assert.Equal(edgesBefore, CountEdges(mesh));
        Assert.Equal(facesBefore, CountFaces(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void FlipEdge_DoubleFlipRestoresOriginalDiagonal()
    {
        using HalfEdgeMesh mesh = BuildTwoTriangleQuad(
            out VertexHandle a,
            out VertexHandle b,
            out VertexHandle c,
            out VertexHandle d,
            out _,
            out _
        );
        HalfEdgeHandle edge = FindEdge(mesh, a, c);

        Assert.True(mesh.FlipEdge(edge));
        Assert.True(mesh.FlipEdge(edge));

        Assert.True(HasEdge(mesh, a, c));
        Assert.False(HasEdge(mesh, b, d));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void FlipEdge_BoundaryEdgeReturnsFalseWithoutMutation()
    {
        using HalfEdgeMesh mesh = BuildTriangle(out VertexHandle a, out VertexHandle b, out _);
        HalfEdgeHandle edge = FindEdge(mesh, a, b);

        Assert.False(mesh.FlipEdge(edge));

        Assert.True(HasEdge(mesh, a, b));
        Assert.Equal(1, CountFaces(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void FlipEdge_NonTriangleFaceReturnsFalseWithoutMutation()
    {
        using HalfEdgeMesh mesh = BuildTriangleAndQuad(out VertexHandle a, out VertexHandle c);
        HalfEdgeHandle edge = FindEdge(mesh, a, c);

        Assert.False(mesh.FlipEdge(edge));

        Assert.True(HasEdge(mesh, a, c));
        Assert.Equal(2, CountFaces(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void FlipEdge_ExistingReplacementEdgeReturnsFalseWithoutMutation()
    {
        using HalfEdgeMesh mesh = BuildTwoTrianglesWithExistingOppositeEdge(
            out VertexHandle a,
            out VertexHandle b,
            out VertexHandle c,
            out VertexHandle d
        );
        HalfEdgeHandle edge = FindEdge(mesh, a, c);

        Assert.False(mesh.FlipEdge(edge));

        Assert.True(HasEdge(mesh, a, c));
        Assert.True(HasEdge(mesh, b, d));
        Assert.Equal(4, CountFaces(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void FlipEdge_DeadEdgeReturnsFalse()
    {
        using HalfEdgeMesh mesh = BuildTriangle(out _, out _, out _);

        Assert.False(mesh.FlipEdge(HalfEdgeHandle.Null));

        mesh.ValidateConsistency();
    }

    private static HalfEdgeMesh BuildTwoTriangleQuad(
        out VertexHandle a,
        out VertexHandle b,
        out VertexHandle c,
        out VertexHandle d,
        out FaceHandle first,
        out FaceHandle second
    )
    {
        HalfEdgeMesh mesh = new();
        a = mesh.Vertices.Allocate();
        b = mesh.Vertices.Allocate();
        c = mesh.Vertices.Allocate();
        d = mesh.Vertices.Allocate();
        first = mesh.AddFace([a, c, b]);
        second = mesh.AddFace([a, d, c]);
        return mesh;
    }

    private static HalfEdgeMesh BuildTriangle(
        out VertexHandle a,
        out VertexHandle b,
        out VertexHandle c
    )
    {
        HalfEdgeMesh mesh = new();
        a = mesh.Vertices.Allocate();
        b = mesh.Vertices.Allocate();
        c = mesh.Vertices.Allocate();
        mesh.AddFace([a, b, c]);
        return mesh;
    }

    private static HalfEdgeMesh BuildTriangleAndQuad(out VertexHandle a, out VertexHandle c)
    {
        HalfEdgeMesh mesh = new();
        a = mesh.Vertices.Allocate();
        VertexHandle b = mesh.Vertices.Allocate();
        c = mesh.Vertices.Allocate();
        VertexHandle d = mesh.Vertices.Allocate();
        VertexHandle e = mesh.Vertices.Allocate();
        mesh.AddFace([a, c, b]);
        mesh.AddFace([c, a, d, e]);
        return mesh;
    }

    private static HalfEdgeMesh BuildTwoTrianglesWithExistingOppositeEdge(
        out VertexHandle a,
        out VertexHandle b,
        out VertexHandle c,
        out VertexHandle d
    )
    {
        HalfEdgeMesh mesh = BuildTwoTriangleQuad(out a, out b, out c, out d, out _, out _);
        mesh.AddFace([b, c, d]);
        mesh.AddFace([b, d, a]);
        return mesh;
    }

    private static bool HasEdge(HalfEdgeMesh mesh, VertexHandle a, VertexHandle b)
    {
        foreach (HalfEdgeHandle edge in mesh.EnumerateLiveHalfEdges())
        {
            HalfEdge halfEdge = mesh.GetHalfEdge(edge);
            VertexHandle destination = mesh.GetHalfEdge(halfEdge.Twin).Origin;
            if (
                (halfEdge.Origin == a && destination == b)
                || (halfEdge.Origin == b && destination == a)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static HalfEdgeHandle FindEdge(
        HalfEdgeMesh mesh,
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

    private static int CountFaceCorners(HalfEdgeMesh mesh, FaceHandle face)
    {
        int count = 0;
        foreach (FaceCornerHandle _ in mesh.HalfEdgesAroundFace(face))
            count++;
        return count;
    }

    private static int CountFaces(HalfEdgeMesh mesh)
    {
        int count = 0;
        foreach (FaceHandle _ in mesh.EnumerateLiveFaces())
            count++;
        return count;
    }

    private static int CountVertices(HalfEdgeMesh mesh)
    {
        int count = 0;
        foreach (VertexHandle _ in mesh.EnumerateLiveVertices())
            count++;
        return count;
    }

    private static int CountEdges(HalfEdgeMesh mesh)
    {
        int count = 0;
        foreach (HalfEdgeHandle _ in mesh.EnumerateLiveHalfEdges())
            count++;
        return count / 2;
    }
}
