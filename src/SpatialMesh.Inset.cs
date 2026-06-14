namespace TREditorSharp;

using System.Numerics;

public partial class SpatialMesh
{
    private const float InsetValidityEpsilon = 1e-5f;
    private const int MaximumInsetSearchIterations = 32;

    /// <summary>
    /// Replace a face with an inward-offset cap and a surrounding ring.
    /// </summary>
    public InsetFaceResult InsetFace(FaceHandle face, float depth)
    {
        if (!(depth > 0f) || !float.IsFinite(depth))
            throw new ArgumentOutOfRangeException(nameof(depth), "Inset depth must be positive.");

        VertexHandle[] vertices = CollectFaceVertices(face);
        Vector3[] positions = vertices.Select(GetVertexPosition).ToArray();
        Vector3 normal = ComputeFaceNormal(face);
        if (normal == Vector3.Zero)
            throw new ArgumentException("Inset source face must not be degenerate.", nameof(face));
        float maximumDepth = ComputeMaximumInsetDepth(positions, normal);
        if (!(maximumDepth > 0f) || depth > maximumDepth)
            throw new ArgumentOutOfRangeException(
                nameof(depth),
                $"Inset depth must not exceed {maximumDepth}."
            );

        RingResult ring = BuildExtrusionRing(
            face,
            (index, position) => OffsetVertex(positions, index, position, normal, depth),
            inheritNeighborMaterials: false
        );
        return new InsetFaceResult(ring.CapFace, ring.SideFaces, ring.NewVertices);
    }

    /// <summary>
    /// Find the largest positive inset depth that keeps the cap non-degenerate and free of
    /// self-intersections.
    /// </summary>
    public float ComputeMaximumInsetDepth(FaceHandle face)
    {
        Vector3[] positions = CollectFaceVertices(face).Select(GetVertexPosition).ToArray();
        Vector3 normal = ComputeFaceNormal(face);
        return normal == Vector3.Zero ? 0f : ComputeMaximumInsetDepth(positions, normal);
    }

    private static float ComputeMaximumInsetDepth(Vector3[] positions, Vector3 normal)
    {
        Vector3[] directions = new Vector3[positions.Length];
        for (int i = 0; i < positions.Length; i++)
            directions[i] = OffsetVertex(positions, i, positions[i], normal, 1f) - positions[i];

        float upperBound = positions
            .Select(
                (position, index) =>
                    Vector3.Distance(position, positions[(index + 1) % positions.Length])
            )
            .Max();
        if (!(upperBound > 0f))
            return 0f;

        int expansionCount = 0;
        while (
            expansionCount++ < MaximumInsetSearchIterations
            && IsValidInset(positions, directions, normal, upperBound)
        )
            upperBound *= 2f;
        if (!float.IsFinite(upperBound) || IsValidInset(positions, directions, normal, upperBound))
            return 0f;

        float lowerBound = 0f;
        for (int i = 0; i < MaximumInsetSearchIterations; i++)
        {
            float candidate = (lowerBound + upperBound) * 0.5f;
            if (IsValidInset(positions, directions, normal, candidate))
                lowerBound = candidate;
            else
                upperBound = candidate;
        }

        return lowerBound;
    }

    private static bool IsValidInset(
        Vector3[] positions,
        Vector3[] directions,
        Vector3 normal,
        float depth
    )
    {
        Vector3[] inset = new Vector3[positions.Length];
        for (int i = 0; i < positions.Length; i++)
            inset[i] = positions[i] + directions[i] * depth;

        for (int i = 0; i < positions.Length; i++)
        {
            int next = (i + 1) % positions.Length;
            Vector3 originalEdge = positions[next] - positions[i];
            Vector3 insetEdge = inset[next] - inset[i];
            float originalLengthSquared = originalEdge.LengthSquared();
            if (
                insetEdge.LengthSquared()
                    <= originalLengthSquared * InsetValidityEpsilon * InsetValidityEpsilon
                || Vector3.Dot(originalEdge, insetEdge)
                    <= originalLengthSquared * InsetValidityEpsilon
            )
            {
                return false;
            }
        }

        Vector3 axisU = Vector3.Normalize(positions[1] - positions[0]);
        Vector3 axisV = Vector3.Normalize(Vector3.Cross(normal, axisU));
        Vector2[] projected = inset
            .Select(position => new Vector2(
                Vector3.Dot(position, axisU),
                Vector3.Dot(position, axisV)
            ))
            .ToArray();
        float maximumEdgeLengthSquared = projected
            .Select(
                (position, index) =>
                    Vector2.DistanceSquared(position, projected[(index + 1) % projected.Length])
            )
            .Max();
        return !HasSelfIntersection(
            projected,
            maximumEdgeLengthSquared * InsetValidityEpsilon,
            MathF.Sqrt(maximumEdgeLengthSquared) * InsetValidityEpsilon
        );
    }

