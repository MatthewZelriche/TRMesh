namespace TREditorSharp;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using TREditorSharp.Storage;

public partial class HalfEdgeMesh
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

    /// <summary>
    /// Iterate the half-edges that bound <paramref name="face"/> in the order they form
    /// the interior loop, starting from <see cref="Face.FirstHalfEdge"/> and stepping
    /// via <see cref="HalfEdge.Next"/> until the loop closes. If the face's <c>FirstHalfEdge</c>
    /// is the null handle, the iteration is empty.
    ///
    /// <para>
    /// <b>Iteration safety:</b> the enumerator reads half-edge connectivity via
    /// <see cref="TopologyStorage{TTag,TConnectivity}.GetUnsafeRef{T,TColumnTag}"/>, so the
    /// same lifetime rules apply: do not allocate, free, clear, or otherwise mutate the
    /// half-edge or face topology of this mesh while iterating. Mutating component data
    /// (other than connectivity) is fine.
    /// </para>
    ///
    /// <para>
    /// In Debug builds the enumerator validates each step (the start handle is live, every
    /// visited half-edge is alive and has matching <c>Face</c>) and throws
    /// <see cref="InvalidOperationException"/> on inconsistency. In Release the per-step
    /// validation is elided, but a hard safety cap of <c>HalfEdges.LiveCount + 1</c> visits
    /// is enforced to prevent infinite loops on a malformed loop; exceeding it also throws
    /// <see cref="InvalidOperationException"/>.
    /// </para>
    /// </summary>
    // TODO: add unit tests for HalfEdgesAroundFace
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FaceHalfEdgeLoopEnumerable HalfEdgesAroundFace(FaceHandle face) => new(this, face);

    /// <summary>
    /// <c>foreach</c>-friendly wrapper produced by
    /// <see cref="HalfEdgesAroundFace"/>.
    /// </summary>
    public readonly ref struct FaceHalfEdgeLoopEnumerable
    {
        private readonly HalfEdgeMesh _mesh;
        private readonly FaceHandle _face;

        internal FaceHalfEdgeLoopEnumerable(HalfEdgeMesh mesh, FaceHandle face)
        {
            _mesh = mesh;
            _face = face;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FaceHalfEdgeLoopEnumerator GetEnumerator() => new(_mesh, _face);
    }

    /// <summary>
    /// Ref-struct enumerator that yields half-edge handles forming the boundary loop
    /// of a face in interior (CCW) order. See <see cref="HalfEdgesAroundFace"/> for further
    /// information.
    /// </summary>
    public ref struct FaceHalfEdgeLoopEnumerator
    {
        private readonly HalfEdgeMesh _mesh;
        private readonly FaceHandle _face;
        private HalfEdgeHandle _start;
        private HalfEdgeHandle _current;
        private bool _started;
        private int _budget;

        internal FaceHalfEdgeLoopEnumerator(HalfEdgeMesh mesh, FaceHandle face)
        {
            _mesh = mesh;
            _face = face;
            _start = default;
            _current = default;
            _started = false;
            // Hard safety cap: a well-formed face loop must close within LiveCount visits.
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
                DebugValidateFaceIsLive(_mesh, _face);
                var first = _mesh.Faces[_face].FirstHalfEdge;
                if (first.IsNull)
                    return false;
                DebugValidateStep(_mesh, first, _face);
                if (--_budget < 0)
                    ThrowSafetyCap(_face);
                _start = first;
                _current = first;
                return true;
            }

            ref var cur = ref _mesh.HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(_current);
            var next = cur.Next;
            DebugValidateHalfEdgeLinkIsLive(_mesh, "Next", _current, next);

            if (next == _start)
                return false;

            DebugValidateStep(_mesh, next, _face);
            if (--_budget < 0)
                ThrowSafetyCap(_face);
            _current = next;
            return true;
        }

        [Conditional("DEBUG")]
        private static void DebugValidateFaceIsLive(HalfEdgeMesh mesh, FaceHandle f)
        {
            if (f.IsNull || !mesh.Faces.IsAlive(f))
                throw new InvalidOperationException(
                    $"HalfEdgesAroundFace: face {f} is null or not live."
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
                    $"HalfEdgesAroundFace: {field} link from {from} is null or dead (got {to})."
                );
        }

        [Conditional("DEBUG")]
        private static void DebugValidateStep(HalfEdgeMesh mesh, HalfEdgeHandle h, FaceHandle owner)
        {
            if (!mesh.HalfEdges.IsAlive(h))
                throw new InvalidOperationException(
                    $"HalfEdgesAroundFace: visited half-edge {h} is null or not live."
                );
            ref var he = ref mesh.HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(h);
            if (he.Face != owner)
                throw new InvalidOperationException(
                    $"HalfEdgesAroundFace: half-edge {h} has Face={he.Face}, expected {owner}; "
                        + "loop is malformed or a foreign edge was reached."
                );
        }

        private static void ThrowSafetyCap(FaceHandle f) =>
            throw new InvalidOperationException(
                $"HalfEdgesAroundFace: traversal exceeded HalfEdges.LiveCount around face {f}; "
                    + "the loop is likely malformed (non-closing)."
            );
    }
}
