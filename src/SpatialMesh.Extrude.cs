namespace TREditorSharp;

using System.Numerics;

public partial class SpatialMesh
{
    /// <summary>
    /// Extrude <paramref name="face"/> along its unit normal by <paramref name="distance"/>,
    /// replacing it with a side ring and a translated cap.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="face"/> is not live.</exception>
    public ExtrudeFaceResult ExtrudeFace(FaceHandle face, float distance)
    {
        Vector3 offset = ComputeFaceNormal(face) * distance;
        RingResult ring = BuildExtrusionRing(face, (_, position) => position + offset);
        return new ExtrudeFaceResult(ring.CapFace, ring.SideFaces, ring.NewVertices);
    }

    /// <summary>
    /// Replace a face with a ring of side quads and a cap whose vertices are positioned by
    /// <paramref name="placeVertex"/>. The cap keeps the source material; each side inherits the
    /// material across its corresponding original edge, or is untextured at a boundary. Shared by
    /// face extrusion and inset operations.
    /// </summary>
    internal RingResult BuildExtrusionRing(
        FaceHandle face,
        Func<int, Vector3, Vector3> placeVertex,
        bool inheritNeighborMaterials = true
    )
    {
        ArgumentNullException.ThrowIfNull(placeVertex);

        VertexHandle[] originalVertices = CollectFaceVertices(face);
        if (
            originalVertices.Length < 3
            || originalVertices.Distinct().Count() != originalVertices.Length
        )
            throw new ArgumentException(
                "Extrusion source face must have at least three distinct vertices.",
                nameof(face)
            );

        int capMaterialSlot = GetFaceMaterialSlot(face);
        int[] sideMaterialSlots = inheritNeighborMaterials
            ? CollectExtrusionSideMaterialSlots(face)
            : Enumerable.Repeat(capMaterialSlot, originalVertices.Length).ToArray();
        Vector3[] newPositions = new Vector3[originalVertices.Length];
        for (int i = 0; i < originalVertices.Length; i++)
            newPositions[i] = placeVertex(i, GetVertexPosition(originalVertices[i]));

        VertexHandle[] newVertices = new VertexHandle[originalVertices.Length];
        for (int i = 0; i < newVertices.Length; i++)
            newVertices[i] = AddVertex(newPositions[i]);

        RemoveFace(face);

        FaceHandle[] sideFaces = new FaceHandle[originalVertices.Length];
        for (int i = 0; i < originalVertices.Length; i++)
        {
            int next = (i + 1) % originalVertices.Length;
            FaceHandle sideFace = AddFace([
                originalVertices[i],
                originalVertices[next],
                newVertices[next],
                newVertices[i],
            ]);
            InitializeExtrusionFace(sideFace, sideMaterialSlots[i]);
            sideFaces[i] = sideFace;
        }

        FaceHandle capFace = AddFace(newVertices);
        InitializeExtrusionFace(capFace, capMaterialSlot);
        return new RingResult(capFace, sideFaces, newVertices);
    }

    private int[] CollectExtrusionSideMaterialSlots(FaceHandle face)
    {
        List<int> materialSlots = [];
        foreach (HalfEdgeHandle edge in HalfEdgesAroundFace(face))
        {
            FaceHandle neighbor = GetHalfEdge(GetHalfEdge(edge).Twin).Face;
            materialSlots.Add(
                IsFaceAlive(neighbor) ? GetFaceMaterialSlot(neighbor) : UntexturedMaterialSlot
            );
        }
        return materialSlots.ToArray();
    }

    private void InitializeExtrusionFace(FaceHandle face, int materialSlot)
    {
        SetFaceMaterialSlot(face, materialSlot);
        SetFaceUvsInitialized(face, false);
    }

    internal readonly record struct RingResult(
        FaceHandle CapFace,
        FaceHandle[] SideFaces,
        VertexHandle[] NewVertices
    );

    public readonly record struct ExtrudeFaceResult(
        FaceHandle CapFace,
        FaceHandle[] SideFaces,
        VertexHandle[] NewVertices
    );
}
