namespace TREditorSharp;

public partial class HalfEdgeMesh
{
    /// <summary>
    /// Removes an isolated vertex. Callers must remove every incident edge before removing the
    /// vertex.
    /// </summary>
    /// <returns><c>true</c> when a live isolated vertex was removed; otherwise <c>false</c>.</returns>
    public bool RemoveVertex(VertexHandle vertex)
    {
        if (!Vertices.IsAlive(vertex) || !Vertices[vertex].OutgoingHalfEdge.IsNull)
            return false;

        Vertices.Free(vertex);
        return true;
    }
}
