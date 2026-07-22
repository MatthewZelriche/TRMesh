namespace TREditorSharp;

using System.Numerics;

public partial class SpatialMesh
{
    /// <summary>
    /// Compute the unit-length Newell normal of <paramref name="face"/>. A geometrically
    /// degenerate face has no direction and returns <see cref="Vector3.Zero"/>.
    /// </summary>
    public Vector3 ComputeFaceNormal(FaceHandle face)
    {
        VertexHandle[] vertices = CollectFaceVertices(face);
        var positions = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
            positions[i] = GetVertexPosition(vertices[i]);

        return ComputeFaceNormal(positions);
    }

    public static Vector3 ComputeFaceNormal(ReadOnlySpan<Vector3> positions)
    {
        Vector3 normal = ComputeNewellNormal(positions);
        float lengthSquared = normal.LengthSquared();
        return lengthSquared > 0f ? normal / MathF.Sqrt(lengthSquared) : Vector3.Zero;
    }

    /// <summary>Compute the average position of the vertices around <paramref name="face"/>.</summary>
    public Vector3 ComputeFaceCentroid(FaceHandle face)
    {
        VertexHandle[] vertices = CollectFaceVertices(face);
        Vector3 sum = Vector3.Zero;
        for (int i = 0; i < vertices.Length; i++)
            sum += GetVertexPosition(vertices[i]);

        return sum / vertices.Length;
    }

    private VertexHandle[] CollectFaceVertices(FaceHandle face)
    {
        if (!Faces.IsAlive(face))
            throw new ArgumentException($"Face {face} is not live.", nameof(face));

        List<VertexHandle> vertices = [];
        foreach (FaceCornerHandle corner in HalfEdgesAroundFace(face))
            vertices.Add(GetHalfEdge(corner).Origin);
        return vertices.ToArray();
    }

    private static Vector3 ComputeNewellNormal(ReadOnlySpan<Vector3> positions)
    {
        Vector3 normal = Vector3.Zero;
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 current = positions[i];
            Vector3 next = positions[(i + 1) % positions.Length];
            normal.X += (current.Y - next.Y) * (current.Z + next.Z);
            normal.Y += (current.Z - next.Z) * (current.X + next.X);
            normal.Z += (current.X - next.X) * (current.Y + next.Y);
        }

        return normal;
    }
}
