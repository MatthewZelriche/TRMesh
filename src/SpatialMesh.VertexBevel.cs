namespace TREditorSharp;

using System.Numerics;

public partial class SpatialMesh
{
    /// <summary>
    /// Truncate a closed-manifold vertex by cutting each incident edge and capping the resulting
    /// opening. The vertex may have any valence of at least three.
    /// </summary>
    /// <returns>
    /// Live post-edit handles plus source-face metadata needed to regenerate UVs. The supplied
    /// vertex and every source face in the result are no longer live.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="vertex"/> is not a live closed-manifold vertex with at least three unique
    /// incident edges and faces.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="width"/> is non-positive, non-finite, or exceeds
    /// <see cref="TryGetMaximumVertexBevelWidth"/>.
    /// </exception>
    public BevelVertexResult BevelVertex(VertexHandle vertex, float width)
    {
        if (!(width > 0f) || !float.IsFinite(width))
            throw new ArgumentOutOfRangeException(nameof(width), "Bevel width must be positive.");
        if (!TryGetVertexBevelNeighborhood(vertex, out VertexBevelNeighborhood neighborhood))
            throw new ArgumentException("Vertex cannot be beveled.", nameof(vertex));
        if (width > neighborhood.MaximumWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"Bevel width must not exceed {neighborhood.MaximumWidth}."
            );
        }

        Vector3 sourcePosition = GetVertexPosition(vertex);
        Dictionary<VertexHandle, VertexHandle> cutVertices = [];
        foreach (VertexHandle neighbor in neighborhood.OrderedNeighbors)
        {
            Vector3 direction = Vector3.Normalize(GetVertexPosition(neighbor) - sourcePosition);
            cutVertices.Add(neighbor, AddVertex(sourcePosition + direction * width));
        }

        List<VertexBevelFaceSnapshot> snapshots = [];
        foreach (FaceHandle face in neighborhood.AffectedFaces)
        {
            snapshots.Add(
                new VertexBevelFaceSnapshot(
                    face,
                    CollectFaceVertices(face),
                    GetFaceMaterialSlot(face),
                    AreFaceUvsInitialized(face)
                )
            );
        }

        foreach (VertexBevelFaceSnapshot snapshot in snapshots)
            RemoveFace(snapshot.Face);
        foreach (HalfEdgeHandle incidentEdge in neighborhood.IncidentEdges)
        {
            if (IsHalfEdgeAlive(incidentEdge))
                RemoveEdge(incidentEdge);
        }
        if (!RemoveVertex(vertex))
            throw new InvalidOperationException("Beveled vertex did not become isolated.");

        List<FaceReplacement> rebuiltFaces = [];
        foreach (VertexBevelFaceSnapshot snapshot in snapshots)
        {
            VertexHandle[] vertices = RebuildVertexBevelFace(snapshot, vertex, cutVertices);
            FaceHandle replacement = AddFace(vertices);
            SetFaceMaterialSlot(replacement, snapshot.MaterialSlot);
            SetFaceUvsInitialized(replacement, false);
            rebuiltFaces.Add(
                new FaceReplacement(snapshot.Face, replacement, snapshot.HadInitializedUvs)
            );
        }

        VertexHandle[] orderedCuts = neighborhood
            .OrderedNeighbors.Select(neighbor => cutVertices[neighbor])
            .ToArray();
        FaceHandle bevelFace = AddFace(orderedCuts);
        SetFaceMaterialSlot(bevelFace, neighborhood.BevelMaterialSlot);
        SetFaceUvsInitialized(bevelFace, false);

