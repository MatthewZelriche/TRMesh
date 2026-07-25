namespace TREditorSharp;

using TREditorSharp.Storage;

/// <summary>
/// Owns the reservations and complete component state needed to repeatedly move one local
/// topology domain between its state before and after an edit.
/// </summary>
public sealed class TopologyPatch : IDisposable
{
    private readonly HalfEdgeMesh _mesh;
    private readonly TopologyDelta _delta;
    private PatchSide _currentSide = PatchSide.After;
    private bool _disposed;

    /// <summary>Detached before/after data and affected handles owned by this patch.</summary>
    public TopologyDelta Delta => _delta;

    /// <summary>
    /// Creates a patch after its edit has completed. The mesh must currently match
    /// the delta's after state, and entities present only in its before state must
    /// remain reserved for the patch.
    /// </summary>
    internal TopologyPatch(HalfEdgeMesh mesh, TopologyDelta delta)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(delta);

        _mesh = mesh;
        _delta = delta;

        ValidateSchemas();
        ValidateCurrentState(_delta.After, _delta.Before);
    }

    public void ApplyBefore() => Apply(_delta.Before, PatchSide.Before);

    public void ApplyAfter() => Apply(_delta.After, PatchSide.After);

    public void Dispose()
    {
        if (_disposed)
            return;

        ReleaseReserved(_mesh.Vertices, _delta.Before.Vertices, _delta.After.Vertices);
        ReleaseReserved(_mesh.HalfEdges, _delta.Before.HalfEdges, _delta.After.HalfEdges);
        ReleaseReserved(_mesh.Faces, _delta.Before.Faces, _delta.After.Faces);
        _disposed = true;
    }

    private void Apply(TopologyPatchState target, PatchSide targetSide)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSchemas();

        TopologyPatchState source =
            _currentSide == PatchSide.Before ? _delta.Before : _delta.After;
        TopologyPatchState sourceOther =
            _currentSide == PatchSide.Before ? _delta.After : _delta.Before;
        ValidateCurrentState(source, sourceOther);

        if (_currentSide == targetSide)
            return;

        // First establish target liveness for the entire domain. Entry values are restored only
        // after every required handle is live, so connectivity may safely reference entities
        // restored later in this phase.
        ReserveAbsent(_mesh.Vertices, source.Vertices, target.Vertices);
        ReserveAbsent(_mesh.HalfEdges, source.HalfEdges, target.HalfEdges);
        ReserveAbsent(_mesh.Faces, source.Faces, target.Faces);

        RestoreMissing(_mesh.Vertices, target.Vertices, source.Vertices);
        RestoreMissing(_mesh.HalfEdges, target.HalfEdges, source.HalfEdges);
        RestoreMissing(_mesh.Faces, target.Faces, source.Faces);

        RestoreEntries(_mesh.Vertices, target.Vertices);
        RestoreEntries(_mesh.HalfEdges, target.HalfEdges);
        RestoreEntries(_mesh.Faces, target.Faces);

        _currentSide = targetSide;
    }

    private void ValidateSchemas()
    {
        ValidateSchemas(_mesh.Vertices, _delta.Before.Vertices);
        ValidateSchemas(_mesh.Vertices, _delta.After.Vertices);
        ValidateSchemas(_mesh.HalfEdges, _delta.Before.HalfEdges);
        ValidateSchemas(_mesh.HalfEdges, _delta.After.HalfEdges);
        ValidateSchemas(_mesh.Faces, _delta.Before.Faces);
        ValidateSchemas(_mesh.Faces, _delta.After.Faces);
    }

    private void ValidateCurrentState(TopologyPatchState expected, TopologyPatchState other)
    {
        ValidateCurrentState(_mesh.Vertices, expected.Vertices, other.Vertices);
        ValidateCurrentState(_mesh.HalfEdges, expected.HalfEdges, other.HalfEdges);
        ValidateCurrentState(_mesh.Faces, expected.Faces, other.Faces);
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

    private static void ValidateCurrentState<TTag, TConnectivity>(
        TopologyStorage<TTag, TConnectivity> storage,
        IReadOnlyList<EntitySnapshot<TTag>> expected,
        IReadOnlyList<EntitySnapshot<TTag>> other
    )
        where TTag : unmanaged
        where TConnectivity : unmanaged
    {
        Dictionary<Handle<TTag>, EntitySnapshot<TTag>> expectedByHandle = Index(expected);

        for (int i = 0; i < expected.Count; i++)
        {
            EntitySnapshot<TTag> expectedSnapshot = expected[i];
            if (!storage.IsAlive(expectedSnapshot.Handle))
                ThrowStateMismatch(expectedSnapshot.Handle);

            EntitySnapshot<TTag> actualSnapshot = storage.Capture(expectedSnapshot.Handle);
            if (!SnapshotsEqual(actualSnapshot, expectedSnapshot))
                ThrowStateMismatch(expectedSnapshot.Handle);
        }

        HashSet<Handle<TTag>> checkedAbsent = [];
        for (int i = 0; i < other.Count; i++)
        {
            Handle<TTag> handle = other[i].Handle;
            if (
                !expectedByHandle.ContainsKey(handle)
                && checkedAbsent.Add(handle)
                && !storage.IsReserved(handle)
            )
            {
                ThrowStateMismatch(handle);
            }
        }
    }

    private static void ReserveAbsent<TTag, TConnectivity>(
        TopologyStorage<TTag, TConnectivity> storage,
        IReadOnlyList<EntitySnapshot<TTag>> source,
        IReadOnlyList<EntitySnapshot<TTag>> target
    )
        where TTag : unmanaged
        where TConnectivity : unmanaged
    {
        Dictionary<Handle<TTag>, EntitySnapshot<TTag>> targetByHandle = Index(target);
        for (int i = 0; i < source.Count; i++)
        {
            if (!targetByHandle.ContainsKey(source[i].Handle))
                storage.CaptureAndReserve(source[i].Handle);
        }
    }

    private static void RestoreMissing<TTag, TConnectivity>(
        TopologyStorage<TTag, TConnectivity> storage,
        IReadOnlyList<EntitySnapshot<TTag>> target,
        IReadOnlyList<EntitySnapshot<TTag>> source
    )
        where TTag : unmanaged
        where TConnectivity : unmanaged
    {
        Dictionary<Handle<TTag>, EntitySnapshot<TTag>> sourceByHandle = Index(source);
        for (int i = 0; i < target.Count; i++)
        {
            EntitySnapshot<TTag> snapshot = target[i];
            if (!sourceByHandle.ContainsKey(snapshot.Handle))
                storage.RestoreReserved(snapshot);
        }
    }

    private static void RestoreEntries<TTag, TConnectivity>(
        TopologyStorage<TTag, TConnectivity> storage,
        IReadOnlyList<EntitySnapshot<TTag>> target
    )
        where TTag : unmanaged
        where TConnectivity : unmanaged
    {
        for (int i = 0; i < target.Count; i++)
            storage.RestoreEntries(target[i]);
    }

    private static void ReleaseReserved<TTag, TConnectivity>(
        TopologyStorage<TTag, TConnectivity> storage,
        IReadOnlyList<EntitySnapshot<TTag>> before,
        IReadOnlyList<EntitySnapshot<TTag>> after
    )
        where TTag : unmanaged
        where TConnectivity : unmanaged
    {
        HashSet<Handle<TTag>> released = [];
        Release(before);
        Release(after);

        void Release(IReadOnlyList<EntitySnapshot<TTag>> snapshots)
        {
            for (int i = 0; i < snapshots.Count; i++)
            {
                Handle<TTag> handle = snapshots[i].Handle;
                if (released.Add(handle) && storage.IsReserved(handle))
                    storage.ReleaseReserved(handle);
            }
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
                throw new ArgumentException(
                    $"Topology patch contains duplicate handle {snapshot.Handle}."
                );
        }
        return result;
    }

    private static bool SnapshotsEqual<TTag>(EntitySnapshot<TTag> left, EntitySnapshot<TTag> right)
        where TTag : unmanaged
    {
        if (left.Handle != right.Handle || left.ColumnSchema.Count != right.ColumnSchema.Count)
            return false;

        for (int i = 0; i < left.ColumnSchema.Count; i++)
        {
            if (left.ColumnSchema[i] != right.ColumnSchema[i])
                return false;
        }

        return left.ComponentData.Span.SequenceEqual(right.ComponentData.Span);
    }

    private static void ThrowStateMismatch<TTag>(Handle<TTag> handle)
        where TTag : unmanaged =>
        throw new InvalidOperationException(
            $"Current local topology state does not match the patch source state at handle {handle}."
        );

    private enum PatchSide
    {
        Before,
        After,
    }
}
