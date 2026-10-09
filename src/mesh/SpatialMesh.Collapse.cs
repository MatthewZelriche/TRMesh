using System.Numerics;

namespace TRMesh.Mesh;

public unsafe partial class SpatialMesh
{
    // Contracts one edge into either endpoint, preserving that endpoint's handle and placing it at
    // targetPosition. Faces lose the contracted corner and are removed if fewer than three remain.
    public void CollapseEdge(int halfEdge, int target, Vector3 targetPosition)
    {
        if (!IsHalfEdgeAlive(halfEdge))
            throw new ArgumentException("The half-edge handle is not live.", nameof(halfEdge));
        if (!IsFinite(targetPosition))
            throw new ArgumentException("The collapse position must be finite.", nameof(targetPosition));

        ref readonly HalfEdge edge = ref HalfEdgeRef(halfEdge);
        int from = edge.SourceVertex;
        int to = OppositeVertex(halfEdge);
        if (target != from && target != to)
        {
            throw new ArgumentException(
                "The target must be one of the edge's endpoints.",
                nameof(target)
            );
        }

        int source = target == from ? to : from;
        int sourceToTarget = from == source ? halfEdge : edge.TwinHalfEdge;
        int targetToSource = HalfEdgeRef(sourceToTarget).TwinHalfEdge;
        CollapseSide firstSide = AnalyzeCollapseSide(sourceToTarget);
        CollapseSide secondSide = AnalyzeCollapseSide(targetToSource);
        ValidateCollapseLink(source, target, firstSide, secondSide);

        var sourceOutgoing = new List<int>();
        foreach (int outgoing in HalfEdgeRing.AroundVertex(this, source))
            sourceOutgoing.Add(outgoing);

        HashSet<int> deletedHalfEdges = [sourceToTarget, targetToSource];
        HashSet<int> deletedFaces = [];
        var mergedEdges = new List<(int First, int Second)>();
        var detached = new HashSet<int>();
        ConsiderSide(firstSide);
        ConsiderSide(secondSide);

        foreach ((int first, int second) in mergedEdges)
        {
            if (
                HalfEdgeRef(first).AdjacentFace != INVALID_HANDLE
                || HalfEdgeRef(second).AdjacentFace != INVALID_HANDLE
            )
            {
                continue;
            }

            deletedHalfEdges.Add(first);
            deletedHalfEdges.Add(second);
            detached.Add(first);
            detached.Add(second);
        }

        var affectedVertices = new HashSet<int> { source, target };
        foreach (int deleted in deletedHalfEdges)
            affectedVertices.Add(HalfEdgeRef(deleted).SourceVertex);

        MarkMutated();
        if (!secondSide.IsTriangle && secondSide.Face != INVALID_HANDLE)
            HalfEdgeUvMut(HalfEdgeRef(secondSide.HalfEdge).NextHalfEdge) =
                _halfEdges.Get(secondSide.HalfEdge).UV;
        foreach (int removedHalfEdge in detached)
            DetachHalfEdge(removedHalfEdge);

        foreach ((int first, int second) in mergedEdges)
        {
            if (deletedHalfEdges.Contains(first))
                continue;
            HalfEdgeMut(first).TwinHalfEdge = second;
            HalfEdgeMut(second).TwinHalfEdge = first;
        }

        foreach (int outgoing in sourceOutgoing)
        {
            if (!deletedHalfEdges.Contains(outgoing))
                HalfEdgeMut(outgoing).SourceVertex = target;
        }

        _vertexRecorder.Capture(target);
        _vertices.SetPosition(target, targetPosition);
        foreach (int face in deletedFaces)
            _faceRecorder.RemoveAt(face);
        foreach (int deleted in deletedHalfEdges)
            _halfEdgeRecorder.RemoveAt(deleted);
        _vertexRecorder.RemoveAt(source);

        ResetOutgoingHalfEdges(affectedVertices);
        RecomputeNormalsAroundVertices([target]);

        void ConsiderSide(CollapseSide side)
        {
            if (!side.IsTriangle)
            {
                detached.Add(side.HalfEdge);
                return;
            }

            ref readonly HalfEdge sideEdge = ref HalfEdgeRef(side.HalfEdge);
            deletedFaces.Add(side.Face);
            deletedHalfEdges.Add(sideEdge.NextHalfEdge);
            deletedHalfEdges.Add(sideEdge.PrevHalfEdge);
            mergedEdges.Add(
                (
                    HalfEdgeRef(sideEdge.NextHalfEdge).TwinHalfEdge,
                    HalfEdgeRef(sideEdge.PrevHalfEdge).TwinHalfEdge
                )
            );
        }
    }

