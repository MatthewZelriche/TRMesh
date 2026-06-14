namespace TREditorSharp;

public partial class SpatialMesh
{
    /// <summary>
    /// Collect the vertices around the open boundary containing <paramref name="edge"/>.
    /// The selected edge may be either the boundary half-edge or its interior twin.
    /// </summary>
    public bool TryGetHoleBoundaryVertices(HalfEdgeHandle edge, List<VertexHandle> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();
        if (!IsHalfEdgeAlive(edge))
            return false;

        HalfEdgeHandle boundary = ResolveBoundaryHalfEdge(edge);
        if (boundary.IsNull)
            return false;

        HalfEdgeHandle current = boundary;
        int remaining = HalfEdges.LiveCount + 1;
        do
        {
            if (--remaining < 0 || !IsHalfEdgeAlive(current))
            {
                output.Clear();
                return false;
            }

            HalfEdge halfEdge = GetHalfEdge(current);
            if (!halfEdge.Face.IsNull || output.Contains(halfEdge.Origin))
            {
                output.Clear();
                return false;
            }

            output.Add(halfEdge.Origin);
            current = halfEdge.Next;
        } while (current != boundary);

        if (output.Count >= 3)
            return true;

        output.Clear();
        return false;
    }

    /// <summary>Fill the open boundary containing <paramref name="edge"/> with one polygon face.</summary>
    public bool TryFillHole(HalfEdgeHandle edge, out FaceHandle face)
    {
        List<VertexHandle> vertices = [];
        if (!TryGetHoleBoundaryVertices(edge, vertices))
        {
            face = FaceHandle.Null;
            return false;
        }

        face = AddFace(vertices.ToArray());
        SetFaceMaterialSlot(face, UntexturedMaterialSlot);
        SetFaceUvsInitialized(face, false);
        return true;
    }

    private HalfEdgeHandle ResolveBoundaryHalfEdge(HalfEdgeHandle edge)
    {
        HalfEdge halfEdge = GetHalfEdge(edge);
        if (halfEdge.Face.IsNull)
            return edge;

        return IsHalfEdgeAlive(halfEdge.Twin) && GetHalfEdge(halfEdge.Twin).Face.IsNull
            ? halfEdge.Twin
            : HalfEdgeHandle.Null;
    }
}
