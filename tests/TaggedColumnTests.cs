using System.Numerics;

namespace TREditorSharp.Tests;

public class TaggedColumnTests
{
    // Phantom tag types: never instantiated, used only for type identity.
    public abstract class Position { private Position() { } }
    public abstract class VertexNormal { private VertexNormal() { } }
    public abstract class Tangent { private Tangent() { } }

    [Fact]
    public void TwoColumnsOfSameTypeWithDifferentTagsCoexist()
    {
        using var mesh = new HalfEdgeMesh();
        var pos = mesh.Vertices.RegisterColumn<Vector3, Position>();
        var nrm = mesh.Vertices.RegisterColumn<Vector3, VertexNormal>();

        Assert.NotSame(pos, nrm);

        var v = mesh.Vertices.Allocate();
        pos[v.Index] = new Vector3(1, 2, 3);
        nrm[v.Index] = new Vector3(0, 1, 0);

        Assert.Equal(new Vector3(1, 2, 3), pos[v.Index]);
        Assert.Equal(new Vector3(0, 1, 0), nrm[v.Index]);
    }

    [Fact]
    public void ManagedAndNativeColumnsOfSameTypeCoexistViaTags()
    {
        using var mesh = new HalfEdgeMesh();
        var pos = mesh.Vertices.RegisterNativeColumn<Vector3, Position>();
        var tan = mesh.Vertices.RegisterColumn<Vector3, Tangent>();

        var v = mesh.Vertices.Allocate();
        pos[v.Index] = new Vector3(5, 6, 7);
        tan[v.Index] = new Vector3(8, 9, 10);

        Assert.Equal(new Vector3(5, 6, 7), pos[v.Index]);
        Assert.Equal(new Vector3(8, 9, 10), tan[v.Index]);
    }

    [Fact]
    public void GetColumnByTagReturnsTheRegisteredInstance()
    {
        using var mesh = new HalfEdgeMesh();
        var registered = mesh.Vertices.RegisterColumn<Vector3, Position>();
        var fetched = mesh.Vertices.GetColumn<Vector3, Position>();
        Assert.Same(registered, fetched);
    }

    [Fact]
    public void GetColumnWithWrongElementTypeForTagThrows()
    {
        using var mesh = new HalfEdgeMesh();
        mesh.Vertices.RegisterColumn<Vector3, Position>();
        Assert.Throws<InvalidOperationException>(
            () => mesh.Vertices.GetColumn<float, Position>());
    }

    [Fact]
    public void GetNativeColumnAgainstManagedRegistrationThrows()
    {
        using var mesh = new HalfEdgeMesh();
        mesh.Vertices.RegisterColumn<Vector3, Position>();
        Assert.Throws<InvalidOperationException>(
            () => mesh.Vertices.GetNativeColumn<Vector3, Position>());
    }

    [Fact]
    public void RegisterColumnTwiceWithSameTagThrows()
    {
        using var mesh = new HalfEdgeMesh();
        mesh.Vertices.RegisterColumn<Vector3, Position>();
        Assert.Throws<InvalidOperationException>(
            () => mesh.Vertices.RegisterColumn<Vector3, Position>());
        Assert.Throws<InvalidOperationException>(
            () => mesh.Vertices.RegisterNativeColumn<Vector3, Position>());
    }

    [Fact]
    public void DefaultTagRegistrationStillWorks()
    {
        // The single-generic API uses the element type as its own tag; behavior
        // is unchanged from the pre-tag API.
        using var mesh = new HalfEdgeMesh();
        var col = mesh.Vertices.RegisterColumn<Vector3>();
        Assert.Same(col, mesh.Vertices.GetColumn<Vector3>());
        Assert.Same(col, mesh.Vertices.GetColumn<Vector3, Vector3>());

        Assert.True(mesh.Vertices.HasColumn<Vector3>());
        Assert.True(mesh.Vertices.HasColumnTag<Vector3>());
    }

    [Fact]
    public void DefaultTagAndExplicitDifferentTagAreIndependent()
    {
        using var mesh = new HalfEdgeMesh();
        var implicitCol = mesh.Vertices.RegisterColumn<Vector3>();          // tag = Vector3
        var explicitCol = mesh.Vertices.RegisterColumn<Vector3, Position>(); // tag = Position

        Assert.NotSame(implicitCol, explicitCol);
        Assert.True(mesh.Vertices.HasColumnTag<Vector3>());
        Assert.True(mesh.Vertices.HasColumnTag<Position>());
        Assert.False(mesh.Vertices.HasColumnTag<VertexNormal>());
    }

    [Fact]
    public void TaggedColumnsGrowTogetherWithThePool()
    {
        using var mesh = new HalfEdgeMesh(initialVertexCapacity: 2);
        var pos = mesh.Vertices.RegisterColumn<Vector3, Position>();
        var nrm = mesh.Vertices.RegisterColumn<Vector3, VertexNormal>();

        for (int i = 0; i < 50; i++) mesh.Vertices.Allocate();

        Assert.True(pos.Capacity >= mesh.Vertices.Capacity);
        Assert.True(nrm.Capacity >= mesh.Vertices.Capacity);
    }
}
