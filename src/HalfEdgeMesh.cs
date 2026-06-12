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
    private readonly TopologyStorage<VertexTag, Vertex> _vertices;
    private readonly TopologyStorage<HalfEdgeTag, HalfEdge> _halfEdges;
    private readonly TopologyStorage<FaceTag, Face> _faces;

    /// <summary>
    /// Vertex topology storage. <c>internal</c> so format writers and other in-assembly
    /// code can read columns and dense indices; external assemblies use public mesh APIs only.
    /// </summary>
    internal TopologyStorage<VertexTag, Vertex> Vertices => _vertices;

    /// <summary>Half-edge topology storage. See <see cref="Vertices"/>.</summary>
    internal TopologyStorage<HalfEdgeTag, HalfEdge> HalfEdges => _halfEdges;

    /// <summary>Face topology storage. See <see cref="Vertices"/>.</summary>
    internal TopologyStorage<FaceTag, Face> Faces => _faces;

    /// <summary>Stack-only iteration of live <see cref="FaceHandle"/> values in unspecified order.</summary>
    public TopologyStorage<FaceTag, Face>.LiveHandleEnumerable EnumerateLiveFaces() => _faces.Live;

    /// <summary>Stack-only iteration of live <see cref="VertexHandle"/> values in unspecified order.</summary>
    public TopologyStorage<VertexTag, Vertex>.LiveHandleEnumerable EnumerateLiveVertices() =>
        _vertices.Live;

    /// <summary>Stack-only iteration of live <see cref="HalfEdgeHandle"/> values in unspecified order.</summary>
    public TopologyStorage<HalfEdgeTag, HalfEdge>.LiveHandleEnumerable EnumerateLiveHalfEdges() =>
        _halfEdges.Live;

    /// <summary>Connectivity read for <paramref name="handle"/>; throws if not live.</summary>
    public HalfEdge GetHalfEdge(HalfEdgeHandle handle) => _halfEdges[handle];

    /// <summary>True when <paramref name="vertex"/> still refers to a live vertex.</summary>
    public bool IsVertexAlive(VertexHandle vertex) => _vertices.IsAlive(vertex);

    /// <summary>True when <paramref name="halfEdge"/> still refers to a live half-edge.</summary>
    public bool IsHalfEdgeAlive(HalfEdgeHandle halfEdge) => _halfEdges.IsAlive(halfEdge);

    /// <summary>True when <paramref name="face"/> still refers to a live face.</summary>
    public bool IsFaceAlive(FaceHandle face) => _faces.IsAlive(face);

    private bool _disposed;

    public HalfEdgeMesh()
    {
        _vertices = new TopologyStorage<VertexTag, Vertex>();
        _halfEdges = new TopologyStorage<HalfEdgeTag, HalfEdge>();
        _faces = new TopologyStorage<FaceTag, Face>();
    }

    /// <summary>
    /// Reset entire mesh. Backing memory is not released. All handles are invalidated.
    /// </summary>
    public void Clear()
    {
        _vertices.Clear();
        _halfEdges.Clear();
        _faces.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _vertices.Dispose();
        _halfEdges.Dispose();
        _faces.Dispose();
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

        // Cache the OLD boundary Prev/Next of every existing (promoted) half-edge before the
        // linking phase begins, since the linking phase will overwrite those fields with
        // interior values.
        // TODO: I feel like we are starting to push the boundaries of what is reasonable when it
        // comes to the use of stackalloc.
        Span<HalfEdgeHandle> oldPrev = stackalloc HalfEdgeHandle[vertices.Length];
        Span<HalfEdgeHandle> oldNext = stackalloc HalfEdgeHandle[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            if (existingHandles[i].IsNull)
                continue;
            ref var existingHe = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(existingHandles[i]);
            oldPrev[i] = existingHe.Prev;
            oldNext[i] = existingHe.Next;
        }

        // Pointer linking phase. Stitch the new face's half-edges into a closed
        // interior loop and splice their twins into the surrounding boundary.
        // At each corner V_{i+1}, we identify the boundary half-edge that arrives at
        // V_{i+1} ("incoming") and the one that leaves V_{i+1} ("outgoing"), then link
        // them. Whether incoming/outgoing are old boundary halves or freshly created
        // twins depends on whether the current/next edge of the new face already existed.
        for (int i = 0; i < vertices.Length; i++)
        {
            int iNext = (i + 1) % vertices.Length;
            var heHandle = hedges[i];
            var heNextHandle = hedges[iNext];

            // Cache the twin handles (and existence flags) up front, since the boundary
            // update may overwrite he.Twin / heNext.Twin's Prev/Next fields.
            HalfEdgeHandle heTwinHandle;
            HalfEdgeHandle heNextTwinHandle;
            {
                ref var he = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(heHandle);
                heTwinHandle = he.Twin;
                ref var heNext = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(heNextHandle);
                heNextTwinHandle = heNext.Twin;
            }

            bool currExisted = !existingHandles[i].IsNull;
            bool nextExisted = !existingHandles[iNext].IsNull;

            // Outgoing from V_{i+1}: heA.Next_old if heA existed, else heA.Twin (new boundary).
            HalfEdgeHandle outgoing = currExisted ? oldNext[i] : heTwinHandle;
            // Incoming to V_{i+1}: heB.Prev_old if heB existed, else heB.Twin (new boundary).
            HalfEdgeHandle incoming = nextExisted ? oldPrev[iNext] : heNextTwinHandle;

            HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(incoming).Next = outgoing;
            HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(outgoing).Prev = incoming;

            // Interior linking: the new face's interior loop runs CCW through hedges[].
            // (Safe to overwrite even after the boundary update above, since for the
            // both-existed adjacent case the boundary update wrote the same values.)
            ref var heW = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(heHandle);
            ref var heNextW = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(heNextHandle);
            heW.Next = heNextHandle;
            heNextW.Prev = heHandle;
        }

        // Let out a sigh of relief, all that hard work is done!
        return faceHandle;
    }

    /// <summary>
    /// Removes only <paramref name="face"/>, preserving its boundary edges and vertices.
    /// The face's interior half-edges become a boundary loop and retain their handles and
    /// component-column values so the same polygon can be restored later with <see cref="AddFace"/>.
    /// </summary>
    /// <returns><c>true</c> when a live face was removed; otherwise <c>false</c>.</returns>
    public bool RemoveFace(FaceHandle face)
    {
        if (!Faces.IsAlive(face))
            return false;

        foreach (HalfEdgeHandle halfEdge in HalfEdgesAroundFace(face))
            HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(halfEdge).Face = FaceHandle.Null;

        Faces.Free(face);
        return true;
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
