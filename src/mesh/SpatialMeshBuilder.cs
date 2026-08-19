using System.Numerics;

namespace TRMesh.Mesh;

public static class SpatialMeshBuilder
{
    static readonly Vector2[] QuadUvs =
    [
        new(0f, 0f),
        new(1f, 0f),
        new(1f, 1f),
        new(0f, 1f),
    ];

    public static SpatialMesh CreateAabb(Vector3 position, Vector3 size)
    {
        if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                "Every size component must be greater than zero."
            );
        }

        Vector3 maximum = position + size;
        if (maximum.X <= position.X
            || maximum.Y <= position.Y
            || maximum.Z <= position.Z
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                "Position plus size must produce finite, non-degenerate bounds."
            );
        }

        var mesh = new SpatialMesh();
        try
        {
            float x0 = position.X;
            float y0 = position.Y;
            float z0 = position.Z;
            float x1 = maximum.X;
            float y1 = maximum.Y;
            float z1 = maximum.Z;

            int v000 = mesh.AddVertex(new Vector3(x0, y0, z0));
            int v100 = mesh.AddVertex(new Vector3(x1, y0, z0));
            int v101 = mesh.AddVertex(new Vector3(x1, y0, z1));
            int v001 = mesh.AddVertex(new Vector3(x0, y0, z1));
            int v010 = mesh.AddVertex(new Vector3(x0, y1, z0));
            int v110 = mesh.AddVertex(new Vector3(x1, y1, z0));
            int v111 = mesh.AddVertex(new Vector3(x1, y1, z1));
            int v011 = mesh.AddVertex(new Vector3(x0, y1, z1));

            mesh.AddFace([v000, v100, v101, v001], QuadUvs); // -Y
            mesh.AddFace([v000, v010, v110, v100], QuadUvs); // -Z
            mesh.AddFace([v100, v110, v111, v101], QuadUvs); // +X
            mesh.AddFace([v101, v111, v011, v001], QuadUvs); // +Z
            mesh.AddFace([v001, v011, v010, v000], QuadUvs); // -X
            mesh.AddFace([v010, v011, v111, v110], QuadUvs); // +Y

            return mesh;
        }
        catch
        {
            mesh.Dispose();
            throw;
        }
    }

    public static SpatialMesh CreateCylinder(float height, float diameter, int sides)
    {
        if (!float.IsFinite(height) || height <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                "Height must be greater than zero."
            );
        }
        if (diameter <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(diameter),
                "Diameter must be greater than zero."
            );
        }
        if (sides < 3)
            throw new ArgumentOutOfRangeException(nameof(sides), "A cylinder needs at least 3 sides.");

        float radius = diameter * 0.5f;
        float halfHeight = height * 0.5f;
        if (radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(diameter), "Diameter is too small to represent.");
        if (halfHeight <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                "Height is too small to represent."
            );
        }

        var mesh = new SpatialMesh();
        try
        {
            var bottom = new int[sides];
            var top = new int[sides];
            var ringPositions = new Vector3[sides];

            for (int i = 0; i < sides; i++)
            {
                // Walk clockwise when viewed from +Y so wall UVs retain outward orientation.
                float angle = -MathF.Tau * i / sides;
                float x = radius * MathF.Cos(angle);
                float z = radius * MathF.Sin(angle);
                ringPositions[i] = new Vector3(x, 0f, z);
                bottom[i] = mesh.AddVertex(new Vector3(x, -halfHeight, z));
                top[i] = mesh.AddVertex(new Vector3(x, halfHeight, z));
            }

            var bottomFace = new int[sides];
            var bottomUvs = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                int ringIndex = sides - 1 - i;
                bottomFace[i] = bottom[ringIndex];
                Vector3 point = ringPositions[ringIndex];
                bottomUvs[i] = new Vector2(
                    0.5f + point.X / diameter,
                    0.5f + point.Z / diameter
                );
            }
            mesh.AddFace(bottomFace, bottomUvs);

            for (int i = 0; i < sides; i++)
            {
                int next = i + 1 == sides ? 0 : i + 1;
                float u0 = (float)i / sides;
                float u1 = (float)(i + 1) / sides;
                mesh.AddFace(
                    [bottom[i], bottom[next], top[next], top[i]],
                    [new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 1f), new Vector2(u0, 1f)]
                );
            }

            var topUvs = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                Vector3 point = ringPositions[i];
                topUvs[i] = new Vector2(0.5f + point.X / diameter, 0.5f - point.Z / diameter);
            }
            mesh.AddFace(top, topUvs);

            return mesh;
        }
        catch
        {
            mesh.Dispose();
            throw;
        }
    }
}
