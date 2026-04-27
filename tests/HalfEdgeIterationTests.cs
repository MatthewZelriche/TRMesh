namespace TREditorSharp.Tests;

/// <summary>
/// Tests for <see cref="HalfEdgeMesh.HalfEdgesAroundVertex"/> covering ordering,
/// boundary handling, and Debug-only malformed-ring validation.
///
/// These tests build half-edge connectivity by hand rather than going through a
/// higher-level mesh constructor, since the library does not yet ship one.
/// </summary>
public class HalfEdgeIterationTests
{
    /// <summary>
    /// Wire up <paramref name="count"/> outgoing half-edges around <paramref name="vertex"/>
    /// so that repeated <c>Prev.Twin</c> stepping visits them in the order
    /// <c>outgoing[0], outgoing[1], ..., outgoing[count - 1]</c>, then closes back
    /// to <c>outgoing[0]</c>.
    ///
    /// Each outgoing edge gets a dedicated twin. We only set <c>Origin</c>, <c>Twin</c>,
    /// and <c>Prev</c> — those are the fields the iterator depends on. <c>Next</c>,
    /// <c>Face</c>, etc. are left at their default values; Face being null on every
    /// twin therefore mirrors a fully-boundary fan, which is the most permissive case.
    /// </summary>
    private static (HalfEdgeHandle[] outgoing, HalfEdgeHandle[] twins) WireRing(
        HalfEdgeMesh mesh,
        VertexHandle vertex,
        int count
    )
    {
        var outgoing = new HalfEdgeHandle[count];
        var twins = new HalfEdgeHandle[count];

        for (int i = 0; i < count; i++)
        {
            outgoing[i] = mesh.HalfEdges.Allocate();
            twins[i] = mesh.HalfEdges.Allocate();
        }

        for (int i = 0; i < count; i++)
        {
            // Outgoing[i].Origin = vertex, Twin = twins[i],
            // Prev = twins[(i + 1) % count] so that Prev.Twin == outgoing[(i + 1) % count].
            var oh = mesh.HalfEdges[outgoing[i]];
            oh.Origin = vertex;
            oh.Twin = twins[i];
            oh.Prev = twins[(i + 1) % count];
            mesh.HalfEdges[outgoing[i]] = oh;

            var th = mesh.HalfEdges[twins[i]];
            th.Twin = outgoing[i];
            mesh.HalfEdges[twins[i]] = th;
        }

        var v = mesh.Vertices[vertex];
        v.OutgoingHalfEdge = outgoing[0];
        mesh.Vertices[vertex] = v;

        return (outgoing, twins);
    }

    private static List<HalfEdgeHandle> Collect(HalfEdgeMesh.HalfEdgeRingEnumerable ring)
    {
        var collected = new List<HalfEdgeHandle>();
        foreach (var h in ring)
            collected.Add(h);
        return collected;
    }

    [Fact]
    public void EmptyOutgoingYieldsNothing()
    {
        using var mesh = new HalfEdgeMesh();
        var v = mesh.Vertices.Allocate();
        // OutgoingHalfEdge defaults to Null.

        var visited = Collect(mesh.HalfEdgesAroundVertex(v));
        Assert.Empty(visited);
    }

