using System.Runtime.CompilerServices;

namespace TRMesh.Mesh;

public unsafe partial class SpatialMesh
{
    // Circulates outgoing half-edges around a vertex, or boundary half-edges around a face.
    ref struct HalfEdgeRing
    {
        readonly SpatialMesh _mesh;
        readonly int _start;
        readonly bool _aroundVertex;
        int _current;

        HalfEdgeRing(SpatialMesh mesh, int start, bool aroundVertex)
        {
            _mesh = mesh;
            _start = start;
            _aroundVertex = aroundVertex;
            _current = INVALID_HANDLE;
        }

        public static HalfEdgeRing AroundVertex(SpatialMesh mesh, int vertex) =>
            new(mesh, mesh._vertices.Get(vertex).topology.OutgoingHalfEdge, aroundVertex: true);

        public static HalfEdgeRing AroundFace(SpatialMesh mesh, int face) =>
            new(mesh, mesh._faces.GetTopology(face).AdjacentHalfEdge, aroundVertex: false);

        public readonly int Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _current;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly HalfEdgeRing GetEnumerator() => this;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            if (_start == INVALID_HANDLE)
                return false;

            if (_current == INVALID_HANDLE)
            {
                _current = _start;
                return true;
            }

            int next = _aroundVertex
                ? _mesh.HalfEdgeRef(_mesh.HalfEdgeRef(_current).TwinHalfEdge).NextHalfEdge
                : _mesh.HalfEdgeRef(_current).NextHalfEdge;
            if (next == _start || next == INVALID_HANDLE)
                return false;

            _current = next;
            return true;
        }
    }

    int OppositeVertex(int halfEdge) =>
        HalfEdgeRef(HalfEdgeRef(halfEdge).TwinHalfEdge).SourceVertex;
}
