using System.Numerics;
using TREditorSharp.Storage;

namespace TREditorSharp.Tests;

public class NativeColumnTests
{
    [Fact]
    public void NativeColumnRoundTripsValues()
    {
        using var mesh = new HalfEdgeMesh();
        var pos = mesh.Vertices.RegisterNativeColumn<Vector3>();

        var handles = new VertexHandle[64];
        for (int i = 0; i < handles.Length; i++)
        {
            handles[i] = mesh.Vertices.Allocate();
            pos[mesh.Vertices.GetDenseIndex(handles[i])] = new Vector3(i, -i, i * 0.5f);
        }

        for (int i = 0; i < handles.Length; i++)
        {
            Assert.Equal(
                new Vector3(i, -i, i * 0.5f),
                pos[mesh.Vertices.GetDenseIndex(handles[i])]
            );
        }
    }

    [Fact]
    public unsafe void NativeColumnGrowZeroesNewTail()
    {
        using var mesh = new HalfEdgeMesh();
        var col = mesh.Vertices.RegisterNativeColumn<int>();

        // Force at least one geometric grow by allocating well past the initial capacity.
        for (int i = 0; i < 4; i++)
            col[mesh.Vertices.GetDenseIndex(mesh.Vertices.Allocate())] = 999;

        var oldCapacity = col.Capacity;
        for (int i = 0; i < 100; i++)
            mesh.Vertices.Allocate();

        Assert.True(col.Capacity > oldCapacity);
        // Past the live region the buffer must be zeroed.
        for (int i = col.Count; i < col.Capacity; i++)
            Assert.Equal(0, *(col.DataPtr + i));
    }

    [Fact]
    public void NativeColumnClearRetainsCapacityThenAddsReuseBuffer()
    {
        using var col = new NativeColumn<int>();
        for (int i = 0; i < 16; i++)
            col.Add();
        var capAfterAdds = col.Capacity;
        col.Clear();
        Assert.Equal(0, col.Count);
        Assert.Equal(capAfterAdds, col.Capacity);
        col.Add();
        col.Add();
        Assert.Equal(2, col.Count);
        Assert.True(col.Capacity >= capAfterAdds);
    }

    [Fact]
    public void NativeColumnDisposesViaMesh()
    {
        var mesh = new HalfEdgeMesh();
        var col = mesh.Vertices.RegisterNativeColumn<long>();
        var v = mesh.Vertices.Allocate();
        col[mesh.Vertices.GetDenseIndex(v)] = 42;
        mesh.Dispose();

        // After disposal, attempting to mutate should throw rather than corrupt memory.
        Assert.Throws<ObjectDisposedException>(() => col.Add());
    }

    [Fact]
    public unsafe void NativeColumnPreservesValuesAcrossGrow()
    {
        using var mesh = new HalfEdgeMesh();
        var col = mesh.Vertices.RegisterNativeColumn<long>();

        var handles = new List<VertexHandle>();
        for (int i = 0; i < 50; i++)
        {
            var v = mesh.Vertices.Allocate();
            handles.Add(v);
            col[mesh.Vertices.GetDenseIndex(v)] = (long)i * 1_000_000_007L;
        }

        for (int i = 0; i < handles.Count; i++)
        {
            Assert.Equal((long)i * 1_000_000_007L, col[mesh.Vertices.GetDenseIndex(handles[i])]);
        }
    }

    [Fact]
    public void NativeColumnIndexerOutOfRangeThrows()
    {
        using var col = new NativeColumn<int>();
        col.Add();
        // Live region is [0, 1); 1 is out of range.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            int _ = col[1];
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => col[1] = 0);
    }

    [Fact]
    public void NativeColumnSwapRemoveAtMovesLastIntoIndex()
    {
        using var col = new NativeColumn<int>();
        for (int i = 0; i < 5; i++)
        {
            col.Add();
            col[i] = i + 1; // 1, 2, 3, 4, 5
        }

        col.SwapRemoveAt(1);
        Assert.Equal(4, col.Count);
        Assert.Equal(new[] { 1, 5, 3, 4 }, col.AsSpan().ToArray());
    }

    [Fact]
    public void NativeColumnAsSpanIsTrimmedToCount()
    {
        using var col = new NativeColumn<int>();
        for (int i = 0; i < 24; i++)
            col.Add();
        col.Clear();
        col.Add();
        col.Add();
        col[0] = 7;
        col[1] = 8;

        var span = col.AsSpan();
        Assert.Equal(2, span.Length);
        Assert.Equal(7, span[0]);
        Assert.Equal(8, span[1]);
    }

    [Fact]
    public void NativeColumnEnsureCountFillsWithDefault()
    {
        using var col = new NativeColumn<int>();
        col.EnsureCount(10);
        Assert.Equal(10, col.Count);
        for (int i = 0; i < 10; i++)
            Assert.Equal(0, col[i]);
    }

    [Fact]
    public void NativeColumnClearResetsCountButKeepsCapacity()
    {
        using var col = new NativeColumn<int>();
        for (int i = 0; i < 8; i++)
            col.Add();
        var capBefore = col.Capacity;

        col.Clear();
        Assert.Equal(0, col.Count);
        Assert.Equal(capBefore, col.Capacity);

        col.Add();
        Assert.Equal(0, col[0]); // freshly added entry is default
    }
}
