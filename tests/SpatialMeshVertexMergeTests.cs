using System.Numerics;

namespace TREditorSharp.Tests;

public sealed class SpatialMeshVertexMergeTests
{
    [Fact]
    public void TryMergeVertices_ConnectedVerticesPreservesTargetHandleAndPosition()
    {
        using SpatialMesh mesh = BuildQuad(
            out VertexHandle source,
            out VertexHandle target,
            out VertexHandle c,
            out VertexHandle d,
            out FaceHandle face
        );
        Vector3 targetPosition = new(8f, 4f, 2f);
        mesh.SetVertexPosition(target, targetPosition);

        Assert.True(mesh.TryMergeVertices(source, target));

        Assert.False(mesh.IsVertexAlive(source));
        Assert.True(mesh.IsVertexAlive(target));
        Assert.Equal(targetPosition, mesh.GetVertexPosition(target));
        Assert.True(mesh.IsFaceAlive(face));
        Assert.Equal([target, c, d], CollectFaceVertices(mesh, face));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryMergeVertices_ReversedDirectionPreservesOtherVertex()
    {
        using SpatialMesh mesh = BuildQuad(
            out VertexHandle source,
            out VertexHandle target,
            out VertexHandle c,
            out VertexHandle d,
            out FaceHandle face
        );
        Vector3 sourcePosition = new(-3f, 7f, 1f);
        mesh.SetVertexPosition(source, sourcePosition);

        Assert.True(mesh.TryMergeVertices(target, source));

        Assert.True(mesh.IsVertexAlive(source));
        Assert.False(mesh.IsVertexAlive(target));
        Assert.Equal(sourcePosition, mesh.GetVertexPosition(source));
        Assert.Equal([source, c, d], CollectFaceVertices(mesh, face));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryMergeVertices_ConnectedVertexIncidentTopologyRewiresToTarget()
    {
        using SpatialMesh mesh = BuildSourceWithIncidentFace(
            out VertexHandle source,
            out VertexHandle target,
            out FaceHandle incidentFace
        );

        Assert.True(mesh.TryMergeVertices(source, target));

        Assert.DoesNotContain(source, CollectFaceVertices(mesh, incidentFace));
        Assert.Contains(target, CollectFaceVertices(mesh, incidentFace));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryMergeVertices_DisconnectedVerticesReturnFalseWithoutMutation()
    {
        using SpatialMesh mesh = BuildTriangle(out VertexHandle source, out _, out _, out _);
        VertexHandle target = mesh.AddVertex(new Vector3(5f, 6f, 7f));
        int verticesBefore = CountVertices(mesh);
        int edgesBefore = CountEdges(mesh);
        int facesBefore = CountFaces(mesh);

        Assert.False(mesh.TryMergeVertices(source, target));

        Assert.True(mesh.IsVertexAlive(source));
        Assert.True(mesh.IsVertexAlive(target));
        Assert.Equal(verticesBefore, CountVertices(mesh));
        Assert.Equal(edgesBefore, CountEdges(mesh));
        Assert.Equal(facesBefore, CountFaces(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryMergeVertices_EqualVerticesReturnFalseWithoutMutation()
    {
        using SpatialMesh mesh = BuildTriangle(out VertexHandle vertex, out _, out _, out _);
        int verticesBefore = CountVertices(mesh);

        Assert.False(mesh.TryMergeVertices(vertex, vertex));

        Assert.Equal(verticesBefore, CountVertices(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryMergeVertices_LinkConditionViolationReturnsFalseWithoutMutation()
    {
        using SpatialMesh mesh = BuildLinkConditionViolation(
            out VertexHandle source,
            out VertexHandle target
        );
        Vector3 sourcePosition = mesh.GetVertexPosition(source);
        Vector3 targetPosition = mesh.GetVertexPosition(target);
        int verticesBefore = CountVertices(mesh);
        int edgesBefore = CountEdges(mesh);
        int facesBefore = CountFaces(mesh);

        Assert.False(mesh.TryMergeVertices(source, target));

        Assert.Equal(sourcePosition, mesh.GetVertexPosition(source));
        Assert.Equal(targetPosition, mesh.GetVertexPosition(target));
        Assert.Equal(verticesBefore, CountVertices(mesh));
        Assert.Equal(edgesBefore, CountEdges(mesh));
        Assert.Equal(facesBefore, CountFaces(mesh));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryMergeVertices_DeadVertexReturnsFalseWithoutMutation()
    {
        using SpatialMesh mesh = BuildTriangle(
            out VertexHandle source,
            out VertexHandle target,
            out _,
            out _
        );

        Assert.False(mesh.TryMergeVertices(VertexHandle.Null, target));
        Assert.False(mesh.TryMergeVertices(source, VertexHandle.Null));

        Assert.True(mesh.IsVertexAlive(source));
        Assert.True(mesh.IsVertexAlive(target));
        mesh.ValidateConsistency();
    }

    private static SpatialMesh BuildTriangle(
        out VertexHandle a,
        out VertexHandle b,
        out VertexHandle c,
        out FaceHandle face
    )
    {
        SpatialMesh mesh = new();
        a = mesh.AddVertex(Vector3.Zero);
        b = mesh.AddVertex(Vector3.UnitX);
        c = mesh.AddVertex(Vector3.UnitY);
        face = mesh.AddFace([a, b, c]);
        return mesh;
    }

    private static SpatialMesh BuildQuad(
        out VertexHandle a,
        out VertexHandle b,
        out VertexHandle c,
        out VertexHandle d,
        out FaceHandle face
    )
    {
        SpatialMesh mesh = new();
        a = mesh.AddVertex(Vector3.Zero);
        b = mesh.AddVertex(Vector3.UnitX);
        c = mesh.AddVertex(Vector3.One);
        d = mesh.AddVertex(Vector3.UnitY);
        face = mesh.AddFace([a, b, c, d]);
        return mesh;
    }

    private static SpatialMesh BuildSourceWithIncidentFace(
        out VertexHandle source,
        out VertexHandle target,
        out FaceHandle incidentFace
    )
    {
        SpatialMesh mesh = new();
        source = mesh.AddVertex(Vector3.Zero);
        target = mesh.AddVertex(Vector3.UnitX);
        VertexHandle a = mesh.AddVertex(Vector3.UnitY);
        VertexHandle b = mesh.AddVertex(Vector3.One);
        VertexHandle c = mesh.AddVertex(-Vector3.UnitX);
        mesh.AddFace([source, target, b, a]);
        incidentFace = mesh.AddFace([source, a, c]);
        return mesh;
    }

    private static SpatialMesh BuildLinkConditionViolation(
        out VertexHandle source,
        out VertexHandle target
    )
    {
        SpatialMesh mesh = new();
        source = mesh.AddVertex(Vector3.Zero);
        target = mesh.AddVertex(Vector3.UnitX);
        VertexHandle c = mesh.AddVertex(Vector3.UnitY);
        VertexHandle shared = mesh.AddVertex(Vector3.One);
        VertexHandle x = mesh.AddVertex(Vector3.UnitZ);
        VertexHandle y = mesh.AddVertex(Vector3.UnitX + Vector3.UnitZ);
        mesh.AddFace([source, target, c]);
        mesh.AddFace([source, shared, x]);
        mesh.AddFace([target, y, shared]);
        return mesh;
    }

    private static List<VertexHandle> CollectFaceVertices(SpatialMesh mesh, FaceHandle face)
    {
        List<VertexHandle> vertices = [];
        foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
            vertices.Add(mesh.GetHalfEdge(corner).Origin);
        return vertices;
    }

    private static int CountVertices(SpatialMesh mesh)
    {
        int count = 0;
        foreach (VertexHandle _ in mesh.EnumerateLiveVertices())
            count++;
        return count;
    }

    private static int CountEdges(SpatialMesh mesh)
    {
        int count = 0;
        foreach (HalfEdgeHandle _ in mesh.EnumerateLiveHalfEdges())
            count++;
        return count / 2;
    }

    private static int CountFaces(SpatialMesh mesh)
    {
        int count = 0;
        foreach (FaceHandle _ in mesh.EnumerateLiveFaces())
            count++;
        return count;
    }
}
