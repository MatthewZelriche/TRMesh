using System.Numerics;

namespace TREditorSharp.Tests;

public sealed class SpatialMeshEdgeExtrudeTests
{
    [Fact]
    public void ExtrudeEdge_BoundaryEdgeCreatesTranslatedQuad()
    {
        using SpatialMesh mesh = BuildQuad(out FaceHandle source, out HalfEdgeHandle edge);
        HalfEdge sourceEdge = mesh.GetHalfEdge(edge);
        VertexHandle origin = sourceEdge.Origin;
        VertexHandle destination = mesh.GetHalfEdge(sourceEdge.Twin).Origin;
        Vector3 offset = new(0, 0, 2);

        SpatialMesh.ExtrudeEdgeResult result = mesh.ExtrudeEdge(edge, offset);

        Assert.True(mesh.IsFaceAlive(source));
        Assert.True(mesh.IsFaceAlive(result.Face));
        Assert.Equal(2, result.NewVertices.Length);
        Assert.Equal(
            mesh.GetVertexPosition(origin) + offset,
            mesh.GetVertexPosition(result.NewVertices[1])
        );
        Assert.Equal(
            mesh.GetVertexPosition(destination) + offset,
            mesh.GetVertexPosition(result.NewVertices[0])
        );
        Assert.Equal(result.NewVertices[1], mesh.GetHalfEdge(result.OuterEdge).Origin);
        Assert.Equal(
            result.NewVertices[0],
            mesh.GetHalfEdge(mesh.GetHalfEdge(result.OuterEdge).Twin).Origin
        );
        mesh.ValidateConsistency();
    }

    [Fact]
    public void ExtrudeEdge_AcceptsBoundaryHalfAndInheritsAdjacentFaceState()
    {
        using SpatialMesh mesh = BuildQuad(out FaceHandle source, out HalfEdgeHandle edge);
        mesh.SetFaceMaterialSlot(source, 17);
        mesh.SetFaceUvsInitialized(source, true);
        HalfEdgeHandle boundary = mesh.GetHalfEdge(edge).Twin;

        SpatialMesh.ExtrudeEdgeResult result = mesh.ExtrudeEdge(boundary, Vector3.UnitZ);

        Assert.Equal(17, mesh.GetFaceMaterialSlot(result.Face));
        Assert.True(result.SourceHadInitializedUvs);
        Assert.False(mesh.AreFaceUvsInitialized(result.Face));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void ExtrudeEdge_InteriorEdgeIsRejectedWithoutMutation()
    {
        using SpatialMesh mesh = BuildAdjacentTriangles(out HalfEdgeHandle shared);
        int faceCount = CountFaces(mesh);
        int vertexCount = CountVertices(mesh);

        Assert.False(mesh.CanExtrudeEdge(shared));
        Assert.Throws<ArgumentException>(() => mesh.ExtrudeEdge(shared, Vector3.UnitZ));
        Assert.Equal(faceCount, CountFaces(mesh));
        Assert.Equal(vertexCount, CountVertices(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void ExtrudeEdge_IsolatedWireEdgeCreatesFirstFace()
    {
        using SpatialMesh mesh = BuildQuad(out FaceHandle face, out HalfEdgeHandle edge);
        Assert.True(mesh.RemoveFace(face));
        List<HalfEdgeHandle> halfEdges = [];
        foreach (HalfEdgeHandle other in mesh.EnumerateLiveHalfEdges())
            halfEdges.Add(other);
        foreach (HalfEdgeHandle other in halfEdges)
        {
            if (other == edge || other == mesh.GetHalfEdge(edge).Twin)
                continue;
            mesh.RemoveEdge(other);
        }

        Assert.True(mesh.CanExtrudeEdge(edge));
        SpatialMesh.ExtrudeEdgeResult result = mesh.ExtrudeEdge(edge, Vector3.UnitZ);

        Assert.True(mesh.IsFaceAlive(result.Face));
        Assert.Equal(1, CountFaces(mesh));
        mesh.ValidateConsistency();
    }

    private static SpatialMesh BuildQuad(out FaceHandle face, out HalfEdgeHandle edge)
    {
        SpatialMesh mesh = new();
        VertexHandle a = mesh.AddVertex(Vector3.Zero);
        VertexHandle b = mesh.AddVertex(Vector3.UnitX);
        VertexHandle c = mesh.AddVertex(Vector3.UnitX + Vector3.UnitY);
        VertexHandle d = mesh.AddVertex(Vector3.UnitY);
        face = mesh.AddFace([a, b, c, d]);
        edge = HalfEdgeHandle.Null;
        foreach (HalfEdgeHandle faceEdge in mesh.HalfEdgesAroundFace(face))
        {
            edge = faceEdge;
            break;
        }
        return mesh;
    }

    private static SpatialMesh BuildAdjacentTriangles(out HalfEdgeHandle shared)
    {
        SpatialMesh mesh = new();
        VertexHandle a = mesh.AddVertex(Vector3.Zero);
        VertexHandle b = mesh.AddVertex(Vector3.UnitX);
        VertexHandle c = mesh.AddVertex(Vector3.UnitY);
        VertexHandle d = mesh.AddVertex(-Vector3.UnitY);
        FaceHandle first = mesh.AddFace([a, b, c]);
        mesh.AddFace([b, a, d]);
        shared = HalfEdgeHandle.Null;
        foreach (HalfEdgeHandle candidate in mesh.HalfEdgesAroundFace(first))
        {
            if (
                mesh.GetHalfEdge(candidate).Origin == a
                && mesh.GetHalfEdge(mesh.GetHalfEdge(candidate).Twin).Origin == b
            )
            {
                shared = candidate;
                break;
            }
        }
        return mesh;
    }

    private static int CountFaces(SpatialMesh mesh)
    {
        int count = 0;
        foreach (FaceHandle _ in mesh.EnumerateLiveFaces())
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
}
