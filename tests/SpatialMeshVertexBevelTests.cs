using System.Numerics;
using TREditorSharp.Builders;

namespace TREditorSharp.Tests;

public sealed class SpatialMeshVertexBevelTests
{
    [Fact]
    public void BevelVertex_BoxTruncatesCornerAtRequestedWidth()
    {
        using SpatialMesh mesh = BuildBox();
        VertexHandle vertex = FindVertex(mesh, new Vector3(1, 1, 1));

        SpatialMesh.BevelVertexResult result = mesh.BevelVertex(vertex, 0.25f);

        Assert.Equal(3, result.NewVertices.Length);
        Assert.Equal(3, result.RebuiltFaces.Length);
        Assert.Equal(7, CountFaces(mesh));
        Assert.Equal(10, CountVertices(mesh));
        Assert.Contains(
            result.NewVertices,
            cut => Vector3.Distance(mesh.GetVertexPosition(cut), new Vector3(0.75f, 1, 1)) < 1e-5f
        );
        Assert.Contains(
            result.NewVertices,
            cut => Vector3.Distance(mesh.GetVertexPosition(cut), new Vector3(1, 0.75f, 1)) < 1e-5f
        );
        Assert.Contains(
            result.NewVertices,
            cut => Vector3.Distance(mesh.GetVertexPosition(cut), new Vector3(1, 1, 0.75f)) < 1e-5f
        );
        Assert.True(
            Vector3.Dot(mesh.ComputeFaceNormal(result.BevelFace), Vector3.One) > 0f,
            "The bevel cap should face away from the box interior."
        );
        mesh.ValidateConsistency();
    }

    [Fact]
    public void BevelVertex_SupportsValenceFour()
    {
        using SpatialMesh mesh = MeshBuilders.Build(
            new UvSphereOptions
            {
                Center = Vector3.Zero,
                Radius = 1f,
                LatSegments = 2,
                LonSegments = 4,
            }
        );
        VertexHandle northPole = FindVertex(mesh, Vector3.UnitY);

        SpatialMesh.BevelVertexResult result = mesh.BevelVertex(northPole, 0.25f);

        Assert.Equal(4, result.NewVertices.Length);
        Assert.Equal(4, result.RebuiltFaces.Length);
        mesh.ValidateConsistency();
    }

    [Fact]
    public void BevelVertex_PreservesAffectedFaceMaterials()
    {
        using SpatialMesh mesh = BuildBox();
        VertexHandle vertex = FindVertex(mesh, new Vector3(1, 1, 1));
        foreach (HalfEdgeHandle edge in mesh.HalfEdgesAroundVertex(vertex))
            mesh.SetFaceMaterialSlot(mesh.GetHalfEdge(edge).Face, 12);

        SpatialMesh.BevelVertexResult result = mesh.BevelVertex(vertex, 0.25f);

        Assert.Equal(12, mesh.GetFaceMaterialSlot(result.BevelFace));
        Assert.All(
            result.RebuiltFaces,
            replacement => Assert.Equal(12, mesh.GetFaceMaterialSlot(replacement.ReplacementFace))
        );
    }

    [Fact]
    public void TryGetMaximumVertexBevelWidth_RejectsBoundaryVertex()
    {
        using SpatialMesh mesh = new();
        VertexHandle source = mesh.AddVertex(Vector3.Zero);
        VertexHandle b = mesh.AddVertex(Vector3.UnitX);
        VertexHandle c = mesh.AddVertex(Vector3.UnitY);
        mesh.AddFace([source, b, c]);

        Assert.False(mesh.TryGetMaximumVertexBevelWidth(source, out float maximumWidth));
        Assert.Equal(0f, maximumWidth);
    }

    [Fact]
    public void BevelVertex_WidthBeyondMaximumDoesNotMutateMesh()
    {
        using SpatialMesh mesh = BuildBox();
        VertexHandle vertex = FindVertex(mesh, new Vector3(1, 1, 1));
        Assert.True(mesh.TryGetMaximumVertexBevelWidth(vertex, out float maximumWidth));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => mesh.BevelVertex(vertex, maximumWidth + 0.01f)
        );

        Assert.Equal(6, CountFaces(mesh));
        Assert.Equal(8, CountVertices(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void BevelVertex_MaximumWidthKeepsTopologyValid()
    {
        using SpatialMesh mesh = BuildBox();
        VertexHandle vertex = FindVertex(mesh, new Vector3(1, 1, 1));
        Assert.True(mesh.TryGetMaximumVertexBevelWidth(vertex, out float maximumWidth));

        mesh.BevelVertex(vertex, maximumWidth);

        mesh.ValidateConsistency();
    }

    private static SpatialMesh BuildBox() =>
        MeshBuilders.Build(new BlockOptions { Min = new Vector3(-1), Max = new Vector3(1) });

    private static VertexHandle FindVertex(SpatialMesh mesh, Vector3 position)
    {
        foreach (VertexHandle vertex in mesh.EnumerateLiveVertices())
        {
            if (mesh.GetVertexPosition(vertex) == position)
                return vertex;
        }

        throw new InvalidOperationException("Expected vertex was not found.");
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
