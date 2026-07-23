namespace TREditorSharp.Tests;

public sealed class HalfEdgeMeshValidationTests
{
    [Fact]
    public void ValidateConsistency_DisconnectedVertexFansThrow()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle shared = mesh.Vertices.Allocate();
        VertexHandle b = mesh.Vertices.Allocate();
        VertexHandle c = mesh.Vertices.Allocate();
        mesh.AddFace([shared, b, c]);

        VertexHandle disconnected = mesh.Vertices.Allocate();
        VertexHandle e = mesh.Vertices.Allocate();
        VertexHandle f = mesh.Vertices.Allocate();
        mesh.AddFace([disconnected, e, f]);

        List<HalfEdgeHandle> disconnectedRing = [];
        foreach (HalfEdgeHandle halfEdge in mesh.HalfEdgesAroundVertex(disconnected))
            disconnectedRing.Add(halfEdge);
        foreach (HalfEdgeHandle halfEdge in disconnectedRing)
        {
            HalfEdge data = mesh.HalfEdges[halfEdge];
            data.Origin = shared;
            mesh.HalfEdges[halfEdge] = data;
        }
        mesh.Vertices.Free(disconnected);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            mesh.ValidateConsistency
        );
        Assert.Contains("disconnected fans", exception.Message);
    }
}
