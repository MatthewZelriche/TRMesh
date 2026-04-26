using System.Runtime.CompilerServices;

namespace TREditorSharp.Tests;

public class HandleTests
{
    [Fact]
    public void DefaultHandleIsNull()
    {
        Assert.True(default(VertexHandle).IsNull);
        Assert.True(default(HalfEdgeHandle).IsNull);
        Assert.True(default(FaceHandle).IsNull);
        Assert.Equal(VertexHandle.Null, default(VertexHandle));
        Assert.Equal(HalfEdgeHandle.Null, default(HalfEdgeHandle));
        Assert.Equal(FaceHandle.Null, default(FaceHandle));
    }

    [Fact]
    public void HandleIsExactlyEightBytes()
    {
        Assert.Equal(8, Unsafe.SizeOf<VertexHandle>());
        Assert.Equal(8, Unsafe.SizeOf<HalfEdgeHandle>());
        Assert.Equal(8, Unsafe.SizeOf<FaceHandle>());
    }

    [Fact]
    public void HandleEqualityIsValueBased()
    {
        var a = new VertexHandle(3, 7);
        var b = new VertexHandle(3, 7);
        var c = new VertexHandle(3, 8);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.NotEqual(a, c);
        Assert.True(a != c);
    }
}
