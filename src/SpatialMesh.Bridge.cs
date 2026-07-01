namespace TREditorSharp;

using System.Numerics;

public partial class SpatialMesh
{
    /// <summary>
    /// Bridge two boundary edges with a strip of quads following a circular arc.
    /// <paramref name="segments"/> is the number of quads across the span; an arch angle of
    /// zero is flat and 180 degrees produces a semicircle.
    /// </summary>
    public BridgeEdgesResult BridgeEdges(
        HalfEdgeHandle firstEdge,
        HalfEdgeHandle secondEdge,
        int segments,
        float archAngleDegrees
    )
    {
        if (segments < 1)
            throw new ArgumentOutOfRangeException(nameof(segments));
        if (archAngleDegrees < 0f || archAngleDegrees > 180f || !float.IsFinite(archAngleDegrees))
        {
            throw new ArgumentOutOfRangeException(
                nameof(archAngleDegrees),
                "Arch angle must be between 0 and 180 degrees."
            );
        }
        if (!TryGetBridgePlan(firstEdge, secondEdge, out BridgePlan plan))
            throw new ArgumentException("Edges cannot be bridged.");

        VertexHandle[] left = new VertexHandle[segments + 1];
        VertexHandle[] right = new VertexHandle[segments + 1];
        left[0] = plan.FirstOrigin;
        right[0] = plan.FirstDestination;
        left[segments] = plan.SecondDestination;
        right[segments] = plan.SecondOrigin;

        bool hasArch = archAngleDegrees > 1e-5f;
        if (!hasArch && !plan.LeftConnector.IsNull)
        {
            VertexHandle[] connector = SubdivideBridgeConnector(plan.LeftConnector, segments);
            for (int row = 1; row < segments; row++)
                left[row] = connector[segments - row];
        }
        if (!hasArch && !plan.RightConnector.IsNull)
        {
            VertexHandle[] connector = SubdivideBridgeConnector(plan.RightConnector, segments);
            for (int row = 1; row < segments; row++)
                right[row] = connector[row];
        }

        List<VertexHandle> newVertices = [];
        for (int row = 1; row < segments; row++)
        {
            float t = row / (float)segments;
            if (left[row].IsNull)
            {
                left[row] = AddVertex(
                    EvaluateBridgeArc(
                        plan.FirstOriginPosition,
                        plan.SecondDestinationPosition,
                        plan.ArchDirection,
                        t,
                        archAngleDegrees
                    )
                );
            }
            if (right[row].IsNull)
            {
                right[row] = AddVertex(
                    EvaluateBridgeArc(
                        plan.FirstDestinationPosition,
                        plan.SecondOriginPosition,
                        plan.ArchDirection,
                        t,
                        archAngleDegrees
                    )
                );
            }
            newVertices.Add(left[row]);
            newVertices.Add(right[row]);
        }

        List<FaceHandle> faces = [];
        for (int row = 0; row < segments; row++)
        {
            FaceHandle face = AddFace([left[row], right[row], right[row + 1], left[row + 1]]);
            SetFaceMaterialSlot(face, plan.MaterialSlot);
            SetFaceUvsInitialized(face, false);
            faces.Add(face);
        }

        if (segments > 1 && hasArch && !plan.LeftConnector.IsNull)
        {
            FaceHandle face = AddFace(left);
            SetFaceMaterialSlot(face, plan.MaterialSlot);
            SetFaceUvsInitialized(face, false);
            faces.Add(face);
        }
        if (segments > 1 && hasArch && !plan.RightConnector.IsNull)
        {
            VertexHandle[] reversedRight = Enumerable.Reverse(right).ToArray();
            FaceHandle face = AddFace(reversedRight);
            SetFaceMaterialSlot(face, plan.MaterialSlot);
            SetFaceUvsInitialized(face, false);
            faces.Add(face);
        }

        return new BridgeEdgesResult(
            faces.ToArray(),
            newVertices.ToArray(),
            plan.SourceHadInitializedUvs
        );
    }

    public bool CanBridgeEdges(HalfEdgeHandle firstEdge, HalfEdgeHandle secondEdge) =>
        TryGetBridgePlan(firstEdge, secondEdge, out _);

