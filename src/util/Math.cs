using System.Numerics;
using System.Runtime.CompilerServices;

namespace TRMesh.Util;

static class Math
{
    public static Vector3 ComputeFaceNormal(ReadOnlySpan<Vector3> vertices)
    {
        Vector3 normal = ComputeNewellNormal(vertices);
        float lengthSquared = normal.LengthSquared();
        return lengthSquared > 0f ? normal / MathF.Sqrt(lengthSquared) : normal;
    }

    // Direction-only Newell normal, cheaper than a full normal calculation
    public static Vector3 ComputeNewellNormal(ReadOnlySpan<Vector3> vertices)
    {
        Vector3 normal = default;
        Vector3 prev = vertices[^1];
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 curr = vertices[i];
            normal.X += (prev.Y - curr.Y) * (prev.Z + curr.Z);
            normal.Y += (prev.Z - curr.Z) * (prev.X + curr.X);
            normal.Z += (prev.X - curr.X) * (prev.Y + curr.Y);
            prev = curr;
        }

        return normal;
    }

    // Inclusive of edges: a vertex on an edge counts as inside so ear clipping will not
    // emit a triangle that another live vertex sits on.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool PointInTriangle3D(
        Vector3 point,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 normal
    )
    {
        if (Vector3.Dot(Vector3.Cross(b - a, point - a), normal) < 0f)
            return false;
        if (Vector3.Dot(Vector3.Cross(c - b, point - b), normal) < 0f)
            return false;
        if (Vector3.Dot(Vector3.Cross(a - c, point - c), normal) < 0f)
            return false;
        return true;
    }
}
