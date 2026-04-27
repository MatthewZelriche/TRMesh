namespace TREditorSharp;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using TREditorSharp.Storage;

public sealed partial class HalfEdgeMesh
{
    /// <summary>
    /// Iterate the outgoing half-edges around <paramref name="vertex"/> in
    /// counter-clockwise order.
    ///
    /// <para>
    /// Each yielded handle has <see cref="HalfEdge.Origin"/> equal to <paramref name="vertex"/>.
    /// If <c>OutgoingHalfEdge</c> is the null handle, the iteration is empty.
    /// </para>
    ///
    /// <para>
    /// <b>Iteration safety:</b> the enumerator reads half-edge connectivity via
    /// <see cref="TopologyStorage{TTag,TConnectivity}.GetUnsafeRef{T,TColumnTag}"/>, so the same
    /// lifetime rules apply: do not allocate, free, clear, or otherwise mutate the
    /// half-edge or vertex topology of this mesh while iterating. Mutating component
    /// data (other than connectivity) is fine.
    /// </para>
    ///
    /// <para>
    /// In Debug builds the enumerator validates each step (the start handle is live,
    /// every visited half-edge has matching <c>Origin</c>, <c>Prev</c> and <c>Twin</c>
    /// are live) and throws <see cref="InvalidOperationException"/> on inconsistency.
    /// In Release the per-step validation is elided, but a hard safety cap of
    /// <c>HalfEdges.LiveCount + 1</c> visits is enforced to prevent infinite loops on
    /// a malformed ring; exceeding it also throws <see cref="InvalidOperationException"/>.
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HalfEdgeRingEnumerable HalfEdgesAroundVertex(VertexHandle vertex) => new(this, vertex);

    /// <summary>
    /// <c>foreach</c>-friendly wrapper produced by
    /// <see cref="HalfEdgesAroundVertex"/>.
    /// </summary>
    public readonly ref struct HalfEdgeRingEnumerable
    {
        private readonly HalfEdgeMesh _mesh;
        private readonly VertexHandle _vertex;

        internal HalfEdgeRingEnumerable(HalfEdgeMesh mesh, VertexHandle vertex)
        {
            _mesh = mesh;
            _vertex = vertex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HalfEdgeRingEnumerator GetEnumerator() => new(_mesh, _vertex);
    }

    /// <summary>
    /// Ref-struct enumerator that yields outgoing half-edge handles around a vertex in
    /// CCW order. See <see cref="HalfEdgesAroundVertex"/> for further information.
    /// </summary>
    public ref struct HalfEdgeRingEnumerator
    {
        private readonly HalfEdgeMesh _mesh;
        private readonly VertexHandle _vertex;
        private HalfEdgeHandle _start;
        private HalfEdgeHandle _current;
        private bool _started;
        private int _budget;

        internal HalfEdgeRingEnumerator(HalfEdgeMesh mesh, VertexHandle vertex)
        {
            _mesh = mesh;
            _vertex = vertex;
            _start = default;
            _current = default;
            _started = false;
            // Hard safety cap: a well-formed ring must close within LiveCount visits.
            // The +1 lets MoveNext yield the LiveCount-th edge before tripping the cap.
            _budget = mesh.HalfEdges.LiveCount + 1;
        }

        public readonly HalfEdgeHandle Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _current;
        }

        public bool MoveNext()
        {
            if (!_started)
            {
                _started = true;
                DebugValidateVertexIsLive(_mesh, _vertex);
                var first = _mesh.Vertices[_vertex].OutgoingHalfEdge;
                if (first.IsNull)
                    return false;
                DebugValidateStep(_mesh, first, _vertex);
                if (--_budget < 0)
                    ThrowSafetyCap(_vertex);
                _start = first;
                _current = first;
                return true;
            }

            ref var cur = ref _mesh.HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(_current);
            var prev = cur.Prev;
            DebugValidateHalfEdgeLinkIsLive(_mesh, "Prev", _current, prev);
            ref var prevHe = ref _mesh.HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(prev);
            var next = prevHe.Twin;
            DebugValidateHalfEdgeLinkIsLive(_mesh, "Twin", prev, next);

            if (next == _start)
                return false;

            DebugValidateStep(_mesh, next, _vertex);
            if (--_budget < 0)
                ThrowSafetyCap(_vertex);
            _current = next;
            return true;
        }

        [Conditional("DEBUG")]
        private static void DebugValidateVertexIsLive(HalfEdgeMesh mesh, VertexHandle v)
        {
            if (v.IsNull || !mesh.Vertices.IsAlive(v))
                throw new InvalidOperationException(
                    $"HalfEdgesAroundVertex: vertex {v} is null or not live."
                );
        }

        [Conditional("DEBUG")]
        private static void DebugValidateHalfEdgeLinkIsLive(
            HalfEdgeMesh mesh,
            string field,
            HalfEdgeHandle from,
            HalfEdgeHandle to
        )
        {
            if (to.IsNull || !mesh.HalfEdges.IsAlive(to))
                throw new InvalidOperationException(
                    $"HalfEdgesAroundVertex: {field} link from {from} is null or dead (got {to})."
                );
        }

        [Conditional("DEBUG")]
        private static void DebugValidateStep(
            HalfEdgeMesh mesh,
            HalfEdgeHandle h,
            VertexHandle owner
        )
        {
            if (!mesh.HalfEdges.IsAlive(h))
                throw new InvalidOperationException(
                    $"HalfEdgesAroundVertex: visited half-edge {h} is null or not live."
                );
            ref var he = ref mesh.HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(h);
            if (he.Origin != owner)
                throw new InvalidOperationException(
                    $"HalfEdgesAroundVertex: half-edge {h} has Origin={he.Origin}, expected {owner}; "
                        + "ring is malformed or a foreign edge was reached."
                );
        }

        private static void ThrowSafetyCap(VertexHandle v) =>
            throw new InvalidOperationException(
                $"HalfEdgesAroundVertex: traversal exceeded HalfEdges.LiveCount around vertex {v}; "
                    + "the ring is likely malformed (non-closing or non-manifold)."
            );
    }
}
