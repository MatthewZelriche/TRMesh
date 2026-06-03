using System.Numerics;
using TREditorSharp.Builders;
using TREditorSharp.IO;

namespace TREditorSharp.Tests;

/// <summary>
/// Builds primitives via <see cref="MeshBuilders"/>, writes them out as OBJ via
/// <see cref="ObjMeshWriter"/>, reads them back via <see cref="ObjMeshReader"/>, and asserts
/// semantic equivalence between the original and the round-tripped mesh. Implicitly exercises
/// the writer, reader, builders, and <see cref="HalfEdgeMesh.ValidateConsistency"/> in one shot.
/// </summary>
public class BuilderRoundTripTests
{
    public static TheoryData<BlockOptions> BlockCases { get; } =
    [
        new() { Min = new Vector3(0, 0, 0), Max = new Vector3(1, 1, 1) },
        new() { Min = new Vector3(-2, -3, -4), Max = new Vector3(2, 3, 4) },
        new() { Min = new Vector3(10, 20, 30), Max = new Vector3(11, 22, 33) },
    ];

    [Theory]
    [MemberData(nameof(BlockCases))]
    public void BlockRoundTrip(BlockOptions options)
    {
        AssertRoundTripEquivalent(MeshBuilders.Build(options), "block");
    }

    public static TheoryData<CylinderOptions> CylinderCases { get; } =
    [
        new()
        {
            Center = Vector3.Zero,
            RadiusX = 1f,
            RadiusZ = 1f,
            Height = 2f,
            RadialSegments = 3,
        },
        new()
        {
            Center = Vector3.Zero,
            RadiusX = 1f,
            RadiusZ = 1f,
            Height = 2f,
            RadialSegments = 8,
        },
        new()
        {
            Center = new Vector3(5, -1, 2),
            RadiusX = 0.5f,
            RadiusZ = 0.5f,
            Height = 4f,
            RadialSegments = 32,
        },
        new()
        {
            Center = Vector3.Zero,
            RadiusX = 1f,
            RadiusZ = 2f,
            Height = 2f,
            RadialSegments = 4,
        },
        new()
        {
            Center = new Vector3(5, -1, 2),
            RadiusX = 0.5f,
            RadiusZ = 1.5f,
            Height = 4f,
            RadialSegments = 32,
        },
    ];

    [Theory]
    [MemberData(nameof(CylinderCases))]
    public void CylinderRoundTrip(CylinderOptions options)
    {
        AssertRoundTripEquivalent(MeshBuilders.Build(options), "cylinder");
    }

    public static TheoryData<UvSphereOptions> UvSphereCases { get; } =
    [
        new()
        {
            Center = Vector3.Zero,
            Radius = 1f,
            LatSegments = 2,
            LonSegments = 3,
        },
        new()
        {
            Center = Vector3.Zero,
            Radius = 1f,
            LatSegments = 8,
            LonSegments = 12,
        },
        new()
        {
            Center = new Vector3(-3, 4, 0.25f),
            Radius = 2f,
            LatSegments = 16,
            LonSegments = 32,
        },
    ];

    [Theory]
    [MemberData(nameof(UvSphereCases))]
    public void UvSphereRoundTrip(UvSphereOptions options)
    {
        AssertRoundTripEquivalent(MeshBuilders.Build(options), "uvsphere");
    }

    public static TheoryData<PlaneOptions> PlaneCases { get; } =
    [
        new()
        {
            Center = Vector3.Zero,
            Width = 1f,
            Height = 1f,
        },
        new()
        {
            Center = new Vector3(2, 0, -1),
            Width = 3f,
            Height = 4f,
            WidthSegments = 3,
            HeightSegments = 4,
        },
        new()
        {
            Center = Vector3.Zero,
            Width = 5f,
            Height = 1f,
            WidthSegments = 1,
            HeightSegments = 10,
        },
    ];

    [Theory]
    [MemberData(nameof(PlaneCases))]
    public void PlaneRoundTrip(PlaneOptions options)
    {
        AssertRoundTripEquivalent(MeshBuilders.Build(options), "plane");
    }