    [Fact]
    public void DegreeOneRingYieldsSingleEdge()
    {
        using var mesh = new HalfEdgeMesh();
        var v = mesh.Vertices.Allocate();
        var (outgoing, _) = WireRing(mesh, v, count: 1);

        var visited = Collect(mesh.HalfEdgesAroundVertex(v));
        Assert.Single(visited);
        Assert.Equal(outgoing[0], visited[0]);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void ArtificialRingVisitsAllOutgoingInCcwOrder(int count)
    {
        using var mesh = new HalfEdgeMesh();
        var v = mesh.Vertices.Allocate();
        var (outgoing, _) = WireRing(mesh, v, count);

        var visited = Collect(mesh.HalfEdgesAroundVertex(v));

        Assert.Equal(count, visited.Count);
        for (int i = 0; i < count; i++)
            Assert.Equal(outgoing[i], visited[i]);
    }

    [Fact]
    public void IteratorIsRestartable()
    {
        using var mesh = new HalfEdgeMesh();
        var v = mesh.Vertices.Allocate();
        var (outgoing, _) = WireRing(mesh, v, count: 4);

        var first = Collect(mesh.HalfEdgesAroundVertex(v));
        var second = Collect(mesh.HalfEdgesAroundVertex(v));

        Assert.Equal(outgoing, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void TriangleVertexYieldsInteriorThenBoundary()
    {
        // Build a single triangle (a, b, c) with one interior face F and a boundary
        // loop on the outside. Verify vertex `a`'s ring yields {ab, ac} in CCW order.
        using var mesh = new HalfEdgeMesh();
        var a = mesh.Vertices.Allocate();
        var b = mesh.Vertices.Allocate();
        var c = mesh.Vertices.Allocate();
        var f = mesh.Faces.Allocate();

        var ab = mesh.HalfEdges.Allocate();
        var bc = mesh.HalfEdges.Allocate();
        var ca = mesh.HalfEdges.Allocate();
        var ba = mesh.HalfEdges.Allocate();
        var cb = mesh.HalfEdges.Allocate();
        var ac = mesh.HalfEdges.Allocate();

        Set(mesh, ab, origin: a, twin: ba, next: bc, prev: ca, face: f);
        Set(mesh, bc, origin: b, twin: cb, next: ca, prev: ab, face: f);
        Set(mesh, ca, origin: c, twin: ac, next: ab, prev: bc, face: f);

        // Boundary loop traverses ba -> ac -> cb -> ba (opposite direction of interior).
        Set(mesh, ba, origin: b, twin: ab, next: ac, prev: cb, face: FaceHandle.Null);
        Set(mesh, ac, origin: a, twin: ca, next: cb, prev: ba, face: FaceHandle.Null);
        Set(mesh, cb, origin: c, twin: bc, next: ba, prev: ac, face: FaceHandle.Null);

        SetOutgoing(mesh, a, ab);
        SetOutgoing(mesh, b, bc);
        SetOutgoing(mesh, c, ca);
        SetFirst(mesh, f, ab);

        var around = Collect(mesh.HalfEdgesAroundVertex(a));

        // Starting at ab, CCW step: ab.Prev = ca; ca.Twin = ac. So next is ac.
        // Then ac.Prev = ba; ba.Twin = ab => closes back to start.
        Assert.Equal(new[] { ab, ac }, around);
    }

    [Fact]
    public void ClosedFanOfThreeTrianglesYieldsThreeOutgoing()
    {
        // Center vertex v with three triangles meeting at it: (v, a, b), (v, b, c),
        // (v, c, a). All faces share v; the fan is closed (no boundary at v).
        using var mesh = new HalfEdgeMesh();
        var v = mesh.Vertices.Allocate();
        var a = mesh.Vertices.Allocate();
        var b = mesh.Vertices.Allocate();
        var c = mesh.Vertices.Allocate();

        var f1 = mesh.Faces.Allocate(); // (v, a, b)
        var f2 = mesh.Faces.Allocate(); // (v, b, c)
        var f3 = mesh.Faces.Allocate(); // (v, c, a)

        // Outgoing from v.
        var va = mesh.HalfEdges.Allocate();
        var vb = mesh.HalfEdges.Allocate();
        var vc = mesh.HalfEdges.Allocate();

        // Outer ring (closing each triangle).
        var ab = mesh.HalfEdges.Allocate();
        var bv = mesh.HalfEdges.Allocate();
        var bc = mesh.HalfEdges.Allocate();
        var cv = mesh.HalfEdges.Allocate();
        var ca = mesh.HalfEdges.Allocate();
        var av = mesh.HalfEdges.Allocate();

        // Outer-ring twins (boundary; Face = null).
        var ba = mesh.HalfEdges.Allocate();
        var cb = mesh.HalfEdges.Allocate();
        var ac = mesh.HalfEdges.Allocate();

        // Triangle (v, a, b): va -> ab -> bv -> va, all on f1.
        Set(mesh, va, origin: v, twin: av, next: ab, prev: bv, face: f1);
        Set(mesh, ab, origin: a, twin: ba, next: bv, prev: va, face: f1);
        Set(mesh, bv, origin: b, twin: vb, next: va, prev: ab, face: f1);

        // Triangle (v, b, c): vb -> bc -> cv -> vb, all on f2.
        Set(mesh, vb, origin: v, twin: bv, next: bc, prev: cv, face: f2);
        Set(mesh, bc, origin: b, twin: cb, next: cv, prev: vb, face: f2);
        Set(mesh, cv, origin: c, twin: vc, next: vb, prev: bc, face: f2);

        // Triangle (v, c, a): vc -> ca -> av -> vc, all on f3.
        Set(mesh, vc, origin: v, twin: cv, next: ca, prev: av, face: f3);
        Set(mesh, ca, origin: c, twin: ac, next: av, prev: vc, face: f3);
        Set(mesh, av, origin: a, twin: va, next: vc, prev: ca, face: f3);

        // Boundary loop on the outside (ab -> bc -> ca going outward = ba <- cb <- ac).
        Set(mesh, ba, origin: b, twin: ab, next: ac, prev: cb, face: FaceHandle.Null);
        Set(mesh, cb, origin: c, twin: bc, next: ba, prev: ac, face: FaceHandle.Null);
        Set(mesh, ac, origin: a, twin: ca, next: cb, prev: ba, face: FaceHandle.Null);

        SetOutgoing(mesh, v, va);
        SetOutgoing(mesh, a, ab);
        SetOutgoing(mesh, b, bc);
        SetOutgoing(mesh, c, ca);
        SetFirst(mesh, f1, va);
        SetFirst(mesh, f2, vb);
        SetFirst(mesh, f3, vc);

        var around = Collect(mesh.HalfEdgesAroundVertex(v));

        // CCW from va: va.Prev = bv; bv.Twin = vb. Then vb.Prev = cv; cv.Twin = vc.
        // Then vc.Prev = av; av.Twin = va => closes back. Expected: va, vb, vc.
        Assert.Equal(new[] { va, vb, vc }, around);

        // Each yielded handle has Origin == v.
        foreach (var h in around)
            Assert.Equal(v, mesh.HalfEdges[h].Origin);
    }

    [Fact]
    public void BoundaryVertexCompletesIteration()
    {
        // A triangle's vertex sees one interior outgoing and one boundary outgoing.
        // The iterator should yield both and terminate (it does not skip boundary edges).
        using var mesh = new HalfEdgeMesh();
        var a = mesh.Vertices.Allocate();
        var b = mesh.Vertices.Allocate();
        var c = mesh.Vertices.Allocate();
        var f = mesh.Faces.Allocate();

        var ab = mesh.HalfEdges.Allocate();
        var bc = mesh.HalfEdges.Allocate();
        var ca = mesh.HalfEdges.Allocate();
        var ba = mesh.HalfEdges.Allocate();
        var cb = mesh.HalfEdges.Allocate();
        var ac = mesh.HalfEdges.Allocate();

        Set(mesh, ab, a, ba, bc, ca, f);
        Set(mesh, bc, b, cb, ca, ab, f);
        Set(mesh, ca, c, ac, ab, bc, f);
        Set(mesh, ba, b, ab, ac, cb, FaceHandle.Null);
        Set(mesh, ac, a, ca, cb, ba, FaceHandle.Null);
        Set(mesh, cb, c, bc, ba, ac, FaceHandle.Null);

        SetOutgoing(mesh, a, ab);
        SetOutgoing(mesh, b, bc);
        SetOutgoing(mesh, c, ca);
        SetFirst(mesh, f, ab);

        // Confirm one of the yielded edges has a null face (the boundary side).
        var around = Collect(mesh.HalfEdgesAroundVertex(a));
        Assert.Equal(2, around.Count);

        bool sawBoundary = false;
        bool sawInterior = false;
        foreach (var h in around)
        {
            var face = mesh.HalfEdges[h].Face;
            if (face.IsNull)
                sawBoundary = true;
            else
                sawInterior = true;
        }
        Assert.True(sawInterior);
        Assert.True(sawBoundary);
    }

#if DEBUG
    [Fact]
    public void DeadVertexThrowsInDebug()
    {
        using var mesh = new HalfEdgeMesh();
        var v = mesh.Vertices.Allocate();
        mesh.Vertices.Free(v);

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var _ in mesh.HalfEdgesAroundVertex(v)) { }
        });
    }

