namespace TRMesh.Mesh;

public unsafe partial class SpatialMesh
{
    // Removes one interior edge and joins its two incident faces.
    public void DissolveEdge(int halfEdge) => DissolveEdges([halfEdge]);

    // Removes selected interior edges and replaces every connected face region with one face.
    // A region must have one simple outer boundary; selections that would leave an internal edge,
    // create a hole, or repeat a boundary vertex are rejected before the mesh is changed.
    public void DissolveEdges(ReadOnlySpan<int> halfEdges)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(halfEdges.Length, 1);

        var selectedEdges = new HashSet<int>();
        var selectedHalfEdges = new HashSet<int>();
        var parent = new Dictionary<int, int>();
        foreach (int selected in halfEdges)
        {
            if (!IsHalfEdgeAlive(selected))
                throw new ArgumentException("A selected edge handle is not live.", nameof(halfEdges));

            ref readonly HalfEdge edge = ref HalfEdgeRef(selected);
            int canonical = Math.Min(selected, edge.TwinHalfEdge);
            if (!selectedEdges.Add(canonical))
                throw new ArgumentException("Selected edge handles must be unique.", nameof(halfEdges));

            ref readonly HalfEdge twin = ref HalfEdgeRef(edge.TwinHalfEdge);
            int firstFace = edge.AdjacentFace;
            int secondFace = twin.AdjacentFace;
            if (
                !IsFaceAlive(firstFace)
                || !IsFaceAlive(secondFace)
                || firstFace == secondFace
            )
            {
                throw new ArgumentException(
                    "Every selected edge must lie between two distinct faces.",
                    nameof(halfEdges)
                );
            }

            selectedHalfEdges.Add(canonical);
            selectedHalfEdges.Add(HalfEdgeRef(canonical).TwinHalfEdge);
            AddFace(firstFace);
            AddFace(secondFace);
            Union(firstFace, secondFace);
        }

        var regions = new Dictionary<int, HashSet<int>>();
        foreach (int face in parent.Keys.ToArray())
        {
            int root = Find(face);
            if (!regions.TryGetValue(root, out HashSet<int>? region))
                regions.Add(root, region = []);
            region.Add(face);
        }

        var plans = new List<DissolveRegion>(regions.Count);
        foreach (HashSet<int> faces in regions.Values)
        {
            var boundary = new List<int>();
            foreach (int face in faces)
            {
                foreach (int edge in HalfEdgeRing.AroundFace(this, face))
                {
                    if (selectedHalfEdges.Contains(edge))
                        continue;

                    int adjacent = HalfEdgeRef(HalfEdgeRef(edge).TwinHalfEdge).AdjacentFace;
                    if (faces.Contains(adjacent))
                    {
                        throw new ArgumentException(
                            "All edges between faces being merged must be selected.",
                            nameof(halfEdges)
                        );
                    }

                    boundary.Add(edge);
                }
            }

            if (boundary.Count < 3)
                throw InvalidRegion();

            var boundaryIndices = new Dictionary<int, int>(boundary.Count);
            for (int index = 0; index < boundary.Count; index++)
                boundaryIndices.Add(boundary[index], index);

            var successors = new int[boundary.Count];
            for (int index = 0; index < boundary.Count; index++)
            {
                int next = HalfEdgeRef(boundary[index]).NextHalfEdge;
                int crossed = 0;
                while (selectedHalfEdges.Contains(next))
                {
                    if (++crossed > selectedHalfEdges.Count)
                        throw InvalidRegion();
                    next = HalfEdgeRef(HalfEdgeRef(next).TwinHalfEdge).NextHalfEdge;
                }

                if (!boundaryIndices.ContainsKey(next))
                    throw InvalidRegion();
                successors[index] = next;
            }

            var visitedEdges = new HashSet<int>();
            var visitedVertices = new HashSet<int>();
            int current = boundary[0];
            while (visitedEdges.Add(current))
            {
                if (!visitedVertices.Add(HalfEdgeRef(current).SourceVertex))
                    throw InvalidRegion();
                current = successors[boundaryIndices[current]];
            }
            if (current != boundary[0] || visitedEdges.Count != boundary.Count)
                throw InvalidRegion();

            int survivor = faces.Min();
            plans.Add(new DissolveRegion(survivor, [.. faces], [.. boundary], successors));
        }

        var affectedVertices = new HashSet<int>();
        foreach (int edge in selectedHalfEdges)
            affectedVertices.Add(HalfEdgeRef(edge).SourceVertex);

        MarkMutated();
        foreach (DissolveRegion plan in plans)
        {
            for (int index = 0; index < plan.Boundary.Length; index++)
            {
                int edge = plan.Boundary[index];
                int next = plan.Successors[index];
                HalfEdgeMut(edge).NextHalfEdge = next;
                HalfEdgeMut(next).PrevHalfEdge = edge;
                HalfEdgeMut(edge).AdjacentFace = plan.Survivor;
            }

            FaceMut(plan.Survivor).AdjacentHalfEdge = plan.Boundary[0];
            foreach (int face in plan.Faces)
            {
                if (face != plan.Survivor)
                    _faceRecorder.RemoveAt(face);
            }
        }

