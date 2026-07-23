namespace TREditorSharp;

using System.Numerics;

public partial class SpatialMesh
{
    /// <summary>
    /// Collapse <paramref name="edge"/> by merging its destination into its origin.
    /// Faces reduced below three corners are removed; other adjacent faces lose one corner.
    /// </summary>
    /// <param name="edge">The directed edge whose origin will survive.</param>
    /// <param name="survivor">
    /// The surviving origin vertex on success; <see cref="VertexHandle.Null"/> on failure.
    /// </param>
    /// <param name="t">
    /// Interpolation parameter from the surviving origin to the removed destination.
    /// Values outside <c>[0, 1]</c> extrapolate the survivor position.
    /// </param>
    /// <returns>
    /// <c>true</c> when the edge was collapsed; otherwise <c>false</c> with no mutation.
    /// </returns>
    public bool TryCollapseEdge(HalfEdgeHandle edge, out VertexHandle survivor, float t = 0.5f)
    {
        survivor = VertexHandle.Null;
        if (!TryGetEdgeCollapseNeighborhood(edge, out EdgeCollapseNeighborhood neighborhood))
            return false;

        VertexHandle keptVertex = neighborhood.EdgeData.Origin;
        VertexHandle removedVertex = neighborhood.TwinData.Origin;
        Vector3 position = Vector3.Lerp(
            GetVertexPosition(keptVertex),
            GetVertexPosition(removedVertex),
            t
        );

        HashSet<HalfEdgeHandle> removedHalfEdges = CollectRemovedHalfEdges(neighborhood);
        HashSet<VertexHandle> affectedVertices = [];
        foreach (HalfEdgeHandle removedHalfEdge in removedHalfEdges)
            affectedVertices.Add(HalfEdges[removedHalfEdge].Origin);

        if (neighborhood.BothSidesBoundary)
            SpliceBoundaryEdgePair(
                neighborhood.Edge,
                neighborhood.EdgeData,
                neighborhood.Twin,
                neighborhood.TwinData
            );
        else
        {
            SpliceCollapsedSide(neighborhood.First);
            SpliceCollapsedSide(neighborhood.Second);
        }

        MergeTriangleSideEdges(neighborhood.First);
        MergeTriangleSideEdges(neighborhood.Second);

        foreach (HalfEdgeHandle halfEdge in HalfEdges)
        {
            if (removedHalfEdges.Contains(halfEdge))
                continue;

            HalfEdge data = HalfEdges[halfEdge];
            if (data.Origin != removedVertex)
                continue;

            data.Origin = keptVertex;
            HalfEdges[halfEdge] = data;
        }

        if (neighborhood.First.IsTriangle)
            Faces.Free(neighborhood.First.EdgeData.Face);
        if (neighborhood.Second.IsTriangle)
            Faces.Free(neighborhood.Second.EdgeData.Face);

        foreach (HalfEdgeHandle removedHalfEdge in removedHalfEdges)
            HalfEdges.Free(removedHalfEdge);

        Vertices.Free(removedVertex);
        SetVertexPosition(keptVertex, position);

        affectedVertices.Add(keptVertex);
        foreach (VertexHandle vertex in affectedVertices)
        {
            if (Vertices.IsAlive(vertex))
                RefreshCollapsedVertexOutgoingHalfEdge(vertex);
        }

        survivor = keptVertex;
        return true;
    }

    private bool TryGetEdgeCollapseNeighborhood(
        HalfEdgeHandle edge,
        out EdgeCollapseNeighborhood neighborhood
    )
    {
        neighborhood = default;
        if (!HalfEdges.IsAlive(edge))
            return false;

        HalfEdge edgeData = HalfEdges[edge];
        HalfEdgeHandle twin = edgeData.Twin;
        if (!HalfEdges.IsAlive(twin) || twin == edge)
            return false;

        HalfEdge twinData = HalfEdges[twin];
        if (
            twinData.Twin != edge
            || !Vertices.IsAlive(edgeData.Origin)
            || !Vertices.IsAlive(twinData.Origin)
            || edgeData.Origin == twinData.Origin
            || (Faces.IsAlive(edgeData.Face) && edgeData.Face == twinData.Face)
        )
        {
            return false;
        }

        if (
            !TryGetCollapseSide(edge, edgeData, out CollapseSide first)
            || !TryGetCollapseSide(twin, twinData, out CollapseSide second)
        )
        {
            return false;
        }

        // Collapsing an interior edge with both endpoints as boundaries would produce non-manifold geometry
        bool isInteriorEdge = !edgeData.Face.IsNull && !twinData.Face.IsNull;
        if (
            isInteriorEdge
            && IsBoundaryVertex(edgeData.Origin)
            && IsBoundaryVertex(twinData.Origin)
        )
        {
            return false;
        }

        neighborhood = new EdgeCollapseNeighborhood(edge, edgeData, twin, twinData, first, second);
        HashSet<HalfEdgeHandle> removedHalfEdges = CollectRemovedHalfEdges(neighborhood);
        if (!HasExpectedRemovedHalfEdges(neighborhood, removedHalfEdges))
            return false;
        if (!HasValidTriangleSurvivors(first, removedHalfEdges))
            return false;
        if (!HasValidTriangleSurvivors(second, removedHalfEdges))
            return false;

        VertexHandle keptVertex = edgeData.Origin;
        VertexHandle removedVertex = twinData.Origin;
        if (
            !SatisfiesSharedNeighborLinkCondition(keptVertex, removedVertex, first, second)
            || WouldCreateRepeatedFaceVertex(keptVertex, removedVertex, first, second)
        )
        {
            return false;
        }

        return true;
    }