    private static bool HasSelfIntersection(
        Vector2[] polygon,
        float crossEpsilon,
        float coordinateEpsilon
    )
    {
        for (int first = 0; first < polygon.Length; first++)
        {
            int firstNext = (first + 1) % polygon.Length;
            for (int second = first + 1; second < polygon.Length; second++)
            {
                int secondNext = (second + 1) % polygon.Length;
                if (first == secondNext || firstNext == second)
                    continue;
                if (
                    SegmentsIntersect(
                        polygon[first],
                        polygon[firstNext],
                        polygon[second],
                        polygon[secondNext],
                        crossEpsilon,
                        coordinateEpsilon
                    )
                )
                    return true;
            }
        }

        return false;
    }

    private static bool SegmentsIntersect(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d,
        float crossEpsilon,
        float coordinateEpsilon
    )
    {
        float abC = Cross(b - a, c - a);
        float abD = Cross(b - a, d - a);
        float cdA = Cross(d - c, a - c);
        float cdB = Cross(d - c, b - c);
        if (abC * abD < 0f && cdA * cdB < 0f)
            return true;

        return MathF.Abs(abC) <= crossEpsilon && IsOnSegment(a, b, c, coordinateEpsilon)
            || MathF.Abs(abD) <= crossEpsilon && IsOnSegment(a, b, d, coordinateEpsilon)
            || MathF.Abs(cdA) <= crossEpsilon && IsOnSegment(c, d, a, coordinateEpsilon)
            || MathF.Abs(cdB) <= crossEpsilon && IsOnSegment(c, d, b, coordinateEpsilon);
    }

    private static bool IsOnSegment(Vector2 a, Vector2 b, Vector2 point, float epsilon) =>
        point.X >= MathF.Min(a.X, b.X) - epsilon
        && point.X <= MathF.Max(a.X, b.X) + epsilon
        && point.Y >= MathF.Min(a.Y, b.Y) - epsilon
        && point.Y <= MathF.Max(a.Y, b.Y) + epsilon;

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static Vector3 OffsetVertex(
        Vector3[] positions,
        int index,
        Vector3 position,
        Vector3 normal,
        float depth
    )
    {
        Vector3 previous = positions[(index - 1 + positions.Length) % positions.Length];
        Vector3 next = positions[(index + 1) % positions.Length];
        Vector3 incomingEdge = position - previous;
        Vector3 outgoingEdge = next - position;
        if (!(incomingEdge.LengthSquared() > 0f) || !(outgoingEdge.LengthSquared() > 0f))
            throw new ArgumentException("Inset source face contains an invalid edge.");

        Vector3 incoming = Vector3.Normalize(incomingEdge);
        Vector3 outgoing = Vector3.Normalize(outgoingEdge);
        Vector3 inwardIncoming = Vector3.Normalize(Vector3.Cross(normal, incoming));
        Vector3 inwardOutgoing = Vector3.Normalize(Vector3.Cross(normal, outgoing));
        Vector3 bisector = inwardIncoming + inwardOutgoing;
        float bisectorLengthSquared = bisector.LengthSquared();
        if (!(bisectorLengthSquared > 0f))
            throw new ArgumentException("Inset source face contains an invalid corner.");

        bisector /= MathF.Sqrt(bisectorLengthSquared);
        float denominator = Vector3.Dot(bisector, inwardOutgoing);
        if (MathF.Abs(denominator) < 1e-6f)
            throw new ArgumentException("Inset source face contains an invalid corner.");

        return position + bisector * (depth / denominator);
    }

    public readonly record struct InsetFaceResult(
        FaceHandle CapFace,
        FaceHandle[] RingFaces,
        VertexHandle[] NewVertices
    );
}
