namespace TREditorSharp;

using System.Numerics;

public partial class SpatialMesh
{
    /// <summary>
    /// Merge <paramref name="source"/> into a directly connected <paramref name="target"/>.
    /// The target handle and position are preserved.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the vertices were merged; otherwise <c>false</c> with no mutation.
    /// Disconnected vertex welding is not supported.
    /// </returns>
    public bool TryMergeVertices(VertexHandle source, VertexHandle target)
    {
        if (source == target || !Vertices.IsAlive(source) || !Vertices.IsAlive(target))
            return false;

        HalfEdgeHandle connectingEdge = FindHalfEdge(target, source);
        if (connectingEdge.IsNull)
            return false;

        Vector3 targetPosition = GetVertexPosition(target);
        if (!TryCollapseEdge(connectingEdge, out _, 0f))
            return false;

        // Keep this explicit rather than relying solely on collapse interpolation semantics.
        SetVertexPosition(target, targetPosition);
        return true;
    }
}
