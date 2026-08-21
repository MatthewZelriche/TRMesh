using System.Numerics;
using TRMesh.Mesh;

namespace tr_mesh.Tests;

public class SpatialMeshBuilderTests
{
    static readonly Vector2[] QuadUvs =
    [
        new(0f, 0f),
        new(1f, 0f),
        new(1f, 1f),
        new(0f, 1f),
    ];

    [Fact]
    public void CreateAabb_BuildsClosedBoxWithOutwardFacesAndUnitUvs()
    {
        Vector3 position = new(-2f, 3f, 5f);
        Vector3 size = new(4f, 6f, 8f);

        using SpatialMesh mesh = SpatialMeshBuilder.CreateAabb(position, size);

        Assert.Equal(8, mesh.VertexCount);
        Assert.Equal(12, mesh.EdgeCount);
        Assert.Equal(6, mesh.FaceCount);
        Assert.Equal(position, mesh.GetVertexData(0).position);
        Assert.Equal(position + size, mesh.GetVertexData(6).position);
        Assert.Equal(
            [
                -Vector3.UnitY,
                -Vector3.UnitZ,
                Vector3.UnitX,
                Vector3.UnitZ,
                -Vector3.UnitX,
                Vector3.UnitY,
            ],
            Enumerable.Range(0, mesh.FaceCount).Select(face => mesh.GetFaceData(face).normal)
        );

        for (int face = 0; face < mesh.FaceCount; face++)
            Assert.Equal(QuadUvs, GetFaceUvs(mesh, face));

        AssertClosed(mesh);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    public void CreateCylinder_BuildsClosedCenteredCylinderWithExpectedUvs(int sides)
    {
        const float height = 4f;
        const float width = 6f;
        const float radius = width / 2f;

        using SpatialMesh mesh = SpatialMeshBuilder.CreateCylinder(height, width, sides);

        Assert.Equal(2 * sides, mesh.VertexCount);
        Assert.Equal(3 * sides, mesh.EdgeCount);
        Assert.Equal(sides + 2, mesh.FaceCount);
        AssertVectorApproximately(
            new Vector3(radius, -height / 2f, 0f),
            mesh.GetVertexData(0).position
        );
        AssertVectorApproximately(
            new Vector3(radius, height / 2f, 0f),
            mesh.GetVertexData(1).position
        );
        AssertVectorApproximately(-Vector3.UnitY, mesh.GetFaceData(0).normal);
        AssertVectorApproximately(Vector3.UnitY, mesh.GetFaceData(sides + 1).normal);

        for (int i = 0; i < sides; i++)
        {
            Vector3 normal = mesh.GetFaceData(i + 1).normal;
            Assert.InRange(MathF.Abs(normal.Y), 0f, 0.00001f);
            Assert.InRange(normal.Length(), 0.99999f, 1.00001f);

            float u0 = (float)i / sides;
            float u1 = (float)(i + 1) / sides;
            Assert.Equal(
                [
                    new Vector2(u0, 0f),
                    new Vector2(u1, 0f),
                    new Vector2(u1, 1f),
                    new Vector2(u0, 1f),
                ],
                GetFaceUvs(mesh, i + 1)
            );
        }

        foreach (Vector2 uv in GetFaceUvs(mesh, 0).Concat(GetFaceUvs(mesh, sides + 1)))
        {
            Assert.InRange(uv.X, 0f, 1f);
            Assert.InRange(uv.Y, 0f, 1f);
        }

        Assert.Equal(new Vector2(1f, 0.5f), GetFaceUvs(mesh, sides + 1)[0]);
        AssertClosed(mesh);
    }

    [Fact]
    public void CreateAabb_RejectsInvalidDescriptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SpatialMeshBuilder.CreateAabb(Vector3.Zero, new Vector3(0f, 1f, 1f))
        );
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SpatialMeshBuilder.CreateAabb(Vector3.Zero, new Vector3(1f, -1f, 1f))
        );
    }

    [Fact]
    public void CreateCylinder_RejectsInvalidDescriptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SpatialMeshBuilder.CreateCylinder(0f, 1f, 3)
        );
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SpatialMeshBuilder.CreateCylinder(1f, -1f, 3)
        );
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SpatialMeshBuilder.CreateCylinder(1f, 1f, 2)
        );
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SpatialMeshBuilder.CreateCylinder(float.Epsilon, 1f, 3)
        );
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SpatialMeshBuilder.CreateCylinder(1f, float.Epsilon, 3)
        );
    }

    static Vector2[] GetFaceUvs(SpatialMesh mesh, int face)
    {
        var result = new List<Vector2>();
        int start = mesh.GetFaceData(face).topology.AdjacentHalfEdge;
        int halfEdge = start;
        do
        {
            result.Add(mesh.GetHalfEdgeData(halfEdge).UV);
            halfEdge = mesh.GetHalfEdgeData(halfEdge).topology.NextHalfEdge;
        } while (halfEdge != start);
        return [.. result];
    }

    static void AssertClosed(SpatialMesh mesh)
    {
        foreach (HalfEdgeDataSoA.ReadOnlyView halfEdge in mesh.HalfEdges)
        {
            Assert.NotEqual(
                SpatialMesh.INVALID_HANDLE,
                halfEdge.topology.AdjacentFace
            );
        }
    }

    static void AssertVectorApproximately(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(actual.X, expected.X - 0.00001f, expected.X + 0.00001f);
        Assert.InRange(actual.Y, expected.Y - 0.00001f, expected.Y + 0.00001f);
        Assert.InRange(actual.Z, expected.Z - 0.00001f, expected.Z + 0.00001f);
    }
}
