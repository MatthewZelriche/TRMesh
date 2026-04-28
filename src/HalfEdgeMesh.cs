namespace TREditorSharp;

using System;
using TREditorSharp.Storage;

/// <summary>
/// Top-level Half-Edge Mesh facade. Owns one <see cref="TopologyStorage{TTag,TConnectivity}"/>
/// per entity kind (vertices, half-edges, faces), each of which manages its own
/// allocator and registered component columns.
///
/// The mesh itself stores no geometry — geometry is just a registered column.
/// This keeps the connectivity core minimal and decouples it from any particular
/// math library.
///
/// Implements <see cref="IDisposable"/> because user-registered native columns
/// own unmanaged memory.
/// </summary>
public partial class HalfEdgeMesh : IDisposable
{
    public TopologyStorage<VertexTag, Vertex> Vertices { get; }

    public TopologyStorage<HalfEdgeTag, HalfEdge> HalfEdges { get; }

    public TopologyStorage<FaceTag, Face> Faces { get; }

    private bool _disposed;

    public HalfEdgeMesh()
    {
        Vertices = new TopologyStorage<VertexTag, Vertex>();
        HalfEdges = new TopologyStorage<HalfEdgeTag, HalfEdge>();
        Faces = new TopologyStorage<FaceTag, Face>();
    }

