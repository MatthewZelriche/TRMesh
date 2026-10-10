using System.Numerics;
using TRMesh.Mesh;

namespace TRMesh.Tests;

public sealed class SpatialMeshCloneTests
{
    [Fact]
    public void ClonePreservesSparseHandlesAndOwnsIndependentStorage()
    {
        using var source = new SpatialMesh();
        int removed = source.AddVertex(Vector3.Zero);
        int target = source.AddVertex(new Vector3(2f, 0f, 0f));
        int c = source.AddVertex(new Vector3(2f, 2f, 0f));
        int d = source.AddVertex(new Vector3(0f, 2f, 0f));
        int face = source.AddFace(
            [removed, target, c, d],
            [Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY]
        );
        int edge = source.GetFaceData(face).topology.AdjacentHalfEdge;
        source.CollapseEdge(edge, target, new Vector3(1f, 0f, 0f));

        using SpatialMesh clone = source.Clone();

        Assert.False(clone.IsVertexAlive(removed));
        Assert.True(clone.IsVertexAlive(target));
        Assert.True(clone.IsFaceAlive(face));
        Assert.Equal(source.GetVertexData(target).position, clone.GetVertexData(target).position);
        Assert.Equal(source.GetFaceData(face), clone.GetFaceData(face));

        clone.SetVertexPositions([target], [new Vector3(3f, 0f, 0f)]);

        Assert.Equal(new Vector3(1f, 0f, 0f), source.GetVertexData(target).position);
        Assert.Equal(new Vector3(3f, 0f, 0f), clone.GetVertexData(target).position);
    }
}
