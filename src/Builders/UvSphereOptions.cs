using System.Numerics;

namespace TREditorSharp.Builders;

/// <summary>
/// Parameters for a UV (latitude/longitude) sphere primitive built by
/// <see cref="MeshBuilders.Build(in UvSphereOptions)"/>.
/// </summary>
/// <remarks>
/// Produces <c>2 + (LatSegments - 1) * LonSegments</c> vertices: two pole vertices on the +Y/-Y
/// axis and <c>(LatSegments - 1)</c> interior latitude rings, each with <c>LonSegments</c>
/// vertices. Faces are <c>2 * LonSegments</c> polar triangles plus
/// <c>(LatSegments - 2) * LonSegments</c> mid-band quads. Pole faces are triangles only because
/// the topology demands it; they are not produced by triangulating quads.
/// </remarks>
public readonly struct UvSphereOptions
{
    /// <summary>Center of the sphere.</summary>
    public Vector3 Center { get; init; }

    /// <summary>Radius. Must be strictly positive.</summary>
    public float Radius { get; init; }

    /// <summary>
    /// Number of latitude bands (faces between the two poles, inclusive of pole rings).
    /// Must be at least 2.
    /// </summary>
    public int LatSegments { get; init; }

    /// <summary>Number of longitude divisions (faces around). Must be at least 3.</summary>
    public int LonSegments { get; init; }
}
