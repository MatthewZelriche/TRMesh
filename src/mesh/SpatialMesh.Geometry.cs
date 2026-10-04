using System.Numerics;

namespace TRMesh.Mesh;

public partial class SpatialMesh
{
    // Sets vertex positions as one validated operation. Topology is unchanged, but every face
    // normal is rebuilt because neighboring faces may also have been deformed by the moved vertices.
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

        for (int index = 0; index < vertices.Length; index++)
            _vertices.SetPosition(vertices[index], positions[index]);

        RecomputeFaceNormals();
    }

    private void RecomputeFaceNormals()
    {
        FaceDataSoA.Enumerator faces = _faces.GetEnumerator();
        while (faces.MoveNext())
        {
            int face = faces.CurrentSlot;
            FaceData data = faces.Current;
            int start = data.topology.AdjacentHalfEdge;
            var positions = new List<Vector3>();
            int halfEdge = start;
            do
            {
                HalfEdge topology = _halfEdges.Get(halfEdge).topology;
                positions.Add(_vertices.Get(topology.SourceVertex).position);
                halfEdge = topology.NextHalfEdge;
            } while (halfEdge != start);

            data.normal = Util.Math.ComputeFaceNormal(positions.ToArray());
            _faces.Set(face, data);
        }
    }
}
