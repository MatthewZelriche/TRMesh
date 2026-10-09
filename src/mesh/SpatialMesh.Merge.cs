using System.Numerics;

namespace TRMesh.Mesh;

public unsafe partial class SpatialMesh
{
    // Contracts a selected vertex subgraph into target. Disconnected selected islands are first
    // joined by face diagonals, then every resulting selected edge is collapsed toward target.
    public void MergeVertices(
        ReadOnlySpan<int> vertices,
        int target,
        Vector3 targetPosition
    )
    {
        HashSet<int> selected = ValidateMerge(vertices, target, targetPosition);
        HashSet<int> targetComponentIsland = ConnectedSelected(target, selected);

        // Multiple islands?
        while (targetComponentIsland.Count != selected.Count)
        {
            if (!TrySplitFace(selected, targetComponentIsland))
            {
                throw new ArgumentException(
                    "The selected vertex islands cannot be connected across shared faces.",
                    nameof(vertices)
                );
            }

            targetComponentIsland = ConnectedSelected(target, selected);
        }

        selected.Remove(target);
        while (selected.Count != 0)
        {
            int halfEdge = FindEdgeToSelectedVertex(target, selected);
            if (halfEdge == INVALID_HANDLE)
            {
                throw new ArgumentException(
                    "The selected vertices became disconnected while collapsing.",
                    nameof(vertices)
                );
            }

            int source = OppositeVertex(halfEdge);
            CollapseEdge(halfEdge, target, targetPosition);
            selected.Remove(source);
        }
    }

    private HashSet<int> ValidateMerge(
        ReadOnlySpan<int> vertices,
        int target,
        Vector3 targetPosition
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(vertices.Length, 2);
        if (!IsFinite(targetPosition))
            throw new ArgumentException("The merge position must be finite.", nameof(targetPosition));

        var selected = new HashSet<int>();
        foreach (int vertex in vertices)
        {
            if (!IsVertexAlive(vertex))
                throw new ArgumentException("A selected vertex handle is not live.", nameof(vertices));
            if (!selected.Add(vertex))
                throw new ArgumentException("Selected vertex handles must be unique.", nameof(vertices));
        }

        if (!selected.Contains(target))
            throw new ArgumentException("The target must be one of the selected vertices.", nameof(target));
        return selected;
    }

    private int FindEdgeToSelectedVertex(int target, HashSet<int> selected)
    {
        foreach (int halfEdge in HalfEdgeRing.AroundVertex(this, target))
        {
            if (selected.Contains(OppositeVertex(halfEdge)))
                return halfEdge;
        }

        return INVALID_HANDLE;
    }

    // BFS over selected vertices using the half-edge mesh as the graph. Merge can only
    // collapse along existing selected-selected edges, so this finds the island already
    // reachable from start; anything left out must be bridged (via face splits) first.
    private HashSet<int> ConnectedSelected(int start, HashSet<int> selected)
    {
        var connected = new HashSet<int> { start };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out int vertex))
        {
            foreach (int halfEdge in HalfEdgeRing.AroundVertex(this, vertex))
            {
                int other = OppositeVertex(halfEdge);
                if (selected.Contains(other) && connected.Add(other))
                    queue.Enqueue(other);
            }
        }

        return connected;
    }

    private bool TrySplitFace(HashSet<int> selected, HashSet<int> targetComponent)
    {
        var faces = _faces.GetEnumerator();
        while (faces.MoveNext())
        {
            int face = faces.CurrentSlot;
            var vertices = new List<int>();
            foreach (int halfEdge in HalfEdgeRing.AroundFace(this, face))
                vertices.Add(HalfEdgeRef(halfEdge).SourceVertex);

            for (int first = 0; first < vertices.Count; first++)
            {
                int from = vertices[first];
                if (!targetComponent.Contains(from))
                    continue;

                for (int second = 0; second < vertices.Count; second++)
                {
                    int to = vertices[second];
                    if (
                        !selected.Contains(to)
                        || targetComponent.Contains(to)
                        || AreAdjacent(first, second, vertices.Count)
                    )
                    {
                        continue;
                    }

                    SplitFace(face, from, to);
                    return true;
                }
            }
        }

        return false;
    }

    private static bool AreAdjacent(int first, int second, int count) =>
        first == second || (first + 1) % count == second || (second + 1) % count == first;
}
