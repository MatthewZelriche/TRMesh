using System.Numerics;

namespace TREditorSharp.Builders;

/// <summary>
/// Parameters for an XZ-plane grid primitive built by
/// <see cref="MeshBuilders.Build(in PlaneOptions)"/>.
/// </summary>
/// <remarks>
/// The plane lies in the XZ plane (normal = +Y) centered at <see cref="Center"/>. With the
/// default segment counts this yields a single quad. Produces
/// <c>(WidthSegments + 1) * (HeightSegments + 1)</c> grid vertices and
/// <c>WidthSegments * HeightSegments</c> quad faces.
/// </remarks>
public readonly struct PlaneOptions
{
    /// <summary>Center of the plane.</summary>
    public Vector3 Center { get; init; }

    /// <summary>Extent along +X. Must be strictly positive.</summary>
    public float Width { get; init; }

    /// <summary>Extent along +Z. Must be strictly positive.</summary>
    public float Height { get; init; }

    /// <summary>Number of subdivisions along X. Default 1; must be at least 1.</summary>
    public int WidthSegments { get; init; } = 1;

    /// <summary>Number of subdivisions along Z. Default 1; must be at least 1.</summary>
    public int HeightSegments { get; init; } = 1;

    public PlaneOptions() { }
}
