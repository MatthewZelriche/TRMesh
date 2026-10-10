using TRMesh.Containers;

namespace TRMesh.Mesh;

public partial class SpatialMesh
{
    // Copies topology and data while preserving sparse runtime handles. Serialized snapshots
    // intentionally compact handles, so they are unsuitable for transient copies that share
    // handle-based state.
    public SpatialMesh Clone()
    {
        var clone = new SpatialMesh();
        try
        {
            CopyTable(_vertices, clone._vertices, _vertices.IsAlive);
            CopyTable(_halfEdges, clone._halfEdges, _halfEdges.IsAlive);
            CopyTable(_faces, clone._faces, _faces.IsAlive);
            clone.MarkMutated();
            return clone;
        }
        catch
        {
            clone.Dispose();
            throw;
        }
    }

    private static void CopyTable<TRow>(
        IRecordableTable<TRow> source,
        IRecordableTable<TRow> target,
        Func<int, bool> isAlive
    )
        where TRow : unmanaged
    {
        for (int handle = 0; handle < source.SparseCount; handle++)
            target.Insert(isAlive(handle) ? source.Get(handle) : default);

        for (int handle = 0; handle < source.SparseCount; handle++)
        {
            if (!isAlive(handle))
                target.RemoveAt(handle);
        }
    }
}
