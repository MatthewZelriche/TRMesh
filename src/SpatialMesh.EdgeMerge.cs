namespace TREditorSharp;

// TODO: Possibly support non-quad merging.
public partial class SpatialMesh
{
    /// <summary>
    /// Merge <paramref name="source"/> into the opposite <paramref name="target"/> edge of
    /// the same quad. The target edge handles, endpoint handles, and endpoint positions survive.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the quad strip was merged; otherwise <c>false</c> with no mutation.
    /// </returns>
    public bool TryMergeEdges(HalfEdgeHandle source, HalfEdgeHandle target)
    {
        if (!TryGetEdgeMergeNeighborhood(source, target, out EdgeMergeNeighborhood neighborhood))
            return false;

        HashSet<HalfEdgeHandle> removedHalfEdges = CollectMergedRemovedHalfEdges(neighborhood);

        Dictionary<HalfEdgeHandle, HalfEdge> rewrittenHalfEdges = [];
        foreach (HalfEdgeHandle halfEdge in HalfEdges)
        {
            if (removedHalfEdges.Contains(halfEdge))
                continue;

            HalfEdge data =
                halfEdge == neighborhood.Target ? neighborhood.SourceTwinData : HalfEdges[halfEdge];
            data.Origin = MapMergedVertex(data.Origin, neighborhood);
            data.Next = ResolveMergedLoopLink(
                data.Next,
                useNext: true,
                neighborhood,
                removedHalfEdges
            );
            data.Prev = ResolveMergedLoopLink(
                data.Prev,
                useNext: false,
                neighborhood,
                removedHalfEdges
            );
            if (halfEdge == neighborhood.Target)
            {
                data.Twin = neighborhood.TargetTwin;
                data.Face = neighborhood.SourceTwinData.Face;
            }

            rewrittenHalfEdges.Add(halfEdge, data);
        }

        foreach ((HalfEdgeHandle halfEdge, HalfEdge data) in rewrittenHalfEdges)
            HalfEdges[halfEdge] = data;

        foreach (FaceHandle face in Faces)
        {
            if (face == neighborhood.Face)
                continue;

            Face data = Faces[face];
            if (data.FirstHalfEdge == neighborhood.SourceTwin)
                data.FirstHalfEdge = neighborhood.Target;
            else if (removedHalfEdges.Contains(data.FirstHalfEdge))
            {
                data.FirstHalfEdge = ResolveMergedLoopLink(
                    HalfEdges[data.FirstHalfEdge].Next,
                    useNext: true,
                    neighborhood,
                    removedHalfEdges
                );
            }
            Faces[face] = data;
        }

        Faces.Free(neighborhood.Face);
        foreach (HalfEdgeHandle halfEdge in removedHalfEdges)
            HalfEdges.Free(halfEdge);
        Vertices.Free(neighborhood.SourceOrigin);
        Vertices.Free(neighborhood.SourceDestination);

        RefreshCollapsedVertexOutgoingHalfEdge(neighborhood.TargetOrigin);
        RefreshCollapsedVertexOutgoingHalfEdge(neighborhood.TargetDestination);
        return true;
    }

    private bool TryGetEdgeMergeNeighborhood(
        HalfEdgeHandle source,
        HalfEdgeHandle target,
        out EdgeMergeNeighborhood neighborhood
    )
    {
        neighborhood = default;
        if (source == target || !HalfEdges.IsAlive(source) || !HalfEdges.IsAlive(target))
            return false;

        HalfEdge sourceData = HalfEdges[source];
        if (
            !Faces.IsAlive(sourceData.Face)
            || !TryGetQuadLoop(source, sourceData, target, out QuadMergeLoop quad)
            || !TryGetMergeTwin(source, sourceData, sourceData.Face, out HalfEdge sourceTwinData)
            || !TryGetMergeTwin(quad.Next, quad.NextData, sourceData.Face, out _)
            || !TryGetMergeTwin(
                quad.Target,
                quad.TargetData,
                sourceData.Face,
                out HalfEdge targetTwinData
            )
            || !TryGetMergeTwin(quad.Prev, quad.PrevData, sourceData.Face, out _)
        )
        {
            return false;
        }

        neighborhood = new EdgeMergeNeighborhood(
            sourceData.Face,
            source,
            sourceData,
            sourceData.Twin,
            sourceTwinData,
            quad.Next,
            quad.NextData.Twin,
            quad.Target,
            quad.TargetData,
            quad.TargetData.Twin,
            targetTwinData,
            quad.Prev,
            quad.PrevData.Twin
        );

        if (
            !HasDistinctMergeHandles(neighborhood)
            || !HasDistinctLiveMergeVertices(neighborhood)
            || (Faces.IsAlive(sourceTwinData.Face) && sourceTwinData.Face == targetTwinData.Face)
            || !MergedFacesRemainValid(neighborhood)
            || !MergedEdgesRemainManifold(neighborhood)
            || !MergedLoopLinksRemainValid(neighborhood)
        )
        {
            neighborhood = default;
            return false;
        }

        return true;
    }