        foreach (int edge in selectedHalfEdges)
            _halfEdgeRecorder.RemoveAt(edge);

        var normalFaces = new HashSet<int>();
        foreach (DissolveRegion plan in plans)
            normalFaces.Add(plan.Survivor);

        ResetOutgoingHalfEdges(affectedVertices);
        foreach (int vertex in affectedVertices)
        {
            if (!IsVertexAlive(vertex))
                continue;
            if (VertexRef(vertex).OutgoingHalfEdge == INVALID_HANDLE)
                _vertexRecorder.RemoveAt(vertex);
            else
                TryDissolveInteriorValenceTwoVertex(vertex, normalFaces);
        }

        var positions = new List<System.Numerics.Vector3>();
        foreach (int face in normalFaces)
        {
            if (IsFaceAlive(face))
                RecomputeFaceNormal(face, positions);
        }

        void AddFace(int face)
        {
            if (!parent.ContainsKey(face))
                parent.Add(face, face);
        }

        int Find(int face)
        {
            int root = face;
            while (parent[root] != root)
                root = parent[root];
            while (parent[face] != face)
            {
                int next = parent[face];
                parent[face] = root;
                face = next;
            }
            return root;
        }

        void Union(int first, int second)
        {
            first = Find(first);
            second = Find(second);
            if (first != second)
                parent[second] = first;
        }

        static ArgumentException InvalidRegion() =>
            new("The selected edges do not bound a single simple merged face.", nameof(halfEdges));
    }

    private readonly record struct DissolveRegion(
        int Survivor,
        int[] Faces,
        int[] Boundary,
        int[] Successors
    );

    // Once an interior seam is gone, either endpoint can become a redundant two-edge corner.
    // Join those two edges so closed meshes lose the corner, while preserving open boundaries and
    // avoiding a duplicate edge when the two neighboring vertices are already connected.
    private bool TryDissolveInteriorValenceTwoVertex(int vertex, HashSet<int> normalFaces)
    {
        var outgoing = new List<int>(2);
        foreach (int edge in HalfEdgeRing.AroundVertex(this, vertex))
        {
            outgoing.Add(edge);
            if (outgoing.Count > 2)
                return false;
        }
        if (outgoing.Count != 2)
            return false;

        int first = outgoing[0];
        int second = outgoing[1];
        int firstIncoming = HalfEdgeRef(first).TwinHalfEdge;
        int secondIncoming = HalfEdgeRef(second).TwinHalfEdge;
        if (
            HalfEdgeRef(first).AdjacentFace == INVALID_HANDLE
            || HalfEdgeRef(firstIncoming).AdjacentFace == INVALID_HANDLE
            || HalfEdgeRef(second).AdjacentFace == INVALID_HANDLE
            || HalfEdgeRef(secondIncoming).AdjacentFace == INVALID_HANDLE
        )
        {
            return false;
        }

        int firstNeighbor = HalfEdgeRef(firstIncoming).SourceVertex;
        int secondNeighbor = HalfEdgeRef(secondIncoming).SourceVertex;
        if (
            firstNeighbor == secondNeighbor
            || FindHalfEdgeBetweenUnchecked(firstNeighbor, secondNeighbor) != INVALID_HANDLE
        )
        {
            return false;
        }

        int afterFirstIncoming = HalfEdgeRef(firstIncoming).NextHalfEdge;
        int afterSecondIncoming = HalfEdgeRef(secondIncoming).NextHalfEdge;
        if (
            (afterFirstIncoming != first && afterFirstIncoming != second)
            || (afterSecondIncoming != first && afterSecondIncoming != second)
            || afterFirstIncoming == afterSecondIncoming
        )
        {
            return false;
        }

        int firstNext = HalfEdgeRef(afterFirstIncoming).NextHalfEdge;
        int secondNext = HalfEdgeRef(afterSecondIncoming).NextHalfEdge;
        int firstFace = HalfEdgeRef(firstIncoming).AdjacentFace;
        int secondFace = HalfEdgeRef(secondIncoming).AdjacentFace;
        normalFaces.Add(firstFace);
        normalFaces.Add(secondFace);

        HalfEdgeMut(firstIncoming).TwinHalfEdge = secondIncoming;
        HalfEdgeMut(secondIncoming).TwinHalfEdge = firstIncoming;
        HalfEdgeMut(firstIncoming).NextHalfEdge = firstNext;
        HalfEdgeMut(firstNext).PrevHalfEdge = firstIncoming;
        HalfEdgeMut(secondIncoming).NextHalfEdge = secondNext;
        HalfEdgeMut(secondNext).PrevHalfEdge = secondIncoming;
        if (FaceMut(firstFace).AdjacentHalfEdge == afterFirstIncoming)
            FaceMut(firstFace).AdjacentHalfEdge = firstIncoming;
        if (FaceMut(secondFace).AdjacentHalfEdge == afterSecondIncoming)
            FaceMut(secondFace).AdjacentHalfEdge = secondIncoming;

        _halfEdgeRecorder.RemoveAt(first);
        _halfEdgeRecorder.RemoveAt(second);
        _vertexRecorder.RemoveAt(vertex);
        return true;
    }
}
