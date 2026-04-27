namespace TREditorSharp;

using System.Numerics;
using TREditorSharp.Storage;

/// <summary>
/// HalfEdgeMesh that represents a 3D mesh that can be converted and displayed as a polygon mesh.
/// </summary>
public class SpatialMesh : HalfEdgeMesh
{
    private NativeColumn<Vector3> VertexPositions { get; }

    public SpatialMesh()
        : base()
    {
        VertexPositions = Vertices.RegisterNativeColumn<Vector3, VertexPositionTag>();
    }

    public void AddVertex(Vector3 position)
    {
        var v = Vertices.Allocate();
        VertexPositions[Vertices.GetDenseIndex(v)] = position;
    }
}
