using System.Numerics;

namespace TREditorSharp.Tests;

public class NativeColumnTests
{
    [Fact]
    public void NativeColumnRoundTripsValues()
    {
        using var mesh = new HalfEdgeMesh(initialVertexCapacity: 2);
        var pos = mesh.Vertices.RegisterNativeColumn<Vector3>();

        var handles = new VertexHandle[64];
        for (int i = 0; i < handles.Length; i++)
        {
            handles[i] = mesh.Vertices.Allocate();
            pos[handles[i].Index] = new Vector3(i, -i, i * 0.5f);
        }

        for (int i = 0; i < handles.Length; i++)
        {
            Assert.Equal(new Vector3(i, -i, i * 0.5f), pos[handles[i].Index]);
        }
    }

    [Fact]
    public unsafe void NativeColumnIs64ByteAligned()
    {
        using var mesh = new HalfEdgeMesh();
        var col = mesh.Vertices.RegisterNativeColumn<float>();
        for (int i = 0; i < 100; i++) mesh.Vertices.Allocate();

        var ptr = (nuint)col.DataPtr;
        Assert.Equal(0u, (uint)(ptr % 64));
    }

    [Fact]
    public unsafe void NativeColumnGrowZeroesNewTail()
    {
        using var mesh = new HalfEdgeMesh(initialVertexCapacity: 4);
        var col = mesh.Vertices.RegisterNativeColumn<int>();

        for (int i = 0; i < 4; i++) col[mesh.Vertices.Allocate().Index] = 999;

        var oldCapacity = col.Capacity;
        for (int i = 0; i < 100; i++) mesh.Vertices.Allocate();

        for (int i = oldCapacity; i < col.Capacity; i++)
        {
            Assert.Equal(0, col[i]);
        }
    }

    [Fact]
    public void NativeColumnDisposesViaMesh()
    {
        var mesh = new HalfEdgeMesh();
        var col = mesh.Vertices.RegisterNativeColumn<long>();
        var v = mesh.Vertices.Allocate();
        col[v.Index] = 42;
        mesh.Dispose();

        // After disposal, attempting to resize should throw rather than corrupt memory.
        Assert.Throws<ObjectDisposedException>(() => col.Resize(1024));
    }

    [Fact]
    public unsafe void NativeColumnPreservesValuesAcrossResize()
    {
        using var mesh = new HalfEdgeMesh(initialVertexCapacity: 2);
        var col = mesh.Vertices.RegisterNativeColumn<long>();

        var handles = new List<VertexHandle>();
        for (int i = 0; i < 50; i++)
        {
            var v = mesh.Vertices.Allocate();
            handles.Add(v);
            col[v.Index] = (long)i * 1_000_000_007L;
        }

        for (int i = 0; i < handles.Count; i++)
        {
            Assert.Equal((long)i * 1_000_000_007L, col[handles[i].Index]);
        }
    }
}
