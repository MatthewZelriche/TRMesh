using System.Runtime.CompilerServices;

namespace TRMesh.Mesh;

public partial class SpatialMesh
{
    // Circulates the outgoing half-edges around a vertex.
    ref struct HalfEdgesAroundVertex
    {
        readonly SpatialMesh _mesh;
        readonly int _start;
        int _current;

        public HalfEdgesAroundVertex(SpatialMesh mesh, int vertex)
        {
            _mesh = mesh;
            _start = mesh._vertices.Get(vertex).topology.OutgoingHalfEdge;
            _current = INVALID_HANDLE;
        }

        public readonly int Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _current;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly HalfEdgesAroundVertex GetEnumerator() => this;

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

            int twin = _mesh.HalfEdgeRef(_current).TwinHalfEdge;
            int next = _mesh.HalfEdgeRef(twin).NextHalfEdge;
            if (next == _start || next == INVALID_HANDLE)
                return false;

            _current = next;
            return true;
        }
    }
}
