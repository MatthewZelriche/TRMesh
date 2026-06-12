namespace TREditorSharp.Tests;

public sealed class HalfEdgeVertexRemovalTests
{
    [Fact]
    public void RemoveVertex_IsolatedVertexRemovesItAndMaintainsConsistency()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle vertex = mesh.Vertices.Allocate();

        Assert.True(mesh.RemoveVertex(vertex));

        Assert.False(mesh.IsVertexAlive(vertex));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void RemoveVertex_ConnectedVertexReturnsFalseAndLeavesMeshUnchanged()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle a = mesh.Vertices.Allocate();
        VertexHandle b = mesh.Vertices.Allocate();
        VertexHandle c = mesh.Vertices.Allocate();
        FaceHandle face = mesh.AddFace([a, b, c]);

        Assert.False(mesh.RemoveVertex(a));

        Assert.True(mesh.IsVertexAlive(a));
        Assert.True(mesh.IsFaceAlive(face));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TopologyPatch_RemoveVertexRestoresExactHandle()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle vertex = mesh.Vertices.Allocate();

        using TopologyEditScope edit = mesh.BeginTopologyEdit([vertex]);
        Assert.True(mesh.RemoveVertex(vertex));
        using TopologyPatch patch = edit.Commit();

        patch.ApplyBefore();
        Assert.True(mesh.IsVertexAlive(vertex));
        mesh.ValidateConsistency();

        patch.ApplyAfter();
        Assert.False(mesh.IsVertexAlive(vertex));
        mesh.ValidateConsistency();
    }
}
