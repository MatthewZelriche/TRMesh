using System.Numerics;

namespace TREditorSharp;

/// <summary>
/// One projected UV paired with the original polygon face corner that owns it.
/// </summary>
public readonly record struct ProjectedFaceCornerUv(FaceCornerHandle Corner, Vector2 Uv);

/// <summary>
/// Generates deterministic default UVs for complete polygon faces without mutating the mesh.
/// </summary>
public static class FaceUvProjector
{
    private const float DegenerateNormalLengthSquared = 1e-12f;

    /// <summary>
    /// Reprojects every UV-initialized polygon face adjacent to at least one supplied vertex.
    /// Returns the number of faces successfully reprojected.
    /// </summary>
    /// <remarks>
    /// This provides basic object-space texture lock for geometry editing. A face is projected at
    /// most once even when several of its vertices move. Faces that become degenerate retain their
    /// previous UVs because no valid replacement projection can be generated.
    /// </remarks>
    public static int ReprojectInitializedFacesAroundVertices(
        SpatialMesh mesh,
        IEnumerable<VertexHandle> vertices
    )
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(vertices);

        HashSet<FaceHandle> affectedFaces = [];
        foreach (VertexHandle vertex in vertices)
        {
            foreach (HalfEdgeHandle outgoing in mesh.HalfEdgesAroundVertex(vertex))
            {
                HalfEdge halfEdge = mesh.GetHalfEdge(outgoing);
                AddInitializedFace(mesh, halfEdge.Face, affectedFaces);
                AddInitializedFace(mesh, mesh.GetHalfEdge(halfEdge.Twin).Face, affectedFaces);
            }
        }

        List<ProjectedFaceCornerUv> projected = [];
        int reprojectedCount = 0;
        foreach (FaceHandle face in affectedFaces)
        {
            projected.Clear();
            if (!TryProject(mesh, face, projected))
                continue;

            foreach (ProjectedFaceCornerUv corner in projected)
                mesh.SetFaceCornerUv(corner.Corner, corner.Uv);
            reprojectedCount++;
        }

        return reprojectedCount;
    }

    /// <summary>
    /// Projects the original corners of <paramref name="face"/> into object-local UV space and
    /// appends them to <paramref name="output"/> in face-loop order.
    /// </summary>
    /// <remarks>
    /// The exact polygon normal defines the projection plane, so sloped faces retain one texture
    /// repeat per editor unit without major-axis projection distortion. The nearest major axis
    /// selects a stable orientation anchor, preventing arbitrary UV rotation and mirroring.
    ///
    /// Returns false for a dead face, a face with fewer than three corners, or a degenerate face
    /// with no usable normal. On failure, <paramref name="output"/> is left unchanged.
    /// </remarks>
    public static bool TryProject(
        SpatialMesh mesh,
        FaceHandle face,
        List<ProjectedFaceCornerUv> output
    )
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(output);

        if (!mesh.Faces.IsAlive(face))
            return false;

        List<FaceCornerHandle> corners = [];
        List<Vector3> positions = [];
        foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
        {
            corners.Add(corner);
            positions.Add(mesh.GetVertexPosition(mesh.GetHalfEdge(corner).Origin));
        }

        if (corners.Count < 3)
            return false;

        // Newell's method uses the complete polygon loop rather than an arbitrary first triangle.
        // This gives a stable average normal for n-gons and remains useful when a polygon contains
        // collinear neighboring corners. The vector is normalized below because UV scale must be
        // independent of polygon area.
        Vector3 normal = default;
        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 current = positions[i];
            Vector3 next = positions[(i + 1) % positions.Count];
            normal.X += (current.Y - next.Y) * (current.Z + next.Z);
            normal.Y += (current.Z - next.Z) * (current.X + next.X);
            normal.Z += (current.X - next.X) * (current.Y + next.Y);
        }

        float normalLengthSquared = normal.LengthSquared();
        if (!(normalLengthSquared > DegenerateNormalLengthSquared))
            return false;
        normal /= MathF.Sqrt(normalLengthSquared);

        // The nearest major direction does not become the projection plane. It only chooses the
        // fixed U-axis anchor that establishes a predictable texture orientation. Projecting that
        // anchor into the polygon's actual plane removes distortion on sloped faces while retaining
        // the familiar orientation of the closest axis-aligned wall, floor, or ceiling.
        Vector3 orientationAnchor = GetOrientationAnchor(normal);
        Vector3 uAxis = orientationAnchor - normal * Vector3.Dot(orientationAnchor, normal);
        float uLengthSquared = uAxis.LengthSquared();
        if (!(uLengthSquared > DegenerateNormalLengthSquared))
            return false;
        uAxis /= MathF.Sqrt(uLengthSquared);

        // U and the exact face normal are unit-length and perpendicular. Their cross product is
        // therefore already a unit V axis. Using normal x U fixes the handedness for every face,
        // which is what prevents opposite-facing surfaces from receiving accidental mirror images.
        Vector3 vAxis = Vector3.Cross(normal, uAxis);

        // Deliberately use absolute object-local positions rather than subtracting a per-face
        // origin. This makes separate coplanar faces with the same orientation naturally line up.
        // Unit-length U and V axes also make one object-space unit equal one texture repeat.
        var projected = new ProjectedFaceCornerUv[corners.Count];
        for (int i = 0; i < corners.Count; i++)
        {
            Vector3 position = positions[i];
            projected[i] = new ProjectedFaceCornerUv(
                corners[i],
                new Vector2(Vector3.Dot(position, uAxis), Vector3.Dot(position, vAxis))
            );
        }

        output.AddRange(projected);
        return true;
    }

    private static Vector3 GetOrientationAnchor(Vector3 normal)
    {
        Vector3 absolute = Vector3.Abs(normal);

        // These six anchors form a fixed right-handed basis table once V is calculated as
        // normal x U:
        //   +X: U=-Z, -X: U=+Z, +Y/-Y/+Z: U=+X, -Z: U=-X.
        // Comparisons intentionally prefer X, then Y, then Z on exact ties so a face cannot flicker
        // between orientations due to iteration order or another non-deterministic choice.
        if (absolute.X >= absolute.Y && absolute.X >= absolute.Z)
            return normal.X >= 0f ? -Vector3.UnitZ : Vector3.UnitZ;
        if (absolute.Y >= absolute.Z)
            return Vector3.UnitX;
        return normal.Z >= 0f ? Vector3.UnitX : -Vector3.UnitX;
    }

    private static void AddInitializedFace(
        SpatialMesh mesh,
        FaceHandle face,
        HashSet<FaceHandle> affectedFaces
    )
    {
        if (!face.IsNull && mesh.AreFaceUvsInitialized(face))
            affectedFaces.Add(face);
    }
}
