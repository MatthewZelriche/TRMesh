using System.Numerics;

namespace TRMesh.Mesh;

public readonly record struct FaceExtrusion(int[] Faces, int[] Vertices);

public unsafe partial class SpatialMesh
{
    private static readonly Vector2[] ExtrudeWallUvs =
    [
        Vector2.Zero,
        Vector2.UnitX,
        Vector2.One,
        Vector2.UnitY,
    ];

    // Replaces each edge-connected selected face region with a translated copy and joins every
    // open rim to its original boundary. Disconnected regions are extruded independently, even
    // when they meet at only a vertex.
    public FaceExtrusion ExtrudeFaces(ReadOnlySpan<int> faces, Vector3 offset)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(faces.Length, 1);
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y) || !float.IsFinite(offset.Z))
            throw new ArgumentException("The extrusion offset must be finite.", nameof(offset));

        int[] selectedFaces = faces.ToArray();
        var selected = new HashSet<int>(selectedFaces.Length);
        foreach (int face in selectedFaces)
        {
            if (!IsFaceAlive(face))
                throw new ArgumentException("A selected face handle is not live.", nameof(faces));
            if (!selected.Add(face))
                throw new ArgumentException("Selected face handles must be unique.", nameof(faces));
        }

        Dictionary<int, FaceSnapshot> snapshots = CaptureFaces(selectedFaces);
        // BFS regions so later AddFace calls grow each copied manifold from existing edges.
        var regions = new List<(List<int> Faces, Dictionary<int, int> Copies)>();
        var copiedVertices = new List<int>();
        var remaining = new HashSet<int>(selectedFaces);
        while (remaining.Count != 0)
        {
            int seed = remaining.First();
            remaining.Remove(seed);
            var regionFaces = new List<int>();
            var copies = new Dictionary<int, int>();
            var queue = new Queue<int>();
            queue.Enqueue(seed);
            while (queue.TryDequeue(out int face))
            {
                regionFaces.Add(face);
                foreach (int vertex in snapshots[face].Vertices)
                {
                    if (copies.ContainsKey(vertex))
                        continue;
                    int copy = AddVertex(GetVertexData(vertex).position + offset);
                    copies.Add(vertex, copy);
                    copiedVertices.Add(copy);
                }
                foreach (int halfEdge in snapshots[face].HalfEdges)
                {
                    int adjacent = HalfEdgeRef(HalfEdgeRef(halfEdge).TwinHalfEdge).AdjacentFace;
                    if (selected.Contains(adjacent) && remaining.Remove(adjacent))
                        queue.Enqueue(adjacent);
                }
            }
            regions.Add((regionFaces, copies));
        }

        var boundary = new List<(Dictionary<int, int> Copies, int HalfEdge)>();
        var interiorEdges = new HashSet<int>();
        var affectedVertices = new HashSet<int>();
        foreach ((List<int> regionFaces, Dictionary<int, int> copies) in regions)
        {
            foreach (int face in regionFaces)
            {
                foreach (int halfEdge in snapshots[face].HalfEdges)
                {
                    HalfEdge edge = HalfEdgeRef(halfEdge);
                    affectedVertices.Add(edge.SourceVertex);
                    int adjacent = HalfEdgeRef(edge.TwinHalfEdge).AdjacentFace;
                    if (selected.Contains(adjacent))
                        interiorEdges.Add(Math.Min(halfEdge, edge.TwinHalfEdge));
                    else
                        boundary.Add((copies, halfEdge));
                }
            }
        }

        // Keep open-rim half-edges alive for wall construction; only interior wires go away.
        MarkMutated();
        foreach (FaceSnapshot snapshot in snapshots.Values)
        {
            foreach (int halfEdge in snapshot.HalfEdges)
                HalfEdgeMut(halfEdge).AdjacentFace = INVALID_HANDLE;
        }
        foreach (int face in selectedFaces)
            _faceRecorder.RemoveAt(face);
        foreach (int halfEdge in interiorEdges)
        {
            if (IsHalfEdgeAlive(halfEdge))
                RemoveWireEdge(halfEdge, affectedVertices);
        }
        ResetOutgoingHalfEdges(affectedVertices);
        foreach (int vertex in affectedVertices)
        {
            if (IsVertexAlive(vertex) && VertexRef(vertex).OutgoingHalfEdge == INVALID_HANDLE)
                _vertexRecorder.RemoveAt(vertex);
        }

        foreach ((Dictionary<int, int> copies, int halfEdge) in boundary)
        {
            HalfEdge edge = HalfEdgeRef(halfEdge);
            int source = edge.SourceVertex;
            int target = HalfEdgeRef(edge.TwinHalfEdge).SourceVertex;
            AddFace([source, target, copies[target], copies[source]], ExtrudeWallUvs);
        }

        var capFaces = new Dictionary<int, int>(selectedFaces.Length);
        foreach ((List<int> regionFaces, Dictionary<int, int> copies) in regions)
        {
            foreach (int face in regionFaces)
            {
                FaceSnapshot snapshot = snapshots[face];
                var capVertices = new int[snapshot.Vertices.Length];
                for (int index = 0; index < capVertices.Length; index++)
                    capVertices[index] = copies[snapshot.Vertices[index]];
                capFaces.Add(face, AddFace(capVertices, snapshot.Uvs));
            }
        }

        return new FaceExtrusion(
            Array.ConvertAll(selectedFaces, face => capFaces[face]),
            [.. copiedVertices]
        );
    }

    private Dictionary<int, FaceSnapshot> CaptureFaces(int[] faces)
    {
        var snapshots = new Dictionary<int, FaceSnapshot>(faces.Length);
        foreach (int face in faces)
        {
            var halfEdges = new List<int>();
            var vertices = new List<int>();
            var uvs = new List<Vector2>();
            foreach (int halfEdge in HalfEdgeRing.AroundFace(this, face))
            {
                HalfEdgeData corner = GetHalfEdgeData(halfEdge);
                halfEdges.Add(halfEdge);
                vertices.Add(corner.topology.SourceVertex);
                uvs.Add(corner.UV);
            }
            snapshots.Add(face, new FaceSnapshot([.. halfEdges], [.. vertices], [.. uvs]));
        }
        return snapshots;
    }

    private readonly record struct FaceSnapshot(int[] HalfEdges, int[] Vertices, Vector2[] Uvs);
}
