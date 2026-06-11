namespace TREditorSharp;

public partial class HalfEdgeMesh
{
    /// <summary>
    /// Removes an edge whose two half-edges are both on boundary loops. Callers must remove any
    /// adjacent faces before removing their shared edge. Endpoint vertices are preserved.
    /// </summary>
    /// <returns><c>true</c> when a live boundary edge was removed; otherwise <c>false</c>.</returns>
    public bool RemoveEdge(HalfEdgeHandle edge)
    {
        if (!HalfEdges.IsAlive(edge))
            return false;

        HalfEdge halfEdge = HalfEdges[edge];
        HalfEdgeHandle twinHandle = halfEdge.Twin;
        if (!HalfEdges.IsAlive(twinHandle))
            return false;

        HalfEdge twin = HalfEdges[twinHandle];
        if (Faces.IsAlive(halfEdge.Face) || Faces.IsAlive(twin.Face))
            return false;

        // A completely isolated edge is represented by a two-half-edge boundary loop. There
        // are no neighboring loops to splice together in that case.
        bool isolated = halfEdge.Next == twinHandle && twin.Next == edge;
        if (!isolated)
        {
            ref HalfEdge halfEdgePrev = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(
                halfEdge.Prev
            );
            ref HalfEdge halfEdgeNext = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(
                halfEdge.Next
            );
            ref HalfEdge twinPrev = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(twin.Prev);
            ref HalfEdge twinNext = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(twin.Next);

            // Removing the pair opens both boundary loops at the edge. Cross-connect the four
            // neighboring half-edges to preserve closed boundary loops at each endpoint.
            halfEdgePrev.Next = twin.Next;
            twinNext.Prev = halfEdge.Prev;
            twinPrev.Next = halfEdge.Next;
            halfEdgeNext.Prev = twin.Prev;
        }

        HalfEdges.Free(edge);
        HalfEdges.Free(twinHandle);
        RefreshOutgoingHalfEdge(halfEdge.Origin);
        RefreshOutgoingHalfEdge(twin.Origin);
        return true;
    }

    private void RefreshOutgoingHalfEdge(VertexHandle vertex)
    {
        ref Vertex connectivity = ref Vertices.GetUnsafeRef<Vertex, Vertex>(vertex);
        connectivity.OutgoingHalfEdge = HalfEdgeHandle.Null;
        foreach (HalfEdgeHandle halfEdge in HalfEdges)
        {
            if (HalfEdges[halfEdge].Origin != vertex)
                continue;

            connectivity.OutgoingHalfEdge = halfEdge;
            return;
        }
    }
}
