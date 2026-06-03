using System.Numerics;

namespace TREditorSharp.Builders;

/// <summary>
/// Parameters for a closed cylinder primitive built by
/// <see cref="MeshBuilders.Build(in CylinderOptions)"/>.
/// </summary>
/// <remarks>
/// The cylinder axis is fixed at +Y. Produces <c>2 * RadialSegments</c> ring vertices,
/// <c>RadialSegments</c> side quads, and two cap n-gons (top normal +Y, bottom normal -Y).
/// </remarks>
public readonly struct CylinderOptions
{
    /// <summary>Center of the cylinder (midpoint between the two cap centers).</summary>
    public Vector3 Center { get; init; }

    /// <summary>Cap radius along +X. Must be strictly positive.</summary>
    public float RadiusX { get; init; }

    /// <summary>Cap radius along +Z. Must be strictly positive.</summary>
    public float RadiusZ { get; init; }

    /// <summary>Distance between the two cap centers along +Y. Must be strictly positive.</summary>
    public float Height { get; init; }

    /// <summary>Number of subdivisions around the cylinder axis. Must be at least 3.</summary>
    public int RadialSegments { get; init; }
}
