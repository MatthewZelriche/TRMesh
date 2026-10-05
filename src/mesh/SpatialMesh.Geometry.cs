using System.Numerics;
using System.Runtime.InteropServices;

namespace TRMesh.Mesh;

public unsafe partial class SpatialMesh
{
    // Sets vertex positions as one validated operation. Topology is unchanged; the normal of every
    // face around a moved vertex is rebuilt.
    public void SetVertexPositions(ReadOnlySpan<int> vertices, ReadOnlySpan<Vector3> positions)
    {
        if (vertices.Length != positions.Length)
            throw new ArgumentException("Vertex and position counts must match.", nameof(positions));

        var uniqueVertices = new HashSet<int>();
        for (int index = 0; index < vertices.Length; index++)
        {
            int vertex = vertices[index];
            if (!IsVertexAlive(vertex))
                throw new ArgumentOutOfRangeException(nameof(vertices), "A vertex handle is not live.");
            if (!uniqueVertices.Add(vertex))
                throw new ArgumentException("Vertex handles must be unique.", nameof(vertices));
            Vector3 position = positions[index];
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
                throw new ArgumentException("Vertex positions must be finite.", nameof(positions));
        }

        MarkMutated();
        for (int index = 0; index < vertices.Length; index++)
        {
            _vertexRecorder.Capture(vertices[index]);
            _vertices.SetPosition(vertices[index], positions[index]);
        }

        var faces = new HashSet<int>();
        var facePositions = new List<Vector3>();
        foreach (int vertex in vertices)
        {
            foreach (int halfEdge in new HalfEdgesAroundVertex(this, vertex))
            {
                int face = HalfEdgeRef(halfEdge).AdjacentFace;
                if (face != INVALID_HANDLE && faces.Add(face))
                    RecomputeFaceNormal(face, facePositions);
            }
        }
    }

    private void RecomputeFaceNormal(int face, List<Vector3> positions)
    {
        positions.Clear();
        int start = _faces.GetTopology(face).AdjacentHalfEdge;
        int halfEdge = start;
        do
        {
            ref readonly HalfEdge topology = ref HalfEdgeRef(halfEdge);
            positions.Add(_vertices.GetPosition(topology.SourceVertex));
            halfEdge = topology.NextHalfEdge;
        } while (halfEdge != start);

        _faceRecorder.Capture(face);
        _faces.SetNormal(face, Util.Math.ComputeFaceNormal(CollectionsMarshal.AsSpan(positions)));
    }
}
