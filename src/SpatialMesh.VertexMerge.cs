namespace TREditorSharp;

using System.Numerics;

public partial class SpatialMesh
{
    /// <summary>
    /// Atomically merge every <paramref name="sources"/> into <paramref name="target"/>, bridging
    /// selected edge-components through shared faces when necessary. The target handle and
    /// position are preserved.
    /// </summary>
    /// <param name="sources">
    /// Unique source vertices in deterministic merge order. The target must not be included.
    /// </param>
    /// <param name="target">The vertex that survives the complete operation.</param>
    /// <returns>
    /// An owned, committed patch when all vertices were merged; otherwise <see langword="null"/>
    /// with no mutation. The caller must dispose a successful patch.
    /// </returns>
    public TopologyPatch? TryMergeVertices(
        IReadOnlyList<VertexHandle> sources,
        VertexHandle target
    )
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (!TryValidateMergeSelection(sources, target, out VertexHandle[] affectedVertices))
            return null;

        // The scope captures every selected one-ring. Disposing it before Commit rolls back all
        // intermediate face splits and edge collapses, so callers never observe a partial merge.
        using TopologyEditScope edit = BeginTopologyEdit(affectedVertices);
        if (!TryMergeVertexSelection(sources, target))
            return null;

        FaceUvProjector.ReprojectInitializedFacesAroundVertices(this, [target]);
        return edit.Commit();
    }

    private bool TryValidateMergeSelection(
        IReadOnlyList<VertexHandle> sources,
        VertexHandle target,
        out VertexHandle[] affectedVertices
    )
    {
        affectedVertices = [];
        if (sources.Count == 0 || !Vertices.IsAlive(target))
            return false;

        // Seeding the set with the target rejects both duplicate sources and target-as-source.
        HashSet<VertexHandle> unique = [target];
        affectedVertices = new VertexHandle[sources.Count + 1];
        for (int i = 0; i < sources.Count; i++)
        {
            VertexHandle vertex = sources[i];
            if (!Vertices.IsAlive(vertex) || !unique.Add(vertex))
            {
                affectedVertices = [];
                return false;
            }

            affectedVertices[i] = vertex;
        }
        affectedVertices[^1] = target;
        return true;
    }

    private bool TryMergeVertexSelection(
        IReadOnlyList<VertexHandle> sources,
        VertexHandle target
    )
    {
        HashSet<VertexHandle> selected = [target];
        foreach (VertexHandle vertex in sources)
            selected.Add(vertex);

        // Existing selected edges form components. Face splits first join every component to the
        // target's component, after which the operation can use only ordinary edge collapses.
        Dictionary<VertexHandle, SelectedVertexComponent> components = BuildSelectedComponents(
            sources,
            target,
            selected
        );
        SelectedVertexComponent baseComponent = components[target];
        while (baseComponent.Vertices.Count != selected.Count)
        {
            if (!TryBridgeNextComponent(sources, target, components, baseComponent))
                return false;
        }

        List<VertexHandle> remaining = [.. sources];
        while (remaining.Count > 0)
        {
            bool merged = false;
            // A collapse rewrites neighboring rings and may make an earlier rejected source valid,
            // so restart in caller-supplied order after every successful collapse.
            for (int i = 0; i < remaining.Count; i++)
            {
                if (!TryMergeAdjacentVertex(remaining[i], target))
                    continue;

                remaining.RemoveAt(i);
                merged = true;
                break;
            }

            if (!merged)
                return false;
        }

        return true;
    }

    private Dictionary<VertexHandle, SelectedVertexComponent> BuildSelectedComponents(
        IReadOnlyList<VertexHandle> sources,
        VertexHandle target,
        HashSet<VertexHandle> selected
    )
    {
        Dictionary<VertexHandle, SelectedVertexComponent> components = [];
        Queue<VertexHandle> pending = new();
        AddComponent(target);
        foreach (VertexHandle vertex in sources)
            AddComponent(vertex);
        return components;

        void AddComponent(VertexHandle seed)
        {
            if (components.ContainsKey(seed))
                return;

            SelectedVertexComponent component = new();
            component.Vertices.Add(seed);
            components.Add(seed, component);
            pending.Enqueue(seed);
            while (pending.TryDequeue(out VertexHandle vertex))
            {
                foreach (HalfEdgeHandle outgoing in HalfEdgesAroundVertex(vertex))
                {
                    VertexHandle neighbor = HalfEdges[HalfEdges[outgoing].Twin].Origin;
                    if (!selected.Contains(neighbor) || components.ContainsKey(neighbor))
                        continue;

                    component.Vertices.Add(neighbor);
                    // All vertices in one component map to the same mutable component record.
                    components.Add(neighbor, component);
                    pending.Enqueue(neighbor);
                }
            }
        }
    }

    private bool TryBridgeNextComponent(
        IReadOnlyList<VertexHandle> sources,
        VertexHandle target,
        Dictionary<VertexHandle, SelectedVertexComponent> components,
        SelectedVertexComponent baseComponent
    )
    {
        // The target is always the preferred bridgehead. Remaining vertices retain caller order,
        // making the otherwise-greedy bridge sequence deterministic.
        if (TryBridgeFrom(target))
            return true;

        foreach (VertexHandle bridgehead in sources)
        {
            if (components[bridgehead] == baseComponent && TryBridgeFrom(bridgehead))
                return true;
        }

        return false;

        bool TryBridgeFrom(VertexHandle bridgehead)
        {
            if (components[bridgehead] != baseComponent)
                return false;

            List<(FaceHandle Face, FaceCornerHandle Corner)> incidentFaces =
                CollectIncidentFacesInStableOrder(bridgehead);
            foreach (VertexHandle other in sources)
            {
                SelectedVertexComponent otherComponent = components[other];
                if (otherComponent == baseComponent)
                    continue;

                foreach ((FaceHandle face, FaceCornerHandle bridgeheadCorner) in incidentFaces)
                {
                    if (!TryFindFaceCorner(face, other, out FaceCornerHandle otherCorner))
                        continue;
                    // Adjacent vertices need no bridge, while an interior edge joining two boundary
                    // vertices cannot be collapsed without creating a disconnected vertex fan.
                    if (!FindHalfEdge(bridgehead, other).IsNull)
                        continue;
                    if (IsBoundaryVertex(bridgehead) && IsBoundaryVertex(other))
                        continue;

                    // Virtual dispatch preserves SpatialMesh face attributes on both split faces.
                    SplitFace(bridgeheadCorner, otherCorner);
                    foreach (VertexHandle vertex in otherComponent.Vertices)
                    {
                        baseComponent.Vertices.Add(vertex);
                        components[vertex] = baseComponent;
                    }
                    return true;
                }
            }

            return false;
        }
    }

    private List<(FaceHandle Face, FaceCornerHandle Corner)> CollectIncidentFacesInStableOrder(
        VertexHandle vertex
    )
    {
        // Splitting invalidates the source face handle. Recollect and sort live incident faces on
        // every bridge attempt rather than retaining candidates across topology changes.
        List<(FaceHandle Face, FaceCornerHandle Corner)> faces = [];
        foreach (HalfEdgeHandle outgoing in HalfEdgesAroundVertex(vertex))
        {
            FaceHandle face = HalfEdges[outgoing].Face;
            if (!Faces.IsAlive(face))
                continue;

            faces.Add((face, outgoing));
        }
        faces.Sort(static (left, right) =>
        {
            int indexComparison = left.Face.Index.CompareTo(right.Face.Index);
            return indexComparison != 0
                ? indexComparison
                : left.Face.Generation.CompareTo(right.Face.Generation);
        });
        return faces;
    }

    private bool TryFindFaceCorner(
        FaceHandle face,
        VertexHandle vertex,
        out FaceCornerHandle corner
    )
    {
        foreach (FaceCornerHandle candidate in HalfEdgesAroundFace(face))
        {
            if (HalfEdges[candidate].Origin != vertex)
                continue;

            corner = candidate;
            return true;
        }

        corner = FaceCornerHandle.Null;
        return false;
    }

    private bool TryMergeAdjacentVertex(VertexHandle source, VertexHandle target)
    {
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

    private sealed class SelectedVertexComponent
    {
        public HashSet<VertexHandle> Vertices { get; } = [];
    }
}
