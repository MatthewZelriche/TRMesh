using System.Numerics;

namespace TREditorSharp.Tests;

public class TaggedColumnTests
{
    // Phantom tag types: never instantiated, used only for type identity.
    public abstract class Position
    {
        private Position() { }
    }

    public abstract class VertexNormal
    {
        private VertexNormal() { }
    }

    public abstract class Tangent
    {
        private Tangent() { }
    }

    [Fact]
    public void TwoColumnsOfSameTypeWithDifferentTagsCoexist()
    {
        using var mesh = new HalfEdgeMesh();
        var pos = mesh.Vertices.RegisterNativeColumn<Vector3, Position>();
        var nrm = mesh.Vertices.RegisterNativeColumn<Vector3, VertexNormal>();

        Assert.NotSame(pos, nrm);

        var v = mesh.Vertices.Allocate();
        int dense = mesh.Vertices.GetDenseIndex(v);
        pos[dense] = new Vector3(1, 2, 3);
        nrm[dense] = new Vector3(0, 1, 0);

        Assert.Equal(new Vector3(1, 2, 3), pos[dense]);
        Assert.Equal(new Vector3(0, 1, 0), nrm[dense]);
    }

    [Fact]
    public void TwoNativeColumnsOfSameTypeCoexistViaTags()
    {
        using var mesh = new HalfEdgeMesh();
        var pos = mesh.Vertices.RegisterNativeColumn<Vector3, Position>();
        var tan = mesh.Vertices.RegisterNativeColumn<Vector3, Tangent>();

        var v = mesh.Vertices.Allocate();
        int dense = mesh.Vertices.GetDenseIndex(v);
        pos[dense] = new Vector3(5, 6, 7);
        tan[dense] = new Vector3(8, 9, 10);

        Assert.Equal(new Vector3(5, 6, 7), pos[dense]);
        Assert.Equal(new Vector3(8, 9, 10), tan[dense]);
    }

    [Fact]
    public void GetComponentRoutesByElementTypeAndTag()
    {
        using var mesh = new HalfEdgeMesh();
        mesh.Vertices.RegisterNativeColumn<Vector3, Position>();
        mesh.Vertices.RegisterNativeColumn<Vector3, VertexNormal>();

        var v = mesh.Vertices.Allocate();
        int d = mesh.Vertices.GetDenseIndex(v);
        var posCol = mesh.Vertices.GetNativeColumn<Vector3, Position>();
        var nrmCol = mesh.Vertices.GetNativeColumn<Vector3, VertexNormal>();
        posCol[d] = new Vector3(1, 2, 3);
        nrmCol[d] = new Vector3(0, 1, 0);

        Assert.Equal(new Vector3(1, 2, 3), mesh.Vertices.GetComponent<Vector3, Position>(v));
        Assert.Equal(new Vector3(0, 1, 0), mesh.Vertices.GetComponent<Vector3, VertexNormal>(v));
    }

    [Fact]
    public void GetNativeColumnByTagReturnsTheRegisteredInstance()
    {
        using var mesh = new HalfEdgeMesh();
        var registered = mesh.Vertices.RegisterNativeColumn<Vector3, Position>();
        var fetched = mesh.Vertices.GetNativeColumn<Vector3, Position>();
        Assert.Same(registered, fetched);
    }

    [Fact]
    public void GetNativeColumnWithWrongElementTypeForTagThrows()
    {
        using var mesh = new HalfEdgeMesh();
        mesh.Vertices.RegisterNativeColumn<Vector3, Position>();
        Assert.Throws<InvalidOperationException>(mesh.Vertices.GetNativeColumn<float, Position>);
    }

    [Fact]
    public void RegisterNativeColumnTwiceWithSameTagThrows()
    {
        using var mesh = new HalfEdgeMesh();
        mesh.Vertices.RegisterNativeColumn<Vector3, Position>();
        Assert.Throws<InvalidOperationException>(() =>
            mesh.Vertices.RegisterNativeColumn<Vector3, Position>()
        );
    }

    [Fact]
    public void DefaultTagRegistrationStillWorks()
    {
        using var mesh = new HalfEdgeMesh();
        var col = mesh.Vertices.RegisterNativeColumn<Vector3>();
        Assert.Same(col, mesh.Vertices.GetNativeColumn<Vector3>());
        Assert.Same(col, mesh.Vertices.GetNativeColumn<Vector3, Vector3>());

        Assert.True(mesh.Vertices.HasColumn<Vector3>());
        Assert.True(mesh.Vertices.HasColumnTag<Vector3>());
    }

    [Fact]
    public void DefaultTagAndExplicitDifferentTagAreIndependent()
    {
        using var mesh = new HalfEdgeMesh();
        var implicitCol = mesh.Vertices.RegisterNativeColumn<Vector3>(); // tag = Vector3
        var explicitCol = mesh.Vertices.RegisterNativeColumn<Vector3, Position>(); // tag = Position

        Assert.NotSame(implicitCol, explicitCol);
        Assert.True(mesh.Vertices.HasColumnTag<Vector3>());
        Assert.True(mesh.Vertices.HasColumnTag<Position>());
        Assert.False(mesh.Vertices.HasColumnTag<VertexNormal>());
    }

    [Fact]
    public void TaggedColumnsTrackLiveCountTogether()
    {
        using var mesh = new HalfEdgeMesh();
        var pos = mesh.Vertices.RegisterNativeColumn<Vector3, Position>();
        var nrm = mesh.Vertices.RegisterNativeColumn<Vector3, VertexNormal>();

        for (int i = 0; i < 50; i++)
            mesh.Vertices.Allocate();

        Assert.Equal(mesh.Vertices.LiveCount, pos.Count);
        Assert.Equal(mesh.Vertices.LiveCount, nrm.Count);

        // Free a few and verify both columns shrink in lockstep.
        var first = default(VertexHandle);
        foreach (var h in mesh.Vertices)
        {
            first = h;
            break;
        }
        mesh.Vertices.Free(first);

        Assert.Equal(mesh.Vertices.LiveCount, pos.Count);
        Assert.Equal(mesh.Vertices.LiveCount, nrm.Count);
    }
}