    private bool TryGetCollapseSide(HalfEdgeHandle edge, HalfEdge edgeData, out CollapseSide side)
    {
        side = default;
        if (
            !HalfEdges.IsAlive(edgeData.Next)
            || !HalfEdges.IsAlive(edgeData.Prev)
            || edgeData.Next == edge
            || edgeData.Prev == edge
        )
        {
            return false;
        }

        HalfEdge nextData = HalfEdges[edgeData.Next];
        HalfEdge prevData = HalfEdges[edgeData.Prev];
        if (
            nextData.Prev != edge
            || prevData.Next != edge
            || nextData.Face != edgeData.Face
            || prevData.Face != edgeData.Face
            || (!edgeData.Face.IsNull && !Faces.IsAlive(edgeData.Face))
        )
        {
            return false;
        }

        bool isTriangle =
            Faces.IsAlive(edgeData.Face)
            && nextData.Next == edgeData.Prev
            && prevData.Prev == edgeData.Next;
        if (
            Faces.IsAlive(edgeData.Face)
            && !isTriangle
            && (nextData.Next == edge || prevData.Prev == edge)
        )
        {
            return false;
        }

        side = new CollapseSide(
            edge,
            edgeData,
            edgeData.Next,
            nextData,
            edgeData.Prev,
            prevData,
            isTriangle
        );
        return true;
    }

    private HashSet<HalfEdgeHandle> CollectRemovedHalfEdges(EdgeCollapseNeighborhood neighborhood)
    {
        HashSet<HalfEdgeHandle> removed = [neighborhood.Edge, neighborhood.Twin];
        AddTriangleHalfEdges(neighborhood.First, removed);
        AddTriangleHalfEdges(neighborhood.Second, removed);
        return removed;
    }

    private static void AddTriangleHalfEdges(CollapseSide side, HashSet<HalfEdgeHandle> removed)
    {
        if (!side.IsTriangle)
            return;

        removed.Add(side.Next);
        removed.Add(side.Prev);
    }

    private static bool HasExpectedRemovedHalfEdges(
        EdgeCollapseNeighborhood neighborhood,
        HashSet<HalfEdgeHandle> removed
    )
    {
        int expected = 2;
        if (neighborhood.First.IsTriangle)
            expected += 2;
        if (neighborhood.Second.IsTriangle)
            expected += 2;
        return removed.Count == expected;
    }

    private bool HasValidTriangleSurvivors(
        CollapseSide side,
        HashSet<HalfEdgeHandle> removedHalfEdges
    )
    {
        if (!side.IsTriangle)
            return true;

        HalfEdgeHandle nextTwin = side.NextData.Twin;
        HalfEdgeHandle prevTwin = side.PrevData.Twin;
        return nextTwin != prevTwin
            && HalfEdges.IsAlive(nextTwin)
            && HalfEdges.IsAlive(prevTwin)
            && HalfEdges[nextTwin].Twin == side.Next
            && HalfEdges[prevTwin].Twin == side.Prev
            && !removedHalfEdges.Contains(nextTwin)
            && !removedHalfEdges.Contains(prevTwin);
    }

    private bool SatisfiesSharedNeighborLinkCondition(
        VertexHandle keptVertex,
        VertexHandle removedVertex,
        CollapseSide first,
        CollapseSide second
    )
    {
        HashSet<VertexHandle> keptNeighbors = CollectVertexNeighbors(keptVertex, removedVertex);
        HashSet<VertexHandle> removedNeighbors = CollectVertexNeighbors(removedVertex, keptVertex);
        keptNeighbors.IntersectWith(removedNeighbors);

        HashSet<VertexHandle> allowedSharedNeighbors = [];
        if (first.IsTriangle)
            allowedSharedNeighbors.Add(first.PrevData.Origin);
        if (second.IsTriangle)
            allowedSharedNeighbors.Add(second.PrevData.Origin);

        return keptNeighbors.SetEquals(allowedSharedNeighbors);
    }

