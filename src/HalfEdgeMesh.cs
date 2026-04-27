namespace TREditorSharp;

using TREditorSharp.Storage;

/// <summary>
/// Top-level Half-Edge Mesh facade. Owns one <see cref="TopologyStorage{TTag,TConnectivity}"/>
/// per entity kind (vertices, half-edges, faces), each of which manages its own
/// allocator and registered component columns.
///
/// The mesh itself stores no geometry — geometry is just a registered column.
/// This keeps the connectivity core minimal and decouples it from any particular
/// math library.
///
/// Implements <see cref="IDisposable"/> because user-registered native columns
/// own unmanaged memory.
/// </summary>
public sealed partial class HalfEdgeMesh : IDisposable
{
    public TopologyStorage<VertexTag, Vertex> Vertices { get; }

    public TopologyStorage<HalfEdgeTag, HalfEdge> HalfEdges { get; }

    public TopologyStorage<FaceTag, Face> Faces { get; }

    private bool _disposed;

    public HalfEdgeMesh()
    {
        Vertices = new TopologyStorage<VertexTag, Vertex>();
        HalfEdges = new TopologyStorage<HalfEdgeTag, HalfEdge>();
        Faces = new TopologyStorage<FaceTag, Face>();
    }

    /// <summary>
    /// Reset entire mesh. Backing memory is not released. All handles are invalidated.
    /// </summary>
    public void Clear()
    {
        Vertices.Clear();
        HalfEdges.Clear();
        Faces.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Vertices.Dispose();
        HalfEdges.Dispose();
        Faces.Dispose();
        GC.SuppressFinalize(this);
    }
}
