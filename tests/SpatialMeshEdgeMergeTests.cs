using System.Numerics;

namespace TREditorSharp.Tests;

public sealed class SpatialMeshEdgeMergeTests
{
    [Fact]
    public void TryMergeEdges_QuadSourceIntoTargetPreservesTargetEdgeAndPositions()
    {
        using SpatialMesh mesh = BuildQuad(
            out VertexHandle a,
            out VertexHandle b,
            out VertexHandle c,
            out VertexHandle d,
            out FaceHandle face
        );
        HalfEdgeHandle source = FindEdge(mesh, a, b);
        HalfEdgeHandle target = FindEdge(mesh, c, d);
        HalfEdgeHandle targetTwin = mesh.GetHalfEdge(target).Twin;
        Vector3 cPosition = new(4f, 5f, 6f);
        Vector3 dPosition = new(7f, 8f, 9f);
        mesh.SetVertexPosition(c, cPosition);
        mesh.SetVertexPosition(d, dPosition);

        Assert.True(mesh.TryMergeEdges(source, target));

        Assert.False(mesh.IsFaceAlive(face));
        Assert.False(mesh.IsVertexAlive(a));
        Assert.False(mesh.IsVertexAlive(b));
        Assert.True(mesh.IsVertexAlive(c));
        Assert.True(mesh.IsVertexAlive(d));
        Assert.True(mesh.IsHalfEdgeAlive(target));
        Assert.True(mesh.IsHalfEdgeAlive(targetTwin));
        Assert.Equal(c, mesh.GetHalfEdge(target).Origin);
        Assert.Equal(d, mesh.GetHalfEdge(targetTwin).Origin);
        Assert.Equal(cPosition, mesh.GetVertexPosition(c));
        Assert.Equal(dPosition, mesh.GetVertexPosition(d));
        Assert.Equal(1, CountEdges(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryMergeEdges_ReverseDirectionPreservesOtherEdge()
    {
        using SpatialMesh mesh = BuildQuad(
            out VertexHandle a,
            out VertexHandle b,
            out VertexHandle c,
            out VertexHandle d,
            out _
        );
        HalfEdgeHandle source = FindEdge(mesh, c, d);
        HalfEdgeHandle target = FindEdge(mesh, a, b);
        HalfEdgeHandle targetTwin = mesh.GetHalfEdge(target).Twin;

        Assert.True(mesh.TryMergeEdges(source, target));

        Assert.True(mesh.IsVertexAlive(a));
        Assert.True(mesh.IsVertexAlive(b));
        Assert.False(mesh.IsVertexAlive(c));
        Assert.False(mesh.IsVertexAlive(d));
        Assert.True(mesh.IsHalfEdgeAlive(target));
        Assert.True(mesh.IsHalfEdgeAlive(targetTwin));
        Assert.Equal(a, mesh.GetHalfEdge(target).Origin);
        Assert.Equal(b, mesh.GetHalfEdge(targetTwin).Origin);
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryMergeEdges_SurroundingAttachedFacesRewireCorrectly()
    {
        using SpatialMesh mesh = BuildQuadWithSurroundingFaces(
            out VertexHandle a,
            out VertexHandle b,
            out VertexHandle c,
            out VertexHandle d,
            out FaceHandle sourceSide,
            out FaceHandle nextSide,
            out FaceHandle targetSide,
            out FaceHandle prevSide
        );
        HalfEdgeHandle source = FindEdge(mesh, a, b);
        HalfEdgeHandle target = FindEdge(mesh, c, d);

        Assert.True(mesh.TryMergeEdges(source, target));

        Assert.Equal(sourceSide, mesh.GetHalfEdge(target).Face);
        Assert.Equal(4, CountFaceCorners(mesh, sourceSide));
        Assert.Equal(3, CountFaceCorners(mesh, nextSide));
        Assert.Equal(4, CountFaceCorners(mesh, targetSide));
        Assert.Equal(3, CountFaceCorners(mesh, prevSide));
        Assert.All(
            new[] { sourceSide, nextSide, targetSide, prevSide },
            face =>
            {
                Assert.DoesNotContain(a, CollectFaceVertices(mesh, face));
                Assert.DoesNotContain(b, CollectFaceVertices(mesh, face));
            }
        );
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryMergeEdges_AdjacentEdgesReturnFalseWithoutMutation()
    {
        using SpatialMesh mesh = BuildQuad(
            out VertexHandle a,
            out VertexHandle b,
            out VertexHandle c,
            out _,
            out _
        );

        AssertMergeRejectedWithoutMutation(mesh, FindEdge(mesh, a, b), FindEdge(mesh, b, c));
    }

    [Fact]
    public void TryMergeEdges_EdgesFromDifferentFacesReturnFalseWithoutMutation()
    {
        using SpatialMesh mesh = BuildTwoQuads(
            out HalfEdgeHandle source,
            out HalfEdgeHandle target
        );

        AssertMergeRejectedWithoutMutation(mesh, source, target);
    }

    [Fact]
    public void TryMergeEdges_NonQuadFaceReturnsFalseWithoutMutation()
    {
        using SpatialMesh mesh = BuildTriangle(
            out VertexHandle a,
            out VertexHandle b,
            out VertexHandle c
        );

        AssertMergeRejectedWithoutMutation(mesh, FindEdge(mesh, a, b), FindEdge(mesh, c, a));
    }

    [Fact]
    public void TryMergeEdges_DuplicateResultReturnsFalseWithoutMutation()
    {
        using SpatialMesh mesh = BuildDuplicateResult(
            out VertexHandle a,
            out VertexHandle b,
            out VertexHandle c,
            out VertexHandle d
        );

        AssertMergeRejectedWithoutMutation(mesh, FindEdge(mesh, a, b), FindEdge(mesh, c, d));
    }

    [Fact]
    public void TryMergeEdges_DeadEdgeReturnsFalseWithoutMutation()
    {
        using SpatialMesh mesh = BuildQuad(out _, out _, out _, out _, out _);
        HalfEdgeHandle liveEdge = FirstHalfEdge(mesh);

        AssertMergeRejectedWithoutMutation(mesh, HalfEdgeHandle.Null, liveEdge);
        AssertMergeRejectedWithoutMutation(mesh, liveEdge, HalfEdgeHandle.Null);
    }

    private static void AssertMergeRejectedWithoutMutation(
        SpatialMesh mesh,
        HalfEdgeHandle source,
        HalfEdgeHandle target
    )
    {
        int verticesBefore = CountVertices(mesh);
        int edgesBefore = CountEdges(mesh);
        int facesBefore = CountFaces(mesh);

        Assert.False(mesh.TryMergeEdges(source, target));

        Assert.Equal(verticesBefore, CountVertices(mesh));
        Assert.Equal(edgesBefore, CountEdges(mesh));
        Assert.Equal(facesBefore, CountFaces(mesh));
        mesh.ValidateConsistency();
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

    private static SpatialMesh BuildTriangle(
        out VertexHandle a,
        out VertexHandle b,
        out VertexHandle c
    )
    {
        SpatialMesh mesh = new();
        a = mesh.AddVertex(Vector3.Zero);
        b = mesh.AddVertex(Vector3.UnitX);
        c = mesh.AddVertex(Vector3.UnitY);
        mesh.AddFace([a, b, c]);
        return mesh;
    }

    private static SpatialMesh BuildQuadWithSurroundingFaces(
        out VertexHandle a,
        out VertexHandle b,
        out VertexHandle c,
        out VertexHandle d,
        out FaceHandle sourceSide,
        out FaceHandle nextSide,
        out FaceHandle targetSide,
        out FaceHandle prevSide
    )
    {
        SpatialMesh mesh = BuildQuad(out a, out b, out c, out d, out _);
        VertexHandle s0 = mesh.AddVertex(new Vector3(0f, -1f, 0f));
        VertexHandle s1 = mesh.AddVertex(new Vector3(1f, -1f, 0f));
        VertexHandle n0 = mesh.AddVertex(new Vector3(2f, 0f, 0f));
        VertexHandle n1 = mesh.AddVertex(new Vector3(2f, 1f, 0f));
        VertexHandle t0 = mesh.AddVertex(new Vector3(1f, 2f, 0f));
        VertexHandle t1 = mesh.AddVertex(new Vector3(0f, 2f, 0f));
        VertexHandle p0 = mesh.AddVertex(new Vector3(-1f, 1f, 0f));
        VertexHandle p1 = mesh.AddVertex(new Vector3(-1f, 0f, 0f));
        sourceSide = mesh.AddFace([b, a, s0, s1]);
        nextSide = mesh.AddFace([c, b, n0, n1]);
        targetSide = mesh.AddFace([d, c, t0, t1]);
        prevSide = mesh.AddFace([a, d, p0, p1]);
        return mesh;
    }

    private static SpatialMesh BuildTwoQuads(out HalfEdgeHandle source, out HalfEdgeHandle target)
    {
        SpatialMesh mesh = BuildQuad(out VertexHandle a, out VertexHandle b, out _, out _, out _);
        VertexHandle e = mesh.AddVertex(new Vector3(3f, 0f, 0f));
        VertexHandle f = mesh.AddVertex(new Vector3(4f, 0f, 0f));
        VertexHandle g = mesh.AddVertex(new Vector3(4f, 1f, 0f));
        VertexHandle h = mesh.AddVertex(new Vector3(3f, 1f, 0f));
        mesh.AddFace([e, f, g, h]);
        source = FindEdge(mesh, a, b);
        target = FindEdge(mesh, g, h);
        return mesh;
    }

    private static SpatialMesh BuildDuplicateResult(
        out VertexHandle a,
        out VertexHandle b,
        out VertexHandle c,
        out VertexHandle d
    )
    {
        SpatialMesh mesh = BuildQuad(out a, out b, out c, out d, out _);
        VertexHandle shared = mesh.AddVertex(Vector3.UnitZ);
        VertexHandle x = mesh.AddVertex(new Vector3(0f, 0f, 2f));
        VertexHandle y = mesh.AddVertex(new Vector3(1f, 0f, 2f));
        mesh.AddFace([b, shared, x]);
        mesh.AddFace([c, y, shared]);
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

    private static HalfEdgeHandle FirstHalfEdge(SpatialMesh mesh)
    {
        foreach (HalfEdgeHandle halfEdge in mesh.EnumerateLiveHalfEdges())
            return halfEdge;
        throw new InvalidOperationException();
    }

    private static List<VertexHandle> CollectFaceVertices(SpatialMesh mesh, FaceHandle face)
    {
        List<VertexHandle> vertices = [];
        foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
            vertices.Add(mesh.GetHalfEdge(corner).Origin);
        return vertices;
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
