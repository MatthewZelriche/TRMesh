namespace TREditorSharp.Tests;

public sealed class HalfEdgeFaceSplitTests
{
    [Fact]
    public void SplitFace_QuadCreatesTwoTrianglesSharingNewEdge()
    {
        using HalfEdgeMesh mesh = BuildPolygon(4, out FaceHandle original);
        List<FaceCornerHandle> corners = CollectCorners(mesh, original);
        VertexHandle a = mesh.GetHalfEdge(corners[0]).Origin;
        VertexHandle c = mesh.GetHalfEdge(corners[2]).Origin;

        (FaceHandle first, FaceHandle second) = mesh.SplitFace(corners[0], corners[2]);

        Assert.False(mesh.IsFaceAlive(original));
        Assert.True(mesh.IsFaceAlive(first));
        Assert.True(mesh.IsFaceAlive(second));
        Assert.Equal(2, CountFaces(mesh));
        Assert.Equal(5, CountEdges(mesh));
        Assert.Equal(3, CollectCorners(mesh, first).Count);
        Assert.Equal(3, CollectCorners(mesh, second).Count);
        AssertFacesShareEdge(mesh, first, second, a, c);
        mesh.ValidateConsistency();
    }

    [Fact]
    public void SplitFace_HexagonCreatesTriangleAndPentagon()
    {
        using HalfEdgeMesh mesh = BuildPolygon(6, out FaceHandle original);
        List<FaceCornerHandle> corners = CollectCorners(mesh, original);

        (FaceHandle first, FaceHandle second) = mesh.SplitFace(corners[0], corners[2]);

        Assert.Equal(3, CollectCorners(mesh, first).Count);
        Assert.Equal(5, CollectCorners(mesh, second).Count);
        Assert.Equal(2, CountFaces(mesh));
        Assert.Equal(7, CountEdges(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void SplitFace_PreservesNeighborAttachedToOriginalBoundary()
    {
        using HalfEdgeMesh mesh = BuildQuadWithNeighbor(
            out FaceHandle quad,
            out FaceHandle neighbor
        );
        List<FaceCornerHandle> corners = CollectCorners(mesh, quad);

        mesh.SplitFace(corners[0], corners[2]);

        Assert.True(mesh.IsFaceAlive(neighbor));
        Assert.Equal(3, CountFaces(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void SplitFace_ReversedCornerOrderProducesValidFaces()
    {
        using HalfEdgeMesh mesh = BuildPolygon(5, out FaceHandle original);
        List<FaceCornerHandle> corners = CollectCorners(mesh, original);

        mesh.SplitFace(corners[3], corners[0]);

        Assert.Equal(2, CountFaces(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void SplitFace_AdjacentCornersThrowWithoutChangingMesh()
    {
        using HalfEdgeMesh mesh = BuildPolygon(4, out FaceHandle face);
        List<FaceCornerHandle> corners = CollectCorners(mesh, face);

        Assert.Throws<ArgumentException>(() => mesh.SplitFace(corners[0], corners[1]));

        Assert.True(mesh.IsFaceAlive(face));
        Assert.Equal(1, CountFaces(mesh));
        Assert.Equal(4, CountEdges(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void SplitFace_SameCornerThrowsWithoutChangingMesh()
    {
        using HalfEdgeMesh mesh = BuildPolygon(4, out FaceHandle face);
        FaceCornerHandle corner = CollectCorners(mesh, face)[0];

        Assert.Throws<ArgumentException>(() => mesh.SplitFace(corner, corner));

        Assert.True(mesh.IsFaceAlive(face));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void SplitFace_CornersFromDifferentFacesThrowWithoutChangingMesh()
    {
        using HalfEdgeMesh mesh = BuildTwoDisconnectedQuads(
            out FaceHandle first,
            out FaceHandle second
        );
        FaceCornerHandle firstCorner = CollectCorners(mesh, first)[0];
        FaceCornerHandle secondCorner = CollectCorners(mesh, second)[0];

        Assert.Throws<ArgumentException>(() => mesh.SplitFace(firstCorner, secondCorner));

        Assert.True(mesh.IsFaceAlive(first));
        Assert.True(mesh.IsFaceAlive(second));
        Assert.Equal(2, CountFaces(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void SplitFace_ExistingDiagonalThrowsWithoutChangingMesh()
    {
        using HalfEdgeMesh mesh = BuildQuadWithExistingDiagonal(
            out FaceHandle quad,
            out FaceHandle diagonalFace
        );
        List<FaceCornerHandle> corners = CollectCorners(mesh, quad);

        Assert.Throws<ArgumentException>(() => mesh.SplitFace(corners[0], corners[2]));

        Assert.True(mesh.IsFaceAlive(quad));
        Assert.True(mesh.IsFaceAlive(diagonalFace));
        Assert.Equal(2, CountFaces(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void SplitFace_DeadCornerThrowsWithoutChangingMesh()
    {
        using HalfEdgeMesh mesh = BuildPolygon(4, out FaceHandle face);
        FaceCornerHandle liveCorner = CollectCorners(mesh, face)[0];

        Assert.Throws<ArgumentException>(() => mesh.SplitFace(HalfEdgeHandle.Null, liveCorner));

        Assert.True(mesh.IsFaceAlive(face));
        mesh.ValidateConsistency();
    }

    private static HalfEdgeMesh BuildPolygon(int count, out FaceHandle face)
    {
        HalfEdgeMesh mesh = new();
        var vertices = new VertexHandle[count];
        for (int i = 0; i < count; i++)
            vertices[i] = mesh.Vertices.Allocate();
        face = mesh.AddFace(vertices);
        return mesh;
    }

    private static HalfEdgeMesh BuildQuadWithNeighbor(out FaceHandle quad, out FaceHandle neighbor)
    {
        HalfEdgeMesh mesh = new();
        VertexHandle a = mesh.Vertices.Allocate();
        VertexHandle b = mesh.Vertices.Allocate();
        VertexHandle c = mesh.Vertices.Allocate();
        VertexHandle d = mesh.Vertices.Allocate();
        VertexHandle e = mesh.Vertices.Allocate();
        VertexHandle f = mesh.Vertices.Allocate();
        quad = mesh.AddFace([a, b, c, d]);
        neighbor = mesh.AddFace([b, a, e, f]);
        return mesh;
    }

    private static HalfEdgeMesh BuildTwoDisconnectedQuads(
        out FaceHandle first,
        out FaceHandle second
    )
    {
        HalfEdgeMesh mesh = new();
        first = mesh.AddFace(
            [
                mesh.Vertices.Allocate(),
                mesh.Vertices.Allocate(),
                mesh.Vertices.Allocate(),
                mesh.Vertices.Allocate(),
            ]
        );
        second = mesh.AddFace(
            [
                mesh.Vertices.Allocate(),
                mesh.Vertices.Allocate(),
                mesh.Vertices.Allocate(),
                mesh.Vertices.Allocate(),
            ]
        );
        return mesh;
    }

    private static HalfEdgeMesh BuildQuadWithExistingDiagonal(
        out FaceHandle quad,
        out FaceHandle diagonalFace
    )
    {
        HalfEdgeMesh mesh = new();
        VertexHandle a = mesh.Vertices.Allocate();
        VertexHandle b = mesh.Vertices.Allocate();
        VertexHandle c = mesh.Vertices.Allocate();
        VertexHandle d = mesh.Vertices.Allocate();
        VertexHandle x = mesh.Vertices.Allocate();
        diagonalFace = mesh.AddFace([a, c, x]);
        quad = mesh.AddFace([a, b, c, d]);
        return mesh;
    }

    private static void AssertFacesShareEdge(
        HalfEdgeMesh mesh,
        FaceHandle first,
        FaceHandle second,
        VertexHandle a,
        VertexHandle b
    )
    {
        foreach (HalfEdgeHandle edge in mesh.HalfEdgesAroundFace(first))
        {
            HalfEdge halfEdge = mesh.GetHalfEdge(edge);
            HalfEdge twin = mesh.GetHalfEdge(halfEdge.Twin);
            if (
                (halfEdge.Origin == a && twin.Origin == b && twin.Face == second)
                || (halfEdge.Origin == b && twin.Origin == a && twin.Face == second)
            )
            {
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("Faces do not share the expected cut edge.");
    }

    private static List<FaceCornerHandle> CollectCorners(HalfEdgeMesh mesh, FaceHandle face)
    {
        List<FaceCornerHandle> corners = [];
        foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
            corners.Add(corner);
        return corners;
    }

    private static int CountFaces(HalfEdgeMesh mesh)
    {
        int count = 0;
        foreach (FaceHandle _ in mesh.EnumerateLiveFaces())
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
