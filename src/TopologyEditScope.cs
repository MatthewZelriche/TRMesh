namespace TREditorSharp;

using TREditorSharp.Storage;

/// <summary>
/// Tracks one topology mutation and converts its before/after local states into a reusable patch.
/// Disposing without committing rolls the mutation back.
/// </summary>
public sealed class TopologyEditScope
    : IDisposable,
        ITopologyStorageEditTracker<VertexTag>,
        ITopologyStorageEditTracker<HalfEdgeTag>,
        ITopologyStorageEditTracker<FaceTag>
{
    private readonly HalfEdgeMesh _mesh;
    private readonly Dictionary<VertexHandle, EntitySnapshot<VertexTag>> _beforeVertices;
    private readonly Dictionary<HalfEdgeHandle, EntitySnapshot<HalfEdgeTag>> _beforeHalfEdges;
    private readonly Dictionary<FaceHandle, EntitySnapshot<FaceTag>> _beforeFaces;
    private readonly HashSet<VertexHandle> _allocatedVertices = [];
    private readonly HashSet<HalfEdgeHandle> _allocatedHalfEdges = [];
    private readonly HashSet<FaceHandle> _allocatedFaces = [];
#if DEBUG
    private readonly TopologyPatchState _debugInitialFullState;
#endif
    private bool _tracking = true;
    private bool _completed;

    internal TopologyEditScope(HalfEdgeMesh mesh, TopologyPatchState before)
    {
        _mesh = mesh;
        _beforeVertices = Index(before.Vertices);
        _beforeHalfEdges = Index(before.HalfEdges);
        _beforeFaces = Index(before.Faces);
#if DEBUG
        _debugInitialFullState = mesh.CaptureFullTopologyPatchState();
#endif

        mesh.Vertices.BeginEditTracking(this);
        mesh.HalfEdges.BeginEditTracking(this);
        mesh.Faces.BeginEditTracking(this);
    }

    /// <summary>
    /// Finish the edit and return a patch that owns its reserved handles and can repeatedly
    /// apply the captured before and after states.
    /// </summary>
    public TopologyPatch Commit()
    {
        ThrowIfCompleted();
        StopTracking();

#if DEBUG
        try
        {
            ValidateUnaffectedEntitiesUnchanged();
        }
        catch
        {
            RollBackToDebugInitialState();
            Complete();
            throw;
        }
#endif

        ReleaseCreatedAndRemoved(_mesh.Vertices, _allocatedVertices, _beforeVertices);
        ReleaseCreatedAndRemoved(_mesh.HalfEdges, _allocatedHalfEdges, _beforeHalfEdges);
        ReleaseCreatedAndRemoved(_mesh.Faces, _allocatedFaces, _beforeFaces);

        TopologyPatchState before = BuildBeforeState();
        TopologyPatchState after = BuildCurrentState();
        TopologyPatch patch = new(_mesh, before, after);
        Complete();
        return patch;
    }

    public void Dispose()
    {
        if (_completed)
            return;

        StopTracking();
        ReleaseCreatedAndRemoved(_mesh.Vertices, _allocatedVertices, _beforeVertices);
        ReleaseCreatedAndRemoved(_mesh.HalfEdges, _allocatedHalfEdges, _beforeHalfEdges);
        ReleaseCreatedAndRemoved(_mesh.Faces, _allocatedFaces, _beforeFaces);

        TopologyPatchState before = BuildBeforeState();
        TopologyPatchState after = BuildCurrentState();
        using (TopologyPatch rollback = new(_mesh, before, after))
            rollback.ApplyBefore();
        Complete();
    }

    void ITopologyStorageEditTracker<VertexTag>.OnAllocated(VertexHandle handle) =>
        _allocatedVertices.Add(handle);

    void ITopologyStorageEditTracker<HalfEdgeTag>.OnAllocated(HalfEdgeHandle handle) =>
        _allocatedHalfEdges.Add(handle);

    void ITopologyStorageEditTracker<FaceTag>.OnAllocated(FaceHandle handle) =>
        _allocatedFaces.Add(handle);

    void ITopologyStorageEditTracker<VertexTag>.OnReserved(EntitySnapshot<VertexTag> snapshot) =>
        AddRemovedSnapshot(snapshot, _allocatedVertices, _beforeVertices);

    void ITopologyStorageEditTracker<HalfEdgeTag>.OnReserved(
        EntitySnapshot<HalfEdgeTag> snapshot
    ) => AddRemovedSnapshot(snapshot, _allocatedHalfEdges, _beforeHalfEdges);

    void ITopologyStorageEditTracker<FaceTag>.OnReserved(EntitySnapshot<FaceTag> snapshot) =>
        AddRemovedSnapshot(snapshot, _allocatedFaces, _beforeFaces);

    private TopologyPatchState BuildBeforeState() =>
        new(
            _beforeVertices.Values.ToArray(),
            _beforeHalfEdges.Values.ToArray(),
            _beforeFaces.Values.ToArray()
        );

    private TopologyPatchState BuildCurrentState() =>
        new(
            CaptureCurrent(_mesh.Vertices, _beforeVertices.Keys, _allocatedVertices),
            CaptureCurrent(_mesh.HalfEdges, _beforeHalfEdges.Keys, _allocatedHalfEdges),
            CaptureCurrent(_mesh.Faces, _beforeFaces.Keys, _allocatedFaces)
        );

    private void StopTracking()
    {
        if (!_tracking)
            return;

        _mesh.Vertices.EndEditTracking(this);
        _mesh.HalfEdges.EndEditTracking(this);
        _mesh.Faces.EndEditTracking(this);
        _tracking = false;
    }

    private void Complete()
    {
        _completed = true;
        _mesh.CompleteTopologyEdit(this);
    }

    private void ThrowIfCompleted()
    {
        if (_completed)
            throw new InvalidOperationException("The topology edit has already completed.");
    }

#if DEBUG
    private void ValidateUnaffectedEntitiesUnchanged()
    {
        ValidateUnaffectedEntitiesUnchanged(
            _mesh.Vertices,
            _debugInitialFullState.Vertices,
            _beforeVertices
        );
        ValidateUnaffectedEntitiesUnchanged(
            _mesh.HalfEdges,
            _debugInitialFullState.HalfEdges,
            _beforeHalfEdges
        );
        ValidateUnaffectedEntitiesUnchanged(
            _mesh.Faces,
            _debugInitialFullState.Faces,
            _beforeFaces
        );
    }

    private void RollBackToDebugInitialState()
    {
        ReleaseCreatedAndRemoved(_mesh.Vertices, _allocatedVertices, _beforeVertices);
        ReleaseCreatedAndRemoved(_mesh.HalfEdges, _allocatedHalfEdges, _beforeHalfEdges);
        ReleaseCreatedAndRemoved(_mesh.Faces, _allocatedFaces, _beforeFaces);

        TopologyPatchState currentFullState = _mesh.CaptureFullTopologyPatchState();
        using TopologyPatch rollback = new(_mesh, _debugInitialFullState, currentFullState);
        rollback.ApplyBefore();
    }

    private static void ValidateUnaffectedEntitiesUnchanged<TTag, TConnectivity>(
        TopologyStorage<TTag, TConnectivity> storage,
        IReadOnlyList<EntitySnapshot<TTag>> initialSnapshots,
        Dictionary<Handle<TTag>, EntitySnapshot<TTag>> patchBefore
    )
        where TTag : unmanaged
        where TConnectivity : unmanaged
    {
        for (int i = 0; i < initialSnapshots.Count; i++)
        {
            EntitySnapshot<TTag> initial = initialSnapshots[i];
            if (patchBefore.ContainsKey(initial.Handle))
                continue;

            if (!storage.IsAlive(initial.Handle))
                ThrowUnderCaptured(initial.Handle);

            EntitySnapshot<TTag> current = storage.Capture(initial.Handle);
            if (!SnapshotsEqual(initial, current))
                ThrowUnderCaptured(initial.Handle);
        }
    }

    private static bool SnapshotsEqual<TTag>(EntitySnapshot<TTag> left, EntitySnapshot<TTag> right)
        where TTag : unmanaged =>
        left.Handle == right.Handle
        && left.ComponentData.Span.SequenceEqual(right.ComponentData.Span);

    private static void ThrowUnderCaptured<TTag>(Handle<TTag> handle)
        where TTag : unmanaged =>
        throw new InvalidOperationException(
            $"Topology edit modified pre-existing entity {handle} outside the captured patch domain. "
                + "Include a more conservative affected-vertex set."
        );
#endif

    private static void AddRemovedSnapshot<TTag>(
        EntitySnapshot<TTag> snapshot,
        HashSet<Handle<TTag>> allocated,
        Dictionary<Handle<TTag>, EntitySnapshot<TTag>> before
    )
        where TTag : unmanaged
    {
        if (!allocated.Contains(snapshot.Handle))
            before.TryAdd(snapshot.Handle, snapshot);
    }

    private static void ReleaseCreatedAndRemoved<TTag, TConnectivity>(
        TopologyStorage<TTag, TConnectivity> storage,
        HashSet<Handle<TTag>> allocated,
        Dictionary<Handle<TTag>, EntitySnapshot<TTag>> before
    )
        where TTag : unmanaged
        where TConnectivity : unmanaged
    {
        foreach (Handle<TTag> handle in allocated)
        {
            if (!before.ContainsKey(handle) && storage.IsReserved(handle))
                storage.ReleaseReserved(handle);
        }
    }

    private static EntitySnapshot<TTag>[] CaptureCurrent<TTag, TConnectivity>(
        TopologyStorage<TTag, TConnectivity> storage,
        IEnumerable<Handle<TTag>> beforeHandles,
        IEnumerable<Handle<TTag>> allocatedHandles
    )
        where TTag : unmanaged
        where TConnectivity : unmanaged
    {
        HashSet<Handle<TTag>> included = [.. beforeHandles, .. allocatedHandles];
        List<EntitySnapshot<TTag>> snapshots = [];
        foreach (Handle<TTag> handle in storage)
        {
            if (included.Contains(handle))
                snapshots.Add(storage.Capture(handle));
        }
        return snapshots.ToArray();
    }

    private static Dictionary<Handle<TTag>, EntitySnapshot<TTag>> Index<TTag>(
        IReadOnlyList<EntitySnapshot<TTag>> snapshots
    )
        where TTag : unmanaged
    {
        Dictionary<Handle<TTag>, EntitySnapshot<TTag>> result = new(snapshots.Count);
        for (int i = 0; i < snapshots.Count; i++)
            result.Add(snapshots[i].Handle, snapshots[i]);
        return result;
    }
}
