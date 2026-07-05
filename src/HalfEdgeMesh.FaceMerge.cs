namespace TREditorSharp;

using TREditorSharp.Storage;

public partial class HalfEdgeMesh
{
    /// <summary>
    /// Returns whether two adjacent faces can be merged while preserving
    /// <paramref name="target"/>.
    /// </summary>
    public bool CanMergeFaces(FaceHandle source, FaceHandle target) =>
        TryGetFaceMergePlan(source, target, out _);

    /// <summary>
    /// Removes the edge shared by two adjacent faces and folds <paramref name="source"/> into
    /// <paramref name="target"/>. The target face handle and its component data survive.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the faces were merged; otherwise <c>false</c> with no mutation.
    /// </returns>
    public bool TryMergeFaces(FaceHandle source, FaceHandle target)
    {
        if (!TryGetFaceMergePlan(source, target, out FaceMergePlan plan))
            return false;

        foreach (HalfEdgeHandle halfEdge in plan.SourceLoop)
        {
            if (halfEdge == plan.SourceSharedEdge)
                continue;

            ref HalfEdge data = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(halfEdge);
            data.Face = target;
        }

        ref HalfEdge sourcePrev = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(plan.SourcePrev);
        ref HalfEdge sourceNext = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(plan.SourceNext);
        ref HalfEdge targetPrev = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(plan.TargetPrev);
        ref HalfEdge targetNext = ref HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(plan.TargetNext);

        sourcePrev.Next = plan.TargetNext;
        targetNext.Prev = plan.SourcePrev;
        targetPrev.Next = plan.SourceNext;
        sourceNext.Prev = plan.TargetPrev;

        ref Face targetData = ref Faces.GetUnsafeRef<Face, Face>(target);
        if (targetData.FirstHalfEdge == plan.TargetSharedEdge)
            targetData.FirstHalfEdge = plan.TargetNext;

        Faces.Free(source);
        HalfEdges.Free(plan.SourceSharedEdge);
        HalfEdges.Free(plan.TargetSharedEdge);
        RefreshOutgoingHalfEdge(plan.SourceOrigin);
        RefreshOutgoingHalfEdge(plan.SourceDestination);
        return true;
    }

    private bool TryGetFaceMergePlan(FaceHandle source, FaceHandle target, out FaceMergePlan plan)
    {
        plan = default;
        if (source == target || !Faces.IsAlive(source) || !Faces.IsAlive(target))
            return false;

        List<HalfEdgeHandle> sourceLoop = [];
        HashSet<VertexHandle> sourceVertices = [];
        HalfEdgeHandle sourceSharedEdge = HalfEdgeHandle.Null;
        int sharedEdgeCount = 0;
        foreach (HalfEdgeHandle halfEdge in HalfEdgesAroundFace(source))
        {
            HalfEdge data = HalfEdges[halfEdge];
            if (!sourceVertices.Add(data.Origin))
                return false;

            sourceLoop.Add(halfEdge);
            if (HalfEdges.IsAlive(data.Twin) && HalfEdges[data.Twin].Face == target)
            {
                sourceSharedEdge = halfEdge;
                sharedEdgeCount++;
            }
        }

        if (sourceLoop.Count < 3 || sharedEdgeCount != 1)
            return false;

        HalfEdge sourceSharedData = HalfEdges[sourceSharedEdge];
        HalfEdgeHandle targetSharedEdge = sourceSharedData.Twin;
        if (
            !HalfEdges.IsAlive(targetSharedEdge)
            || HalfEdges[targetSharedEdge].Twin != sourceSharedEdge
        )
        {
            return false;
        }

        HashSet<VertexHandle> targetVertices = [];
        int targetCornerCount = 0;
        bool foundTargetSharedEdge = false;
        foreach (HalfEdgeHandle halfEdge in HalfEdgesAroundFace(target))
        {
            if (!targetVertices.Add(HalfEdges[halfEdge].Origin))
                return false;

            targetCornerCount++;
            foundTargetSharedEdge |= halfEdge == targetSharedEdge;
        }

        if (targetCornerCount < 3 || !foundTargetSharedEdge)
            return false;

        HalfEdge targetSharedData = HalfEdges[targetSharedEdge];
        VertexHandle sourceOrigin = sourceSharedData.Origin;
        VertexHandle sourceDestination = targetSharedData.Origin;
        int sharedVertexCount = 0;
        foreach (VertexHandle vertex in sourceVertices)
        {
            if (!targetVertices.Contains(vertex))
                continue;

            if (vertex != sourceOrigin && vertex != sourceDestination)
                return false;
            sharedVertexCount++;
        }

        if (sharedVertexCount != 2)
            return false;

        if (
            !IsValidMergeLoopLink(sourceSharedData.Prev, source)
            || !IsValidMergeLoopLink(sourceSharedData.Next, source)
            || !IsValidMergeLoopLink(targetSharedData.Prev, target)
            || !IsValidMergeLoopLink(targetSharedData.Next, target)
        )
        {
            return false;
        }

        plan = new FaceMergePlan(
            sourceLoop,
            sourceSharedEdge,
            targetSharedEdge,
            sourceSharedData.Prev,
            sourceSharedData.Next,
            targetSharedData.Prev,
            targetSharedData.Next,
            sourceOrigin,
            sourceDestination
        );
        return true;
    }

    private bool IsValidMergeLoopLink(HalfEdgeHandle halfEdge, FaceHandle face) =>
        HalfEdges.IsAlive(halfEdge) && HalfEdges[halfEdge].Face == face;

    private readonly record struct FaceMergePlan(
        List<HalfEdgeHandle> SourceLoop,
        HalfEdgeHandle SourceSharedEdge,
        HalfEdgeHandle TargetSharedEdge,
        HalfEdgeHandle SourcePrev,
        HalfEdgeHandle SourceNext,
        HalfEdgeHandle TargetPrev,
        HalfEdgeHandle TargetNext,
        VertexHandle SourceOrigin,
        VertexHandle SourceDestination
    );
}
