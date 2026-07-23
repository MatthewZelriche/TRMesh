using TREditorSharp.Storage;

namespace TREditorSharp.Tests;

public sealed class HalfEdgeFaceMergeTests
{
    [Fact]
    public void TryMergeFaces_AdjacentQuadsPreservesTargetAndRemovesSharedEdge()
    {
        using HalfEdgeMesh mesh = BuildAdjacentQuads(
            out FaceHandle source,
            out FaceHandle target,
            out HalfEdgeHandle shared
        );
        HalfEdgeHandle sharedTwin = mesh.GetHalfEdge(shared).Twin;

        Assert.True(mesh.TryMergeFaces(source, target));

        Assert.False(mesh.IsFaceAlive(source));
        Assert.True(mesh.IsFaceAlive(target));
        Assert.False(mesh.IsHalfEdgeAlive(shared));
        Assert.False(mesh.IsHalfEdgeAlive(sharedTwin));
        Assert.Equal(6, CountFaceCorners(mesh, target));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryMergeFaces_ReverseDirectionPreservesOtherFace()
    {
        using HalfEdgeMesh mesh = BuildAdjacentQuads(
            out FaceHandle first,
            out FaceHandle second,
            out _
        );

        Assert.True(mesh.TryMergeFaces(second, first));

        Assert.True(mesh.IsFaceAlive(first));
        Assert.False(mesh.IsFaceAlive(second));
        Assert.Equal(6, CountFaceCorners(mesh, first));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void CanMergeFaces_RejectsDisconnectedFacesWithoutMutation()
    {
        using HalfEdgeMesh mesh = BuildDisconnectedTriangles(
            out FaceHandle first,
            out FaceHandle second
        );

        Assert.False(mesh.CanMergeFaces(first, second));
        Assert.False(mesh.TryMergeFaces(first, second));

        Assert.True(mesh.IsFaceAlive(first));
        Assert.True(mesh.IsFaceAlive(second));
        Assert.Equal(3, CountFaceCorners(mesh, first));
        Assert.Equal(3, CountFaceCorners(mesh, second));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void CanMergeFaces_RejectsFacesSharingAnExtraVertex()
    {
        using HalfEdgeMesh mesh = BuildFacesSharingEdgeAndExtraVertex(
            out FaceHandle first,
            out FaceHandle second
        );

        Assert.False(mesh.CanMergeFaces(first, second));
        Assert.False(mesh.TryMergeFaces(first, second));
        mesh.ValidateConsistency();
    }

    private static HalfEdgeMesh BuildAdjacentQuads(
        out FaceHandle first,
        out FaceHandle second,
        out HalfEdgeHandle shared
    )
    {
        HalfEdgeMesh mesh = new();
        VertexHandle a = mesh.Vertices.Allocate();
        VertexHandle b = mesh.Vertices.Allocate();
        VertexHandle c = mesh.Vertices.Allocate();
        VertexHandle d = mesh.Vertices.Allocate();
        VertexHandle e = mesh.Vertices.Allocate();
        VertexHandle f = mesh.Vertices.Allocate();
        first = mesh.AddFace([a, b, c, d]);
        second = mesh.AddFace([b, e, f, c]);
        shared = FindEdge(mesh, b, c);
        return mesh;
    }

    private static HalfEdgeMesh BuildDisconnectedTriangles(
        out FaceHandle first,
        out FaceHandle second
    )
    {
        HalfEdgeMesh mesh = new();
        first = mesh.AddFace(
            [mesh.Vertices.Allocate(), mesh.Vertices.Allocate(), mesh.Vertices.Allocate()]
        );
        second = mesh.AddFace(
            [mesh.Vertices.Allocate(), mesh.Vertices.Allocate(), mesh.Vertices.Allocate()]
        );
        return mesh;
    }

    private static HalfEdgeMesh BuildFacesSharingEdgeAndExtraVertex(
        out FaceHandle first,
        out FaceHandle second
    )
    {
        HalfEdgeMesh mesh = new();
        VertexHandle a = mesh.Vertices.Allocate();
        VertexHandle b = mesh.Vertices.Allocate();
        VertexHandle c = mesh.Vertices.Allocate();
        VertexHandle d = mesh.Vertices.Allocate();
        VertexHandle e = mesh.Vertices.Allocate();
        VertexHandle f = mesh.Vertices.Allocate();
        first = mesh.AddFace([a, b, c, d]);
        second = mesh.AddFace([b, e, d, f, c]);
        VertexHandle connector = mesh.Vertices.Allocate();
        mesh.AddFace([d, c, connector, f]);
        return mesh;
    }

    private static HalfEdgeHandle FindEdge(
        HalfEdgeMesh mesh,
        VertexHandle origin,
        VertexHandle destination
    )
    {
        foreach (HalfEdgeHandle edge in mesh.EnumerateLiveHalfEdges())
        {
            HalfEdge data = mesh.GetHalfEdge(edge);
            if (
                data.Origin == origin
                && mesh.GetHalfEdge(data.Twin).Origin == destination
            )
            {
                return edge;
            }
        }

        throw new InvalidOperationException("Expected edge was not found.");
    }

    private static int CountFaceCorners(HalfEdgeMesh mesh, FaceHandle face)
    {
        int count = 0;
        foreach (HalfEdgeHandle _ in mesh.HalfEdgesAroundFace(face))
            count++;
        return count;
    }
}
