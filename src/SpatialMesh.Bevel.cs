namespace TREditorSharp;

using System.Numerics;

public partial class SpatialMesh
{
    private const float BevelWidthSafetyFactor = 1f - 1e-5f;

    /// <summary>
    /// Replace an interior edge with a chamfer face. Both endpoint vertices must have valence
    /// three, which gives the bevel an unambiguous single-segment termination.
    /// </summary>
    public BevelEdgeResult BevelEdge(HalfEdgeHandle edge, float width)
    {
        if (!(width > 0f) || !float.IsFinite(width))
            throw new ArgumentOutOfRangeException(nameof(width), "Bevel width must be positive.");
        if (!TryGetBevelNeighborhood(edge, out BevelNeighborhood neighborhood))
            throw new ArgumentException("Edge cannot be beveled.", nameof(edge));
        if (width > neighborhood.MaximumWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"Bevel width must not exceed {neighborhood.MaximumWidth}."
            );
        }

        Dictionary<(VertexHandle Endpoint, VertexHandle Neighbor), VertexHandle> cutVertices = [];
        foreach (BevelCut cut in neighborhood.Cuts)
        {
            Vector3 endpoint = GetVertexPosition(cut.Endpoint);
            Vector3 direction = Vector3.Normalize(GetVertexPosition(cut.Neighbor) - endpoint);
            cutVertices.Add((cut.Endpoint, cut.Neighbor), AddVertex(endpoint + direction * width));
        }

        List<FaceSnapshot> snapshots = [];
        foreach (FaceHandle face in neighborhood.AffectedFaces)
        {
            snapshots.Add(
                new FaceSnapshot(
                    face,
                    CollectFaceVertices(face),
                    GetFaceMaterialSlot(face),
                    AreFaceUvsInitialized(face)
                )
            );
        }

        foreach (FaceSnapshot snapshot in snapshots)
            RemoveFace(snapshot.Face);
        foreach (HalfEdgeHandle incidentEdge in neighborhood.IncidentEdges)
        {
            if (IsHalfEdgeAlive(incidentEdge))
                RemoveEdge(incidentEdge);
        }
        if (!RemoveVertex(neighborhood.Origin) || !RemoveVertex(neighborhood.Destination))
            throw new InvalidOperationException("Bevel endpoints did not become isolated.");

        List<FaceReplacement> rebuiltFaces = [];
        foreach (FaceSnapshot snapshot in snapshots)
        {
            VertexHandle[] vertices = RebuildBeveledFace(snapshot, neighborhood, cutVertices);
            FaceHandle replacement = AddFace(vertices);
            SetFaceMaterialSlot(replacement, snapshot.MaterialSlot);
            SetFaceUvsInitialized(replacement, false);
            rebuiltFaces.Add(
                new FaceReplacement(snapshot.Face, replacement, snapshot.HadInitializedUvs)
            );
        }

        VertexHandle originFirst = cutVertices[
            (neighborhood.Origin, neighborhood.OriginFirstNeighbor)
        ];
        VertexHandle destinationFirst = cutVertices[
            (neighborhood.Destination, neighborhood.DestinationFirstNeighbor)
        ];
        VertexHandle originSecond = cutVertices[
            (neighborhood.Origin, neighborhood.OriginSecondNeighbor)
        ];
        VertexHandle destinationSecond = cutVertices[
            (neighborhood.Destination, neighborhood.DestinationSecondNeighbor)
        ];
        FaceHandle bevelFace = AddFace(
            [destinationFirst, originFirst, originSecond, destinationSecond]
        );
        SetFaceMaterialSlot(bevelFace, neighborhood.BevelMaterialSlot);
        SetFaceUvsInitialized(bevelFace, false);