    private bool TryGetBridgePlan(
        HalfEdgeHandle firstEdge,
        HalfEdgeHandle secondEdge,
        out BridgePlan plan
    )
    {
        plan = default;
        if (
            !TryGetBoundaryEdge(
                firstEdge,
                out HalfEdgeHandle firstBoundary,
                out FaceHandle firstFace
            )
            || !TryGetBoundaryEdge(
                secondEdge,
                out HalfEdgeHandle secondBoundary,
                out FaceHandle secondFace
            )
            || firstBoundary == secondBoundary
        )
        {
            return false;
        }

        HalfEdge first = HalfEdges[firstBoundary];
        HalfEdge second = HalfEdges[secondBoundary];
        VertexHandle firstOrigin = first.Origin;
        VertexHandle firstDestination = HalfEdges[first.Twin].Origin;
        VertexHandle secondOrigin = second.Origin;
        VertexHandle secondDestination = HalfEdges[second.Twin].Origin;
        if (
            new HashSet<VertexHandle>
            {
                firstOrigin,
                firstDestination,
                secondOrigin,
                secondDestination,
            }.Count != 4
            || !TryGetBridgeConnector(
                secondDestination,
                firstOrigin,
                out HalfEdgeHandle leftConnector
            )
            || !TryGetBridgeConnector(
                firstDestination,
                secondOrigin,
                out HalfEdgeHandle rightConnector
            )
        )
        {
            return false;
        }

        Vector3 firstOriginPosition = GetVertexPosition(firstOrigin);
        Vector3 firstDestinationPosition = GetVertexPosition(firstDestination);
        Vector3 secondOriginPosition = GetVertexPosition(secondOrigin);
        Vector3 secondDestinationPosition = GetVertexPosition(secondDestination);
        Vector3 firstDirection = firstDestinationPosition - firstOriginPosition;
        Vector3 secondRowDirection = secondOriginPosition - secondDestinationPosition;
        Vector3 across =
            (secondOriginPosition + secondDestinationPosition)
            - (firstOriginPosition + firstDestinationPosition);
        if (
            !(firstDirection.LengthSquared() > 0f)
            || !(secondRowDirection.LengthSquared() > 0f)
            || !(across.LengthSquared() > 0f)
        )
        {
            return false;
        }

        Vector3 longitudinal =
            Vector3.Normalize(firstDirection) + Vector3.Normalize(secondRowDirection);
        if (!(longitudinal.LengthSquared() > 1e-8f))
            longitudinal = Vector3.Normalize(firstDirection);
        else
            longitudinal = Vector3.Normalize(longitudinal);

        Vector3 archDirection = Vector3.Cross(longitudinal, Vector3.Normalize(across));
        if (!(archDirection.LengthSquared() > 1e-8f))
            return false;
        archDirection = Vector3.Normalize(archDirection);

        Vector3 desiredDirection =
            -ComputeBoundaryFaceDirection(firstOriginPosition, firstDestinationPosition, firstFace)
            - ComputeBoundaryFaceDirection(
                secondOriginPosition,
                secondDestinationPosition,
                secondFace
            );
        if (!(desiredDirection.LengthSquared() > 1e-8f))
            desiredDirection = Vector3.UnitY;
        if (Vector3.Dot(archDirection, desiredDirection) < 0f)
            archDirection = -archDirection;
        if (
            !CanEvaluateBridgeArc(firstOriginPosition, secondDestinationPosition, archDirection)
            || !CanEvaluateBridgeArc(firstDestinationPosition, secondOriginPosition, archDirection)
        )
        {
            return false;
        }

        plan = new BridgePlan(
            firstOrigin,
            firstDestination,
            secondOrigin,
            secondDestination,
            firstOriginPosition,
            firstDestinationPosition,
            secondOriginPosition,
            secondDestinationPosition,
            archDirection,
            GetFaceMaterialSlot(firstFace),
            AreFaceUvsInitialized(firstFace),
            leftConnector,
            rightConnector
        );
        return true;
    }

    private static bool CanEvaluateBridgeArc(Vector3 start, Vector3 end, Vector3 archDirection)
    {
        Vector3 chord = end - start;
        if (!(chord.LengthSquared() > 1e-8f))
            return false;
        Vector3 chordDirection = Vector3.Normalize(chord);
        Vector3 projected =
            archDirection - chordDirection * Vector3.Dot(archDirection, chordDirection);
        return projected.LengthSquared() > 1e-8f;
    }

    private bool TryGetBridgeConnector(
        VertexHandle origin,
        VertexHandle destination,
        out HalfEdgeHandle connector
    )
    {
        connector = HalfEdgeHandle.Null;
        foreach (HalfEdgeHandle edge in HalfEdges)
        {
            HalfEdge data = HalfEdges[edge];
            if (
                data.Origin == origin
                && HalfEdges.IsAlive(data.Twin)
                && HalfEdges[data.Twin].Origin == destination
            )
            {
                if (!data.Face.IsNull)
                    return false;
                connector = edge;
                return true;
            }
        }
        return true;
    }

