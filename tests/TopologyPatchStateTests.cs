namespace TREditorSharp.Tests;

using TREditorSharp.Storage;

public sealed class TopologyPatchStateTests
{
    [Fact]
    public void Capture_IsolatedVertexContainsOnlySuppliedVertex()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle isolated = mesh.Vertices.Allocate();

        TopologyPatchState state = mesh.CaptureTopologyPatchState([isolated]);

        Assert.Equal(isolated, Assert.Single(Handles(state.Vertices)));
        Assert.Empty(state.HalfEdges);
        Assert.Empty(state.Faces);
        Assert.True(mesh.Vertices.IsAlive(isolated));
    }

    [Fact]
    public void Capture_BoundaryVertexIncludesIncidentPairsAndCompleteIncidentFace()
    {
        using HalfEdgeMesh mesh = BuildTriangle(out VertexHandle a, out _, out _);
        FaceHandle face = Assert.Single(CollectFaces(mesh));
        HashSet<HalfEdgeHandle> expectedHalfEdges = [];
        foreach (HalfEdgeHandle edge in mesh.HalfEdgesAroundVertex(a))
        {
            expectedHalfEdges.Add(edge);
            expectedHalfEdges.Add(mesh.GetHalfEdge(edge).Twin);
        }
        foreach (HalfEdgeHandle edge in mesh.HalfEdgesAroundFace(face))
            expectedHalfEdges.Add(edge);

        TopologyPatchState state = mesh.CaptureTopologyPatchState([a]);

        Assert.Equal(3, state.Vertices.Count);
        Assert.Equal(expectedHalfEdges, Handles(state.HalfEdges).ToHashSet());
        Assert.Equal(face, Assert.Single(Handles(state.Faces)));
    }

    [Fact]
    public void Capture_InteriorVertexIncludesCompleteOneRingButNotUnrelatedFace()
    {
        using HalfEdgeMesh mesh = BuildFanWithUnrelatedTriangle(
            out VertexHandle center,
            out FaceHandle unrelated
        );
        HashSet<FaceHandle> expectedFaces = [];
        foreach (HalfEdgeHandle edge in mesh.HalfEdgesAroundVertex(center))
        {
            HalfEdge halfEdge = mesh.GetHalfEdge(edge);
            AddFace(halfEdge.Face);
            AddFace(mesh.GetHalfEdge(halfEdge.Twin).Face);
        }

        TopologyPatchState state = mesh.CaptureTopologyPatchState([center]);

        Assert.Equal(4, state.Faces.Count);
        Assert.Equal(expectedFaces, Handles(state.Faces).ToHashSet());
        Assert.DoesNotContain(unrelated, Handles(state.Faces));
        Assert.Equal(5, state.Vertices.Count);

        void AddFace(FaceHandle face)
        {
            if (!face.IsNull)
                expectedFaces.Add(face);
        }
    }

    [Fact]
    public void Capture_SharedEdgeEndpointsDeduplicatesOverlappingOneRings()
    {
        using HalfEdgeMesh mesh = BuildTwoAdjacentTriangles(
            out VertexHandle sharedA,
            out VertexHandle sharedB
        );

        TopologyPatchState state = mesh.CaptureTopologyPatchState([sharedA, sharedB, sharedA]);

        Assert.Equal(4, state.Vertices.Count);
        Assert.Equal(10, state.HalfEdges.Count);
        Assert.Equal(2, state.Faces.Count);
        Assert.Equal(Handles(state.Vertices).Length, Handles(state.Vertices).Distinct().Count());
        Assert.Equal(Handles(state.HalfEdges).Length, Handles(state.HalfEdges).Distinct().Count());
    }

    [Fact]
    public void Capture_DoesNotMutateMeshOrReserveEntities()
    {
        using HalfEdgeMesh mesh = BuildTriangle(out VertexHandle a, out _, out _);
        VertexHandle[] verticesBefore = CollectVertices(mesh);
        HalfEdgeHandle[] halfEdgesBefore = CollectHalfEdges(mesh);
        FaceHandle[] facesBefore = CollectFaces(mesh);

        TopologyPatchState state = mesh.CaptureTopologyPatchState([a]);

        Assert.Equal(verticesBefore, CollectVertices(mesh));
        Assert.Equal(halfEdgesBefore, CollectHalfEdges(mesh));
        Assert.Equal(facesBefore, CollectFaces(mesh));
        Assert.All(
            Handles(state.Vertices),
            vertex => Assert.False(mesh.Vertices.IsReserved(vertex))
        );
        Assert.All(
            Handles(state.HalfEdges),
            halfEdge => Assert.False(mesh.HalfEdges.IsReserved(halfEdge))
        );
        Assert.All(Handles(state.Faces), face => Assert.False(mesh.Faces.IsReserved(face)));
    }

    [Fact]
    public void Capture_RejectsDeadAffectedVertex()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle dead = mesh.Vertices.Allocate();
        mesh.Vertices.Free(dead);

        Assert.Throws<ArgumentException>(() => mesh.CaptureTopologyPatchState([dead]));
    }

    private static HalfEdgeMesh BuildTriangle(
        out VertexHandle a,
        out VertexHandle b,
        out VertexHandle c
    )
    {
        HalfEdgeMesh mesh = new();
        a = mesh.Vertices.Allocate();
        b = mesh.Vertices.Allocate();
        c = mesh.Vertices.Allocate();
        mesh.AddFace([a, b, c]);
        return mesh;
    }

    private static HalfEdgeMesh BuildTwoAdjacentTriangles(
        out VertexHandle sharedA,
        out VertexHandle sharedB
    )
    {
        HalfEdgeMesh mesh = new();
        sharedA = mesh.Vertices.Allocate();
        sharedB = mesh.Vertices.Allocate();
        VertexHandle c = mesh.Vertices.Allocate();
        VertexHandle d = mesh.Vertices.Allocate();
        mesh.AddFace([sharedA, sharedB, c]);
        mesh.AddFace([sharedB, sharedA, d]);
        return mesh;
    }

    private static HalfEdgeMesh BuildFanWithUnrelatedTriangle(
        out VertexHandle center,
        out FaceHandle unrelated
    )
    {
        HalfEdgeMesh mesh = new();
        center = mesh.Vertices.Allocate();
        VertexHandle a = mesh.Vertices.Allocate();
        VertexHandle b = mesh.Vertices.Allocate();
        VertexHandle c = mesh.Vertices.Allocate();
        VertexHandle d = mesh.Vertices.Allocate();
        mesh.AddFace([center, a, b]);
        mesh.AddFace([center, b, c]);
        mesh.AddFace([center, c, d]);
        mesh.AddFace([center, d, a]);

        VertexHandle x = mesh.Vertices.Allocate();
        VertexHandle y = mesh.Vertices.Allocate();
        VertexHandle z = mesh.Vertices.Allocate();
        unrelated = mesh.AddFace([x, y, z]);
        return mesh;
    }

    private static VertexHandle[] Handles(IReadOnlyList<EntitySnapshot<VertexTag>> snapshots) =>
        snapshots.Select(snapshot => snapshot.Handle).ToArray();

    private static HalfEdgeHandle[] Handles(IReadOnlyList<EntitySnapshot<HalfEdgeTag>> snapshots) =>
        snapshots.Select(snapshot => snapshot.Handle).ToArray();

    private static FaceHandle[] Handles(IReadOnlyList<EntitySnapshot<FaceTag>> snapshots) =>
        snapshots.Select(snapshot => snapshot.Handle).ToArray();

    private static VertexHandle[] CollectVertices(HalfEdgeMesh mesh)
    {
        List<VertexHandle> handles = [];
        foreach (VertexHandle handle in mesh.EnumerateLiveVertices())
            handles.Add(handle);
        return handles.ToArray();
    }

    private static HalfEdgeHandle[] CollectHalfEdges(HalfEdgeMesh mesh)
    {
        List<HalfEdgeHandle> handles = [];
        foreach (HalfEdgeHandle handle in mesh.EnumerateLiveHalfEdges())
            handles.Add(handle);
        return handles.ToArray();
    }

    private static FaceHandle[] CollectFaces(HalfEdgeMesh mesh)
    {
        List<FaceHandle> handles = [];
        foreach (FaceHandle handle in mesh.EnumerateLiveFaces())
            handles.Add(handle);
        return handles.ToArray();
    }
}