    private CollapseSide AnalyzeCollapseSide(int halfEdge)
    {
        ref readonly HalfEdge edge = ref HalfEdgeRef(halfEdge);
        int face = edge.AdjacentFace;
        if (face == INVALID_HANDLE)
            return new CollapseSide(halfEdge, face, false);

        int count = 1;
        for (int current = edge.NextHalfEdge; current != halfEdge; count++)
            current = HalfEdgeRef(current).NextHalfEdge;
        return new CollapseSide(halfEdge, face, count == 3);
    }

    private void ValidateCollapseLink(
        int source,
        int target,
        CollapseSide firstSide,
        CollapseSide secondSide
    )
    {
        HashSet<int> sourceNeighbors = VertexNeighbors(source);
        HashSet<int> targetNeighbors = VertexNeighbors(target);
        sourceNeighbors.Remove(target);
        targetNeighbors.Remove(source);
        sourceNeighbors.IntersectWith(targetNeighbors);

        var triangleOpposites = new HashSet<int>();
        if (firstSide.IsTriangle)
            triangleOpposites.Add(HalfEdgeRef(HalfEdgeRef(firstSide.HalfEdge).PrevHalfEdge).SourceVertex);
        if (secondSide.IsTriangle)
            triangleOpposites.Add(HalfEdgeRef(HalfEdgeRef(secondSide.HalfEdge).PrevHalfEdge).SourceVertex);
        if (!sourceNeighbors.SetEquals(triangleOpposites))
        {
            throw new ArgumentException(
                "Collapsing the edge would create duplicate edges or a disconnected vertex fan."
            );
        }
    }

    private HashSet<int> VertexNeighbors(int vertex)
    {
        var neighbors = new HashSet<int>();
        foreach (int outgoing in HalfEdgeRing.AroundVertex(this, vertex))
            neighbors.Add(OppositeVertex(outgoing));
        return neighbors;
    }

    private void DetachHalfEdge(int halfEdge)
    {
        HalfEdge edge = _halfEdges.GetTopology(halfEdge);
        HalfEdgeMut(edge.PrevHalfEdge).NextHalfEdge = edge.NextHalfEdge;
        HalfEdgeMut(edge.NextHalfEdge).PrevHalfEdge = edge.PrevHalfEdge;
        if (
            edge.AdjacentFace != INVALID_HANDLE
            && _faces.GetTopology(edge.AdjacentFace).AdjacentHalfEdge == halfEdge
        )
        {
            FaceMut(edge.AdjacentFace).AdjacentHalfEdge = edge.NextHalfEdge;
        }
    }

    private void ResetOutgoingHalfEdges(HashSet<int> affectedVertices)
    {
        var outgoing = new Dictionary<int, int>(affectedVertices.Count);
        foreach (int vertex in affectedVertices)
        {
            if (IsVertexAlive(vertex))
                outgoing.Add(vertex, INVALID_HANDLE);
        }

        var halfEdges = _halfEdges.GetEnumerator();
        while (halfEdges.MoveNext())
        {
            int halfEdge = halfEdges.CurrentSlot;
            int vertex = HalfEdgeRef(halfEdge).SourceVertex;
            if (outgoing.TryGetValue(vertex, out int current) && current == INVALID_HANDLE)
                outgoing[vertex] = halfEdge;
        }

        foreach ((int vertex, int halfEdge) in outgoing)
            VertexMut(vertex).OutgoingHalfEdge = halfEdge;
    }

    private readonly record struct CollapseSide(int HalfEdge, int Face, bool IsTriangle);
}
