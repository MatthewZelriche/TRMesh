namespace TREditorSharp;

using System.Numerics;

public partial class SpatialMesh
{
    /// <summary>
    /// Extrude an open edge by <paramref name="offset"/>, creating a quad between the source
    /// edge and a translated copy. The selected half-edge may be either side of the edge.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="edge"/> is not a live open edge.
    /// </exception>
    public ExtrudeEdgeResult ExtrudeEdge(HalfEdgeHandle edge, Vector3 offset)
    {
        if (!TryGetEdgeExtrusionPlan(edge, out EdgeExtrusionPlan plan))
            throw new ArgumentException("Edge must be live and have an open side.", nameof(edge));

        VertexHandle newOrigin = AddVertex(GetVertexPosition(plan.Origin) + offset);
        VertexHandle newDestination = AddVertex(GetVertexPosition(plan.Destination) + offset);
        FaceHandle face = AddFace([plan.Origin, plan.Destination, newDestination, newOrigin]);
        SetFaceMaterialSlot(face, plan.MaterialSlot);
        SetFaceUvsInitialized(face, false);

        HalfEdgeHandle outerEdge = HalfEdgeHandle.Null;
        foreach (HalfEdgeHandle faceEdge in HalfEdgesAroundFace(face))
        {
            HalfEdge data = GetHalfEdge(faceEdge);
            if (data.Origin == newDestination && GetHalfEdge(data.Twin).Origin == newOrigin)
            {
                outerEdge = faceEdge;
                break;
            }
        }

        if (outerEdge.IsNull)
            throw new InvalidOperationException("Extruded quad did not contain its outer edge.");

        return new ExtrudeEdgeResult(
            face,
            outerEdge,
            [newOrigin, newDestination],
            plan.SourceHadInitializedUvs
        );
    }

    public bool CanExtrudeEdge(HalfEdgeHandle edge) => TryGetEdgeExtrusionPlan(edge, out _);

    private bool TryGetEdgeExtrusionPlan(HalfEdgeHandle edge, out EdgeExtrusionPlan plan)
    {
        plan = default;
        if (!IsHalfEdgeAlive(edge))
            return false;

        HalfEdge selected = GetHalfEdge(edge);
        if (!IsHalfEdgeAlive(selected.Twin) || selected.Twin == edge)
            return false;

        HalfEdge twin = GetHalfEdge(selected.Twin);
        if (twin.Twin != edge)
            return false;

        bool selectedHasFace = IsFaceAlive(selected.Face);
        bool twinHasFace = IsFaceAlive(twin.Face);
        if (selectedHasFace && twinHasFace)
            return false;

        HalfEdgeHandle boundary = selectedHasFace ? selected.Twin : edge;
        HalfEdge boundaryData = GetHalfEdge(boundary);
        HalfEdge boundaryTwin = GetHalfEdge(boundaryData.Twin);
        FaceHandle adjacentFace = IsFaceAlive(boundaryTwin.Face)
            ? boundaryTwin.Face
            : FaceHandle.Null;

        plan = new EdgeExtrusionPlan(
            boundaryData.Origin,
            boundaryTwin.Origin,
            adjacentFace.IsNull ? UntexturedMaterialSlot : GetFaceMaterialSlot(adjacentFace),
            !adjacentFace.IsNull && AreFaceUvsInitialized(adjacentFace)
        );
        return true;
    }

    private readonly record struct EdgeExtrusionPlan(
        VertexHandle Origin,
        VertexHandle Destination,
        int MaterialSlot,
        bool SourceHadInitializedUvs
    );

    public readonly record struct ExtrudeEdgeResult(
        FaceHandle Face,
        HalfEdgeHandle OuterEdge,
        VertexHandle[] NewVertices,
        bool SourceHadInitializedUvs
    );
}
