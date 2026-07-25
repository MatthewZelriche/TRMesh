namespace TREditorSharp;

using System.Numerics;
using TREditorSharp.Storage;

/// <summary>
/// Read-only sparse view of a <see cref="TopologyDelta"/>'s after state over a
/// <see cref="SpatialMesh"/> that remains in the delta's before state.
/// Unaffected entities read through to the base mesh; affected entities read from the delta.
/// </summary>
public sealed class SpatialMeshFork
{
    private readonly SpatialMesh _baseMesh;
    private readonly MeshRevision _baseRevision;
    private readonly Dictionary<VertexHandle, EntitySnapshot<VertexTag>> _afterVertices;
    private readonly Dictionary<HalfEdgeHandle, EntitySnapshot<HalfEdgeTag>> _afterHalfEdges;
    private readonly Dictionary<FaceHandle, EntitySnapshot<FaceTag>> _afterFaces;
    private readonly HashSet<VertexHandle> _affectedVertices;
    private readonly HashSet<HalfEdgeHandle> _affectedHalfEdges;
    private readonly HashSet<FaceHandle> _affectedFaces;

    public SpatialMesh BaseMesh => _baseMesh;

    public MeshRevision BaseRevision => _baseRevision;

    public TopologyDelta Delta { get; }

    /// <summary>Whether the base mesh still matches the state used to create this fork.</summary>
    public bool IsValid => _baseMesh.Revision == _baseRevision;

    /// <summary>
    /// Create a view of <paramref name="delta"/>'s after state. The base mesh must currently
    /// match the delta's before state throughout its affected domain.
    /// </summary>
    public SpatialMeshFork(SpatialMesh baseMesh, TopologyDelta delta)
    {
        ArgumentNullException.ThrowIfNull(baseMesh);
        ArgumentNullException.ThrowIfNull(delta);

        _baseMesh = baseMesh;
        Delta = delta;
        _afterVertices = Index(delta.After.Vertices);
        _afterHalfEdges = Index(delta.After.HalfEdges);
        _afterFaces = Index(delta.After.Faces);
        _affectedVertices = [.. delta.AffectedVertices];
        _affectedHalfEdges = [.. delta.AffectedHalfEdges];
        _affectedFaces = [.. delta.AffectedFaces];

        MeshRevision revision = baseMesh.Revision;
        ValidateSchemas(baseMesh, delta);
        ValidateBaseState(
            baseMesh.Vertices,
            delta.Before.Vertices,
            delta.After.Vertices
        );
        ValidateBaseState(
            baseMesh.HalfEdges,
            delta.Before.HalfEdges,
            delta.After.HalfEdges
        );
        ValidateBaseState(baseMesh.Faces, delta.Before.Faces, delta.After.Faces);
        if (baseMesh.Revision != revision)
        {
            throw new InvalidOperationException(
                "The base mesh changed while the spatial mesh fork was being created."
            );
        }

        _baseRevision = revision;
    }

