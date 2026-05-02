using System.Text;
using TREditorSharp.IO;

namespace TREditorSharp.Tests;

/// <summary>
/// Negative-path tests for <see cref="ObjMeshReader"/>. The reader's strict subset of OBJ is
/// otherwise covered by <see cref="BuilderRoundTripTests"/>; this class drives the error
/// branches directly so the contract documented on <see cref="ObjMeshReader"/> doesn't
/// silently regress.
/// </summary>
public class ObjReaderTests
{
    [Fact]
    public void UnsupportedTokenVnThrows()
    {
        var ex = Assert.Throws<NotSupportedException>(() => Read("v 0 0 0\nvn 0 0 1\n"));
        Assert.Contains("vn", ex.Message);
    }

    [Fact]
    public void UnsupportedTokenVtThrows()
    {
        Assert.Throws<NotSupportedException>(() => Read("vt 0 0\n"));
    }

    [Fact]
    public void UnsupportedTokenMtllibThrows()
    {
        Assert.Throws<NotSupportedException>(() => Read("mtllib foo.mtl\n"));
    }

    [Fact]
    public void UnknownTokenThrows()
    {
        Assert.Throws<NotSupportedException>(() => Read("xyz 1 2 3\n"));
    }

    [Fact]
    public void SlashedFaceRefThrows()
    {
        var src = "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1/1/1 2/2/2 3/3/3\n";
        var ex = Assert.Throws<NotSupportedException>(() => Read(src));
        Assert.Contains("/", ex.Message);
    }

    [Fact]
    public void NegativeFaceIndexThrows()
    {
        var src = "v 0 0 0\nv 1 0 0\nv 0 1 0\nf -1 -2 -3\n";
        var ex = Assert.Throws<NotSupportedException>(() => Read(src));
        Assert.Contains("-1", ex.Message);
    }

    [Fact]
    public void VertexWithTwoComponentsThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Read("v 1.0 2.0\n"));
    }

    [Fact]
    public void VertexWithFourComponentsThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Read("v 1 2 3 4\n"));
    }

    [Fact]
    public void OutOfRangeFaceIndexThrowsFormatException()
    {
        var src = "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 99\n";
        Assert.Throws<FormatException>(() => Read(src));
    }

    [Fact]
    public void FaceWithOnlyTwoIndicesThrowsFormatException()
    {
        var src = "v 0 0 0\nv 1 0 0\nf 1 2\n";
        Assert.Throws<FormatException>(() => Read(src));
    }

    [Fact]
    public void NonNumericVertexComponentThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Read("v 1 abc 3\n"));
    }

    [Fact]
    public void CommentsAndObjectNameAreSkipped()
    {
        var src = "# comment\no my-object\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n";
        using var mesh = Read(src);
        Assert.Equal(3, mesh.Vertices.LiveCount);
        Assert.Equal(1, mesh.Faces.LiveCount);
    }

    [Fact]
    public void EmptyAndWhitespaceLinesAreSkipped()
    {
        var src = "\n   \nv 0 0 0\n\nv 1 0 0\n\nv 0 1 0\n\nf 1 2 3\n   \n";
        using var mesh = Read(src);
        Assert.Equal(3, mesh.Vertices.LiveCount);
        Assert.Equal(1, mesh.Faces.LiveCount);
    }

    private static SpatialMesh Read(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        using var ms = new MemoryStream(bytes);
        return new ObjMeshReader().Read(ms);
    }
}
