namespace TREditorSharp;

/// <summary>
/// Top-level Half-Edge Mesh facade. Owns one <see cref="TopologyStorage{TTag,TConnectivity}"/>
/// per entity kind (vertices, half-edges, faces), each of which manages its own
/// allocator, alive-set, and registered component columns.
///
/// The mesh itself stores no geometry — geometry is just a registered column
/// (e.g. <c>RegisterColumn&lt;Vector3&gt;()</c> on <see cref="Vertices"/> for positions).
/// This keeps the connectivity core minimal and decouples it from any particular
/// math library.
///
/// Implements <see cref="IDisposable"/> because user-registered native columns
/// own unmanaged memory.
/// </summary>
public sealed class HalfEdgeMesh : IDisposable
{
    /// <summary>Vertex slot storage; user components attach here.</summary>
    public TopologyStorage<VertexTag, Vertex> Vertices { get; }

    /// <summary>Half-edge slot storage; user components attach here.</summary>
    public TopologyStorage<HalfEdgeTag, HalfEdge> HalfEdges { get; }

    /// <summary>Face slot storage; user components attach here.</summary>
    public TopologyStorage<FaceTag, Face> Faces { get; }

    private bool _disposed;

    public HalfEdgeMesh(
        int initialVertexCapacity = 16,
        int initialHalfEdgeCapacity = 32,
        int initialFaceCapacity = 16
    )
    {
        Vertices = new TopologyStorage<VertexTag, Vertex>(initialVertexCapacity);
        HalfEdges = new TopologyStorage<HalfEdgeTag, HalfEdge>(initialHalfEdgeCapacity);
        Faces = new TopologyStorage<FaceTag, Face>(initialFaceCapacity);
    }

    /// <summary>
    /// Reset all three storages without releasing their backing buffers. Existing
    /// handles are invalidated.
    /// </summary>
    public void Clear()
    {
        Vertices.Clear();
        HalfEdges.Clear();
        Faces.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Vertices.Dispose();
        HalfEdges.Dispose();
        Faces.Dispose();
        GC.SuppressFinalize(this);
    }
}
