using System.Numerics;
using TRMesh.Mesh;

namespace tr_mesh.Tests;

public class SpatialMeshTests
{
    static readonly Vector2 Uv = new(0.5f, 0.5f);

    [Fact]
    public void AddFace_Triangle_BuildsInteriorLoopAndBoundaryTwins()
    {
        using var mesh = new SpatialMesh();
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0), Uv);
        int v1 = mesh.AddVertex(new Vector3(1, 0, 0), Uv);
        int v2 = mesh.AddVertex(new Vector3(0, 1, 0), Uv);

        int face = mesh.AddFace([v0, v1, v2]);

        Assert.Equal(0, face);
        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(3, mesh.EdgeCount);
        Assert.Equal(1, mesh.FaceCount);

        int h0 = mesh.GetFace(face).AdjacentHalfEdge;
        int h1 = mesh.GetHalfEdge(h0).NextHalfEdge;
        int h2 = mesh.GetHalfEdge(h1).NextHalfEdge;

        Assert.Equal(h0, mesh.GetHalfEdge(h2).NextHalfEdge);
        Assert.Equal(h0, mesh.GetHalfEdge(h1).PrevHalfEdge);
        Assert.Equal(h1, mesh.GetHalfEdge(h2).PrevHalfEdge);
        Assert.Equal(h2, mesh.GetHalfEdge(h0).PrevHalfEdge);

        Assert.Equal(v0, mesh.GetHalfEdge(h0).SourceVertex);
        Assert.Equal(v1, mesh.GetHalfEdge(h1).SourceVertex);
        Assert.Equal(v2, mesh.GetHalfEdge(h2).SourceVertex);

        Assert.Equal(face, mesh.GetHalfEdge(h0).AdjacentFace);
        Assert.Equal(face, mesh.GetHalfEdge(h1).AdjacentFace);
        Assert.Equal(face, mesh.GetHalfEdge(h2).AdjacentFace);

        int t0 = h0 ^ 1;
        int t1 = h1 ^ 1;
        int t2 = h2 ^ 1;
        Assert.Equal(SpatialMesh.INVALID_HANDLE, mesh.GetHalfEdge(t0).AdjacentFace);
        Assert.Equal(t2, mesh.GetHalfEdge(t0).NextHalfEdge);
        Assert.Equal(t1, mesh.GetHalfEdge(t2).NextHalfEdge);
        Assert.Equal(t0, mesh.GetHalfEdge(t1).NextHalfEdge);

        Assert.Equal(new Vector3(0, 0, 1), mesh.GetFaceNormal(face));
    }

    [Fact]
    public void AddFace_SharedBoundaryEdge_StitchesOppositeWinding()
    {
        using var mesh = new SpatialMesh();
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0), Uv);
        int v1 = mesh.AddVertex(new Vector3(1, 0, 0), Uv);
        int v2 = mesh.AddVertex(new Vector3(0, 1, 0), Uv);
        int v3 = mesh.AddVertex(new Vector3(1, 1, 0), Uv);

        int first = mesh.AddFace([v0, v1, v2]);
        int second = mesh.AddFace([v0, v2, v3]);

        Assert.Equal(5, mesh.EdgeCount);
        Assert.Equal(2, mesh.FaceCount);

        int shared = FindOutgoing(mesh, v2, v0);
        Assert.NotEqual(SpatialMesh.INVALID_HANDLE, shared);
        Assert.Equal(first, mesh.GetHalfEdge(shared).AdjacentFace);
        Assert.Equal(second, mesh.GetHalfEdge(shared ^ 1).AdjacentFace);
    }

    [Fact]
    public void AddFace_InteriorEdge_ThrowsAndLeavesMeshUnchanged()
    {
        using var mesh = new SpatialMesh();
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0), Uv);
        int v1 = mesh.AddVertex(new Vector3(1, 0, 0), Uv);
        int v2 = mesh.AddVertex(new Vector3(0, 1, 0), Uv);
        mesh.AddFace([v0, v1, v2]);

        var error = Assert.Throws<ArgumentException>(() => mesh.AddFace([v0, v1, v2]));
        Assert.Equal("vertices", error.ParamName);
        Assert.Equal(3, mesh.EdgeCount);
        Assert.Equal(1, mesh.FaceCount);
    }

    [Fact]
    public void AddFace_RequiresThreeVerticesAndLiveHandles()
    {
        using var mesh = new SpatialMesh();
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0), Uv);
        int v1 = mesh.AddVertex(new Vector3(1, 0, 0), Uv);

        Assert.Throws<ArgumentException>(() => mesh.AddFace([v0, v1]));
        Assert.Throws<ArgumentException>(() => mesh.AddFace([v0, v1, 99]));
        Assert.Equal(0, mesh.FaceCount);
        Assert.Equal(0, mesh.EdgeCount);
    }

    [Fact]
    public void AddFace_RepeatedVertex_ThrowsAndLeavesMeshUnchanged()
    {
        using var mesh = new SpatialMesh();
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0), Uv);
        int v1 = mesh.AddVertex(new Vector3(1, 0, 0), Uv);

        var error = Assert.Throws<ArgumentException>(() => mesh.AddFace([v0, v1, v0]));

        Assert.Equal("vertices", error.ParamName);
        Assert.Equal(0, mesh.FaceCount);
        Assert.Equal(0, mesh.EdgeCount);
    }

    [Fact]
    public void AddFace_DisconnectedVertexFan_ThrowsAndLeavesMeshUnchanged()
    {
        using var mesh = new SpatialMesh();
        int shared = mesh.AddVertex(new Vector3(0, 0, 0), Uv);
        int v1 = mesh.AddVertex(new Vector3(1, 0, 0), Uv);
        int v2 = mesh.AddVertex(new Vector3(0, 1, 0), Uv);
        int v3 = mesh.AddVertex(new Vector3(-1, 0, 0), Uv);
        int v4 = mesh.AddVertex(new Vector3(0, -1, 0), Uv);
        mesh.AddFace([shared, v1, v2]);

        var error = Assert.Throws<ArgumentException>(() => mesh.AddFace([shared, v3, v4]));

        Assert.Equal("vertices", error.ParamName);
        Assert.Equal(1, mesh.FaceCount);
        Assert.Equal(3, mesh.EdgeCount);
    }

    static int FindOutgoing(SpatialMesh mesh, int from, int to)
    {
        int start = mesh.GetVertex(from).OutgoingHalfEdge;
        int he = start;
        do
        {
            if (mesh.GetHalfEdge(he ^ 1).SourceVertex == to)
                return he;
            he = mesh.GetHalfEdge(he ^ 1).NextHalfEdge;
        } while (he != start);

        return SpatialMesh.INVALID_HANDLE;
    }
}