    private bool TryGetQuadLoop(
        HalfEdgeHandle source,
        HalfEdge sourceData,
        HalfEdgeHandle expectedTarget,
        out QuadMergeLoop quad
    )
    {
        quad = default;
        if (!HalfEdges.IsAlive(sourceData.Next) || !HalfEdges.IsAlive(sourceData.Prev))
            return false;

        HalfEdgeHandle next = sourceData.Next;
        HalfEdgeHandle prev = sourceData.Prev;
        HalfEdge nextData = HalfEdges[next];
        HalfEdge prevData = HalfEdges[prev];
        HalfEdgeHandle target = nextData.Next;
        if (target != expectedTarget || !HalfEdges.IsAlive(target))
            return false;

        HalfEdge targetData = HalfEdges[target];
        if (
            next == source
            || prev == source
            || target == source
            || next == prev
            || next == target
            || prev == target
            || sourceData.Prev != prev
            || nextData.Prev != source
            || targetData.Prev != next
            || targetData.Next != prev
            || prevData.Prev != target
            || prevData.Next != source
            || nextData.Face != sourceData.Face
            || targetData.Face != sourceData.Face
            || prevData.Face != sourceData.Face
        )
        {
            return false;
        }

        quad = new QuadMergeLoop(next, nextData, target, targetData, prev, prevData);
        return true;
    }

    private bool TryGetMergeTwin(
        HalfEdgeHandle halfEdge,
        HalfEdge data,
        FaceHandle interveningFace,
        out HalfEdge twinData
    )
    {
        twinData = default;
        if (!HalfEdges.IsAlive(data.Twin) || data.Twin == halfEdge)
            return false;

        twinData = HalfEdges[data.Twin];
        return twinData.Twin == halfEdge
            && twinData.Face != interveningFace
            && (twinData.Face.IsNull || Faces.IsAlive(twinData.Face))
            && IsValidMergeLoopMember(data.Twin, twinData);
    }

    private bool IsValidMergeLoopMember(HalfEdgeHandle halfEdge, HalfEdge data)
    {
        if (!HalfEdges.IsAlive(data.Next) || !HalfEdges.IsAlive(data.Prev))
            return false;

        HalfEdge next = HalfEdges[data.Next];
        HalfEdge prev = HalfEdges[data.Prev];
        return data.Next != halfEdge
            && data.Prev != halfEdge
            && next.Prev == halfEdge
            && prev.Next == halfEdge
            && next.Face == data.Face
            && prev.Face == data.Face;
    }

    private static bool HasDistinctMergeHandles(EdgeMergeNeighborhood neighborhood) =>
        new HashSet<HalfEdgeHandle>
        {
            neighborhood.Source,
            neighborhood.SourceTwin,
            neighborhood.Next,
            neighborhood.NextTwin,
            neighborhood.Target,
            neighborhood.TargetTwin,
            neighborhood.Prev,
            neighborhood.PrevTwin,
        }.Count == 8;

    private bool HasDistinctLiveMergeVertices(EdgeMergeNeighborhood neighborhood)
    {
        HashSet<VertexHandle> vertices =
        [
            neighborhood.SourceOrigin,
            neighborhood.SourceDestination,
            neighborhood.TargetOrigin,
            neighborhood.TargetDestination,
        ];
        if (vertices.Count != 4)
            return false;

        foreach (VertexHandle vertex in vertices)
        {
            if (!Vertices.IsAlive(vertex))
                return false;
        }
        return true;
    }

    private bool MergedFacesRemainValid(EdgeMergeNeighborhood neighborhood)
    {
        foreach (FaceHandle face in Faces)
        {
            if (face == neighborhood.Face)
                continue;

            HashSet<VertexHandle> vertices = [];
            int cornerCount = 0;
            foreach (HalfEdgeHandle corner in HalfEdgesAroundFace(face))
            {
                if (corner == neighborhood.NextTwin || corner == neighborhood.PrevTwin)
                    continue;

                VertexHandle vertex = MapMergedVertex(HalfEdges[corner].Origin, neighborhood);
                if (!vertices.Add(vertex))
                    return false;
                cornerCount++;
            }

            if (cornerCount < 3)
                return false;
        }

        return true;
    }

