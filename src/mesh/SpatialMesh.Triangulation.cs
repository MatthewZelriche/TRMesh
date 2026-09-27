using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TRMesh.Mesh;

public unsafe partial class SpatialMesh
{
    // Appends a CCW triangulation of `face` to `output`.
    //
    // Each group of three integers is a triangle. Each integer is a half-edge handle on
    // this face; SourceVertex is the corner vertex and the half-edge UV is that corner's UV.
    //
    // On failure `output` is left unchanged. Vertex positions must not be mutated during
    // this call: positions are cached up front and the rest of the algorithm reads only
    // that cache.
    public bool TriangulateFace(int face, List<int> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!_faces.IsAlive(face))
            return false;

        int start = _faces.Get(face).topology.AdjacentHalfEdge;
        if (start == INVALID_HANDLE)
            return false;

        // Triangle peek: the overwhelmingly common case, so skip stackalloc and
        // the second loop walk entirely.
        int h0 = start;
        int h1 = HalfEdgePtr(h0)->NextHalfEdge;
        if (h1 == INVALID_HANDLE)
            return false;
        int h2 = HalfEdgePtr(h1)->NextHalfEdge;
        if (h2 == INVALID_HANDLE)
            return false;
        int h3 = HalfEdgePtr(h2)->NextHalfEdge;
        if (h3 == INVALID_HANDLE)
            return false;
        // Trivial case check - is this just a triangle?
        if (h3 == h0)
        {
            CommitThree(output, h0, h1, h2);
            return true;
        }

        // For an arbitrary ngon, compute the number of face corners and collect the relevant data
        int count = 3;
        int he = h3;
        do
        {
            count++;
            he = HalfEdgePtr(he)->NextHalfEdge;
            if (he == INVALID_HANDLE)
                return false;
        } while (he != start);

        Span<int> corners = stackalloc int[count];
        Span<Vector3> positions = stackalloc Vector3[count];
        he = start;
        for (int i = 0; i < count; i++)
        {
            HalfEdge* halfEdge = HalfEdgePtr(he);
            corners[i] = he;
            positions[i] = _vertices.Get(halfEdge->SourceVertex).position;
            he = halfEdge->NextHalfEdge;
        }

        return TriangulateFaceCorners(corners, positions, output);
    }

    // Ear-clips a simple polygonal face given its corners and matching positions.
    // Convex polygons take an O(n) fan; concave polygons fall through to ear clipping.
    internal static bool TriangulateFaceCorners(
        ReadOnlySpan<int> corners,
        ReadOnlySpan<Vector3> positions,
        List<int> output
    )
    {
        if (corners.Length != positions.Length || corners.Length < 3)
            return false;

        int count = corners.Length;
        if (count == 3)
        {
            CommitThree(output, corners[0], corners[1], corners[2]);
            return true;
        }

        Vector3 normal = Util.Math.ComputeNewellNormal(positions);
        Span<int> triangles = stackalloc int[(count - 2) * 3];

        // If our face is convex we can get away with a quick O(n) triangle fan
        if (IsConvex(positions, normal))
        {
            int written = 0;
            int apex = corners[0];
            for (int i = 1; i < count - 1; i++)
            {
                triangles[written++] = apex;
                triangles[written++] = corners[i];
                triangles[written++] = corners[i + 1];
            }

            CommitTriangles(output, triangles);
            return true;
        }

        return EarClip(corners, positions, normal, triangles, output);
    }

    static bool EarClip(
        ReadOnlySpan<int> corners,
        ReadOnlySpan<Vector3> positions,
        Vector3 normal,
        Span<int> triangles,
        List<int> output
    )
    {
        int count = corners.Length;
        Span<int> ringPrev = stackalloc int[count];
        Span<int> ringNext = stackalloc int[count];
        ringPrev[0] = count - 1;
        ringNext[count - 1] = 0;
        for (int i = 1; i < count; i++)
        {
            ringPrev[i] = i - 1;
            ringNext[i - 1] = i;
        }

        int remaining = count;
        int cursor = 0;
        int written = 0;

        while (remaining > 3)
        {
            bool foundEar = false;
            int candidate = cursor;
            for (int step = 0; step < remaining; step++)
            {
                int i = candidate;
                int p = ringPrev[i];
                int q = ringNext[i];

                Vector3 edge1 = positions[i] - positions[p];
                Vector3 edge2 = positions[q] - positions[i];
                if (Vector3.Dot(Vector3.Cross(edge1, edge2), normal) <= 0f)
                {
                    candidate = q;
                    continue;
                }

                bool anyInside = false;
                for (int j = ringNext[q]; j != p; j = ringNext[j])
                {
                    if (
                        Util.Math.PointInTriangle3D(
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

                triangles[written++] = corners[p];
                triangles[written++] = corners[i];
                triangles[written++] = corners[q];

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

        int t0 = cursor;
        int t1 = ringNext[t0];
        int t2 = ringNext[t1];
        triangles[written++] = corners[t0];
        triangles[written++] = corners[t1];
        triangles[written++] = corners[t2];

        CommitTriangles(output, triangles);
        return true;
    }

    static bool IsConvex(ReadOnlySpan<Vector3> positions, Vector3 normal)
    {
        int count = positions.Length;
        int prev = count - 1;
        for (int i = 0; i < count; i++)
        {
            int next = i + 1;
            if (next == count)
                next = 0;

            Vector3 edge1 = positions[i] - positions[prev];
            Vector3 edge2 = positions[next] - positions[i];
            if (Vector3.Dot(Vector3.Cross(edge1, edge2), normal) <= 0f)
                return false;

            prev = i;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void CommitThree(List<int> output, int a, int b, int c)
    {
        int index = output.Count;
        CollectionsMarshal.SetCount(output, index + 3);
        Span<int> span = CollectionsMarshal.AsSpan(output);
        span[index] = a;
        span[index + 1] = b;
        span[index + 2] = c;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void CommitTriangles(List<int> output, ReadOnlySpan<int> triangles)
    {
        int index = output.Count;
        CollectionsMarshal.SetCount(output, index + triangles.Length);
        triangles.CopyTo(CollectionsMarshal.AsSpan(output)[index..]);
    }
}
