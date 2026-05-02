using System.Numerics;

namespace TREditorSharp.Builders;

/// <summary>
/// Parameters for an axis-aligned bounding box block primitive built by
/// <see cref="MeshBuilders.Build(in BlockOptions)"/>.
/// </summary>
/// <remarks>
/// Produces 8 corner vertices and 6 quad faces. Faces are wound CCW so each face's normal points
/// outward from the box interior.
/// </remarks>
public readonly struct BlockOptions
{
    /// <summary>Minimum corner of the AABB (component-wise minimum).</summary>
    public Vector3 Min { get; init; }

    /// <summary>Maximum corner of the AABB. Each component must be strictly greater than the matching component of <see cref="Min"/>.</summary>
    public Vector3 Max { get; init; }
}
