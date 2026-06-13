namespace TREditorSharp;

public partial class HalfEdgeMesh
{
    /// <summary>
    /// Rotate an interior edge shared by two triangles to connect their opposite vertices.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the edge was flipped; <c>false</c> when the edge is dead, on a boundary,
    /// not shared by two triangles, or the replacement edge already exists.
    /// </returns>
    public bool FlipEdge(HalfEdgeHandle edge)
    {
        if (!TryGetEdgeFlipNeighborhood(edge, out EdgeFlipNeighborhood neighborhood))
            return false;

        TriangleLoop first = neighborhood.First;
        TriangleLoop second = neighborhood.Second;
        HalfEdge halfEdge = first.EdgeData;
        HalfEdge twin = second.EdgeData;
        HalfEdge nextHalfEdge = first.NextData;
        HalfEdge prevHalfEdge = first.PrevData;
        HalfEdge twinNextHalfEdge = second.NextData;
        HalfEdge twinPrevHalfEdge = second.PrevData;

        // The original loops are:
        //   edge -> next -> prev
        //   twin -> twinNext -> twinPrev
        // Rotate the diagonal and form:
        //   edge -> prev -> twinNext
        //   twin -> twinPrev -> next
        halfEdge.Origin = neighborhood.TwinOpposite;
        halfEdge.Next = first.Prev;
        halfEdge.Prev = second.Next;

        twin.Origin = neighborhood.Opposite;
        twin.Next = second.Prev;
        twin.Prev = first.Next;

        nextHalfEdge.Next = second.Edge;
        nextHalfEdge.Prev = second.Prev;
        nextHalfEdge.Face = twin.Face;

        prevHalfEdge.Next = second.Next;
        prevHalfEdge.Prev = first.Edge;

        twinNextHalfEdge.Next = first.Edge;
        twinNextHalfEdge.Prev = first.Prev;
        twinNextHalfEdge.Face = halfEdge.Face;

        twinPrevHalfEdge.Next = first.Next;
        twinPrevHalfEdge.Prev = second.Edge;

        HalfEdges[first.Edge] = halfEdge;
        HalfEdges[second.Edge] = twin;
        HalfEdges[first.Next] = nextHalfEdge;
        HalfEdges[first.Prev] = prevHalfEdge;
        HalfEdges[second.Next] = twinNextHalfEdge;
        HalfEdges[second.Prev] = twinPrevHalfEdge;
        Faces[halfEdge.Face] = new Face { FirstHalfEdge = first.Edge };
        Faces[twin.Face] = new Face { FirstHalfEdge = second.Edge };

        RefreshOutgoingHalfEdge(nextHalfEdge.Origin);
        RefreshOutgoingHalfEdge(twinNextHalfEdge.Origin);
        RefreshOutgoingHalfEdge(neighborhood.Opposite);
        RefreshOutgoingHalfEdge(neighborhood.TwinOpposite);
        return true;
    }

    private bool TryGetEdgeFlipNeighborhood(
        HalfEdgeHandle edge,
        out EdgeFlipNeighborhood neighborhood
    )
    {
        neighborhood = default;
        if (!TryGetTriangleLoop(edge, out TriangleLoop first))
            return false;

        HalfEdgeHandle twinHandle = first.EdgeData.Twin;
        if (
            twinHandle == edge
            || !TryGetTriangleLoop(twinHandle, out TriangleLoop second)
            || second.EdgeData.Twin != edge
            || first.EdgeData.Face == second.EdgeData.Face
        )
        {
            return false;
        }

        if (
            !Vertices.IsAlive(first.EdgeData.Origin)
            || !Vertices.IsAlive(second.EdgeData.Origin)
            || first.EdgeData.Origin == second.EdgeData.Origin
            || !HasSixDistinctHalfEdges(first, second)
        )
        {
            return false;
        }

        VertexHandle opposite = first.PrevData.Origin;
        VertexHandle twinOpposite = second.PrevData.Origin;
        if (
            !Vertices.IsAlive(opposite)
            || !Vertices.IsAlive(twinOpposite)
            || opposite == twinOpposite
            || EdgeExistsBetween(opposite, twinOpposite)
        )
        {
            return false;
        }

        neighborhood = new EdgeFlipNeighborhood(first, second, opposite, twinOpposite);
        return true;
    }

    private bool TryGetTriangleLoop(HalfEdgeHandle edge, out TriangleLoop triangle)
    {
        triangle = default;
        if (!HalfEdges.IsAlive(edge))
            return false;

        HalfEdge edgeData = HalfEdges[edge];
        HalfEdgeHandle next = edgeData.Next;
        HalfEdgeHandle prev = edgeData.Prev;
        if (
            !Faces.IsAlive(edgeData.Face)
            || !HalfEdges.IsAlive(next)
            || !HalfEdges.IsAlive(prev)
            || next == edge
            || prev == edge
            || next == prev
        )
        {
            return false;
        }

        HalfEdge nextHalfEdge = HalfEdges[next];
        HalfEdge prevHalfEdge = HalfEdges[prev];
        if (
            nextHalfEdge.Face != edgeData.Face
            || prevHalfEdge.Face != edgeData.Face
            || nextHalfEdge.Next != prev
            || nextHalfEdge.Prev != edge
            || prevHalfEdge.Next != edge
            || prevHalfEdge.Prev != next
        )
        {
            return false;
        }

        triangle = new TriangleLoop(edge, edgeData, next, nextHalfEdge, prev, prevHalfEdge);
        return true;
    }

    private static bool HasSixDistinctHalfEdges(TriangleLoop first, TriangleLoop second) =>
        new HashSet<HalfEdgeHandle>
        {
            first.Edge,
            first.Next,
            first.Prev,
            second.Edge,
            second.Next,
            second.Prev,
        }.Count == 6;

    private readonly record struct TriangleLoop(
        HalfEdgeHandle Edge,
        HalfEdge EdgeData,
        HalfEdgeHandle Next,
        HalfEdge NextData,
        HalfEdgeHandle Prev,
        HalfEdge PrevData
    );

    private readonly record struct EdgeFlipNeighborhood(
        TriangleLoop First,
        TriangleLoop Second,
        VertexHandle Opposite,
        VertexHandle TwinOpposite
    );

    private bool EdgeExistsBetween(VertexHandle first, VertexHandle second)
    {
        foreach (HalfEdgeHandle edge in HalfEdges)
        {
            HalfEdge halfEdge = HalfEdges[edge];
            if (halfEdge.Origin != first && halfEdge.Origin != second)
                continue;
            if (!HalfEdges.IsAlive(halfEdge.Twin))
                continue;

            HalfEdge twin = HalfEdges[halfEdge.Twin];
            if (
                (halfEdge.Origin == first && twin.Origin == second)
                || (halfEdge.Origin == second && twin.Origin == first)
            )
            {
                return true;
            }
        }

        return false;
    }
}
