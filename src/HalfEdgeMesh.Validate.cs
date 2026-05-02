namespace TREditorSharp;

using System;
using System.Collections.Generic;
using TREditorSharp.Storage;

public partial class HalfEdgeMesh
{
    /// <summary>
    /// Audit the mesh for structural and edge-manifold consistency. Throws
    /// <see cref="InvalidOperationException"/> with a descriptive message on the first issue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a one-shot, whole-mesh audit. It is not gated on <c>DEBUG</c>;
    /// callers (typically tests) opt in by calling it.
    /// </para>
    /// <para>
    /// Checks performed:
    /// <list type="bullet">
    ///   <item><description>Per half-edge: <see cref="HalfEdge.Twin"/> reciprocity, distinctness from self, live origin/twin/next/prev/face links, twin's origin equals destination, prev/next reciprocity, next-origin matches twin-origin (loop stitching), and consistent face membership across the loop.</description></item>
    ///   <item><description>Per face: <see cref="Face.FirstHalfEdge"/> is live and points back at the face; the face loop closes within <see cref="LiveCount(TopologyStorage{TTag,TConnectivity})"/> steps.</description></item>
    ///   <item><description>Per vertex: <see cref="Vertex.OutgoingHalfEdge"/> is live and originates at the vertex; the vertex ring closes within the half-edge live count.</description></item>
    ///   <item><description>Edge manifoldness (Tier 1): no two live half-edges share the same ordered (origin, destination) pair.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Limitation:</b> non-manifold vertex fans (a "bowtie", where multiple disjoint half-edge
    /// rings share a single vertex) are not detected. Catching that would require comparing the
    /// total number of live half-edges with <c>Origin == v</c> against the length of
    /// <see cref="HalfEdgesAroundVertex"/> for each vertex; this is currently deferred.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The mesh is in an inconsistent state.</exception>
    internal void ValidateConsistency()
    {
        // Phase 1: per half-edge structural checks.
        foreach (var h in HalfEdges)
        {
            ref var he = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(h);

            // Origin must be live.
            if (he.Origin.IsNull || !Vertices.IsAlive(he.Origin))
                throw new InvalidOperationException(
                    $"ValidateConsistency: half-edge {h} has dead/null Origin {he.Origin}."
                );

            // Twin must be live and reciprocal.
            if (he.Twin.IsNull || !HalfEdges.IsAlive(he.Twin))
                throw new InvalidOperationException(
                    $"ValidateConsistency: half-edge {h} has dead/null Twin {he.Twin}."
                );
            if (he.Twin == h)
                throw new InvalidOperationException(
                    $"ValidateConsistency: half-edge {h} is its own twin."
                );
            ref var twin = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(he.Twin);
            if (twin.Twin != h)
                throw new InvalidOperationException(
                    $"ValidateConsistency: twin reciprocity broken: {h}.Twin == {he.Twin}, but {he.Twin}.Twin == {twin.Twin}."
                );

            // Twin's origin = current's destination, hence not equal to current's origin.
            if (twin.Origin == he.Origin)
                throw new InvalidOperationException(
                    $"ValidateConsistency: half-edge {h} and its twin {he.Twin} share Origin {he.Origin}; expected distinct endpoints."
                );

            // Face membership and loop links must be live.
            if (!he.Face.IsNull && !Faces.IsAlive(he.Face))
                throw new InvalidOperationException(
                    $"ValidateConsistency: half-edge {h} has dead Face {he.Face}."
                );
            if (he.Next.IsNull || !HalfEdges.IsAlive(he.Next))
                throw new InvalidOperationException(
                    $"ValidateConsistency: half-edge {h} has dead/null Next {he.Next}."
                );
            if (he.Prev.IsNull || !HalfEdges.IsAlive(he.Prev))
                throw new InvalidOperationException(
                    $"ValidateConsistency: half-edge {h} has dead/null Prev {he.Prev}."
                );

            ref var heNext = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(he.Next);
            ref var hePrev = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(he.Prev);

            if (heNext.Prev != h)
                throw new InvalidOperationException(
                    $"ValidateConsistency: prev/next reciprocity broken: {h}.Next == {he.Next}, but {he.Next}.Prev == {heNext.Prev}."
                );
            if (hePrev.Next != h)
                throw new InvalidOperationException(
                    $"ValidateConsistency: prev/next reciprocity broken: {h}.Prev == {he.Prev}, but {he.Prev}.Next == {hePrev.Next}."
                );

            // Loop stitching: Next half-edge must originate where this one ends (twin's origin).
            if (heNext.Origin != twin.Origin)
                throw new InvalidOperationException(
                    $"ValidateConsistency: half-edge {h} ends at {twin.Origin}, but {h}.Next == {he.Next} starts at {heNext.Origin}."
                );

            // Loop members share the same face (interior or boundary loop).
            if (heNext.Face != he.Face)
                throw new InvalidOperationException(
                    $"ValidateConsistency: half-edge {h} has Face {he.Face}, but {h}.Next == {he.Next} has Face {heNext.Face}."
                );
            if (hePrev.Face != he.Face)
                throw new InvalidOperationException(
                    $"ValidateConsistency: half-edge {h} has Face {he.Face}, but {h}.Prev == {he.Prev} has Face {hePrev.Face}."
                );
        }

        // Phase 2: per-face checks.
        foreach (var f in Faces)
        {
            ref var face = ref Faces.GetUnsafeRef<Face, Face>(f);
            if (face.FirstHalfEdge.IsNull || !HalfEdges.IsAlive(face.FirstHalfEdge))
                throw new InvalidOperationException(
                    $"ValidateConsistency: face {f} has dead/null FirstHalfEdge {face.FirstHalfEdge}."
                );

            ref var anchor = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(face.FirstHalfEdge);
            if (anchor.Face != f)
                throw new InvalidOperationException(
                    $"ValidateConsistency: face {f}.FirstHalfEdge == {face.FirstHalfEdge}, but that half-edge has Face {anchor.Face}."
                );

            // Walk the loop with a hard safety cap; per-step face membership is already
            // checked in Phase 1, so the only thing left is closure within LiveCount steps.
            // I question the usefulness of this, it succeeding doesn't do a lot to guaruntee
            // validity.
            int budget = HalfEdges.LiveCount + 1;
            var current = face.FirstHalfEdge;
            do
            {
                ref var ch = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(current);
                current = ch.Next;
                if (--budget < 0)
                    throw new InvalidOperationException(
                        $"ValidateConsistency: face {f} loop did not close within {HalfEdges.LiveCount} half-edges."
                    );
            } while (current != face.FirstHalfEdge);
        }

        // Phase 3: per-vertex checks.
        foreach (var v in Vertices)
        {
            ref var vertex = ref Vertices.GetUnsafeRef<Vertex, Vertex>(v);
            var outgoing = vertex.OutgoingHalfEdge;
            if (outgoing.IsNull)
                continue;

            if (!HalfEdges.IsAlive(outgoing))
                throw new InvalidOperationException(
                    $"ValidateConsistency: vertex {v} has dead OutgoingHalfEdge {outgoing}."
                );

            ref var anchor = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(outgoing);
            if (anchor.Origin != v)
                throw new InvalidOperationException(
                    $"ValidateConsistency: vertex {v}.OutgoingHalfEdge == {outgoing}, but that half-edge has Origin {anchor.Origin}."
                );

            // Walk the vertex ring with a hard safety cap; per-step Origin is implicitly
            // checked by the ring-stepping rule (Prev.Twin) plus Phase 1 invariants.
            // I question the usefulness of this, it succeeding doesn't do a lot to guaruntee
            // validity.
            int budget = HalfEdges.LiveCount + 1;
            var current = outgoing;
            do
            {
                ref var ch = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(current);
                ref var prev = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(ch.Prev);
                var nextOutgoing = prev.Twin;
                ref var no = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(nextOutgoing);
                if (no.Origin != v)
                    throw new InvalidOperationException(
                        $"ValidateConsistency: vertex {v} ring stepped from {current} to {nextOutgoing} which has Origin {no.Origin}."
                    );
                current = nextOutgoing;
                if (--budget < 0)
                    throw new InvalidOperationException(
                        $"ValidateConsistency: vertex {v} ring did not close within {HalfEdges.LiveCount} half-edges."
                    );
            } while (current != outgoing);
        }

        // Phase 4 (Tier 1 manifold check): no two live half-edges share an ordered (origin, dest) pair.
        // Combined with twin reciprocity proven in Phase 1, this implies every undirected edge has
        // exactly two half-edges (one per direction).
        var seen = new HashSet<long>(HalfEdges.LiveCount);
        foreach (var h in HalfEdges)
        {
            ref var he = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(h);
            ref var twin = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(he.Twin);
            int originDense = Vertices.GetDenseIndex(he.Origin);
            int destDense = Vertices.GetDenseIndex(twin.Origin);
            long key = ((long)(uint)originDense << 32) | (uint)destDense;
            if (!seen.Add(key))
                throw new InvalidOperationException(
                    $"ValidateConsistency: multiple half-edges from vertex (dense {originDense}) to vertex (dense {destDense}); the mesh is not edge-manifold."
                );
        }
    }
}
