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
            if (!IsFinite(position))
                throw new ArgumentException("Vertex positions must be finite.", nameof(positions));
        }

        MarkMutated();
        for (int index = 0; index < vertices.Length; index++)
        {
            _vertexRecorder.Capture(vertices[index]);
            _vertices.SetPosition(vertices[index], positions[index]);
        }

        RecomputeNormalsAroundVertices(vertices);
    }

    private void RecomputeNormalsAroundVertices(ReadOnlySpan<int> vertices)
    {
        var faces = new HashSet<int>();
        var positions = new List<Vector3>();
        foreach (int vertex in vertices)
        {
            foreach (int halfEdge in HalfEdgeRing.AroundVertex(this, vertex))
            {
                int face = HalfEdgeRef(halfEdge).AdjacentFace;
                if (face != INVALID_HANDLE && faces.Add(face))
                    RecomputeFaceNormal(face, positions);
            }
        }
    }

    private void RecomputeFaceNormal(int face, List<Vector3> positions)
    {
        positions.Clear();
        foreach (int halfEdge in HalfEdgeRing.AroundFace(this, face))
            positions.Add(_vertices.GetPosition(HalfEdgeRef(halfEdge).SourceVertex));

        _faceRecorder.Capture(face);
        _faces.SetNormal(face, Util.Math.ComputeFaceNormal(CollectionsMarshal.AsSpan(positions)));
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
