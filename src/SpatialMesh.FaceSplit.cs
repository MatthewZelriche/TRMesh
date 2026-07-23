namespace TREditorSharp;

using System.Numerics;

public partial class SpatialMesh
{
    /// <inheritdoc />
    /// <remarks>
    /// Both replacement faces inherit the source material. When the source UVs are initialized,
    /// its existing corner UVs are retained and the two new diagonal corners copy the source UVs
    /// at their respective vertices.
    /// </remarks>
    public override (FaceHandle First, FaceHandle Second) SplitFace(
        FaceCornerHandle cornerA,
        FaceCornerHandle cornerB
    )
    {
        if (!HalfEdges.IsAlive(cornerA) || !HalfEdges.IsAlive(cornerB))
            return base.SplitFace(cornerA, cornerB);

        HalfEdge halfEdgeA = HalfEdges[cornerA];
        HalfEdge halfEdgeB = HalfEdges[cornerB];
        FaceHandle source = halfEdgeA.Face;
        if (!Faces.IsAlive(source) || halfEdgeB.Face != source)
            return base.SplitFace(cornerA, cornerB);

        int materialSlot = GetFaceMaterialSlot(source);
        bool uvsInitialized = AreFaceUvsInitialized(source);
        Vector2 uvA = uvsInitialized ? GetFaceCornerUv(cornerA) : default;
        Vector2 uvB = uvsInitialized ? GetFaceCornerUv(cornerB) : default;

        (FaceHandle first, FaceHandle second) = base.SplitFace(cornerA, cornerB);

        SetFaceMaterialSlot(first, materialSlot);
        SetFaceMaterialSlot(second, materialSlot);

        if (uvsInitialized)
        {
            HalfEdgeHandle diagonal = FindHalfEdge(halfEdgeA.Origin, halfEdgeB.Origin);
            if (diagonal.IsNull)
                throw new InvalidOperationException("SplitFace did not create the expected diagonal.");

            SetFaceCornerUv(diagonal, uvA);
            SetFaceCornerUv(HalfEdges[diagonal].Twin, uvB);
        }

        SetFaceUvsInitialized(first, uvsInitialized);
        SetFaceUvsInitialized(second, uvsInitialized);
        return (first, second);
    }
}
