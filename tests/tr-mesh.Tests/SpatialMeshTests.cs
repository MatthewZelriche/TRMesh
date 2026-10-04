using System.Numerics;
using TRMesh.Mesh;

namespace tr_mesh.Tests;

public class SpatialMeshTests
{
    static readonly Vector2[] TriangleUvs = [Vector2.Zero, Vector2.UnitX, Vector2.UnitY];

    [Fact]
    public void IsAlive_ReportsLiveHandlesAndRejectsInvalidOnes()
    {
        using var mesh = new SpatialMesh();
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0));
        int v1 = mesh.AddVertex(new Vector3(1, 0, 0));
        int v2 = mesh.AddVertex(new Vector3(0, 1, 0));
        int face = mesh.AddFace([v0, v1, v2], TriangleUvs);
        int halfEdge = mesh.GetFaceData(face).topology.AdjacentHalfEdge;

        Assert.True(mesh.IsVertexAlive(v0));
        Assert.True(mesh.IsHalfEdgeAlive(halfEdge));
        Assert.True(mesh.IsFaceAlive(face));
        Assert.False(mesh.IsVertexAlive(SpatialMesh.INVALID_HANDLE));
        Assert.False(mesh.IsHalfEdgeAlive(SpatialMesh.INVALID_HANDLE));
        Assert.False(mesh.IsFaceAlive(SpatialMesh.INVALID_HANDLE));
        Assert.False(mesh.IsVertexAlive(999));
    }

    [Fact]
    public void AddFace_Triangle_BuildsInteriorLoopAndBoundaryTwins()
    {
        using var mesh = new SpatialMesh();
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0));
        int v1 = mesh.AddVertex(new Vector3(1, 0, 0));
        int v2 = mesh.AddVertex(new Vector3(0, 1, 0));

        int face = mesh.AddFace([v0, v1, v2], TriangleUvs);

        Assert.Equal(0, face);
        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(3, mesh.EdgeCount);
        Assert.Equal(1, mesh.FaceCount);

        int h0 = mesh.GetFaceData(face).topology.AdjacentHalfEdge;
        int h1 = mesh.GetHalfEdgeData(h0).topology.NextHalfEdge;
        int h2 = mesh.GetHalfEdgeData(h1).topology.NextHalfEdge;

        Assert.Equal(h0, mesh.GetHalfEdgeData(h2).topology.NextHalfEdge);
        Assert.Equal(h0, mesh.GetHalfEdgeData(h1).topology.PrevHalfEdge);
        Assert.Equal(h1, mesh.GetHalfEdgeData(h2).topology.PrevHalfEdge);
        Assert.Equal(h2, mesh.GetHalfEdgeData(h0).topology.PrevHalfEdge);

        Assert.Equal(v0, mesh.GetHalfEdgeData(h0).topology.SourceVertex);
        Assert.Equal(v1, mesh.GetHalfEdgeData(h1).topology.SourceVertex);
        Assert.Equal(v2, mesh.GetHalfEdgeData(h2).topology.SourceVertex);

        Assert.Equal(face, mesh.GetHalfEdgeData(h0).topology.AdjacentFace);
        Assert.Equal(face, mesh.GetHalfEdgeData(h1).topology.AdjacentFace);
        Assert.Equal(face, mesh.GetHalfEdgeData(h2).topology.AdjacentFace);

        int t0 = mesh.GetHalfEdgeData(h0).topology.TwinHalfEdge;
        int t1 = mesh.GetHalfEdgeData(h1).topology.TwinHalfEdge;
        int t2 = mesh.GetHalfEdgeData(h2).topology.TwinHalfEdge;
        Assert.Equal(h0, mesh.GetHalfEdgeData(t0).topology.TwinHalfEdge);
        Assert.Equal(SpatialMesh.INVALID_HANDLE, mesh.GetHalfEdgeData(t0).topology.AdjacentFace);
        Assert.Equal(t2, mesh.GetHalfEdgeData(t0).topology.NextHalfEdge);
        Assert.Equal(t1, mesh.GetHalfEdgeData(t2).topology.NextHalfEdge);
        Assert.Equal(t0, mesh.GetHalfEdgeData(t1).topology.NextHalfEdge);

        Assert.Equal(new Vector3(0, 0, 1), mesh.GetFaceData(face).normal);
    }

    [Fact]
    public void AddFace_SharedBoundaryEdge_StitchesOppositeWinding()
    {
        using var mesh = new SpatialMesh();
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0));
        int v1 = mesh.AddVertex(new Vector3(1, 0, 0));
        int v2 = mesh.AddVertex(new Vector3(0, 1, 0));
        int v3 = mesh.AddVertex(new Vector3(1, 1, 0));

        int first = mesh.AddFace([v0, v1, v2], TriangleUvs);
        int second = mesh.AddFace([v0, v2, v3], TriangleUvs);

        Assert.Equal(5, mesh.EdgeCount);
        Assert.Equal(2, mesh.FaceCount);

        int shared = FindOutgoing(mesh, v2, v0);
        Assert.NotEqual(SpatialMesh.INVALID_HANDLE, shared);
        Assert.Equal(first, mesh.GetHalfEdgeData(shared).topology.AdjacentFace);
        int twin = mesh.GetHalfEdgeData(shared).topology.TwinHalfEdge;
        Assert.Equal(second, mesh.GetHalfEdgeData(twin).topology.AdjacentFace);
    }

    [Fact]
    public void AddFace_InteriorEdge_ThrowsAndLeavesMeshUnchanged()
    {
        using var mesh = new SpatialMesh();
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0));
        int v1 = mesh.AddVertex(new Vector3(1, 0, 0));
        int v2 = mesh.AddVertex(new Vector3(0, 1, 0));
        mesh.AddFace([v0, v1, v2], TriangleUvs);

        var error = Assert.Throws<ArgumentException>(
            () => mesh.AddFace([v0, v1, v2], TriangleUvs)
        );
        Assert.Equal("vertices", error.ParamName);
        Assert.Equal(3, mesh.EdgeCount);
        Assert.Equal(1, mesh.FaceCount);
    }

    [Fact]
    public void AddFace_RequiresThreeVerticesAndLiveHandles()
    {
        using var mesh = new SpatialMesh();
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0));
        int v1 = mesh.AddVertex(new Vector3(1, 0, 0));

        Assert.Throws<ArgumentException>(
            () => mesh.AddFace([v0, v1], [Vector2.Zero, Vector2.One])
        );
        Assert.Throws<ArgumentException>(() => mesh.AddFace([v0, v1, 99], TriangleUvs));
        Assert.Equal(0, mesh.FaceCount);
        Assert.Equal(0, mesh.EdgeCount);
    }

    [Fact]
    public void AddFace_RepeatedVertex_ThrowsAndLeavesMeshUnchanged()
    {
        using var mesh = new SpatialMesh();
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0));
        int v1 = mesh.AddVertex(new Vector3(1, 0, 0));

        var error = Assert.Throws<ArgumentException>(
            () => mesh.AddFace([v0, v1, v0], TriangleUvs)
        );

        Assert.Equal("vertices", error.ParamName);
        Assert.Equal(0, mesh.FaceCount);
        Assert.Equal(0, mesh.EdgeCount);
    }

    [Fact]
    public void AddFace_DisconnectedVertexFan_ThrowsAndLeavesMeshUnchanged()
    {
        using var mesh = new SpatialMesh();
        int shared = mesh.AddVertex(new Vector3(0, 0, 0));
        int v1 = mesh.AddVertex(new Vector3(1, 0, 0));
        int v2 = mesh.AddVertex(new Vector3(0, 1, 0));
        int v3 = mesh.AddVertex(new Vector3(-1, 0, 0));
        int v4 = mesh.AddVertex(new Vector3(0, -1, 0));
        mesh.AddFace([shared, v1, v2], TriangleUvs);

        var error = Assert.Throws<ArgumentException>(
            () => mesh.AddFace([shared, v3, v4], TriangleUvs)
        );

        Assert.Equal("vertices", error.ParamName);
        Assert.Equal(1, mesh.FaceCount);
        Assert.Equal(3, mesh.EdgeCount);
    }

    [Fact]
    public void AddFace_CornerUvsBelongToSourceVertices()
    {
        using var mesh = new SpatialMesh();
        int v0 = mesh.AddVertex(Vector3.Zero);
        int v1 = mesh.AddVertex(Vector3.UnitX);
        int v2 = mesh.AddVertex(Vector3.UnitY);
        Vector2[] uvs = [new(0, 0), new(1, 0), new(0, 1)];

        int face = mesh.AddFace([v0, v1, v2], uvs);
        int halfEdge = mesh.GetFaceData(face).topology.AdjacentHalfEdge;
        for (int i = 0; i < uvs.Length; i++)
        {
            Assert.Equal(uvs[i], mesh.GetHalfEdgeData(halfEdge).UV);
            int twin = mesh.GetHalfEdgeData(halfEdge).topology.TwinHalfEdge;
            Assert.Equal(Vector2.Zero, mesh.GetHalfEdgeData(twin).UV);
            halfEdge = mesh.GetHalfEdgeData(halfEdge).topology.NextHalfEdge;
        }

    }

    [Fact]
    public void AddFace_MismatchedCornerUvsThrowAndLeaveMeshUnchanged()
    {
        using var mesh = new SpatialMesh();
        int v0 = mesh.AddVertex(Vector3.Zero);
        int v1 = mesh.AddVertex(Vector3.UnitX);
        int v2 = mesh.AddVertex(Vector3.UnitY);

        var error = Assert.Throws<ArgumentException>(
            () => mesh.AddFace([v0, v1, v2], [Vector2.Zero, Vector2.One])
        );

        Assert.Equal("cornerUvs", error.ParamName);
        Assert.Equal(0, mesh.FaceCount);
        Assert.Equal(0, mesh.EdgeCount);
    }

    static int FindOutgoing(SpatialMesh mesh, int from, int to)
    {
        int start = mesh.GetVertexData(from).topology.OutgoingHalfEdge;
        int he = start;
        do
        {
            int twin = mesh.GetHalfEdgeData(he).topology.TwinHalfEdge;
            if (mesh.GetHalfEdgeData(twin).topology.SourceVertex == to)
                return he;
            he = mesh.GetHalfEdgeData(twin).topology.NextHalfEdge;
        } while (he != start);

        return SpatialMesh.INVALID_HANDLE;
    }
}
