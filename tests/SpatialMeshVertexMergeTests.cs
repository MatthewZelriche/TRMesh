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

        TopologyPatch? createdPatch = mesh.TryMergeVertices([source], target);
        using TopologyPatch patch = Assert.IsType<TopologyPatch>(createdPatch);

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

        TopologyPatch? createdPatch = mesh.TryMergeVertices([target], source);
        using TopologyPatch patch = Assert.IsType<TopologyPatch>(createdPatch);

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

        TopologyPatch? createdPatch = mesh.TryMergeVertices([source], target);
        using TopologyPatch patch = Assert.IsType<TopologyPatch>(createdPatch);

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

        TopologyPatch? patch = mesh.TryMergeVertices([source], target);

        Assert.Null(patch);
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

        TopologyPatch? patch = mesh.TryMergeVertices([vertex], vertex);

        Assert.Null(patch);
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

        TopologyPatch? patch = mesh.TryMergeVertices([source], target);

        Assert.Null(patch);
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

        TopologyPatch? sourcePatch = mesh.TryMergeVertices([VertexHandle.Null], target);
        TopologyPatch? targetPatch = mesh.TryMergeVertices([source], VertexHandle.Null);

        Assert.Null(sourcePatch);
        Assert.Null(targetPatch);
        Assert.True(mesh.IsVertexAlive(source));
        Assert.True(mesh.IsVertexAlive(target));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryMergeVertices_NonAdjacentInteriorVerticesBridgeAndRestoreExactPatch()
    {
        using SpatialMesh mesh = BuildClosedOctagonalPrism(
            out VertexHandle[] top,
            out _,
            out FaceHandle topFace
        );
        VertexHandle source = top[0];
        VertexHandle target = top[2];
        Vector3 targetPosition = mesh.GetVertexPosition(target);
        mesh.SetFaceMaterialSlot(topFace, 17);
        Assert.True(FaceUvProjector.TryProjectAndApply(mesh, topFace));
        Dictionary<FaceCornerHandle, Vector2> originalUvs = CollectFaceUvs(mesh, topFace);

        TopologyPatch? createdPatch = mesh.TryMergeVertices([source], target);
        using TopologyPatch patch = Assert.IsType<TopologyPatch>(createdPatch);

        Assert.False(mesh.IsVertexAlive(source));
        Assert.True(mesh.IsVertexAlive(target));
        Assert.Equal(targetPosition, mesh.GetVertexPosition(target));
        Assert.False(mesh.IsFaceAlive(topFace));
        AssertInitializedMaterialFacesAreProjected(mesh, 17);
        mesh.ValidateConsistency();

        patch.ApplyBefore();

        Assert.True(mesh.IsVertexAlive(source));
        Assert.True(mesh.IsVertexAlive(target));
        Assert.True(mesh.IsFaceAlive(topFace));
        Assert.Equal(17, mesh.GetFaceMaterialSlot(topFace));
        Assert.True(mesh.AreFaceUvsInitialized(topFace));
        foreach ((FaceCornerHandle corner, Vector2 uv) in originalUvs)
            Assert.Equal(uv, mesh.GetFaceCornerUv(corner));
        mesh.ValidateConsistency();

        patch.ApplyAfter();

        Assert.False(mesh.IsVertexAlive(source));
        Assert.True(mesh.IsVertexAlive(target));
        AssertInitializedMaterialFacesAreProjected(mesh, 17);
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryMergeVertices_NonAdjacentBoundaryVerticesReturnFalseWithoutMutation()
    {
        using SpatialMesh mesh = BuildQuad(
            out VertexHandle source,
            out _,
            out VertexHandle target,
            out _,
            out FaceHandle face
        );
        List<VertexHandle> faceVertices = CollectFaceVertices(mesh, face);

        TopologyPatch? patch = mesh.TryMergeVertices([source], target);

        Assert.Null(patch);
        Assert.True(mesh.IsVertexAlive(source));
        Assert.True(mesh.IsVertexAlive(target));
        Assert.Equal(faceVertices, CollectFaceVertices(mesh, face));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryMergeVertices_ThreeSeparatedCapVerticesUseRepeatedSplits()
    {
        using SpatialMesh mesh = BuildClosedOctagonalPrism(
            out VertexHandle[] top,
            out _,
            out FaceHandle topFace
        );
        mesh.SetFaceMaterialSlot(topFace, 23);
        Assert.True(FaceUvProjector.TryProjectAndApply(mesh, topFace));
        Vector3 targetPosition = mesh.GetVertexPosition(top[4]);

        TopologyPatch? createdPatch = mesh.TryMergeVertices([top[0], top[2]], top[4]);
        using TopologyPatch patch = Assert.IsType<TopologyPatch>(createdPatch);

        Assert.False(mesh.IsVertexAlive(top[0]));
        Assert.False(mesh.IsVertexAlive(top[2]));
        Assert.True(mesh.IsVertexAlive(top[4]));
        Assert.Equal(targetPosition, mesh.GetVertexPosition(top[4]));
        AssertInitializedMaterialFacesAreProjected(mesh, 23);
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryMergeVertices_BridgeChainUsesIntermediateSelectedComponent()
    {
        using SpatialMesh mesh = BuildClosedOctagonalPrism(
            out VertexHandle[] top,
            out VertexHandle[] bottom,
            out _
        );

        TopologyPatch? createdPatch = mesh.TryMergeVertices([top[2], bottom[3]], top[0]);
        using TopologyPatch patch = Assert.IsType<TopologyPatch>(createdPatch);

        Assert.False(mesh.IsVertexAlive(top[2]));
        Assert.False(mesh.IsVertexAlive(bottom[3]));
        Assert.True(mesh.IsVertexAlive(top[0]));
        mesh.ValidateConsistency();
    }

    [Fact]
    public void TryMergeVertices_BridgeThenNoSharedFacePathRollsBackWithoutPatch()
    {
        using SpatialMesh mesh = BuildClosedOctagonalPrism(
            out VertexHandle[] top,
            out VertexHandle[] bottom,
            out FaceHandle topFace
        );
        mesh.SetFaceMaterialSlot(topFace, 29);
        Assert.True(FaceUvProjector.TryProjectAndApply(mesh, topFace));
        Dictionary<FaceCornerHandle, Vector2> originalUvs = CollectFaceUvs(mesh, topFace);
        int verticesBefore = CountVertices(mesh);
        int edgesBefore = CountEdges(mesh);
        int facesBefore = CountFaces(mesh);

        TopologyPatch? patch = mesh.TryMergeVertices([top[2], bottom[4]], top[0]);

        Assert.Null(patch);
        Assert.True(mesh.IsVertexAlive(top[0]));
        Assert.True(mesh.IsVertexAlive(top[2]));
        Assert.True(mesh.IsVertexAlive(bottom[4]));
        Assert.True(mesh.IsFaceAlive(topFace));
        Assert.Equal(29, mesh.GetFaceMaterialSlot(topFace));
        Assert.True(mesh.AreFaceUvsInitialized(topFace));
        foreach ((FaceCornerHandle corner, Vector2 uv) in originalUvs)
            Assert.Equal(uv, mesh.GetFaceCornerUv(corner));
        Assert.Equal(verticesBefore, CountVertices(mesh));
        Assert.Equal(edgesBefore, CountEdges(mesh));
        Assert.Equal(facesBefore, CountFaces(mesh));
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
        VertexHandle d = mesh.AddVertex(Vector3.One);
        VertexHandle shared = mesh.AddVertex(Vector3.UnitZ);
        mesh.AddFace([source, target, c]);
        mesh.AddFace([target, source, d]);
        mesh.AddFace([source, c, shared]);
        mesh.AddFace([c, target, shared]);
        mesh.AddFace([target, d, shared]);
        mesh.AddFace([d, source, shared]);
        return mesh;
    }

    private static SpatialMesh BuildClosedOctagonalPrism(
        out VertexHandle[] top,
        out VertexHandle[] bottom,
        out FaceHandle topFace
    )
    {
        SpatialMesh mesh = new();
        const int sideCount = 8;
        top = new VertexHandle[sideCount];
        bottom = new VertexHandle[sideCount];
        for (int i = 0; i < sideCount; i++)
        {
            float angle = MathF.Tau * i / sideCount;
            float x = MathF.Cos(angle);
            float y = MathF.Sin(angle);
            top[i] = mesh.AddVertex(new Vector3(x, y, 1f));
            bottom[i] = mesh.AddVertex(new Vector3(x, y, 0f));
        }

        topFace = mesh.AddFace(top);
        for (int i = 0; i < sideCount; i++)
        {
            int next = (i + 1) % sideCount;
            mesh.AddFace([top[next], top[i], bottom[i], bottom[next]]);
        }
        VertexHandle[] bottomFace = new VertexHandle[sideCount];
        for (int i = 0; i < sideCount; i++)
            bottomFace[i] = bottom[sideCount - i - 1];
        mesh.AddFace(bottomFace);
        mesh.ValidateConsistency();
        return mesh;
    }

    private static Dictionary<FaceCornerHandle, Vector2> CollectFaceUvs(
        SpatialMesh mesh,
        FaceHandle face
    )
    {
        Dictionary<FaceCornerHandle, Vector2> uvs = [];
        foreach (FaceCornerHandle corner in mesh.HalfEdgesAroundFace(face))
            uvs.Add(corner, mesh.GetFaceCornerUv(corner));
        return uvs;
    }

    private static void AssertInitializedMaterialFacesAreProjected(
        SpatialMesh mesh,
        int materialSlot
    )
    {
        List<FaceHandle> materialFaces = [];
        foreach (FaceHandle face in mesh.EnumerateLiveFaces())
        {
            if (mesh.GetFaceMaterialSlot(face) == materialSlot)
                materialFaces.Add(face);
        }

        Assert.NotEmpty(materialFaces);
        foreach (FaceHandle face in materialFaces)
        {
            Assert.True(mesh.AreFaceUvsInitialized(face));
            List<ProjectedFaceCornerUv> expected = [];
            Assert.True(FaceUvProjector.TryProject(mesh, face, expected));
            foreach (ProjectedFaceCornerUv corner in expected)
                Assert.Equal(corner.Uv, mesh.GetFaceCornerUv(corner.Corner));
        }
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
