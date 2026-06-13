using System.Numerics;

namespace TREditorSharp.Tests;

public sealed class SpatialMeshExtrusionRingTests
{
    [Fact]
    public void BuildExtrusionRing_QuadCreatesSideRingAndPlacedCap()
    {
        using SpatialMesh mesh = BuildQuad(out FaceHandle face, out VertexHandle[] originalVertices);

        SpatialMesh.RingResult result = mesh.BuildExtrusionRing(
            face,
            (index, position) => position + Vector3.UnitZ * (index + 1)
        );

        Assert.False(mesh.IsFaceAlive(face));
        Assert.Equal(8, CountVertices(mesh));
        Assert.Equal(12, CountEdges(mesh));
        Assert.Equal(5, CountFaces(mesh));
        Assert.Equal(4, result.SideFaces.Length);
        Assert.Equal(4, result.NewVertices.Length);
        Assert.Equal(result.NewVertices, CollectFaceVertices(mesh, result.CapFace));

        for (int i = 0; i < originalVertices.Length; i++)
        {
            int next = (i + 1) % originalVertices.Length;
            Assert.Equal(
                new VertexHandle[]
                {
                    originalVertices[i],
                    originalVertices[next],
                    result.NewVertices[next],
                    result.NewVertices[i],
                },
                CollectFaceVertices(mesh, result.SideFaces[i])
            );
            Assert.Equal(
                mesh.GetVertexPosition(originalVertices[i]) + Vector3.UnitZ * (i + 1),
                mesh.GetVertexPosition(result.NewVertices[i])
            );
        }

        mesh.ValidateConsistency();
    }

    [Fact]
    public void BuildExtrusionRing_InteriorFaceKeepsNeighborAttachedToOriginalRim()
    {
        using SpatialMesh mesh = BuildAdjacentQuads(
            out FaceHandle source,
            out FaceHandle neighbor,
            out VertexHandle sharedA,
            out VertexHandle sharedB
        );
        HalfEdgeHandle neighborSharedEdge = FindEdge(mesh, sharedB, sharedA);

        SpatialMesh.RingResult result = mesh.BuildExtrusionRing(
            source,
            (_, position) => position + Vector3.UnitZ
        );

        Assert.True(mesh.IsFaceAlive(neighbor));
        Assert.Equal(neighbor, mesh.GetHalfEdge(neighborSharedEdge).Face);
        Assert.Contains(
            result.SideFaces,
            side => FindFaceEdge(mesh, side, sharedA, sharedB) is not null
        );
        mesh.ValidateConsistency();
    }

    [Fact]
    public void BuildExtrusionRing_InheritsMaterialAndMarksGeneratedFacesUvsUninitialized()
    {
        using SpatialMesh mesh = BuildQuad(out FaceHandle face, out _);
        mesh.SetFaceMaterialSlot(face, 17);
        mesh.SetFaceUvsInitialized(face, true);

        SpatialMesh.RingResult result = mesh.BuildExtrusionRing(face, (_, position) => position);

        foreach (FaceHandle generatedFace in result.SideFaces.Append(result.CapFace))
        {
            Assert.Equal(17, mesh.GetFaceMaterialSlot(generatedFace));
            Assert.False(mesh.AreFaceUvsInitialized(generatedFace));
        }
        mesh.ValidateConsistency();
    }

    [Fact]
    public void BuildExtrusionRing_PlacementFailureLeavesMeshUnchanged()
    {
        using SpatialMesh mesh = BuildQuad(out FaceHandle face, out _);
        int verticesBefore = CountVertices(mesh);
        int edgesBefore = CountEdges(mesh);
        int facesBefore = CountFaces(mesh);

        Assert.Throws<InvalidOperationException>(() =>
            mesh.BuildExtrusionRing(
                face,
                (index, position) =>
                    index == 2 ? throw new InvalidOperationException() : position
            )
        );

        Assert.True(mesh.IsFaceAlive(face));
        Assert.Equal(verticesBefore, CountVertices(mesh));
        Assert.Equal(edgesBefore, CountEdges(mesh));
        Assert.Equal(facesBefore, CountFaces(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void BuildExtrusionRing_DeadFaceThrowsWithoutMutation()
    {
        using SpatialMesh mesh = BuildQuad(out FaceHandle face, out _);
        Assert.True(mesh.RemoveFace(face));
        int verticesBefore = CountVertices(mesh);
        int edgesBefore = CountEdges(mesh);

        Assert.Throws<ArgumentException>(() =>
            mesh.BuildExtrusionRing(face, (_, position) => position)
        );

        Assert.Equal(verticesBefore, CountVertices(mesh));
        Assert.Equal(edgesBefore, CountEdges(mesh));
        mesh.ValidateConsistency();
    }

    private static SpatialMesh BuildQuad(out FaceHandle face, out VertexHandle[] vertices)
    {
        SpatialMesh mesh = new();
        vertices =
        [
            mesh.AddVertex(Vector3.Zero),
            mesh.AddVertex(Vector3.UnitX),
            mesh.AddVertex(Vector3.One),
            mesh.AddVertex(Vector3.UnitY),
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
}