    [Fact]
    public void OriginMismatchThrowsInDebug()
    {
        // Construct a ring whose second edge has the wrong Origin. The iterator should
        // detect this on the second step.
        using var mesh = new HalfEdgeMesh();
        var v = mesh.Vertices.Allocate();
        var foreign = mesh.Vertices.Allocate();
        var (outgoing, _) = WireRing(mesh, v, count: 3);

        // Corrupt outgoing[1].Origin to point at a foreign vertex.
        var bad = mesh.HalfEdges[outgoing[1]];
        bad.Origin = foreign;
        mesh.HalfEdges[outgoing[1]] = bad;

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var _ in mesh.HalfEdgesAroundVertex(v)) { }
        });
    }
#endif

    private static void Set(
        HalfEdgeMesh mesh,
        HalfEdgeHandle h,
        VertexHandle origin,
        HalfEdgeHandle twin,
        HalfEdgeHandle next,
        HalfEdgeHandle prev,
        FaceHandle face
    )
    {
        var he = mesh.HalfEdges[h];
        he.Origin = origin;
        he.Twin = twin;
        he.Next = next;
        he.Prev = prev;
        he.Face = face;
        mesh.HalfEdges[h] = he;
    }

    private static void SetOutgoing(HalfEdgeMesh mesh, VertexHandle v, HalfEdgeHandle h)
    {
        var data = mesh.Vertices[v];
        data.OutgoingHalfEdge = h;
        mesh.Vertices[v] = data;
    }

    private static void SetFirst(HalfEdgeMesh mesh, FaceHandle f, HalfEdgeHandle h)
    {
        var data = mesh.Faces[f];
        data.FirstHalfEdge = h;
        mesh.Faces[f] = data;
    }
}