    private VertexHandle[] SubdivideBridgeConnector(HalfEdgeHandle connector, int segments)
    {
        VertexHandle[] vertices = new VertexHandle[segments + 1];
        HalfEdge data = HalfEdges[connector];
        VertexHandle destination = HalfEdges[data.Twin].Origin;
        vertices[0] = data.Origin;
        vertices[segments] = destination;

        HalfEdgeHandle remaining = connector;
        for (int index = 1; index < segments; index++)
        {
            VertexHandle inserted = SplitEdge(remaining, 1f / (segments - index + 1));
            vertices[index] = inserted;
            remaining = FindDirectedBridgeEdge(inserted, destination);
            if (remaining.IsNull)
            {
                throw new InvalidOperationException(
                    "Split bridge connector did not retain its destination."
                );
            }
        }
        return vertices;
    }

    private HalfEdgeHandle FindDirectedBridgeEdge(VertexHandle origin, VertexHandle destination)
    {
        foreach (HalfEdgeHandle edge in HalfEdgesAroundVertex(origin))
        {
            HalfEdge data = HalfEdges[edge];
            if (HalfEdges.IsAlive(data.Twin) && HalfEdges[data.Twin].Origin == destination)
                return edge;
        }
        return HalfEdgeHandle.Null;
    }

    private bool TryGetBoundaryEdge(
        HalfEdgeHandle edge,
        out HalfEdgeHandle boundary,
        out FaceHandle adjacentFace
    )
    {
        boundary = HalfEdgeHandle.Null;
        adjacentFace = FaceHandle.Null;
        if (!HalfEdges.IsAlive(edge))
            return false;

        HalfEdge data = HalfEdges[edge];
        if (!HalfEdges.IsAlive(data.Twin) || data.Twin == edge)
            return false;
        HalfEdge twin = HalfEdges[data.Twin];
        if (twin.Twin != edge)
            return false;

        if (data.Face.IsNull && Faces.IsAlive(twin.Face))
        {
            boundary = edge;
            adjacentFace = twin.Face;
            return true;
        }
        if (twin.Face.IsNull && Faces.IsAlive(data.Face))
        {
            boundary = data.Twin;
            adjacentFace = data.Face;
            return true;
        }
        return false;
    }

    private Vector3 ComputeBoundaryFaceDirection(
        Vector3 origin,
        Vector3 destination,
        FaceHandle face
    )
    {
        Vector3 edgeDirection = Vector3.Normalize(destination - origin);
        Vector3 intoFace = ComputeFaceCentroid(face) - (origin + destination) * 0.5f;
        intoFace -= edgeDirection * Vector3.Dot(intoFace, edgeDirection);
        return intoFace.LengthSquared() > 1e-8f ? Vector3.Normalize(intoFace) : Vector3.Zero;
    }

    private static Vector3 EvaluateBridgeArc(
        Vector3 start,
        Vector3 end,
        Vector3 commonArchDirection,
        float t,
        float archAngleDegrees
    )
    {
        Vector3 chord = end - start;
        float length = chord.Length();
        Vector3 chordDirection = chord / length;
        Vector3 archDirection =
            commonArchDirection - chordDirection * Vector3.Dot(commonArchDirection, chordDirection);
        archDirection = Vector3.Normalize(archDirection);

        float angle = archAngleDegrees * (MathF.PI / 180f);
        if (angle < 1e-5f)
            return Vector3.Lerp(start, end, t);

        float halfAngle = angle * 0.5f;
        float radius = length / (2f * MathF.Sin(halfAngle));
        float sampleAngle = -halfAngle + angle * t;
        float along = radius * (MathF.Sin(sampleAngle) + MathF.Sin(halfAngle));
        float height = radius * (MathF.Cos(sampleAngle) - MathF.Cos(halfAngle));
        return start + chordDirection * along + archDirection * height;
    }

    private readonly record struct BridgePlan(
        VertexHandle FirstOrigin,
        VertexHandle FirstDestination,
        VertexHandle SecondOrigin,
        VertexHandle SecondDestination,
        Vector3 FirstOriginPosition,
        Vector3 FirstDestinationPosition,
        Vector3 SecondOriginPosition,
        Vector3 SecondDestinationPosition,
        Vector3 ArchDirection,
        int MaterialSlot,
        bool SourceHadInitializedUvs,
        HalfEdgeHandle LeftConnector,
        HalfEdgeHandle RightConnector
    );

    public readonly record struct BridgeEdgesResult(
        FaceHandle[] Faces,
        VertexHandle[] NewVertices,
        bool SourceHadInitializedUvs
    );
}
