using System.Numerics;
using TREditorSharp.Builders;

namespace TREditorSharp.Tests;

public class CylinderBuilderTests
{
    public static TheoryData<CylinderOptions, string> InvalidCases { get; } =
        new()
        {
            {
                new CylinderOptions
                {
                    Center = Vector3.Zero,
                    RadiusX = 0f,
                    RadiusZ = 1f,
                    Height = 1f,
                    RadialSegments = 8,
                },
                "RadiusX"
            },
            {
                new CylinderOptions
                {
                    Center = Vector3.Zero,
                    RadiusX = 1f,
                    RadiusZ = 0f,
                    Height = 1f,
                    RadialSegments = 8,
                },
                "RadiusZ"
            },
            {
                new CylinderOptions
                {
                    Center = Vector3.Zero,
                    RadiusX = 1f,
                    RadiusZ = 1f,
                    Height = 0f,
                    RadialSegments = 8,
                },
                "Height"
            },
            {
                new CylinderOptions
                {
                    Center = Vector3.Zero,
                    RadiusX = 1f,
                    RadiusZ = 1f,
                    Height = 1f,
                    RadialSegments = 2,
                },
                "RadialSegments"
            },
        };

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void BuildRejectsInvalidOptions(CylinderOptions options, string expectedMessageFragment)
    {
        var exception = Assert.Throws<ArgumentException>(() => MeshBuilders.Build(options));

        Assert.Contains(expectedMessageFragment, exception.Message);
    }

    [Fact]
    public void BuildCreatesExpectedTopologyCounts()
    {
        const int radialSegments = 7;
        using var mesh = MeshBuilders.Build(
            new CylinderOptions
            {
                Center = Vector3.Zero,
                RadiusX = 2f,
                RadiusZ = 1f,
                Height = 3f,
                RadialSegments = radialSegments,
            }
        );

        mesh.ValidateConsistency();

        Assert.Equal(2 * radialSegments, mesh.Vertices.LiveCount);
        Assert.Equal(radialSegments + 2, mesh.Faces.LiveCount);
    }

    [Fact]
    public void BuildCreatesExpectedExtents()
    {
        Vector3 center = new(10f, -2f, 3f);
        using var mesh = MeshBuilders.Build(
            new CylinderOptions
            {
                Center = center,
                RadiusX = 2f,
                RadiusZ = 4f,
                Height = 6f,
                RadialSegments = 4,
            }
        );

        mesh.ValidateConsistency();

        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);
        for (int i = 0; i < mesh.Vertices.LiveCount; i++)
        {
            Vector3 position = mesh.GetVertexPositionByDenseIndex(i);
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        Assert.Equal(new Vector3(8f, -5f, -1f), min);
        Assert.Equal(new Vector3(12f, 1f, 7f), max);
    }
}