    /// <summary>
    /// Validates the source mesh, writes it as OBJ to a memory stream, reads it back, validates
    /// the result, and asserts semantic equivalence with the original. Disposes both meshes.
    /// </summary>
    /// <remarks>
    /// Also writes a copy of the OBJ under <c>TREditorSharp-obj-roundtrip\</c> next to the test
    /// assembly (<see cref="AppContext.BaseDirectory"/>) for ad-hoc inspection (e.g. Blender).
    /// Filename is <c><paramref name="objDumpStem"/>.obj</c>; existing files are overwritten so
    /// repeated test runs do not accumulate junk (multiple theory rows for the same stem
    /// overwrite each other as well).
    /// </remarks>
    private static void AssertRoundTripEquivalent(SpatialMesh expected, string objDumpStem)
    {
        try
        {
            expected.ValidateConsistency();

            using var ms = new MemoryStream();
            new ObjMeshWriter().Write(expected, ms);

            // Testing-only: duplicate OBJ bytes to disk for external viewers; round-trip still uses ms.
            var dumpDir = Path.Combine(AppContext.BaseDirectory, "TREditorSharp-obj-roundtrip");
            Directory.CreateDirectory(dumpDir);
            var dumpPath = Path.Combine(dumpDir, $"{objDumpStem}.obj");
            File.WriteAllBytes(dumpPath, ms.ToArray());

            ms.Position = 0;

            using var actual = new ObjMeshReader().Read(ms);
            actual.ValidateConsistency();

            AssertSemanticallyEquivalent(expected, actual);
        }
        finally
        {
            expected.Dispose();
        }
    }

    private static void AssertSemanticallyEquivalent(
        SpatialMesh expected,
        SpatialMesh actual,
        float tolerance = 1e-5f
    )
    {
        Assert.Equal(expected.Vertices.LiveCount, actual.Vertices.LiveCount);
        Assert.Equal(expected.Faces.LiveCount, actual.Faces.LiveCount);
        Assert.Equal(expected.HalfEdges.LiveCount, actual.HalfEdges.LiveCount);

        var expPositions = expected.Vertices.GetNativeColumn<Vector3, VertexPositionTag>();
        var actPositions = actual.Vertices.GetNativeColumn<Vector3, VertexPositionTag>();
        for (int dense = 0; dense < expected.Vertices.LiveCount; dense++)
        {
            var ep = expPositions[dense];
            var ap = actPositions[dense];
            Assert.True(
                Math.Abs(ep.X - ap.X) <= tolerance
                    && Math.Abs(ep.Y - ap.Y) <= tolerance
                    && Math.Abs(ep.Z - ap.Z) <= tolerance,
                $"Vertex {dense} position differs: expected {ep}, actual {ap} (tolerance {tolerance})."
            );
        }

        // Walk faces in dense order; both meshes built faces in OBJ order, which matches dense order.
        var expectedFaces = new List<FaceHandle>(expected.Faces.LiveCount);
        foreach (var f in expected.Faces.Live)
            expectedFaces.Add(f);
        var actualFaces = new List<FaceHandle>(actual.Faces.LiveCount);
        foreach (var f in actual.Faces.Live)
            actualFaces.Add(f);

        Assert.Equal(expectedFaces.Count, actualFaces.Count);

        var expCorners = new List<int>(8);
        var actCorners = new List<int>(8);
        for (int i = 0; i < expectedFaces.Count; i++)
        {
            CollectFaceCorners(expected, expectedFaces[i], expCorners);
            CollectFaceCorners(actual, actualFaces[i], actCorners);
            Assert.Equal(expCorners, actCorners);
        }
    }

    private static void CollectFaceCorners(SpatialMesh mesh, FaceHandle face, List<int> output)
    {
        output.Clear();
        foreach (var he in mesh.HalfEdgesAroundFace(face))
        {
            ref var halfEdge = ref mesh.HalfEdges.GetUnsafeRef<HalfEdge, HalfEdge>(he);
            output.Add(mesh.Vertices.GetDenseIndex(halfEdge.Origin));
        }
    }
}