    private bool MergedEdgesRemainManifold(EdgeMergeNeighborhood neighborhood)
    {
        HashSet<HalfEdgeHandle> removedHalfEdges = CollectMergedRemovedHalfEdges(neighborhood);
        HashSet<(VertexHandle Origin, VertexHandle Destination)> directedEdges = [];
        foreach (HalfEdgeHandle halfEdge in HalfEdges)
        {
            if (removedHalfEdges.Contains(halfEdge))
                continue;

            HalfEdge data = HalfEdges[halfEdge];
            if (!HalfEdges.IsAlive(data.Twin) || removedHalfEdges.Contains(data.Twin))
                return false;

            VertexHandle origin = MapMergedVertex(data.Origin, neighborhood);
            VertexHandle destination = MapMergedVertex(HalfEdges[data.Twin].Origin, neighborhood);
            if (origin == destination || !directedEdges.Add((origin, destination)))
                return false;
        }

        return true;
    }

    private bool MergedLoopLinksRemainValid(EdgeMergeNeighborhood neighborhood)
    {
        HashSet<HalfEdgeHandle> removedHalfEdges = CollectMergedRemovedHalfEdges(neighborhood);
        foreach (HalfEdgeHandle halfEdge in HalfEdges)
        {
            if (removedHalfEdges.Contains(halfEdge))
                continue;

            HalfEdge data =
                halfEdge == neighborhood.Target ? neighborhood.SourceTwinData : HalfEdges[halfEdge];
            HalfEdgeHandle next = ResolveMergedLoopLink(
                data.Next,
                useNext: true,
                neighborhood,
                removedHalfEdges
            );
            HalfEdgeHandle prev = ResolveMergedLoopLink(
                data.Prev,
                useNext: false,
                neighborhood,
                removedHalfEdges
            );
            if (
                next.IsNull
                || prev.IsNull
                || next == halfEdge
                || prev == halfEdge
                || !HalfEdges.IsAlive(next)
                || !HalfEdges.IsAlive(prev)
            )
            {
                return false;
            }
        }
        return true;
    }

    private static HashSet<HalfEdgeHandle> CollectMergedRemovedHalfEdges(
        EdgeMergeNeighborhood neighborhood
    ) =>
        [
            neighborhood.Source,
            neighborhood.SourceTwin,
            neighborhood.Next,
            neighborhood.NextTwin,
            neighborhood.Prev,
            neighborhood.PrevTwin,
        ];

    private HalfEdgeHandle ResolveMergedLoopLink(
        HalfEdgeHandle link,
        bool useNext,
        EdgeMergeNeighborhood neighborhood,
        HashSet<HalfEdgeHandle> removedHalfEdges
    )
    {
        int budget = HalfEdges.LiveCount + 1;
        while (removedHalfEdges.Contains(link))
        {
            if (link == neighborhood.SourceTwin)
                return neighborhood.Target;
            if (!HalfEdges.IsAlive(link) || --budget < 0)
                return HalfEdgeHandle.Null;

            HalfEdge data = HalfEdges[link];
            link = useNext ? data.Next : data.Prev;
        }

        return link;
    }

    private static VertexHandle MapMergedVertex(
        VertexHandle vertex,
        EdgeMergeNeighborhood neighborhood
    )
    {
        if (vertex == neighborhood.SourceOrigin)
            return neighborhood.TargetDestination;
        if (vertex == neighborhood.SourceDestination)
            return neighborhood.TargetOrigin;
        return vertex;
    }

    private readonly record struct QuadMergeLoop(
        HalfEdgeHandle Next,
        HalfEdge NextData,
        HalfEdgeHandle Target,
        HalfEdge TargetData,
        HalfEdgeHandle Prev,
        HalfEdge PrevData
    );

    private readonly record struct EdgeMergeNeighborhood(
        FaceHandle Face,
        HalfEdgeHandle Source,
        HalfEdge SourceData,
        HalfEdgeHandle SourceTwin,
        HalfEdge SourceTwinData,
        HalfEdgeHandle Next,
        HalfEdgeHandle NextTwin,
        HalfEdgeHandle Target,
        HalfEdge TargetData,
        HalfEdgeHandle TargetTwin,
        HalfEdge TargetTwinData,
        HalfEdgeHandle Prev,
        HalfEdgeHandle PrevTwin
    )
    {
        public VertexHandle SourceOrigin => SourceData.Origin;
        public VertexHandle SourceDestination => SourceTwinData.Origin;
        public VertexHandle TargetOrigin => TargetData.Origin;
        public VertexHandle TargetDestination => TargetTwinData.Origin;
    }
}
