using System.Numerics;

namespace TRMesh.Mesh;

public unsafe partial class SpatialMesh
{
    // Splits one face by inserting an interior edge between two non-adjacent face vertices.
    // The original face and every existing vertex/edge handle remain alive. Returns the new
    // half-edge directed from firstVertex to secondVertex.
    public int SplitFace(int face, int firstVertex, int secondVertex)
    {
        if (!IsFaceAlive(face))
            throw new ArgumentException("The face handle is not live.", nameof(face));
        if (firstVertex == secondVertex)
            throw new ArgumentException("The split vertices must be distinct.", nameof(secondVertex));

        int firstCorner = INVALID_HANDLE;
        int secondCorner = INVALID_HANDLE;
        foreach (int corner in HalfEdgeRing.AroundFace(this, face))
        {
            int vertex = HalfEdgeRef(corner).SourceVertex;
            if (vertex == firstVertex)
                firstCorner = corner;
            if (vertex == secondVertex)
                secondCorner = corner;
        }

        if (firstCorner == INVALID_HANDLE || secondCorner == INVALID_HANDLE)
        {
            throw new ArgumentException(
                "Both split vertices must belong to the face.",
                nameof(secondVertex)
            );
        }
        if (
            HalfEdgeRef(firstCorner).NextHalfEdge == secondCorner
            || HalfEdgeRef(secondCorner).NextHalfEdge == firstCorner
        )
        {
            throw new ArgumentException("The split vertices are already adjacent.", nameof(secondVertex));
        }
        if (FindHalfEdgeBetweenUnchecked(firstVertex, secondVertex) != INVALID_HANDLE)
        {
            throw new ArgumentException(
                "An edge already connects the split vertices.",
                nameof(secondVertex)
            );
        }

        int firstPrevious = HalfEdgeRef(firstCorner).PrevHalfEdge;
        int secondPrevious = HalfEdgeRef(secondCorner).PrevHalfEdge;
        Vector2 firstUv = _halfEdges.Get(firstCorner).UV;
        Vector2 secondUv = _halfEdges.Get(secondCorner).UV;

        MarkMutated();
        int newFace = _faceRecorder.Insert(
            new FaceData { topology = new Face { AdjacentHalfEdge = secondCorner } }
        );
        int firstToSecond = _halfEdgeRecorder.Insert(default);
        int secondToFirst = _halfEdgeRecorder.Insert(default);
        HalfEdgeMut(firstToSecond) = new HalfEdge
        {
            SourceVertex = firstVertex,
            TwinHalfEdge = secondToFirst,
            NextHalfEdge = secondCorner,
            PrevHalfEdge = firstPrevious,
            AdjacentFace = newFace,
        };
        HalfEdgeUvMut(firstToSecond) = firstUv;
        HalfEdgeMut(secondToFirst) = new HalfEdge
        {
            SourceVertex = secondVertex,
            TwinHalfEdge = firstToSecond,
            NextHalfEdge = firstCorner,
            PrevHalfEdge = secondPrevious,
            AdjacentFace = face,
        };
        HalfEdgeUvMut(secondToFirst) = secondUv;

        for (int corner = secondCorner; corner != firstCorner; corner = HalfEdgeRef(corner).NextHalfEdge)
            HalfEdgeMut(corner).AdjacentFace = newFace;

        HalfEdgeMut(firstPrevious).NextHalfEdge = firstToSecond;
        HalfEdgeMut(firstCorner).PrevHalfEdge = secondToFirst;
        HalfEdgeMut(secondPrevious).NextHalfEdge = secondToFirst;
        HalfEdgeMut(secondCorner).PrevHalfEdge = firstToSecond;
        FaceMut(face).AdjacentHalfEdge = firstCorner;

        var positions = new List<Vector3>();
        RecomputeFaceNormal(face, positions);
        RecomputeFaceNormal(newFace, positions);
        return firstToSecond;
    }
}
