namespace TRMesh.Mesh;

public unsafe partial class SpatialMesh
{
    // True when the selection implies at least one live face, without mutating the mesh.
    public bool WouldDeleteAnyFace(
        ReadOnlySpan<int> faces,
        ReadOnlySpan<int> halfEdges,
        ReadOnlySpan<int> vertices
    ) => CollectDeletableFaces(faces, halfEdges, vertices, validate: false).Count > 0;

    // Deletes every face implied by the selection: explicit faces, faces adjacent to edges, and
    // faces incident to vertices. Orphaned wire edges and isolated vertices are cleaned up.
    public void DeleteComponents(
        ReadOnlySpan<int> faces,
        ReadOnlySpan<int> halfEdges,
        ReadOnlySpan<int> vertices
    )
    {
        HashSet<int> toDelete = CollectDeletableFaces(faces, halfEdges, vertices, validate: true);
        if (toDelete.Count == 0)
        {
            throw new ArgumentException(
                "The selection does not bound any faces that can be deleted."
            );
        }

        int[] ordered = new int[toDelete.Count];
        toDelete.CopyTo(ordered);
        DeleteFaces(ordered);
    }

    private HashSet<int> CollectDeletableFaces(
        ReadOnlySpan<int> faces,
        ReadOnlySpan<int> halfEdges,
        ReadOnlySpan<int> vertices,
        bool validate
    )
    {
        var toDelete = new HashSet<int>();
        foreach (int face in faces)
        {
            if (!IsFaceAlive(face))
            {
                if (validate)
                {
                    throw new ArgumentException(
                        "A selected face handle is not live.",
                        nameof(faces)
                    );
                }

                continue;
            }

            toDelete.Add(face);
        }

        foreach (int halfEdge in halfEdges)
        {
            if (!IsHalfEdgeAlive(halfEdge))
            {
                if (validate)
                {
                    throw new ArgumentException(
                        "A selected edge handle is not live.",
                        nameof(halfEdges)
                    );
                }

                continue;
            }

            AddAdjacentFace(toDelete, halfEdge);
            AddAdjacentFace(toDelete, HalfEdgeRef(halfEdge).TwinHalfEdge);
        }

        foreach (int vertex in vertices)
        {
            if (!IsVertexAlive(vertex))
            {
                if (validate)
                {
                    throw new ArgumentException(
                        "A selected vertex handle is not live.",
                        nameof(vertices)
                    );
                }

                continue;
            }

            foreach (int halfEdge in HalfEdgeRing.AroundVertex(this, vertex))
                AddAdjacentFace(toDelete, halfEdge);
        }

        return toDelete;
    }

    // Removes the selected faces. Edges that lose both adjacent faces are deleted, and vertices
    // that become isolated are removed. Surviving boundary half-edges keep a valid hole walk.
    public void DeleteFaces(ReadOnlySpan<int> faces)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(faces.Length, 1);

        var unique = new HashSet<int>();
        foreach (int face in faces)
        {
            if (!IsFaceAlive(face))
                throw new ArgumentException("A selected face handle is not live.", nameof(faces));
            if (!unique.Add(face))
            {
                throw new ArgumentException(
                    "Selected face handles must be unique.",
                    nameof(faces)
                );
            }
        }

        var faceHalfEdges = new List<int>();
        var affectedVertices = new HashSet<int>();
        foreach (int face in unique)
        {
            foreach (int halfEdge in HalfEdgeRing.AroundFace(this, face))
            {
                faceHalfEdges.Add(halfEdge);
                affectedVertices.Add(HalfEdgeRef(halfEdge).SourceVertex);
            }
        }

        MarkMutated();
        foreach (int halfEdge in faceHalfEdges)
            HalfEdgeMut(halfEdge).AdjacentFace = INVALID_HANDLE;
        foreach (int face in unique)
            _faceRecorder.RemoveAt(face);

        var wireEdges = new HashSet<int>();
        foreach (int halfEdge in faceHalfEdges)
        {
            if (!IsHalfEdgeAlive(halfEdge))
                continue;

            int twin = HalfEdgeRef(halfEdge).TwinHalfEdge;
            if (
                HalfEdgeRef(halfEdge).AdjacentFace != INVALID_HANDLE
                || HalfEdgeRef(twin).AdjacentFace != INVALID_HANDLE
            )
            {
                continue;
            }

            wireEdges.Add(Math.Min(halfEdge, twin));
        }

        foreach (int halfEdge in wireEdges)
        {
            if (IsHalfEdgeAlive(halfEdge))
                RemoveWireEdge(halfEdge, affectedVertices);
        }

        ResetOutgoingHalfEdges(affectedVertices);
        foreach (int vertex in affectedVertices)
        {
            if (IsVertexAlive(vertex) && VertexRef(vertex).OutgoingHalfEdge == INVALID_HANDLE)
                _vertexRecorder.RemoveAt(vertex);
        }
    }

    // Deletes an edge whose faces are already cleared on both sides. Relinks the two boundary
    // walks so remaining hole half-edges stay circularly consistent (OpenMesh-style splice).
    private void RemoveWireEdge(int halfEdge, HashSet<int> affectedVertices)
    {
        int twin = HalfEdgeRef(halfEdge).TwinHalfEdge;
        int next = HalfEdgeRef(halfEdge).NextHalfEdge;
        int prev = HalfEdgeRef(halfEdge).PrevHalfEdge;
        int twinNext = HalfEdgeRef(twin).NextHalfEdge;
        int twinPrev = HalfEdgeRef(twin).PrevHalfEdge;

        affectedVertices.Add(HalfEdgeRef(halfEdge).SourceVertex);
        affectedVertices.Add(HalfEdgeRef(twin).SourceVertex);

        if (next != twin || twinNext != halfEdge)
        {
            HalfEdgeMut(prev).NextHalfEdge = twinNext;
            HalfEdgeMut(twinNext).PrevHalfEdge = prev;
            HalfEdgeMut(twinPrev).NextHalfEdge = next;
            HalfEdgeMut(next).PrevHalfEdge = twinPrev;
        }

        _halfEdgeRecorder.RemoveAt(halfEdge);
        _halfEdgeRecorder.RemoveAt(twin);
    }

    private void AddAdjacentFace(HashSet<int> faces, int halfEdge)
    {
        int face = HalfEdgeRef(halfEdge).AdjacentFace;
        if (face != INVALID_HANDLE)
            faces.Add(face);
    }
}
