namespace TREditorSharp;

using System.Numerics;

public partial class SpatialMesh
{
    /// <summary>
    /// Insert a positioned vertex along <paramref name="edge"/>, subdividing the edge in both
    /// adjacent loops without creating or removing faces.
    /// </summary>
    /// <param name="edge">Either live half-edge of the edge to split.</param>
    /// <param name="t">
    /// Interpolation parameter from the supplied half-edge's origin to its destination.
    /// Values outside <c>[0, 1]</c> extrapolate the position.
    /// </param>
    /// <returns>The newly inserted vertex.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="edge"/> or its local connectivity is not valid.
    /// </exception>
    public VertexHandle SplitEdge(HalfEdgeHandle edge, float t = 0.5f)
    {
        if (!HalfEdges.IsAlive(edge))
            throw new ArgumentException($"Edge {edge} is not live.", nameof(edge));

        HalfEdge halfEdge = HalfEdges[edge];
        HalfEdgeHandle twinHandle = halfEdge.Twin;
        if (!HalfEdges.IsAlive(twinHandle))
            throw new ArgumentException($"Edge {edge} has no live twin.", nameof(edge));

        HalfEdge twin = HalfEdges[twinHandle];
        ValidateSplitEdgeConnectivity(edge, halfEdge, twinHandle, twin);

        Vector3 position = Vector3.Lerp(
            GetVertexPosition(halfEdge.Origin),
            GetVertexPosition(twin.Origin),
            t
        );
        Vector2 halfEdgeUv = default;
        Vector2 twinUv = default;
        bool interpolateHalfEdgeUv = Faces.IsAlive(halfEdge.Face);
        bool interpolateTwinUv = Faces.IsAlive(twin.Face);
        if (interpolateHalfEdgeUv)
        {
            halfEdgeUv = Vector2.Lerp(GetFaceCornerUv(edge), GetFaceCornerUv(halfEdge.Next), t);
        }
        if (interpolateTwinUv)
        {
            twinUv = Vector2.Lerp(GetFaceCornerUv(twinHandle), GetFaceCornerUv(twin.Next), 1f - t);
        }

        VertexHandle inserted = AddVertex(position);
        HalfEdgeHandle insertedToDestination = HalfEdges.Allocate();
        HalfEdgeHandle insertedToOrigin = HalfEdges.Allocate();

        HalfEdges[edge] = new HalfEdge
        {
            Origin = halfEdge.Origin,
            Twin = insertedToOrigin,
            Next = insertedToDestination,
            Prev = halfEdge.Prev,
            Face = halfEdge.Face,
        };
        HalfEdges[twinHandle] = new HalfEdge
        {
            Origin = twin.Origin,
            Twin = insertedToDestination,
            Next = insertedToOrigin,
            Prev = twin.Prev,
            Face = twin.Face,
        };
        HalfEdges[insertedToDestination] = new HalfEdge
        {
            Origin = inserted,
            Twin = twinHandle,
            Next = halfEdge.Next,
            Prev = edge,
            Face = halfEdge.Face,
        };
        HalfEdges[insertedToOrigin] = new HalfEdge
        {
            Origin = inserted,
            Twin = edge,
            Next = twin.Next,
            Prev = twinHandle,
            Face = twin.Face,
        };

        HalfEdge halfEdgeNext = HalfEdges[halfEdge.Next];
        halfEdgeNext.Prev = insertedToDestination;
        HalfEdges[halfEdge.Next] = halfEdgeNext;

        HalfEdge twinNext = HalfEdges[twin.Next];
        twinNext.Prev = insertedToOrigin;
        HalfEdges[twin.Next] = twinNext;

        Vertices[inserted] = new Vertex { OutgoingHalfEdge = insertedToDestination };
        if (interpolateHalfEdgeUv)
            SetFaceCornerUv(insertedToDestination, halfEdgeUv);
        if (interpolateTwinUv)
            SetFaceCornerUv(insertedToOrigin, twinUv);

        return inserted;
    }

    private void ValidateSplitEdgeConnectivity(
        HalfEdgeHandle edge,
        HalfEdge halfEdge,
        HalfEdgeHandle twinHandle,
        HalfEdge twin
    )
    {
        if (twinHandle == edge || twin.Twin != edge)
            throw new ArgumentException(
                $"Edge {edge} and twin {twinHandle} are not reciprocal.",
                nameof(edge)
            );
        if (!Vertices.IsAlive(halfEdge.Origin) || !Vertices.IsAlive(twin.Origin))
            throw new ArgumentException($"Edge {edge} has a dead endpoint.", nameof(edge));
        if (halfEdge.Origin == twin.Origin)
            throw new ArgumentException($"Edge {edge} has identical endpoints.", nameof(edge));
        if (!HalfEdges.IsAlive(halfEdge.Next) || !HalfEdges.IsAlive(twin.Next))
            throw new ArgumentException($"Edge {edge} has a dead next link.", nameof(edge));
        if (!HalfEdges.IsAlive(halfEdge.Prev) || !HalfEdges.IsAlive(twin.Prev))
            throw new ArgumentException($"Edge {edge} has a dead previous link.", nameof(edge));
        if (!halfEdge.Face.IsNull && !Faces.IsAlive(halfEdge.Face))
            throw new ArgumentException($"Edge {edge} has a dead face.", nameof(edge));
        if (!twin.Face.IsNull && !Faces.IsAlive(twin.Face))
            throw new ArgumentException($"Twin {twinHandle} has a dead face.", nameof(edge));
    }
}