        return new BevelEdgeResult(
            bevelFace,
            rebuiltFaces.ToArray(),
            cutVertices.Values.ToArray(),
            neighborhood.BevelFaceHadInitializedUvs
        );
    }

    /// <summary>
    /// Return the largest safe width for a single-segment bevel, or <c>false</c> when the edge
    /// is not an interior edge with valence-three endpoints.
    /// </summary>
    public bool TryGetMaximumEdgeBevelWidth(HalfEdgeHandle edge, out float maximumWidth)
    {
        maximumWidth = 0f;
        if (!TryGetBevelNeighborhood(edge, out BevelNeighborhood neighborhood))
            return false;

        maximumWidth = neighborhood.MaximumWidth;
        return maximumWidth > 0f;
    }

    private bool TryGetBevelNeighborhood(HalfEdgeHandle edge, out BevelNeighborhood neighborhood)
    {
        neighborhood = default;
        if (!HalfEdges.IsAlive(edge))
            return false;

        // Treat the selected half-edge and its twin as the two directed sides of the bevel.
        // Both must belong to distinct live faces, since boundary edges have no closed strip.
        HalfEdge first = HalfEdges[edge];
        if (!HalfEdges.IsAlive(first.Twin) || first.Twin == edge)
            return false;

        HalfEdge second = HalfEdges[first.Twin];
        if (
            second.Twin != edge
            || !Faces.IsAlive(first.Face)
            || !Faces.IsAlive(second.Face)
            || first.Face == second.Face
            || !Vertices.IsAlive(first.Origin)
            || !Vertices.IsAlive(second.Origin)
        )
        {
            return false;
        }

        VertexHandle origin = first.Origin;
        VertexHandle destination = second.Origin;
        // With three incident edges, removing an endpoint leaves exactly two neighboring
        // edges to cut and one remaining face to bridge between those cuts.
        if (
            !TryCollectValenceThreeNeighborhood(origin, out HalfEdgeHandle[] originEdges)
            || !TryCollectValenceThreeNeighborhood(
                destination,
                out HalfEdgeHandle[] destinationEdges
            )
        )
        {
            return false;
        }

        // For A -> B, First.Prev leads into A and First.Next leads away from B. The twin
        // traverses the opposite face as B -> A and supplies the other two neighbors.
        VertexHandle originFirstNeighbor = HalfEdges[first.Prev].Origin;
        VertexHandle destinationFirstNeighbor = HalfEdges[first.Next].Twin.IsNull
            ? VertexHandle.Null
            : HalfEdges[HalfEdges[first.Next].Twin].Origin;
        VertexHandle originSecondNeighbor = HalfEdges[second.Next].Twin.IsNull
            ? VertexHandle.Null
            : HalfEdges[HalfEdges[second.Next].Twin].Origin;
        VertexHandle destinationSecondNeighbor = HalfEdges[second.Prev].Origin;
        BevelCut[] cuts =
        [
            new(origin, originFirstNeighbor),
            new(destination, destinationFirstNeighbor),
            new(origin, originSecondNeighbor),
            new(destination, destinationSecondNeighbor),
        ];
        if (!HasValidCuts(cuts))
            return false;

        HashSet<FaceHandle> affectedFaces = [];
        foreach (HalfEdgeHandle incidentEdge in originEdges.Concat(destinationEdges))
        {
            HalfEdge incident = HalfEdges[incidentEdge];
            HalfEdge incidentTwin = HalfEdges[incident.Twin];
            if (!Faces.IsAlive(incident.Face) || !Faces.IsAlive(incidentTwin.Face))
                return false;
            affectedFaces.Add(incident.Face);
            affectedFaces.Add(incidentTwin.Face);
        }

        float minimumNeighborLength = cuts.Min(cut =>
            Vector3.Distance(GetVertexPosition(cut.Endpoint), GetVertexPosition(cut.Neighbor))
        );
        float maximumWidth = minimumNeighborLength * BevelWidthSafetyFactor;
        if (!(maximumWidth > 0f) || !float.IsFinite(maximumWidth))
            return false;

        HashSet<HalfEdgeHandle> seenEdges = [];
        List<HalfEdgeHandle> incidentEdges = [];
        // Removal operates on undirected edges, so retain only one half of each twin pair.
        foreach (HalfEdgeHandle incidentEdge in originEdges.Concat(destinationEdges))
        {
            HalfEdgeHandle twin = HalfEdges[incidentEdge].Twin;
            if (!seenEdges.Add(incidentEdge) || !seenEdges.Add(twin))
                continue;
            incidentEdges.Add(incidentEdge);
        }

        neighborhood = new BevelNeighborhood(
            edge,
            first.Twin,
            first.Face,
            second.Face,
            origin,
            destination,
            originFirstNeighbor,
            destinationFirstNeighbor,
            originSecondNeighbor,
            destinationSecondNeighbor,
            maximumWidth,
            GetFaceMaterialSlot(first.Face),
            AreFaceUvsInitialized(first.Face),
            cuts,
            affectedFaces.ToArray(),
            incidentEdges.ToArray()
        );
        return true;
    }

    private bool TryCollectValenceThreeNeighborhood(VertexHandle vertex, out HalfEdgeHandle[] edges)
    {
        List<HalfEdgeHandle> collected = [];
        foreach (HalfEdgeHandle edge in HalfEdgesAroundVertex(vertex))
            collected.Add(edge);

        edges = collected.ToArray();
        return edges.Length == 3;
    }

    private bool HasValidCuts(BevelCut[] cuts)
    {
        HashSet<(VertexHandle, VertexHandle)> uniqueCuts = [];
        foreach (BevelCut cut in cuts)
        {
            if (
                cut.Neighbor.IsNull
                || !Vertices.IsAlive(cut.Neighbor)
                || cut.Endpoint == cut.Neighbor
                || !uniqueCuts.Add((cut.Endpoint, cut.Neighbor))
            )
            {
                return false;
            }

            Vector3 delta = GetVertexPosition(cut.Neighbor) - GetVertexPosition(cut.Endpoint);
            if (!(delta.LengthSquared() > 0f) || !float.IsFinite(delta.LengthSquared()))
                return false;
        }

        return true;
    }

    private static VertexHandle[] RebuildBeveledFace(
        FaceSnapshot snapshot,
        BevelNeighborhood neighborhood,
        IReadOnlyDictionary<
            (VertexHandle Endpoint, VertexHandle Neighbor),
            VertexHandle
        > cutVertices
    )
    {
        List<VertexHandle> rebuilt = [];
        for (int index = 0; index < snapshot.Vertices.Length; index++)
        {
            VertexHandle vertex = snapshot.Vertices[index];
            VertexHandle previous = snapshot.Vertices[
                (index - 1 + snapshot.Vertices.Length) % snapshot.Vertices.Length
            ];
            VertexHandle next = snapshot.Vertices[(index + 1) % snapshot.Vertices.Length];
            if (vertex != neighborhood.Origin && vertex != neighborhood.Destination)
            {
                rebuilt.Add(vertex);
                continue;
            }

            bool isFirstFace = snapshot.Face == neighborhood.FirstFace;
            bool isSecondFace = snapshot.Face == neighborhood.SecondFace;
            if (isFirstFace || isSecondFace)
            {
                VertexHandle neighbor =
                    previous == neighborhood.Origin || previous == neighborhood.Destination
                        ? next
                        : previous;
                rebuilt.Add(cutVertices[(vertex, neighbor)]);
                continue;
            }

            rebuilt.Add(cutVertices[(vertex, previous)]);
            rebuilt.Add(cutVertices[(vertex, next)]);
        }

        if (rebuilt.Count < 3 || rebuilt.Distinct().Count() != rebuilt.Count)
            throw new InvalidOperationException("Bevel would create a degenerate face.");
        return rebuilt.ToArray();
    }

    private readonly record struct BevelCut(VertexHandle Endpoint, VertexHandle Neighbor);

    private readonly record struct FaceSnapshot(
        FaceHandle Face,
        VertexHandle[] Vertices,
        int MaterialSlot,
        bool HadInitializedUvs
    );

    private readonly record struct BevelNeighborhood(
        HalfEdgeHandle Edge,
        HalfEdgeHandle Twin,
        FaceHandle FirstFace,
        FaceHandle SecondFace,
        VertexHandle Origin,
        VertexHandle Destination,
        VertexHandle OriginFirstNeighbor,
        VertexHandle DestinationFirstNeighbor,
        VertexHandle OriginSecondNeighbor,
        VertexHandle DestinationSecondNeighbor,
        float MaximumWidth,
        int BevelMaterialSlot,
        bool BevelFaceHadInitializedUvs,
        BevelCut[] Cuts,
        FaceHandle[] AffectedFaces,
        HalfEdgeHandle[] IncidentEdges
    );

    public readonly record struct FaceReplacement(
        FaceHandle SourceFace,
        FaceHandle ReplacementFace,
        bool SourceHadInitializedUvs
    );

    public readonly record struct BevelEdgeResult(
        FaceHandle BevelFace,
        FaceReplacement[] RebuiltFaces,
        VertexHandle[] NewVertices,
        bool BevelFaceSourceHadInitializedUvs
    );
}
