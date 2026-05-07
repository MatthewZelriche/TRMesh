namespace TREditorSharp;

using System.Numerics;
using System.Runtime.CompilerServices;
using TREditorSharp.Storage;

/// <summary>
/// HalfEdgeMesh that represents a 3D mesh that can be converted and displayed as a polygon mesh.
/// </summary>
public class SpatialMesh : HalfEdgeMesh
{
    private NativeColumn<Vector3> VertexPositions { get; }

    public SpatialMesh()
        : base()
    {
        VertexPositions = Vertices.RegisterNativeColumn<Vector3, VertexPositionTag>();
    }

    /// <summary>
    /// Allocate a fresh vertex with <paramref name="position"/> and return its handle.
    /// </summary>
    public VertexHandle AddVertex(Vector3 position)
    {
        var v = Vertices.Allocate();
        VertexPositions[Vertices.GetDenseIndex(v)] = position;
        return v;
    }

    /// <summary>World-space position for a live vertex.</summary>
    public Vector3 GetVertexPosition(VertexHandle vertex) =>
        VertexPositions[Vertices.GetDenseIndex(vertex)];

    /// <summary>
    /// Position by dense vertex index (as produced by <see cref="TriangulateFace"/> index output).
    /// </summary>
    public Vector3 GetVertexPositionByDenseIndex(int denseIndex) => VertexPositions[denseIndex];

