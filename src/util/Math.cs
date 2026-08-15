using System.Numerics;

namespace TRMesh.Util;

static class Math
{
    public static Vector3 ComputeFaceNormal(ReadOnlySpan<Vector3> vertices)
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

        float lengthSquared = normal.LengthSquared();
        return lengthSquared > 0f ? normal / MathF.Sqrt(lengthSquared) : normal;
    }
}
