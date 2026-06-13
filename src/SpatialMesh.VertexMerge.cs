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
        if (
            source == target
            || !Vertices.IsAlive(source)
            || !Vertices.IsAlive(target)
            || !TryFindUniqueDirectedEdge(target, source, out HalfEdgeHandle connectingEdge)
        )
        {
            return false;
        }

        Vector3 targetPosition = GetVertexPosition(target);
        if (!TryCollapseEdge(connectingEdge, out _, 0f))
            return false;

        // Keep this explicit rather than relying solely on collapse interpolation semantics.
        SetVertexPosition(target, targetPosition);
        return true;
    }

    private bool TryFindUniqueDirectedEdge(
        VertexHandle origin,
        VertexHandle destination,
        out HalfEdgeHandle edge
    )
    {
        edge = HalfEdgeHandle.Null;
        int connectingHalfEdges = 0;
        foreach (HalfEdgeHandle candidate in HalfEdges)
        {
            HalfEdge candidateData = HalfEdges[candidate];
            if (!HalfEdges.IsAlive(candidateData.Twin))
                continue;

            VertexHandle candidateDestination = HalfEdges[candidateData.Twin].Origin;
            bool connectsVertices =
                (candidateData.Origin == origin && candidateDestination == destination)
                || (candidateData.Origin == destination && candidateDestination == origin);
            if (!connectsVertices)
                continue;

            connectingHalfEdges++;
            if (candidateData.Origin == origin)
                edge = candidate;
        }

        return connectingHalfEdges == 2 && !edge.IsNull;
    }
}
