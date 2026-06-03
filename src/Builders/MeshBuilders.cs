using System.Numerics;

namespace TREditorSharp.Builders;

/// <summary>
/// Static factory methods that allocate a fresh <see cref="SpatialMesh"/> populated with a
/// primitive shape described by a per-shape options struct. Builders never triangulate; the
/// produced mesh is polygonal (quads stay quads, n-gons stay n-gons).
/// </summary>
/// <remarks>
/// All primitives are axis-aligned in a Y-up world. Faces are CCW when viewed from outside
/// the surface, which matches <see cref="HalfEdgeMesh.AddFace"/>.
/// </remarks>
public static class MeshBuilders
{
    /// <summary>
    /// Build an axis-aligned box from the AABB described by <paramref name="options"/>.
    /// </summary>
    public static SpatialMesh Build(in BlockOptions options)
    {
        var min = options.Min;
        var max = options.Max;
        if (!(max.X > min.X) || !(max.Y > min.Y) || !(max.Z > min.Z))
            throw new ArgumentException(
                "BlockOptions.Max must be component-wise strictly greater than Min.",
                nameof(options)
            );

        var mesh = new SpatialMesh();

        // Corner indices: bit 0 = X (min/max), bit 1 = Y, bit 2 = Z.
        Span<VertexHandle> corners = stackalloc VertexHandle[8];
        for (int i = 0; i < 8; i++)
        {
            float x = (i & 1) == 0 ? min.X : max.X;
            float y = (i & 2) == 0 ? min.Y : max.Y;
            float z = (i & 4) == 0 ? min.Z : max.Z;
            corners[i] = mesh.AddVertex(new Vector3(x, y, z));
        }

        // Six face quads. Each quad is wound CCW so its (b - a) x (c - a) cross
        // points along the outward axis.
        Span<VertexHandle> quad = stackalloc VertexHandle[4];

        AddQuad(mesh, quad, corners[1], corners[3], corners[7], corners[5]); // +X
        AddQuad(mesh, quad, corners[0], corners[4], corners[6], corners[2]); // -X
        AddQuad(mesh, quad, corners[6], corners[7], corners[3], corners[2]); // +Y
        AddQuad(mesh, quad, corners[1], corners[5], corners[4], corners[0]); // -Y
        AddQuad(mesh, quad, corners[5], corners[7], corners[6], corners[4]); // +Z
        AddQuad(mesh, quad, corners[2], corners[3], corners[1], corners[0]); // -Z

        return mesh;
    }

    /// <summary>
    /// Build a closed cylinder centered at <see cref="CylinderOptions.Center"/> with axis +Y.
    /// </summary>
    public static SpatialMesh Build(in CylinderOptions options)
    {
        if (!(options.RadiusX > 0f))
            throw new ArgumentException("CylinderOptions.RadiusX must be > 0.", nameof(options));
        if (!(options.RadiusZ > 0f))
            throw new ArgumentException("CylinderOptions.RadiusZ must be > 0.", nameof(options));
        if (!(options.Height > 0f))
            throw new ArgumentException("CylinderOptions.Height must be > 0.", nameof(options));
        if (options.RadialSegments < 3)
            throw new ArgumentException(
                "CylinderOptions.RadialSegments must be at least 3.",
                nameof(options)
            );

        int n = options.RadialSegments;
        float radiusX = options.RadiusX;
        float radiusZ = options.RadiusZ;
        float halfH = options.Height * 0.5f;
        var c = options.Center;

        var mesh = new SpatialMesh();

        // Allocate 2N ring vertices: bottom ring [0, n), then top ring [n, 2n).
        var bottom = new VertexHandle[n];
        var top = new VertexHandle[n];
        for (int i = 0; i < n; i++)
        {
            double angle = 2.0 * Math.PI * i / n;
            float cx = (float)Math.Cos(angle) * radiusX;
            float cz = (float)Math.Sin(angle) * radiusZ;
            bottom[i] = mesh.AddVertex(new Vector3(c.X + cx, c.Y - halfH, c.Z + cz));
        }
        for (int i = 0; i < n; i++)
        {
            double angle = 2.0 * Math.PI * i / n;
            float cx = (float)Math.Cos(angle) * radiusX;
            float cz = (float)Math.Sin(angle) * radiusZ;
            top[i] = mesh.AddVertex(new Vector3(c.X + cx, c.Y + halfH, c.Z + cz));
        }

        // Side quads (b_i, t_i, t_{i+1}, b_{i+1}) -> outward normal radial.
        Span<VertexHandle> quad = stackalloc VertexHandle[4];
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            AddQuad(mesh, quad, bottom[i], top[i], top[next], bottom[next]);
        }

        // Top cap n-gon: ring traversal at increasing angle is CW from +Y, so reverse it.
        var capBuf = new VertexHandle[n];
        capBuf[0] = top[0];
        for (int i = 1; i < n; i++)
            capBuf[i] = top[n - i];
        mesh.AddFace(capBuf);

        // Bottom cap n-gon: forward ring order is CCW around -Y for the outward normal.
        for (int i = 0; i < n; i++)
            capBuf[i] = bottom[i];
        mesh.AddFace(capBuf);

