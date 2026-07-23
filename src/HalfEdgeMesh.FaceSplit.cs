namespace TREditorSharp;

public partial class HalfEdgeMesh
{
    /// <summary>
    /// Connect two non-adjacent corners of the same face, replacing that face with two faces
    /// separated by the new edge.
    /// </summary>
    /// <returns>The two newly created faces.</returns>
    /// <exception cref="ArgumentException">
    /// Either corner is dead, the corners do not belong to the same live face, the corners are
    /// equal or adjacent, or their vertices are already connected by an edge.
    /// </exception>
    public virtual (FaceHandle First, FaceHandle Second) SplitFace(
        FaceCornerHandle cornerA,
        FaceCornerHandle cornerB
    )
    {
        if (!HalfEdges.IsAlive(cornerA))
            throw new ArgumentException($"Corner {cornerA} is not live.", nameof(cornerA));
        if (!HalfEdges.IsAlive(cornerB))
            throw new ArgumentException($"Corner {cornerB} is not live.", nameof(cornerB));

        HalfEdge halfEdgeA = HalfEdges[cornerA];
        HalfEdge halfEdgeB = HalfEdges[cornerB];
        FaceHandle face = halfEdgeA.Face;
        if (!Faces.IsAlive(face) || halfEdgeB.Face != face)
        {
            throw new ArgumentException(
                $"Corners {cornerA} and {cornerB} do not belong to the same live face."
            );
        }

        List<VertexHandle> vertices = [];
        int indexA = -1;
        int indexB = -1;
        foreach (FaceCornerHandle corner in HalfEdgesAroundFace(face))
        {
            if (corner == cornerA)
                indexA = vertices.Count;
            if (corner == cornerB)
                indexB = vertices.Count;

            vertices.Add(HalfEdges[corner].Origin);
        }

        if (indexA < 0 || indexB < 0)
            throw new ArgumentException("Both corners must belong to the face loop.");

        int count = vertices.Count;
        int forwardDistance = (indexB - indexA + count) % count;
        if (forwardDistance <= 1 || forwardDistance >= count - 1)
            throw new ArgumentException("Split corners must be distinct and non-adjacent.");

        VertexHandle vertexA = vertices[indexA];
        VertexHandle vertexB = vertices[indexB];
        if (vertexA == vertexB)
            throw new ArgumentException("Split corners must reference distinct vertices.");
        if (HalfEdges.IsAlive(FindHalfEdgeBetweenUnchecked(vertexA, vertexB)))
        {
            throw new ArgumentException(
                $"Vertices {vertexA} and {vertexB} are already connected by an edge."
            );
        }

        VertexHandle[] firstVertices = CollectPath(vertices, indexA, indexB);
        VertexHandle[] secondVertices = CollectPath(vertices, indexB, indexA);

        RemoveFace(face);
        FaceHandle first = AddFace(firstVertices);
        FaceHandle second = AddFace(secondVertices);
        return (first, second);
    }

    private static VertexHandle[] CollectPath(
        IReadOnlyList<VertexHandle> vertices,
        int start,
        int end
    )
    {
        List<VertexHandle> path = [];
        for (int index = start; ; index = (index + 1) % vertices.Count)
        {
            path.Add(vertices[index]);
            if (index == end)
                return path.ToArray();
        }
    }
}
