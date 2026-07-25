namespace TREditorSharp;

using TREditorSharp.Storage;

/// <summary>
/// Immutable before/after state for one conservative local topology domain.
/// Raw component snapshots remain internal; consumers use the handle collections to determine
/// which mesh regions may differ between the two states.
/// </summary>
public sealed class TopologyDelta
{
    internal TopologyPatchState Before { get; }
    internal TopologyPatchState After { get; }

    public IReadOnlyList<VertexHandle> BeforeVertices { get; }
    public IReadOnlyList<VertexHandle> AfterVertices { get; }
    public IReadOnlyList<VertexHandle> AffectedVertices { get; }

    public IReadOnlyList<HalfEdgeHandle> BeforeHalfEdges { get; }
    public IReadOnlyList<HalfEdgeHandle> AfterHalfEdges { get; }
    public IReadOnlyList<HalfEdgeHandle> AffectedHalfEdges { get; }

    public IReadOnlyList<FaceHandle> BeforeFaces { get; }
    public IReadOnlyList<FaceHandle> AfterFaces { get; }
    public IReadOnlyList<FaceHandle> AffectedFaces { get; }

    internal TopologyDelta(TopologyPatchState before, TopologyPatchState after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        Before = before;
        After = after;

        BeforeVertices = Handles(before.Vertices);
        AfterVertices = Handles(after.Vertices);
        AffectedVertices = Union(BeforeVertices, AfterVertices);

        BeforeHalfEdges = Handles(before.HalfEdges);
        AfterHalfEdges = Handles(after.HalfEdges);
        AffectedHalfEdges = Union(BeforeHalfEdges, AfterHalfEdges);

        BeforeFaces = Handles(before.Faces);
        AfterFaces = Handles(after.Faces);
        AffectedFaces = Union(BeforeFaces, AfterFaces);
    }

    private static IReadOnlyList<Handle<TTag>> Handles<TTag>(
        IReadOnlyList<EntitySnapshot<TTag>> snapshots
    )
        where TTag : unmanaged
    {
        Handle<TTag>[] handles = new Handle<TTag>[snapshots.Count];
        for (int i = 0; i < snapshots.Count; i++)
            handles[i] = snapshots[i].Handle;
        return Array.AsReadOnly(handles);
    }

    private static IReadOnlyList<Handle<TTag>> Union<TTag>(
        IReadOnlyList<Handle<TTag>> before,
        IReadOnlyList<Handle<TTag>> after
    )
        where TTag : unmanaged
    {
        HashSet<Handle<TTag>> unique = [.. before, .. after];
        Handle<TTag>[] handles = unique.ToArray();
        Array.Sort(
            handles,
            static (left, right) =>
            {
                int index = left.Index.CompareTo(right.Index);
                return index != 0 ? index : left.Generation.CompareTo(right.Generation);
            }
        );
        return Array.AsReadOnly(handles);
    }
}