    private HashSet<VertexHandle> CollectVertexNeighbors(VertexHandle vertex, VertexHandle excluded)
    {
        HashSet<VertexHandle> neighbors = [];
        foreach (HalfEdgeHandle halfEdge in HalfEdges)
        {
            HalfEdge data = HalfEdges[halfEdge];
            if (data.Origin != vertex || !HalfEdges.IsAlive(data.Twin))
                continue;

            VertexHandle destination = HalfEdges[data.Twin].Origin;
            if (destination != excluded)
                neighbors.Add(destination);
        }
        return neighbors;
    }

    private bool WouldCreateRepeatedFaceVertex(
        VertexHandle keptVertex,
        VertexHandle removedVertex,
        CollapseSide first,
        CollapseSide second
    )
    {
        foreach (FaceHandle face in Faces)
        {
            if (
                (first.IsTriangle && face == first.EdgeData.Face)
                || (second.IsTriangle && face == second.EdgeData.Face)
            )
            {
                continue;
            }

            HalfEdgeHandle skippedCorner =
                face == first.EdgeData.Face ? first.Edge
                : face == second.EdgeData.Face ? second.Edge
                : HalfEdgeHandle.Null;
            HashSet<VertexHandle> vertices = [];
            int cornerCount = 0;
            foreach (HalfEdgeHandle corner in HalfEdgesAroundFace(face))
            {
                if (corner == skippedCorner)
                    continue;

                VertexHandle vertex = HalfEdges[corner].Origin;
                if (vertex == removedVertex)
                    vertex = keptVertex;
                if (!vertices.Add(vertex))
                    return true;
                cornerCount++;
            }

            if (cornerCount < 3)
                return true;
        }

        return false;
    }

    private void SpliceCollapsedSide(CollapseSide side)
    {
        if (side.IsTriangle)
            return;

        HalfEdge prev = side.PrevData;
        HalfEdge next = side.NextData;
        prev.Next = side.Next;
        next.Prev = side.Prev;
        HalfEdges[side.Prev] = prev;
        HalfEdges[side.Next] = next;

        if (
            Faces.IsAlive(side.EdgeData.Face)
            && Faces[side.EdgeData.Face].FirstHalfEdge == side.Edge
        )
            Faces[side.EdgeData.Face] = new Face { FirstHalfEdge = side.Next };
    }

    private void SpliceBoundaryEdgePair(
        HalfEdgeHandle edge,
        HalfEdge edgeData,
        HalfEdgeHandle twin,
        HalfEdge twinData
    )
    {
        bool isolated = edgeData.Next == twin && twinData.Next == edge;
        if (isolated)
            return;

        HalfEdge edgePrev = HalfEdges[edgeData.Prev];
        HalfEdge edgeNext = HalfEdges[edgeData.Next];
        HalfEdge twinPrev = HalfEdges[twinData.Prev];
        HalfEdge twinNext = HalfEdges[twinData.Next];

        edgePrev.Next = twinData.Next;
        twinNext.Prev = edgeData.Prev;
        twinPrev.Next = edgeData.Next;
        edgeNext.Prev = twinData.Prev;

        HalfEdges[edgeData.Prev] = edgePrev;
        HalfEdges[edgeData.Next] = edgeNext;
        HalfEdges[twinData.Prev] = twinPrev;
        HalfEdges[twinData.Next] = twinNext;
    }

    private void MergeTriangleSideEdges(CollapseSide side)
    {
        if (!side.IsTriangle)
            return;

        HalfEdgeHandle nextTwin = side.NextData.Twin;
        HalfEdgeHandle prevTwin = side.PrevData.Twin;
        HalfEdge nextTwinData = HalfEdges[nextTwin];
        HalfEdge prevTwinData = HalfEdges[prevTwin];
        nextTwinData.Twin = prevTwin;
        prevTwinData.Twin = nextTwin;
        HalfEdges[nextTwin] = nextTwinData;
        HalfEdges[prevTwin] = prevTwinData;
    }

    private void RefreshCollapsedVertexOutgoingHalfEdge(VertexHandle vertex)
    {
        HalfEdgeHandle outgoing = HalfEdgeHandle.Null;
        foreach (HalfEdgeHandle halfEdge in HalfEdges)
        {
            if (HalfEdges[halfEdge].Origin != vertex)
                continue;

            outgoing = halfEdge;
            break;
        }

        Vertices[vertex] = new Vertex { OutgoingHalfEdge = outgoing };
    }

    private readonly record struct CollapseSide(
        HalfEdgeHandle Edge,
        HalfEdge EdgeData,
        HalfEdgeHandle Next,
        HalfEdge NextData,
        HalfEdgeHandle Prev,
        HalfEdge PrevData,
        bool IsTriangle
    );

    private readonly record struct EdgeCollapseNeighborhood(
        HalfEdgeHandle Edge,
        HalfEdge EdgeData,
        HalfEdgeHandle Twin,
        HalfEdge TwinData,
        CollapseSide First,
        CollapseSide Second
    )
    {
        public bool BothSidesBoundary => EdgeData.Face.IsNull && TwinData.Face.IsNull;
    }
}