    /// <summary>
    /// Reset entire mesh. Backing memory is not released. All handles are invalidated.
    /// </summary>
    public void Clear()
    {
        Vertices.Clear();
        HalfEdges.Clear();
        Faces.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Vertices.Dispose();
        HalfEdges.Dispose();
        Faces.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Add a face whose boundary, in CCW order, is given by <paramref name="vertices"/>.
    /// Returns the handle of the freshly allocated face.
    ///
    /// <para>
    /// The mesh state is mutated only after a full validation pass succeeds, so a
    /// thrown exception leaves the mesh in its previous state prior to this call.
    /// </para>
    ///
    /// <para>
    /// <b>Preconditions:</b>
    /// <list type="bullet">
    ///   <item><description><paramref name="vertices"/>.Length is at least 3.</description></item>
    ///   <item><description>Every handle in <paramref name="vertices"/> refers to a live vertex.</description></item>
    ///   <item><description>For every consecutive pair of vertices, any pre-existing
    ///   half-edge between them must be a boundary half-edge (Face.IsNull).
    ///   To attach a face on the opposite side of an existing interior edge, pass the
    ///   shared vertices in the reverse order.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public FaceHandle AddFace(ReadOnlySpan<VertexHandle> vertices)
    {
        if (vertices.Length < 3)
            throw new ArgumentException("AddFace: Face must have at least 3 vertices.");

        // Allocate scratch buffers for the validation/write/linking phases.
        // We cache half-edge handles for any existing boundary half-edges between
        // consecutive vertices so we don't iterate vertex rings more than once,
        // and we store the new face's half-edges in sequence so the linking phase
        // can step through them in order.
        // stackalloc memory is zero-initialized, which yields null HalfEdgeHandles
        Span<HalfEdgeHandle> existingHandles = stackalloc HalfEdgeHandle[vertices.Length];
        Span<HalfEdgeHandle> hedges = stackalloc HalfEdgeHandle[vertices.Length];

        // Validation phase. We need to:
        // 1. Ensure all vertex handles are live.
        // 2. Ensure edges between consecutive vertices either don't yet exist,
        //    or, if they do, are boundary edges (Face.IsNull).
        for (int i = 0; i < vertices.Length; i++)
        {
            var vtx = vertices[i];
            var vtxNext = vertices[(i + 1) % vertices.Length];

            if (!Vertices.IsAlive(vtx))
                throw new ArgumentException(
                    $"AddFace: Vertex {vtx} (at index {i}) is not a live handle."
                );
            if (!Vertices.IsAlive(vtxNext))
                throw new ArgumentException(
                    $"AddFace: Vertex {vtxNext} (at index {(i + 1) % vertices.Length}) is not a live handle."
                );

            var existing = FindHalfEdgeBetweenUnchecked(vtx, vtxNext);
            //if (!existing.IsNull)
            if (HalfEdges.IsAlive(existing))
            {
                ref var existingHe = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(existing);
                //if (!existingHe.Face.IsNull)
                if (Faces.IsAlive(existingHe.Face))
                    throw new ArgumentException(
                        $"AddFace: An edge from {vtx} to {vtxNext} already exists and is not a boundary edge."
                    );
                // Existing boundary edge, cache it so we can update it after validation.
                existingHandles[i] = existing;
            }
        }

        // Write phase. Validation passed, so we can safely mutate the mesh.
        var faceHandle = Faces.Allocate();
        for (int i = 0; i < vertices.Length; i++)
        {
            var vtx = vertices[i];
            var vtxNext = vertices[(i + 1) % vertices.Length];

            // Safe to skip an alive check here, since all handles are either zero-initialized or
            // cached from the validation phase.
            if (!existingHandles[i].IsNull)
            {
                // Update the existing boundary half-edge to no longer be a boundary. Later, in
                // the third phase, we will update its prev/next pointers to construct the correct
                // cycle.
                HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(existingHandles[i]).Face = faceHandle;
                hedges[i] = existingHandles[i];
            }
            else
            {
                // Brand-new edge. Allocate both halves, wire up Origin/Twin and
                // each vertex's OutgoingHalfEdge if it didn't already have one.
                var heHandle = HalfEdges.Allocate();
                var twinHandle = HalfEdges.Allocate();
                ConstructEdge(heHandle, twinHandle, vtx, vtxNext);
                // As above, we must assign the face to this new half-edge.
                HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(heHandle).Face = faceHandle;
                hedges[i] = heHandle;
            }
        }

        // Faces store any arbitrary half-edge adjacent to it.
        Faces.GetUnsafeRef<Face, Face>(faceHandle).FirstHalfEdge = hedges[0];

        // Pointer linking phase. Stitch the new face's half-edges into a closed
        // interior loop and splice their twins into the surrounding boundary.
        for (int i = 0; i < vertices.Length; i++)
        {
            var heHandle = hedges[i];
            var heNextHandle = hedges[(i + 1) % vertices.Length];

            // Cache refs once. Safe: this loop performs no allocation, free, or
            // clear, so the backing storage cannot move under us.
            ref var he = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(heHandle);
            ref var heTwin = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(he.Twin);
            ref var heNext = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(heNextHandle);
            ref var heNextTwin = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(heNext.Twin);

            var curr = existingHandles[i];
            var next = existingHandles[(i + 1) % vertices.Length];

            // Boundary-side ("exterior")splicing. The boundary loop traverses opposite to the
            // interior, so heTwin's predecessor on the boundary is heNextTwin.
            // Again, it is safe to skip the alive checks here.
            if (!next.IsNull)
            {
                // The next interior edge already existed; its boundary partner had
                // a Prev pointing into the surrounding mesh. Reroute that Prev's
                // Next through our new heTwin and inherit it as heTwin.Prev.
                HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(heNext.Prev).Next = he.Twin;
                heTwin.Prev = heNext.Prev;
            }
            else if (!curr.IsNull)
            {
                // The current interior edge already existed; splice the existing
                // Next chain onto the brand-new heNextTwin.
                heNextTwin.Next = he.Next;
                HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(he.Next).Prev = heNext.Twin;
            }
            else
            {
                // Both edges are brand new; just link the boundary twins together.
                heTwin.Prev = heNext.Twin;
                heNextTwin.Next = he.Twin;
            }

            // Interior setup is straightforward, just linking together the half-edges in sequence.
            he.Next = heNextHandle;
            heNext.Prev = heHandle;
        }

        // Let out a sigh of relief, all that hard work is done!
        return faceHandle;
    }

    /// <summary>
    /// Find the outgoing half-edge from <paramref name="from"/> whose destination
    /// is <paramref name="to"/>, or <see cref="Handle{T}.Null"/> if no such edge
    /// exists. Does not validate handles; the caller must ensure both vertices
    /// are live.
    /// </summary>
    private HalfEdgeHandle FindHalfEdgeBetweenUnchecked(VertexHandle from, VertexHandle to)
    {
        foreach (var h in HalfEdgesAroundVertex(from))
        {
            ref var he = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(h);
            ref var twin = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(he.Twin);
            if (twin.Origin == to)
                return h;
        }
        return HalfEdgeHandle.Null;
    }

    /// <summary>
    /// Initialize a freshly allocated half-edge pair <paramref name="h"/> /
    /// <paramref name="twin"/> as the edge between <paramref name="origin"/> and
    /// <paramref name="destination"/>. Sets Origin/Twin on both halves, and points
    /// each endpoint vertex's OutgoingHalfEdge at the new half-edge if it was
    /// previously null. Face/Next/Prev are left at default; the caller is
    /// expected to assign them.
    ///
    /// <para>
    /// <b>Warning:</b> The caller must ensure that a half-edge does not already exist between
    /// <paramref name="origin"/> and <paramref name="destination"/>. Failing to do so can cause
    /// invalid mesh topology and result in undefined behavior.
    /// </para>
    /// </summary>
    private void ConstructEdge(
        HalfEdgeHandle h,
        HalfEdgeHandle twin,
        VertexHandle origin,
        VertexHandle destination
    )
    {
        ref var he = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(h);
        he.Origin = origin;
        he.Twin = twin;

        ref var heTwin = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(twin);
        heTwin.Origin = destination;
        heTwin.Twin = h;

        ref var vOrigin = ref Vertices.GetUnsafeRef<Vertex, Vertex>(origin);
        if (vOrigin.OutgoingHalfEdge.IsNull)
            vOrigin.OutgoingHalfEdge = h;

        ref var vDest = ref Vertices.GetUnsafeRef<Vertex, Vertex>(destination);
        if (vDest.OutgoingHalfEdge.IsNull)
            vDest.OutgoingHalfEdge = twin;
    }
}