        return new BevelVertexResult(
            bevelFace,
            rebuiltFaces.ToArray(),
            orderedCuts,
            neighborhood.BevelFaceHadInitializedUvs
        );
    }

    /// <summary>
    /// Return the largest safe truncation width, or <c>false</c> when the vertex is not part of
    /// a closed manifold or has fewer than three incident edges.
    /// </summary>
    public bool TryGetMaximumVertexBevelWidth(VertexHandle vertex, out float maximumWidth)
    {
        maximumWidth = 0f;
        if (!TryGetVertexBevelNeighborhood(vertex, out VertexBevelNeighborhood neighborhood))
            return false;

        maximumWidth = neighborhood.MaximumWidth;
        return maximumWidth > 0f;
    }

    private bool TryGetVertexBevelNeighborhood(
        VertexHandle vertex,
        out VertexBevelNeighborhood neighborhood
    )
    {
        neighborhood = default;
        if (!Vertices.IsAlive(vertex))
            return false;

        List<HalfEdgeHandle> incidentEdges = [];
        List<VertexHandle> orderedNeighbors = [];
        HashSet<VertexHandle> uniqueNeighbors = [];
        HashSet<FaceHandle> affectedFaces = [];
        foreach (HalfEdgeHandle edge in HalfEdgesAroundVertex(vertex))
        {
            HalfEdge data = HalfEdges[edge];
            if (data.Origin != vertex || !HalfEdges.IsAlive(data.Twin) || data.Twin == edge)
            {
                return false;
            }

            HalfEdge twin = HalfEdges[data.Twin];
            if (
                twin.Twin != edge
                || !Vertices.IsAlive(twin.Origin)
                || twin.Origin == vertex
                || !Faces.IsAlive(data.Face)
                || !Faces.IsAlive(twin.Face)
                || data.Face == twin.Face
                || !uniqueNeighbors.Add(twin.Origin)
            )
            {
                return false;
            }

            incidentEdges.Add(edge);
            orderedNeighbors.Add(twin.Origin);
            affectedFaces.Add(data.Face);
            affectedFaces.Add(twin.Face);
        }

        if (orderedNeighbors.Count < 3 || affectedFaces.Count != orderedNeighbors.Count)
            return false;
        int totalOutgoingEdges = 0;
        foreach (HalfEdgeHandle edge in HalfEdges)
        {
            if (HalfEdges[edge].Origin == vertex)
                totalOutgoingEdges++;
        }
        if (totalOutgoingEdges != incidentEdges.Count)
            return false;

        foreach (FaceHandle face in affectedFaces)
        {
            VertexHandle[] faceVertices = CollectFaceVertices(face);
            if (faceVertices.Count(candidate => candidate == vertex) != 1)
                return false;
        }

        float minimumEdgeLength = orderedNeighbors.Min(neighbor =>
            Vector3.Distance(GetVertexPosition(vertex), GetVertexPosition(neighbor))
        );
        float maximumWidth = minimumEdgeLength * BevelWidthSafetyFactor;
        if (!(maximumWidth > 0f) || !float.IsFinite(maximumWidth))
            return false;

        FaceHandle materialSource = HalfEdges[incidentEdges[0]].Face;
        neighborhood = new VertexBevelNeighborhood(
            orderedNeighbors.ToArray(),
            affectedFaces.ToArray(),
            incidentEdges.ToArray(),
            maximumWidth,
            GetFaceMaterialSlot(materialSource),
            AreFaceUvsInitialized(materialSource)
        );
        return true;
    }

    private static VertexHandle[] RebuildVertexBevelFace(
        VertexBevelFaceSnapshot snapshot,
        VertexHandle sourceVertex,
        IReadOnlyDictionary<VertexHandle, VertexHandle> cutVertices
    )
    {
        List<VertexHandle> rebuilt = [];
        int sourceOccurrences = 0;
        for (int index = 0; index < snapshot.Vertices.Length; index++)
        {
            VertexHandle vertex = snapshot.Vertices[index];
            if (vertex != sourceVertex)
            {
                rebuilt.Add(vertex);
                continue;
            }

            sourceOccurrences++;
            VertexHandle previous = snapshot.Vertices[
                (index - 1 + snapshot.Vertices.Length) % snapshot.Vertices.Length
            ];
            VertexHandle next = snapshot.Vertices[(index + 1) % snapshot.Vertices.Length];
            rebuilt.Add(cutVertices[previous]);
            rebuilt.Add(cutVertices[next]);
        }

        if (
            sourceOccurrences != 1
            || rebuilt.Count < 3
            || rebuilt.Distinct().Count() != rebuilt.Count
        )
        {
            throw new InvalidOperationException("Vertex bevel would create a degenerate face.");
        }
        return rebuilt.ToArray();
    }

    private readonly record struct VertexBevelFaceSnapshot(
        FaceHandle Face,
        VertexHandle[] Vertices,
        int MaterialSlot,
        bool HadInitializedUvs
    );

    private readonly record struct VertexBevelNeighborhood(
        VertexHandle[] OrderedNeighbors,
        FaceHandle[] AffectedFaces,
        HalfEdgeHandle[] IncidentEdges,
        float MaximumWidth,
        int BevelMaterialSlot,
        bool BevelFaceHadInitializedUvs
    );

    /// <summary>
    /// Describes topology created by <see cref="BevelVertex"/>. Every returned face and vertex
    /// handle is live in the post-edit mesh; generated UVs are intentionally uninitialized.
    /// </summary>
    public readonly record struct BevelVertexResult(
        FaceHandle BevelFace,
        FaceReplacement[] RebuiltFaces,
        VertexHandle[] NewVertices,
        bool BevelFaceSourceHadInitializedUvs
    );
}
