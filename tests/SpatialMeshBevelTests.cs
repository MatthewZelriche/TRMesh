using System.Numerics;
using TREditorSharp.Builders;

namespace TREditorSharp.Tests;

public sealed class SpatialMeshBevelTests
{
    [Fact]
    public void BevelEdge_BoxCreatesChamferAtRequestedWidth()
    {
        using SpatialMesh mesh = BuildBox();
        HalfEdgeHandle edge = FindDirectedEdge(mesh, new Vector3(1, -1, 1), new Vector3(1, 1, 1));

        SpatialMesh.BevelEdgeResult result = mesh.BevelEdge(edge, 0.25f);

        Assert.Equal(4, result.NewVertices.Length);
        Assert.Equal(4, result.RebuiltFaces.Length);
        Assert.Equal(7, CountFaces(mesh));
        Assert.Equal(10, CountVertices(mesh));
        Assert.Contains(
            result.NewVertices,
            vertex =>
                Vector3.Distance(mesh.GetVertexPosition(vertex), new Vector3(0.75f, -1, 1)) < 1e-5f
        );
        Assert.Contains(
            result.NewVertices,
            vertex =>
                Vector3.Distance(mesh.GetVertexPosition(vertex), new Vector3(1, -1, 0.75f)) < 1e-5f
        );
        mesh.ValidateConsistency();
    }

    [Fact]
    public void BevelEdge_PreservesAffectedFaceMaterials()
    {
        using SpatialMesh mesh = BuildBox();
        HalfEdgeHandle edge = FindDirectedEdge(mesh, new Vector3(1, -1, 1), new Vector3(1, 1, 1));
        FaceHandle firstFace = mesh.GetHalfEdge(edge).Face;
        mesh.SetFaceMaterialSlot(firstFace, 12);

        SpatialMesh.BevelEdgeResult result = mesh.BevelEdge(edge, 0.25f);

        Assert.Equal(12, mesh.GetFaceMaterialSlot(result.BevelFace));
        SpatialMesh.FaceReplacement firstReplacement = Assert.Single(
            result.RebuiltFaces,
            replacement => replacement.SourceFace == firstFace
        );
        Assert.Equal(12, mesh.GetFaceMaterialSlot(firstReplacement.ReplacementFace));
    }

    [Fact]
    public void TryGetMaximumEdgeBevelWidth_RejectsBoundaryEdge()
    {
        using SpatialMesh mesh = new();
        VertexHandle a = mesh.AddVertex(Vector3.Zero);
        VertexHandle b = mesh.AddVertex(Vector3.UnitX);
        VertexHandle c = mesh.AddVertex(Vector3.UnitY);
        FaceHandle face = mesh.AddFace([a, b, c]);
        HalfEdgeHandle edge = HalfEdgeHandle.Null;
        foreach (HalfEdgeHandle candidate in mesh.HalfEdgesAroundFace(face))
        {
            edge = candidate;
            break;
        }

        Assert.False(mesh.TryGetMaximumEdgeBevelWidth(edge, out float maximumWidth));
        Assert.Equal(0f, maximumWidth);
    }

    [Fact]
    public void BevelEdge_WidthBeyondMaximumDoesNotMutateMesh()
    {
        using SpatialMesh mesh = BuildBox();
        HalfEdgeHandle edge = FindDirectedEdge(mesh, new Vector3(1, -1, 1), new Vector3(1, 1, 1));
        Assert.True(mesh.TryGetMaximumEdgeBevelWidth(edge, out float maximumWidth));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => mesh.BevelEdge(edge, maximumWidth + 0.01f)
        );

        Assert.Equal(6, CountFaces(mesh));
        Assert.Equal(8, CountVertices(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void BevelEdge_MaximumWidthKeepsTopologyValid()
    {
        using SpatialMesh mesh = BuildBox();
        HalfEdgeHandle edge = FindDirectedEdge(mesh, new Vector3(1, -1, 1), new Vector3(1, 1, 1));
        Assert.True(mesh.TryGetMaximumEdgeBevelWidth(edge, out float maximumWidth));

        mesh.BevelEdge(edge, maximumWidth);

        mesh.ValidateConsistency();
    }

    private static SpatialMesh BuildBox() =>
        MeshBuilders.Build(new BlockOptions { Min = new Vector3(-1), Max = new Vector3(1) });

    private static HalfEdgeHandle FindDirectedEdge(
        SpatialMesh mesh,
        Vector3 origin,
        Vector3 destination
    )
    {
        foreach (HalfEdgeHandle edge in mesh.EnumerateLiveHalfEdges())
        {
            HalfEdge halfEdge = mesh.GetHalfEdge(edge);
            if (
                mesh.GetVertexPosition(halfEdge.Origin) == origin
                && mesh.GetVertexPosition(mesh.GetHalfEdge(halfEdge.Twin).Origin) == destination
            )
            {
                return edge;
            }
        }

        throw new InvalidOperationException("Expected edge was not found.");
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