    public void EnsureValid()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException(
                "The spatial mesh fork is stale because its base mesh has changed."
            );
        }
    }

    public bool IsVertexAlive(VertexHandle vertex)
    {
        EnsureValid();
        return _afterVertices.ContainsKey(vertex)
            || (!_affectedVertices.Contains(vertex) && _baseMesh.IsVertexAlive(vertex));
    }

    public bool IsHalfEdgeAlive(HalfEdgeHandle halfEdge)
    {
        EnsureValid();
        return _afterHalfEdges.ContainsKey(halfEdge)
            || (!_affectedHalfEdges.Contains(halfEdge) && _baseMesh.IsHalfEdgeAlive(halfEdge));
    }

    public bool IsFaceAlive(FaceHandle face)
    {
        EnsureValid();
        return _afterFaces.ContainsKey(face)
            || (!_affectedFaces.Contains(face) && _baseMesh.IsFaceAlive(face));
    }

    public HalfEdge GetHalfEdge(HalfEdgeHandle halfEdge)
    {
        EnsureValid();
        if (_afterHalfEdges.TryGetValue(halfEdge, out EntitySnapshot<HalfEdgeTag>? snapshot))
            return snapshot.GetComponent<HalfEdge, HalfEdge>();
        if (_affectedHalfEdges.Contains(halfEdge))
            ThrowDead(halfEdge);
        return _baseMesh.GetHalfEdge(halfEdge);
    }

    public Vector3 GetVertexPosition(VertexHandle vertex)
    {
        EnsureValid();
        if (_afterVertices.TryGetValue(vertex, out EntitySnapshot<VertexTag>? snapshot))
            return snapshot.GetComponent<Vector3, VertexPositionTag>();
        if (_affectedVertices.Contains(vertex))
            ThrowDead(vertex);
        return _baseMesh.GetVertexPosition(vertex);
    }

    public Vector2 GetFaceCornerUv(FaceCornerHandle corner)
    {
        HalfEdge halfEdge = GetHalfEdge(corner);
        if (halfEdge.Face.IsNull)
        {
            throw new ArgumentException(
                $"Half-edge {corner} is a boundary edge and does not represent a face corner.",
                nameof(corner)
            );
        }

        return _afterHalfEdges.TryGetValue(corner, out EntitySnapshot<HalfEdgeTag>? snapshot)
            ? snapshot.GetComponent<Vector2, FaceCornerUvTag>()
            : _baseMesh.GetFaceCornerUv(corner);
    }

    public int GetFaceMaterialSlot(FaceHandle face) =>
        (int)(GetFaceTextureState(face) & SpatialMesh.MaterialSlotMask);

    public bool AreFaceUvsInitialized(FaceHandle face) =>
        (GetFaceTextureState(face) & SpatialMesh.UvsInitializedMask) != 0;

    public Vector3 ComputeFaceNormal(FaceHandle face)
    {
        if (!IsFaceAlive(face))
            throw new ArgumentException($"Face {face} is not live.", nameof(face));

        FaceCornerHandle[] corners = CollectFaceCorners(face);
        Vector3[] positions = new Vector3[corners.Length];
        for (int i = 0; i < corners.Length; i++)
            positions[i] = GetVertexPosition(GetHalfEdge(corners[i]).Origin);
        return SpatialMesh.ComputeFaceNormal(positions);
    }

    public bool TriangulateFace(FaceHandle face, List<FaceCornerHandle> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!IsFaceAlive(face))
            return false;

        FaceCornerHandle[] corners = CollectFaceCorners(face);
        if (corners.Length < 3)
            return false;

        Vector3[] positions = new Vector3[corners.Length];
        for (int i = 0; i < corners.Length; i++)
            positions[i] = GetVertexPosition(GetHalfEdge(corners[i]).Origin);
        return SpatialMesh.TriangulateFaceCorners(corners, positions, output);
    }

    private uint GetFaceTextureState(FaceHandle face)
    {
        EnsureValid();
        if (_afterFaces.TryGetValue(face, out EntitySnapshot<FaceTag>? snapshot))
            return snapshot.GetComponent<uint, FaceTextureStateTag>();
        if (_affectedFaces.Contains(face))
            ThrowDead(face);
        return _baseMesh.Faces.GetComponent<uint, FaceTextureStateTag>(face);
    }

    private Face GetFace(FaceHandle face)
    {
        EnsureValid();
        if (_afterFaces.TryGetValue(face, out EntitySnapshot<FaceTag>? snapshot))
            return snapshot.GetComponent<Face, Face>();
        if (_affectedFaces.Contains(face))
            ThrowDead(face);
        return _baseMesh.Faces[face];
    }

    private FaceCornerHandle[] CollectFaceCorners(FaceHandle face)
    {
        Face connectivity = GetFace(face);
        FaceCornerHandle first = connectivity.FirstHalfEdge;
        if (first.IsNull)
            throw new InvalidOperationException($"Face {face} has no first half-edge.");

        List<FaceCornerHandle> corners = [];
        HashSet<FaceCornerHandle> visited = [];
        FaceCornerHandle current = first;
        while (visited.Add(current))
        {
            if (!IsHalfEdgeAlive(current))
            {
                throw new InvalidOperationException(
                    $"Face {face} references dead half-edge {current}."
                );
            }

            HalfEdge halfEdge = GetHalfEdge(current);
            if (halfEdge.Face != face)
            {
                throw new InvalidOperationException(
                    $"Half-edge {current} belongs to face {halfEdge.Face}, expected {face}."
                );
            }

            corners.Add(current);
            current = halfEdge.Next;
        }

        if (current != first)
            throw new InvalidOperationException($"Face {face} contains a non-closing half-edge loop.");
        return corners.ToArray();
    }

    private static void ValidateSchemas(SpatialMesh mesh, TopologyDelta delta)
    {
        ValidateSchemas(mesh.Vertices, delta.Before.Vertices);
        ValidateSchemas(mesh.Vertices, delta.After.Vertices);
        ValidateSchemas(mesh.HalfEdges, delta.Before.HalfEdges);
        ValidateSchemas(mesh.HalfEdges, delta.After.HalfEdges);
        ValidateSchemas(mesh.Faces, delta.Before.Faces);
        ValidateSchemas(mesh.Faces, delta.After.Faces);
    }

    private static void ValidateSchemas<TTag, TConnectivity>(
        TopologyStorage<TTag, TConnectivity> storage,
        IReadOnlyList<EntitySnapshot<TTag>> snapshots
    )
        where TTag : unmanaged
        where TConnectivity : unmanaged
    {
        for (int i = 0; i < snapshots.Count; i++)
            storage.ValidateSnapshotSchema(snapshots[i]);
    }

    private static void ValidateBaseState<TTag, TConnectivity>(
        TopologyStorage<TTag, TConnectivity> storage,
        IReadOnlyList<EntitySnapshot<TTag>> before,
        IReadOnlyList<EntitySnapshot<TTag>> after
    )
        where TTag : unmanaged
        where TConnectivity : unmanaged
    {
        HashSet<Handle<TTag>> beforeHandles = [];
        for (int i = 0; i < before.Count; i++)
        {
            EntitySnapshot<TTag> expected = before[i];
            beforeHandles.Add(expected.Handle);
            if (
                !storage.IsAlive(expected.Handle)
                || !storage.Capture(expected.Handle).StateEquals(expected)
            )
            {
                ThrowBaseMismatch(expected.Handle);
            }
        }

        for (int i = 0; i < after.Count; i++)
        {
            Handle<TTag> handle = after[i].Handle;
            if (!beforeHandles.Contains(handle) && storage.IsAlive(handle))
                ThrowBaseMismatch(handle);
        }
    }

    private static Dictionary<Handle<TTag>, EntitySnapshot<TTag>> Index<TTag>(
        IReadOnlyList<EntitySnapshot<TTag>> snapshots
    )
        where TTag : unmanaged
    {
        Dictionary<Handle<TTag>, EntitySnapshot<TTag>> result = new(snapshots.Count);
        for (int i = 0; i < snapshots.Count; i++)
        {
            EntitySnapshot<TTag> snapshot = snapshots[i];
            if (!result.TryAdd(snapshot.Handle, snapshot))
                throw new ArgumentException($"Topology delta contains duplicate handle {snapshot.Handle}.");
        }

        return result;
    }

    private static void ThrowDead<TTag>(Handle<TTag> handle)
        where TTag : unmanaged =>
        throw new InvalidOperationException($"Handle {handle} is not live in the fork.");

    private static void ThrowBaseMismatch<TTag>(Handle<TTag> handle)
        where TTag : unmanaged =>
        throw new InvalidOperationException(
            $"The base mesh does not match the topology delta's before state at handle {handle}."
        );
}
