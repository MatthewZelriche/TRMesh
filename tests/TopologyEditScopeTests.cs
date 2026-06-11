namespace TREditorSharp.Tests;

using System.Numerics;

public sealed class TopologyEditScopeTests
{
    [Fact]
    public void Commit_CreateTracksNewEntitiesAndProducesUndoablePatch()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle a = mesh.Vertices.Allocate();
        VertexHandle b = mesh.Vertices.Allocate();
        VertexHandle c = mesh.Vertices.Allocate();
        FaceHandle face;
        HalfEdgeHandle[] createdHalfEdges;

        using (TopologyEditScope edit = mesh.BeginTopologyEdit([a, b, c]))
        {
            face = mesh.AddFace([a, b, c]);
            createdHalfEdges = CollectHalfEdges(mesh);
            using TopologyPatch patch = edit.Commit();
            mesh.ValidateConsistency();

            patch.ApplyBefore();
            Assert.False(mesh.Faces.IsAlive(face));
            Assert.All(createdHalfEdges, handle => Assert.False(mesh.HalfEdges.IsAlive(handle)));
            Assert.All([a, b, c], handle => Assert.True(mesh.Vertices.IsAlive(handle)));
            mesh.ValidateConsistency();

            patch.ApplyAfter();
            Assert.True(mesh.Faces.IsAlive(face));
            Assert.All(createdHalfEdges, handle => Assert.True(mesh.HalfEdges.IsAlive(handle)));
            mesh.ValidateConsistency();
        }
    }

#if DEBUG
    [Fact]
    public void Commit_UnderCapturedComponentWriteRollsBackAndThrows()
    {
        using SpatialMesh mesh = new();
        VertexHandle affected = mesh.AddVertex(new(1, 2, 3));
        VertexHandle unrelated = mesh.AddVertex(new(4, 5, 6));
        Vector3 original = mesh.GetVertexPosition(unrelated);

        using TopologyEditScope edit = mesh.BeginTopologyEdit([affected]);
        mesh.SetVertexPosition(unrelated, new(7, 8, 9));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(edit.Commit);

        Assert.Contains("outside the captured patch domain", error.Message);
        Assert.Equal(original, mesh.GetVertexPosition(unrelated));
        Assert.True(mesh.Vertices.IsAlive(affected));
        using TopologyEditScope next = mesh.BeginTopologyEdit([affected]);
        using TopologyPatch patch = next.Commit();
    }

    [Fact]
    public void Commit_UnderCapturedConnectivityWriteRollsBackAndThrows()
    {
        using HalfEdgeMesh mesh = BuildTwoDisconnectedTriangles(
            out VertexHandle affected,
            out FaceHandle unrelatedFace
        );
        Face original = mesh.Faces[unrelatedFace];
        HalfEdgeHandle incorrectAnchor = CollectHalfEdges(mesh)
            .First(halfEdge => mesh.GetHalfEdge(halfEdge).Face != unrelatedFace);

        using TopologyEditScope edit = mesh.BeginTopologyEdit([affected]);
        mesh.Faces[unrelatedFace] = new Face { FirstHalfEdge = incorrectAnchor };

        Assert.Throws<InvalidOperationException>(edit.Commit);

        Assert.Equal(original.FirstHalfEdge, mesh.Faces[unrelatedFace].FirstHalfEdge);
        mesh.ValidateConsistency();
    }
