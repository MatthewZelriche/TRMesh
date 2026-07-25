namespace TREditorSharp;

using System.Numerics;

/// <summary>Read-only geometry and topology operations required by mesh rendering.</summary>
public interface ISpatialMeshView
{
    bool IsVertexAlive(VertexHandle vertex);
    bool IsHalfEdgeAlive(HalfEdgeHandle halfEdge);
    bool IsFaceAlive(FaceHandle face);
    HalfEdge GetHalfEdge(HalfEdgeHandle halfEdge);
    Vector3 GetVertexPosition(VertexHandle vertex);
    Vector2 GetFaceCornerUv(FaceCornerHandle corner);
    int GetFaceMaterialSlot(FaceHandle face);
    bool AreFaceUvsInitialized(FaceHandle face);
    Vector3 ComputeFaceNormal(FaceHandle face);
    bool TriangulateFace(FaceHandle face, List<FaceCornerHandle> output);
}
