using System.Numerics;
using System.Runtime.InteropServices;
using TRMesh.Mesh;

namespace tr_mesh.Tests;

public class SpatialMeshTriangulationTests
{
    static readonly Vector2[] TriangleUvs = [Vector2.Zero, Vector2.UnitX, Vector2.UnitY];
    static readonly Vector2[] QuadUvs =
    [
        new(0f, 0f),
        new(1f, 0f),
        new(1f, 1f),
        new(0f, 1f),
    ];

    [Fact]
    public void TriangulateFace_Triangle_EmitsTheThreeHalfEdges()
    {
        using var mesh = new SpatialMesh();
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0));
        int v1 = mesh.AddVertex(new Vector3(1, 0, 0));
        int v2 = mesh.AddVertex(new Vector3(0, 1, 0));
        int face = mesh.AddFace([v0, v1, v2], TriangleUvs);

        var output = new List<int> { 99 };
        Assert.True(mesh.TriangulateFace(face, output));

        Assert.Equal(4, output.Count);
        Assert.Equal(99, output[0]);

        int h0 = mesh.GetFace(face).AdjacentHalfEdge;
        int h1 = mesh.GetHalfEdge(h0).NextHalfEdge;
        int h2 = mesh.GetHalfEdge(h1).NextHalfEdge;
        Assert.Equal([99, h0, h1, h2], output);
        AssertTriangleWinding(mesh, output, Vector3.UnitZ, skip: 1);
    }

    [Fact]
    public void TriangulateFace_ConvexQuad_FansFromTheFirstCorner()
    {
        using var mesh = new SpatialMesh();
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0));
        int v1 = mesh.AddVertex(new Vector3(2, 0, 0));
        int v2 = mesh.AddVertex(new Vector3(2, 2, 0));
        int v3 = mesh.AddVertex(new Vector3(0, 2, 0));
        int face = mesh.AddFace([v0, v1, v2, v3], QuadUvs);

        var output = new List<int>();
        Assert.True(mesh.TriangulateFace(face, output));
        Assert.Equal(6, output.Count);

        int h0 = mesh.GetFace(face).AdjacentHalfEdge;
        int h1 = mesh.GetHalfEdge(h0).NextHalfEdge;
        int h2 = mesh.GetHalfEdge(h1).NextHalfEdge;
        int h3 = mesh.GetHalfEdge(h2).NextHalfEdge;
        Assert.Equal([h0, h1, h2, h0, h2, h3], output);
        AssertTriangleWinding(mesh, output, Vector3.UnitZ);
        AssertAreaMatches(mesh, face, output);
    }

    [Fact]
    public void TriangulateFace_ConcavePentagon_MatchesPolygonArea()
    {
        using var mesh = new SpatialMesh();
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0));
        int v1 = mesh.AddVertex(new Vector3(2, 0, 0));
        int v2 = mesh.AddVertex(new Vector3(2, 2, 0));
        int v3 = mesh.AddVertex(new Vector3(1, 0.5f, 0));
        int v4 = mesh.AddVertex(new Vector3(0, 2, 0));
        Vector2[] uvs = [.. Enumerable.Repeat(Vector2.Zero, 5)];
        int face = mesh.AddFace([v0, v1, v2, v3, v4], uvs);

        var output = new List<int>();
        Assert.True(mesh.TriangulateFace(face, output));
        Assert.Equal(9, output.Count);
        AssertTriangleWinding(mesh, output, Vector3.UnitZ);
        AssertAreaMatches(mesh, face, output);

        int h0 = mesh.GetFace(face).AdjacentHalfEdge;
        int h1 = mesh.GetHalfEdge(h0).NextHalfEdge;
        int h2 = mesh.GetHalfEdge(h1).NextHalfEdge;
        int h3 = mesh.GetHalfEdge(h2).NextHalfEdge;
        int h4 = mesh.GetHalfEdge(h3).NextHalfEdge;
        Assert.False(output.SequenceEqual([h0, h1, h2, h0, h2, h3, h0, h3, h4]));
    }

    [Fact]
    public void TriangulateFace_TiltedPlaneQuad_PreservesWinding()
    {
        using var mesh = new SpatialMesh();
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0));
        int v1 = mesh.AddVertex(new Vector3(1, 1, 0));
        int v2 = mesh.AddVertex(new Vector3(1, 1, 1));
        int v3 = mesh.AddVertex(new Vector3(0, 0, 1));
        int face = mesh.AddFace([v0, v1, v2, v3], QuadUvs);

        var output = new List<int>();
        Assert.True(mesh.TriangulateFace(face, output));
        Assert.Equal(6, output.Count);
        AssertTriangleWinding(mesh, output, mesh.GetFaceNormal(face));
        AssertAreaMatches(mesh, face, output);
    }

    [Fact]
    public void TriangulateFace_AabbFaces_CoverEachQuad()
    {
        using SpatialMesh mesh = SpatialMeshBuilder.CreateAabb(Vector3.Zero, Vector3.One);
        var output = new List<int>();
        for (int face = 0; face < mesh.FaceCount; face++)
        {
            output.Clear();
            Assert.True(mesh.TriangulateFace(face, output));
            Assert.Equal(6, output.Count);
            AssertTriangleWinding(mesh, output, mesh.GetFaceNormal(face));
            AssertAreaMatches(mesh, face, output);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    public void TriangulateFace_CylinderCaps_CoverTheNgon(int sides)
    {
        using SpatialMesh mesh = SpatialMeshBuilder.CreateCylinder(2f, 4f, sides);
        int bottom = 0;
        int top = sides + 1;

        var output = new List<int>();
        Assert.True(mesh.TriangulateFace(bottom, output));
        Assert.Equal(3 * (sides - 2), output.Count);
        AssertTriangleWinding(mesh, output, mesh.GetFaceNormal(bottom));
        AssertAreaMatches(mesh, bottom, output);

        output.Clear();
        Assert.True(mesh.TriangulateFace(top, output));
        Assert.Equal(3 * (sides - 2), output.Count);
        AssertTriangleWinding(mesh, output, mesh.GetFaceNormal(top));
        AssertAreaMatches(mesh, top, output);
    }

    [Fact]
    public void TriangulateFace_DeadHandle_ReturnsFalseAndLeavesOutput()
    {
        using var mesh = new SpatialMesh();
        var output = new List<int> { 7 };
        Assert.False(mesh.TriangulateFace(0, output));
        Assert.Equal([7], output);
    }

    [Fact]
    public void TriangulateFace_NullOutput_Throws()
    {
        using var mesh = new SpatialMesh();
        Assert.Throws<ArgumentNullException>(() => mesh.TriangulateFace(0, null!));
    }

    [Fact]
    public void TriangulateFaceCorners_CollinearQuad_FailsWithoutWriting()
    {
        int[] corners = [10, 11, 12, 13];
        Vector3[] positions =
        [
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0),
            new(3, 0, 0),
        ];
        var output = new List<int> { 5 };
        Assert.False(SpatialMesh.TriangulateFaceCorners(corners, positions, output));
        Assert.Equal([5], output);
    }

    [Fact]
    public void TriangulateFaceCorners_LengthMismatch_Fails()
    {
        var output = new List<int>();
        Assert.False(
            SpatialMesh.TriangulateFaceCorners(
                [0, 1, 2],
                [Vector3.Zero, Vector3.UnitX],
                output
            )
        );
        Assert.Empty(output);
    }

    [Fact]
    public void TriangulateFaceCorners_ConcaveQuad_ClipsTheReflexVertex()
    {
        int[] corners = [0, 1, 2, 3];
        Vector3[] positions =
        [
            new(0, 0, 0),
            new(3, 0, 0),
            new(1, 1, 0),
            new(0, 3, 0),
        ];
        var output = new List<int>();
        Assert.True(SpatialMesh.TriangulateFaceCorners(corners, positions, output));
        Assert.Equal(6, output.Count);

        Vector3 normal = Vector3.UnitZ;
        for (int i = 0; i < output.Count; i += 3)
        {
            Vector3 a = positions[Array.IndexOf(corners, output[i])];
            Vector3 b = positions[Array.IndexOf(corners, output[i + 1])];
            Vector3 c = positions[Array.IndexOf(corners, output[i + 2])];
            Assert.True(TriangleArea(a, b, c, normal) > 0f);
        }
    }

    static void AssertTriangleWinding(
        SpatialMesh mesh,
        List<int> triangles,
        Vector3 expectedNormal,
        int skip = 0
    )
    {
        ReadOnlySpan<int> span = CollectionsMarshal.AsSpan(triangles)[skip..];
        Assert.Equal(0, span.Length % 3);
        for (int i = 0; i < span.Length; i += 3)
        {
            Vector3 a = CornerPosition(mesh, span[i]);
            Vector3 b = CornerPosition(mesh, span[i + 1]);
            Vector3 c = CornerPosition(mesh, span[i + 2]);
            float signed = Vector3.Dot(Vector3.Cross(b - a, c - b), expectedNormal);
            Assert.True(signed > 0f, $"Triangle at {i} has non-positive winding against the face normal.");
        }
    }

    static void AssertAreaMatches(SpatialMesh mesh, int face, List<int> triangles)
    {
        Vector3[] positions = FacePositions(mesh, face);
        Vector3 normal = mesh.GetFaceNormal(face);
        float expected = PolygonArea(positions, normal);
        float actual = TriangleListArea(mesh, triangles, normal);
        Assert.InRange(actual, expected - 0.0001f, expected + 0.0001f);
    }

    static float TriangleListArea(SpatialMesh mesh, List<int> triangles, Vector3 normal)
    {
        float area = 0f;
        for (int i = 0; i < triangles.Count; i += 3)
        {
            area += TriangleArea(
                CornerPosition(mesh, triangles[i]),
                CornerPosition(mesh, triangles[i + 1]),
                CornerPosition(mesh, triangles[i + 2]),
                normal
            );
        }

        return area;
    }

    static float TriangleArea(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
    {
        Vector3 unit = Vector3.Normalize(normal);
        return 0.5f * Vector3.Dot(Vector3.Cross(b - a, c - a), unit);
    }

    static float PolygonArea(ReadOnlySpan<Vector3> positions, Vector3 normal)
    {
        Vector3 unit = Vector3.Normalize(normal);
        float acc = 0f;
        Vector3 prev = positions[^1];
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 curr = positions[i];
            acc += Vector3.Dot(Vector3.Cross(prev, curr), unit);
            prev = curr;
        }

        return 0.5f * acc;
    }

    static Vector3[] FacePositions(SpatialMesh mesh, int face)
    {
        var positions = new List<Vector3>();
        int start = mesh.GetFace(face).AdjacentHalfEdge;
        int he = start;
        do
        {
            positions.Add(CornerPosition(mesh, he));
            he = mesh.GetHalfEdge(he).NextHalfEdge;
        } while (he != start);
        return [.. positions];
    }

    static Vector3 CornerPosition(SpatialMesh mesh, int halfEdge)
    {
        int vertex = mesh.GetHalfEdge(halfEdge).SourceVertex;
        return mesh.GetVertexPosition(vertex);
    }
}