    /// <summary>
    /// Triangulate <paramref name="face"/> into triangles using ear clipping
    /// and append <c>(n - 2) * 3</c> dense vertex indices to <paramref name="output"/>,
    /// where <c>n</c> is the number of vertices around the face. Dense indices are
    /// looked up via <see cref="TopologyStorage{TTag,TConnectivity}.GetDenseIndex"/>
    /// on the vertex storage and are suitable for direct submission to a GPU index
    /// buffer alongside the dense-ordered vertex position column.
    ///
    /// <para>
    /// Returns true on success. Returns false when any of the following hold;
    /// <paramref name="output"/> may have been partially appended in those cases and the caller
    /// should treat any indices it added past the original <c>Count</c> as discarded:
    /// <list type="bullet">
    ///   <item><description><paramref name="face"/> is dead (e.g. freed or never allocated).</description></item>
    ///   <item><description>The face has fewer than three vertices.</description></item>
    ///   <item><description>An ear could not be found (typically a self-intersecting
    ///   polygon, all-colinear vertices, or otherwise degenerate input).</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Mutation contract:</b> the caller must not allocate, free, or clear vertices
    /// (or otherwise reorder the dense layout of the vertex column or
    /// <see cref="HalfEdges"/>) for the duration of this call. Doing so would invalidate
    /// the dense indices and the cached <see cref="NativeColumn{T}.DataPtr"/> used here.
    /// Additionally, the caller must not mutate vertex data for as long as they are using the output
    /// indices, UNLESS they copy out the dense vertex data and clear the output list first.
    /// TODO: Double check this comment is actually correct. It'll depend on exactly how
    /// batching dirty faces is implemented.
    /// </para>
    /// </summary>
    // TODO: add unit tests for TriangulateFace
    public bool TriangulateFace(FaceHandle face, List<int> output)
    {
        if (!Faces.IsAlive(face))
            return false;

        // Phase A: collect vertices around the face.
        // Walk the face loop once to count, then once more to fill an exactly-sized
        // stackalloc buffer. The double-walk is two pointer chases per half-edge,
        // which is dwarfed by anything else this method does.
        // TODO: We could get rid of this double loop if we replaced the List param with
        // a custom collection type backed by a bump allocator.
        int count = 0;
        foreach (var _ in HalfEdgesAroundFace(face))
            count++;

        if (count < 3)
            return false;

        Span<VertexHandle> verts = stackalloc VertexHandle[count];
        int filled = 0;
        foreach (var heHandle in HalfEdgesAroundFace(face))
        {
            ref var he = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(heHandle);
            verts[filled++] = he.Origin;
        }

        // Phase B: cache dense indices once, since GetDenseIndex is a sparse-set lookup
        // and we read each one multiple times in the ear-clipping inner loop.
        Span<int> denseIdx = stackalloc int[count];
        for (int i = 0; i < count; i++)
            denseIdx[i] = Vertices.GetDenseIndex(verts[i]);

        // After this point we are no longer allowed to mutate the vertex data!

        // Triangle fast path: skip normal/ear work entirely.
        if (count == 3)
        {
            output.Add(denseIdx[0]);
            output.Add(denseIdx[1]);
            output.Add(denseIdx[2]);
            return true;
        }

        // Cache the position column's raw pointer once. Safe because we promised
        // not to mutate the vertex column during this call (see XML doc).
        Span<Vector3> positions = stackalloc Vector3[count];
        unsafe
        {
            Vector3* posPtr = VertexPositions.DataPtr;
            for (int i = 0; i < count; i++)
                positions[i] = posPtr[denseIdx[i]];
        }

        // Phase C: Newell's method for a robust face normal. Direction-only; we never
        // normalize because every consumer (convex test, point-in-triangle test) uses
        // dot/cross sign comparisons that are scale-invariant.
        Vector3 normal = default;
        for (int i = 0; i < count; i++)
        {
            var a = positions[i];
            var b = positions[(i + 1) % count];
            normal.X += (a.Y - b.Y) * (a.Z + b.Z);
            normal.Y += (a.Z - b.Z) * (a.X + b.X);
            normal.Z += (a.X - b.X) * (a.Y + b.Y);
        }

        // Phase D: ear-clipping main loop with a doubly-linked ring over indices
        // [0, count). cursor tracks where to resume the next ear search after a clip; well-behaved
        // convex polygons clip in roughly a single forward pass overall instead of restarting from 0 each time.
        Span<int> ringPrev = stackalloc int[count];
        Span<int> ringNext = stackalloc int[count];
        for (int i = 0; i < count; i++)
        {
            ringPrev[i] = (i + count - 1) % count;
            ringNext[i] = (i + 1) % count;
        }
        int remaining = count;
        int cursor = 0;

        while (remaining > 3)
        {
            bool foundEar = false;
            int candidate = cursor;
            for (int step = 0; step < remaining; step++)
            {
                int i = candidate;
                int p = ringPrev[i];
                int q = ringNext[i];

                // Convex turn test: prev->cur->next must turn in the direction of
                // the face normal (matches the polygon's CCW winding around `normal`).
                var edge1 = positions[i] - positions[p];
                var edge2 = positions[q] - positions[i];
                if (Vector3.Dot(Vector3.Cross(edge1, edge2), normal) <= 0f)
                {
                    candidate = q;
                    continue;
                }

                // Empty-triangle test: walk the live ring strictly between q and p
                // (skipping p, i, q themselves) and reject the ear if any other live
                // vertex falls inside the candidate triangle.
                bool anyInside = false;
                for (int j = ringNext[q]; j != p; j = ringNext[j])
                {
                    if (
                        PointInTriangle3D(
                            positions[j],
                            positions[p],
                            positions[i],
                            positions[q],
                            normal
                        )
                    )
                    {
                        anyInside = true;
                        break;
                    }
                }
                if (anyInside)
                {
                    candidate = q;
                    continue;
                }

                // Emit the ear in the polygon's CCW order, clip i from the ring,
                // and resume next search at q since p and q are the only vertices
                // whose neighborhoods just changed.
                output.Add(denseIdx[p]);
                output.Add(denseIdx[i]);
                output.Add(denseIdx[q]);

                ringNext[p] = q;
                ringPrev[q] = p;
                remaining--;
                cursor = q;
                foundEar = true;
                break;
            }

            if (!foundEar)
                return false;
        }

        // Final triangle: walk three steps of the live ring from cursor.
        int t0 = cursor;
        int t1 = ringNext[t0];
        int t2 = ringNext[t1];
        output.Add(denseIdx[t0]);
        output.Add(denseIdx[t1]);
        output.Add(denseIdx[t2]);
        return true;
    }

    /// <summary>
    /// Standard 3D same-side point-in-triangle test relative to a precomputed face
    /// <paramref name="normal"/>. Inclusive on edges, which avoids spuriously
    /// rejecting ears whose colinear neighbors lie on the candidate triangle's
    /// boundary. Accepts either polygon winding by allowing all-non-negative or
    /// all-non-positive sign combinations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool PointInTriangle3D(
        in Vector3 p,
        in Vector3 a,
        in Vector3 b,
        in Vector3 c,
        in Vector3 normal
    )
    {
        var s1 = Vector3.Dot(Vector3.Cross(b - a, p - a), normal);
        var s2 = Vector3.Dot(Vector3.Cross(c - b, p - b), normal);
        var s3 = Vector3.Dot(Vector3.Cross(a - c, p - c), normal);
        return (s1 >= 0f && s2 >= 0f && s3 >= 0f) || (s1 <= 0f && s2 <= 0f && s3 <= 0f);
    }
}