        return mesh;
    }

    /// <summary>
    /// Build a UV (latitude/longitude) sphere centered at <see cref="UvSphereOptions.Center"/>.
    /// </summary>
    public static SpatialMesh Build(in UvSphereOptions options)
    {
        if (!(options.Radius > 0f))
            throw new ArgumentException("UvSphereOptions.Radius must be > 0.", nameof(options));
        if (options.LatSegments < 2)
            throw new ArgumentException(
                "UvSphereOptions.LatSegments must be at least 2.",
                nameof(options)
            );
        if (options.LonSegments < 3)
            throw new ArgumentException(
                "UvSphereOptions.LonSegments must be at least 3.",
                nameof(options)
            );

        int lat = options.LatSegments;
        int lon = options.LonSegments;
        float radius = options.Radius;
        var c = options.Center;

        var mesh = new SpatialMesh();

        // Vertex layout in dense order:
        //   0: north pole
        //   1 + k*lon + j: ring k vertex j (k in [0, lat-2], j in [0, lon))
        //   1 + (lat-1)*lon: south pole
        var northPole = mesh.AddVertex(new Vector3(c.X, c.Y + radius, c.Z));

        int ringCount = lat - 1;
        var rings = new VertexHandle[ringCount * lon];
        for (int k = 0; k < ringCount; k++)
        {
            double latAngle = Math.PI * (k + 1) / lat;
            float y = (float)Math.Cos(latAngle) * radius;
            float ringR = (float)Math.Sin(latAngle) * radius;
            for (int j = 0; j < lon; j++)
            {
                double lonAngle = 2.0 * Math.PI * j / lon;
                float vx = (float)Math.Cos(lonAngle) * ringR;
                float vz = (float)Math.Sin(lonAngle) * ringR;
                rings[k * lon + j] = mesh.AddVertex(new Vector3(c.X + vx, c.Y + y, c.Z + vz));
            }
        }

        var southPole = mesh.AddVertex(new Vector3(c.X, c.Y - radius, c.Z));

        // North cap triangles: (north, ring0_{j+1}, ring0_j) -> outward +Y.
        Span<VertexHandle> tri = stackalloc VertexHandle[3];
        for (int j = 0; j < lon; j++)
        {
            int jNext = (j + 1) % lon;
            tri[0] = northPole;
            tri[1] = rings[jNext];
            tri[2] = rings[j];
            mesh.AddFace(tri);
        }

        // Mid-band quads: (ring_k_j, ring_k_{j+1}, ring_{k+1}_{j+1}, ring_{k+1}_j) -> outward radial.
        Span<VertexHandle> quad = stackalloc VertexHandle[4];
        for (int k = 0; k < ringCount - 1; k++)
        {
            int rowA = k * lon;
            int rowB = (k + 1) * lon;
            for (int j = 0; j < lon; j++)
            {
                int jNext = (j + 1) % lon;
                AddQuad(
                    mesh,
                    quad,
                    rings[rowA + j],
                    rings[rowA + jNext],
                    rings[rowB + jNext],
                    rings[rowB + j]
                );
            }
        }

        // South cap triangles: (south, ring_last_j, ring_last_{j+1}) -> outward -Y.
        int lastRow = (ringCount - 1) * lon;
        for (int j = 0; j < lon; j++)
        {
            int jNext = (j + 1) % lon;
            tri[0] = southPole;
            tri[1] = rings[lastRow + j];
            tri[2] = rings[lastRow + jNext];
            mesh.AddFace(tri);
        }

        return mesh;
    }

    /// <summary>
    /// Build a flat plane in the XZ plane (normal +Y), centered at <see cref="PlaneOptions.Center"/>.
    /// </summary>
    public static SpatialMesh Build(in PlaneOptions options)
    {
        if (!(options.Width > 0f))
            throw new ArgumentException("PlaneOptions.Width must be > 0.", nameof(options));
        if (!(options.Height > 0f))
            throw new ArgumentException("PlaneOptions.Height must be > 0.", nameof(options));
        if (options.WidthSegments < 1)
            throw new ArgumentException(
                "PlaneOptions.WidthSegments must be at least 1.",
                nameof(options)
            );
        if (options.HeightSegments < 1)
            throw new ArgumentException(
                "PlaneOptions.HeightSegments must be at least 1.",
                nameof(options)
            );

        int wSeg = options.WidthSegments;
        int hSeg = options.HeightSegments;
        int wCount = wSeg + 1;
        int hCount = hSeg + 1;
        float halfW = options.Width * 0.5f;
        float halfH = options.Height * 0.5f;
        var c = options.Center;

        var mesh = new SpatialMesh();

        // Vertex grid in row-major order: outer loop on Z, inner on X.
        // Linear index = gz * wCount + gx.
        var grid = new VertexHandle[wCount * hCount];
        for (int gz = 0; gz < hCount; gz++)
        {
            float fz = -halfH + options.Height * gz / hSeg;
            for (int gx = 0; gx < wCount; gx++)
            {
                float fx = -halfW + options.Width * gx / wSeg;
                grid[gz * wCount + gx] = mesh.AddVertex(new Vector3(c.X + fx, c.Y, c.Z + fz));
            }
        }

        // Quad winding (v00, v01, v11, v10) -> outward +Y.
        Span<VertexHandle> quad = stackalloc VertexHandle[4];
        for (int gz = 0; gz < hSeg; gz++)
        {
            for (int gx = 0; gx < wSeg; gx++)
            {
                int v00 = gz * wCount + gx;
                int v10 = gz * wCount + (gx + 1);
                int v11 = (gz + 1) * wCount + (gx + 1);
                int v01 = (gz + 1) * wCount + gx;
                AddQuad(mesh, quad, grid[v00], grid[v01], grid[v11], grid[v10]);
            }
        }

        return mesh;
    }

    private static void AddQuad(
        SpatialMesh mesh,
        Span<VertexHandle> scratch,
        VertexHandle a,
        VertexHandle b,
        VertexHandle c,
        VertexHandle d
    )
    {
        scratch[0] = a;
        scratch[1] = b;
        scratch[2] = c;
        scratch[3] = d;
        mesh.AddFace(scratch);
    }
}
