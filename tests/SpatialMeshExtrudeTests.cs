using System.Numerics;

namespace TREditorSharp.Tests;

public sealed class SpatialMeshExtrudeTests
{
    [Fact]
    public void ExtrudeFace_QuadCreatesSideRingAndCap()
    {
        using SpatialMesh mesh = BuildQuad(out FaceHandle face, out _);

        SpatialMesh.ExtrudeFaceResult result = mesh.ExtrudeFace(face, 2f);

        Assert.False(mesh.IsFaceAlive(face));
        Assert.True(mesh.IsFaceAlive(result.CapFace));
        Assert.All(result.SideFaces, side => Assert.True(mesh.IsFaceAlive(side)));
        Assert.All(result.NewVertices, vertex => Assert.True(mesh.IsVertexAlive(vertex)));
        Assert.Equal(4, result.SideFaces.Length);
        Assert.Equal(4, result.NewVertices.Length);
        Assert.Equal(8, CountVertices(mesh));
        Assert.Equal(12, CountEdges(mesh));
        Assert.Equal(5, CountFaces(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void ExtrudeFace_PositionsCapAlongFaceNormal()
    {
        using SpatialMesh mesh = BuildQuad(out FaceHandle face, out VertexHandle[] originalVertices);
        Vector3 normal = mesh.ComputeFaceNormal(face);
        Vector3[] originalPositions = originalVertices.Select(mesh.GetVertexPosition).ToArray();

        SpatialMesh.ExtrudeFaceResult result = mesh.ExtrudeFace(face, 3f);

        for (int i = 0; i < result.NewVertices.Length; i++)
        {
            AssertVectorApproximately(
                originalPositions[i] + normal * 3f,
                mesh.GetVertexPosition(result.NewVertices[i])
            );
        }
        Assert.Equal(result.NewVertices, CollectFaceVertices(mesh, result.CapFace));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void ExtrudeFace_NegativeDistanceMovesOppositeFaceNormal()
    {
        using SpatialMesh mesh = BuildQuad(out FaceHandle face, out VertexHandle[] originalVertices);
        Vector3 normal = mesh.ComputeFaceNormal(face);
        Vector3 originalPosition = mesh.GetVertexPosition(originalVertices[0]);

        SpatialMesh.ExtrudeFaceResult result = mesh.ExtrudeFace(face, -2f);

        AssertVectorApproximately(
            originalPosition - normal * 2f,
            mesh.GetVertexPosition(result.NewVertices[0])
        );
        mesh.ValidateConsistency();
    }

    [Fact]
    public void ExtrudeFace_InteriorFaceKeepsNeighborAttachedToOriginalRim()
    {
        using SpatialMesh mesh = BuildAdjacentQuads(
            out FaceHandle source,
            out FaceHandle neighbor,
            out VertexHandle sharedA,
            out VertexHandle sharedB
        );
        HalfEdgeHandle neighborSharedEdge = FindEdge(mesh, sharedB, sharedA);

        SpatialMesh.ExtrudeFaceResult result = mesh.ExtrudeFace(source, 1f);

        Assert.True(mesh.IsFaceAlive(neighbor));
        Assert.Equal(neighbor, mesh.GetHalfEdge(neighborSharedEdge).Face);
        Assert.Contains(
            result.SideFaces,
            side => FindFaceEdge(mesh, side, sharedA, sharedB) is not null
        );
        mesh.ValidateConsistency();
    }

    [Fact]
    public void ExtrudeFace_CapInheritsSourceMaterialAndBoundarySidesAreUntextured()
    {
        using SpatialMesh mesh = BuildQuad(out FaceHandle face, out _);
        mesh.SetFaceMaterialSlot(face, 23);
        mesh.SetFaceUvsInitialized(face, true);

        SpatialMesh.ExtrudeFaceResult result = mesh.ExtrudeFace(face, 1f);

        Assert.Equal(23, mesh.GetFaceMaterialSlot(result.CapFace));
        Assert.All(
            result.SideFaces,
            side => Assert.Equal(SpatialMesh.UntexturedMaterialSlot, mesh.GetFaceMaterialSlot(side))
        );
        Assert.All(
            result.SideFaces.Append(result.CapFace),
            generatedFace => Assert.False(mesh.AreFaceUvsInitialized(generatedFace))
        );
        mesh.ValidateConsistency();
    }

    [Fact]
    public void ExtrudeFace_SideInheritsMaterialFromNeighborAcrossOriginalEdge()
    {
        using SpatialMesh mesh = BuildAdjacentQuads(
            out FaceHandle source,
            out FaceHandle neighbor,
            out VertexHandle sharedA,
            out VertexHandle sharedB
        );
        mesh.SetFaceMaterialSlot(source, 17);
        mesh.SetFaceMaterialSlot(neighbor, 29);

        SpatialMesh.ExtrudeFaceResult result = mesh.ExtrudeFace(source, 1f);

        FaceHandle sharedSide = Assert.Single(
            result.SideFaces.Where(side => FindFaceEdge(mesh, side, sharedA, sharedB) is not null)
        );
        Assert.Equal(29, mesh.GetFaceMaterialSlot(sharedSide));
        Assert.Equal(17, mesh.GetFaceMaterialSlot(result.CapFace));
        Assert.All(
            result.SideFaces.Where(side => side != sharedSide),
            side => Assert.Equal(SpatialMesh.UntexturedMaterialSlot, mesh.GetFaceMaterialSlot(side))
        );
        mesh.ValidateConsistency();
    }

    [Fact]
    public void ExtrudeFace_DeadFaceThrowsWithoutMutation()
    {
        using SpatialMesh mesh = BuildQuad(out FaceHandle face, out _);
        Assert.True(mesh.RemoveFace(face));
        int verticesBefore = CountVertices(mesh);
        int edgesBefore = CountEdges(mesh);
        int facesBefore = CountFaces(mesh);

        Assert.Throws<ArgumentException>(() => mesh.ExtrudeFace(face, 1f));

        Assert.Equal(verticesBefore, CountVertices(mesh));
        Assert.Equal(edgesBefore, CountEdges(mesh));
        Assert.Equal(facesBefore, CountFaces(mesh));
        mesh.ValidateConsistency();
    }

    private static SpatialMesh BuildQuad(out FaceHandle face, out VertexHandle[] vertices)
    {
        SpatialMesh mesh = new();
        vertices =
        [
            mesh.AddVertex(new Vector3(0f, 0f, 0f)),
            mesh.AddVertex(new Vector3(0f, 0f, 2f)),
            mesh.AddVertex(new Vector3(3f, 0f, 2f)),
            mesh.AddVertex(new Vector3(3f, 0f, 0f)),
        ];
        face = mesh.AddFace(vertices);
        return mesh;
    }

    private static SpatialMesh BuildAdjacentQuads(
        out FaceHandle source,
        out FaceHandle neighbor,
        out VertexHandle sharedA,
        out VertexHandle sharedB
    )
    {
        SpatialMesh mesh = new();
        VertexHandle a = mesh.AddVertex(Vector3.Zero);
        sharedA = mesh.AddVertex(Vector3.UnitX);
        sharedB = mesh.AddVertex(Vector3.One);
        VertexHandle d = mesh.AddVertex(Vector3.UnitY);
        VertexHandle e = mesh.AddVertex(new Vector3(2f, 0f, 0f));
        VertexHandle f = mesh.AddVertex(new Vector3(2f, 1f, 0f));
        source = mesh.AddFace([a, sharedA, sharedB, d]);
        neighbor = mesh.AddFace([sharedA, e, f, sharedB]);
        return mesh;
    }

    private static HalfEdgeHandle FindEdge(
        SpatialMesh mesh,
        VertexHandle origin,
        VertexHandle destination
    )
    {
        foreach (HalfEdgeHandle edge in mesh.EnumerateLiveHalfEdges())
        {
            HalfEdge halfEdge = mesh.GetHalfEdge(edge);
            if (halfEdge.Origin == origin && mesh.GetHalfEdge(halfEdge.Twin).Origin == destination)
                return edge;
        }
        throw new InvalidOperationException();
    }

    private static HalfEdgeHandle? FindFaceEdge(
        SpatialMesh mesh,
        FaceHandle face,
        VertexHandle origin,
        VertexHandle destination
    )
    {
        foreach (HalfEdgeHandle edge in mesh.HalfEdgesAroundFace(face))
        {
            HalfEdge halfEdge = mesh.GetHalfEdge(edge);
            if (halfEdge.Origin == origin && mesh.GetHalfEdge(halfEdge.Twin).Origin == destination)
                return edge;
        }
        return null;
    }

    private static VertexHandle[] CollectFaceVertices(SpatialMesh mesh, FaceHandle face)
    {
        List<VertexHandle> vertices = [];
        foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
            vertices.Add(mesh.GetHalfEdge(corner).Origin);
        return vertices.ToArray();
    }

    private static int CountVertices(SpatialMesh mesh)
    {
        int count = 0;
        foreach (VertexHandle _ in mesh.EnumerateLiveVertices())
            count++;
        return count;
    }

    private static int CountEdges(SpatialMesh mesh)
    {
        int count = 0;
        foreach (HalfEdgeHandle _ in mesh.EnumerateLiveHalfEdges())
            count++;
        return count / 2;
    }

    private static int CountFaces(SpatialMesh mesh)
    {
        int count = 0;
        foreach (FaceHandle _ in mesh.EnumerateLiveFaces())
            count++;
        return count;
    }

    private static void AssertVectorApproximately(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0f, 1e-5f);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0f, 1e-5f);
        Assert.InRange(MathF.Abs(expected.Z - actual.Z), 0f, 1e-5f);
    }
}
