using System.Numerics;

namespace TREditorSharp.Tests;

public sealed class SpatialMeshBridgeTests
{
    [Fact]
    public void BridgeEdges_FlatBridgeCreatesOneQuad()
    {
        using SpatialMesh mesh = BuildWalls(out HalfEdgeHandle first, out HalfEdgeHandle second);

        SpatialMesh.BridgeEdgesResult result = mesh.BridgeEdges(first, second, 1, 0f);

        Assert.Single(result.Faces);
        Assert.Empty(result.NewVertices);
        mesh.ValidateConsistency();
    }

    [Fact]
    public void BridgeEdges_SemicircleCreatesSegmentedArch()
    {
        using SpatialMesh mesh = BuildWalls(out HalfEdgeHandle first, out HalfEdgeHandle second);

        SpatialMesh.BridgeEdgesResult result = mesh.BridgeEdges(first, second, 4, 180f);

        Assert.Equal(4, result.Faces.Length);
        Assert.Equal(6, result.NewVertices.Length);
        Assert.Contains(result.NewVertices, vertex => mesh.GetVertexPosition(vertex).Y > 2.99f);
        Assert.All(
            result.Faces,
            face => Assert.True(Vector3.Dot(mesh.ComputeFaceNormal(face), Vector3.UnitY) > 0f)
        );
        mesh.ValidateConsistency();
    }

    [Fact]
    public void CanBridgeEdges_RejectsInteriorEdge()
    {
        using SpatialMesh mesh = TREditorSharp.Builders.MeshBuilders.Build(
            new TREditorSharp.Builders.BlockOptions { Min = new Vector3(-1), Max = new Vector3(1) }
        );
        HalfEdgeHandle first = HalfEdgeHandle.Null;
        HalfEdgeHandle second = HalfEdgeHandle.Null;
        foreach (HalfEdgeHandle edge in mesh.EnumerateLiveHalfEdges())
        {
            if (first.IsNull)
                first = edge;
            else if (mesh.GetHalfEdge(first).Twin != edge)
            {
                second = edge;
                break;
            }
        }

        Assert.False(mesh.CanBridgeEdges(first, second));
    }

    [Fact]
    public void BridgeEdges_OpenBoxConsumesEndConnectorsAndCreatesGables()
    {
        using SpatialMesh mesh = BuildOpenBox(out HalfEdgeHandle first, out HalfEdgeHandle second);

        SpatialMesh.BridgeEdgesResult result = mesh.BridgeEdges(first, second, 2, 55f);

        Assert.Equal(4, result.Faces.Length);
        foreach (HalfEdgeHandle edge in mesh.EnumerateLiveHalfEdges())
            Assert.True(mesh.IsFaceAlive(mesh.GetHalfEdge(edge).Face));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void BridgeEdges_FlatSegmentedOpenBoxOmitsDegenerateGables()
    {
        using SpatialMesh mesh = BuildOpenBox(out HalfEdgeHandle first, out HalfEdgeHandle second);

        SpatialMesh.BridgeEdgesResult result = mesh.BridgeEdges(first, second, 4, 0f);

        Assert.Equal(4, result.Faces.Length);
        Assert.All(
            result.Faces,
            face => Assert.NotEqual(Vector3.Zero, mesh.ComputeFaceNormal(face))
        );
        foreach (HalfEdgeHandle edge in mesh.EnumerateLiveHalfEdges())
            Assert.True(mesh.IsFaceAlive(mesh.GetHalfEdge(edge).Face));
        mesh.ValidateConsistency();
    }

    private static SpatialMesh BuildWalls(out HalfEdgeHandle firstTop, out HalfEdgeHandle secondTop)
    {
        SpatialMesh mesh = new();
        VertexHandle leftBottomFront = mesh.AddVertex(new Vector3(-1, 0, 0));
        VertexHandle leftBottomBack = mesh.AddVertex(new Vector3(-1, 0, 2));
        VertexHandle leftTopBack = mesh.AddVertex(new Vector3(-1, 2, 2));
        VertexHandle leftTopFront = mesh.AddVertex(new Vector3(-1, 2, 0));
        mesh.AddFace([leftBottomFront, leftBottomBack, leftTopBack, leftTopFront]);

        VertexHandle rightBottomBack = mesh.AddVertex(new Vector3(1, 0, 2));
        VertexHandle rightBottomFront = mesh.AddVertex(new Vector3(1, 0, 0));
        VertexHandle rightTopFront = mesh.AddVertex(new Vector3(1, 2, 0));
        VertexHandle rightTopBack = mesh.AddVertex(new Vector3(1, 2, 2));
        mesh.AddFace([rightBottomBack, rightBottomFront, rightTopFront, rightTopBack]);

        firstTop = FindEdge(mesh, leftTopFront, leftTopBack);
        secondTop = FindEdge(mesh, rightTopBack, rightTopFront);
        return mesh;
    }

    private static SpatialMesh BuildOpenBox(out HalfEdgeHandle first, out HalfEdgeHandle second)
    {
        SpatialMesh mesh = TREditorSharp.Builders.MeshBuilders.Build(
            new TREditorSharp.Builders.BlockOptions { Min = new Vector3(-1), Max = new Vector3(1) }
        );
        FaceHandle top = FaceHandle.Null;
        foreach (FaceHandle face in mesh.EnumerateLiveFaces())
        {
            if (Vector3.Dot(mesh.ComputeFaceNormal(face), Vector3.UnitY) > 0.9f)
            {
                top = face;
                break;
            }
        }
        if (!mesh.RemoveFace(top))
            throw new InvalidOperationException("Expected top face was not found.");

        first = FindEdge(mesh, new Vector3(-1, 1, -1), new Vector3(-1, 1, 1));
        second = FindEdge(mesh, new Vector3(1, 1, -1), new Vector3(1, 1, 1));
        return mesh;
    }

    private static HalfEdgeHandle FindEdge(
        SpatialMesh mesh,
        VertexHandle first,
        VertexHandle second
    )
    {
        foreach (HalfEdgeHandle edge in mesh.EnumerateLiveHalfEdges())
        {
            HalfEdge data = mesh.GetHalfEdge(edge);
            VertexHandle destination = mesh.GetHalfEdge(data.Twin).Origin;
            if (
                (data.Origin == first && destination == second)
                || (data.Origin == second && destination == first)
            )
            {
                return edge;
            }
        }

        throw new InvalidOperationException("Expected edge was not found.");
    }

    private static HalfEdgeHandle FindEdge(SpatialMesh mesh, Vector3 first, Vector3 second)
    {
        foreach (HalfEdgeHandle edge in mesh.EnumerateLiveHalfEdges())
        {
            HalfEdge data = mesh.GetHalfEdge(edge);
            Vector3 origin = mesh.GetVertexPosition(data.Origin);
            Vector3 destination = mesh.GetVertexPosition(mesh.GetHalfEdge(data.Twin).Origin);
            if (
                (origin == first && destination == second)
                || (origin == second && destination == first)
            )
                return edge;
        }

        throw new InvalidOperationException("Expected edge was not found.");
    }
}