#endif

    [Fact]
    public void Commit_RemoveTracksUnexpectedEntityOutsideInitialOneRing()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle seed = mesh.Vertices.Allocate();
        VertexHandle unexpectedlyRemoved = mesh.Vertices.Allocate();

        using TopologyEditScope edit = mesh.BeginTopologyEdit([seed]);
        mesh.Vertices.Free(unexpectedlyRemoved);
        using TopologyPatch patch = edit.Commit();

        Assert.True(mesh.Vertices.IsReserved(unexpectedlyRemoved));

        patch.ApplyBefore();
        Assert.True(mesh.Vertices.IsAlive(unexpectedlyRemoved));

        patch.ApplyAfter();
        Assert.True(mesh.Vertices.IsReserved(unexpectedlyRemoved));
    }

    [Fact]
    public void Commit_RemoveFaceRestoresReservedFaceAndChangedConnectivity()
    {
        using HalfEdgeMesh mesh = BuildTriangle(
            out VertexHandle a,
            out VertexHandle b,
            out VertexHandle c,
            out FaceHandle face
        );

        using TopologyEditScope edit = mesh.BeginTopologyEdit([a, b, c]);
        Assert.True(mesh.RemoveFace(face));
        using TopologyPatch patch = edit.Commit();

        Assert.True(mesh.Faces.IsReserved(face));
        mesh.ValidateConsistency();

        patch.ApplyBefore();
        Assert.True(mesh.Faces.IsAlive(face));
        mesh.ValidateConsistency();

        patch.ApplyAfter();
        Assert.True(mesh.Faces.IsReserved(face));
        Assert.All(
            CollectHalfEdges(mesh),
            halfEdge => Assert.True(mesh.GetHalfEdge(halfEdge).Face.IsNull)
        );
        mesh.ValidateConsistency();
    }

    [Fact]
    public void Commit_MixedCreateAndRemoveTracksBothFinalStates()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle removed = mesh.Vertices.Allocate();

        using TopologyEditScope edit = mesh.BeginTopologyEdit([removed]);
        mesh.Vertices.Free(removed);
        VertexHandle created = mesh.Vertices.Allocate();
        using TopologyPatch patch = edit.Commit();

        patch.ApplyBefore();
        Assert.True(mesh.Vertices.IsAlive(removed));
        Assert.True(mesh.Vertices.IsReserved(created));

        patch.ApplyAfter();
        Assert.True(mesh.Vertices.IsReserved(removed));
        Assert.True(mesh.Vertices.IsAlive(created));
    }

    [Fact]
    public void Commit_CreatedThenRemovedEntityIsReleasedBecauseItBelongsToNeitherState()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle seed = mesh.Vertices.Allocate();

        using TopologyEditScope edit = mesh.BeginTopologyEdit([seed]);
        VertexHandle transient = mesh.Vertices.Allocate();
        mesh.Vertices.Free(transient);
        using TopologyPatch patch = edit.Commit();
        VertexHandle replacement = mesh.Vertices.Allocate();

        Assert.False(mesh.Vertices.IsReserved(transient));
        Assert.Equal(transient.Index, replacement.Index);
        Assert.NotEqual(transient.Generation, replacement.Generation);

        patch.ApplyBefore();
        patch.ApplyAfter();
        Assert.True(mesh.Vertices.IsAlive(replacement));
    }

    [Fact]
    public void DisposeWithoutCommit_RollsBackCreateAndRemove()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle existing = mesh.Vertices.Allocate();
        VertexHandle created;

        using (TopologyEditScope edit = mesh.BeginTopologyEdit([existing]))
        {
            mesh.Vertices.Free(existing);
            created = mesh.Vertices.Allocate();
        }

        Assert.True(mesh.Vertices.IsAlive(existing));
        Assert.False(mesh.Vertices.IsAlive(created));
        Assert.False(mesh.Vertices.IsReserved(created));
    }

    [Fact]
    public void DisposeAfterException_RollsBackMutation()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle vertex = mesh.Vertices.Allocate();

        Assert.Throws<InvalidOperationException>(
            (Action)(
                () =>
                {
                    using TopologyEditScope edit = mesh.BeginTopologyEdit([vertex]);
                    mesh.Vertices.Free(vertex);
                    throw new InvalidOperationException("Simulated edit failure.");
                }
            )
        );

        Assert.True(mesh.Vertices.IsAlive(vertex));
        Assert.False(mesh.Vertices.IsReserved(vertex));
    }

    [Fact]
    public void BeginTopologyEdit_RejectsNestedEditAndAllowsAnotherAfterCompletion()
    {
        using HalfEdgeMesh mesh = new();
        VertexHandle vertex = mesh.Vertices.Allocate();

        using (TopologyEditScope outer = mesh.BeginTopologyEdit([vertex]))
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                mesh.BeginTopologyEdit([vertex]);
            });
        }

        using TopologyEditScope next = mesh.BeginTopologyEdit([vertex]);
        using TopologyPatch patch = next.Commit();
    }

    private static HalfEdgeHandle[] CollectHalfEdges(HalfEdgeMesh mesh)
    {
        List<HalfEdgeHandle> handles = [];
        foreach (HalfEdgeHandle handle in mesh.EnumerateLiveHalfEdges())
            handles.Add(handle);
        return handles.ToArray();
    }

    private static HalfEdgeMesh BuildTriangle(
        out VertexHandle a,
        out VertexHandle b,
        out VertexHandle c,
        out FaceHandle face
    )
    {
        HalfEdgeMesh mesh = new();
        a = mesh.Vertices.Allocate();
        b = mesh.Vertices.Allocate();
        c = mesh.Vertices.Allocate();
        face = mesh.AddFace([a, b, c]);
        return mesh;
    }

    private static HalfEdgeMesh BuildTwoDisconnectedTriangles(
        out VertexHandle affected,
        out FaceHandle unrelatedFace
    )
    {
        HalfEdgeMesh mesh = new();
        affected = mesh.Vertices.Allocate();
        VertexHandle b = mesh.Vertices.Allocate();
        VertexHandle c = mesh.Vertices.Allocate();
        mesh.AddFace([affected, b, c]);

        VertexHandle x = mesh.Vertices.Allocate();
        VertexHandle y = mesh.Vertices.Allocate();
        VertexHandle z = mesh.Vertices.Allocate();
        unrelatedFace = mesh.AddFace([x, y, z]);
        return mesh;
    }
}
