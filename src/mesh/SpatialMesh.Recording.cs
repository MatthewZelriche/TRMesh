using TRMesh.Containers;

namespace TRMesh.Mesh;

// Exactly reverses one operation recorded by SpatialMesh.Record.
public sealed class MeshPatch
{
    internal MeshPatch(
        TablePatch<VertexData> vertices,
        TablePatch<HalfEdgeData> halfEdges,
        TablePatch<FaceData> faces,
        long revisionBefore,
        long revisionAfter
    )
    {
        Vertices = vertices;
        HalfEdges = halfEdges;
        Faces = faces;
        RevisionBefore = revisionBefore;
        RevisionAfter = revisionAfter;
    }

    internal TablePatch<VertexData> Vertices { get; }
    internal TablePatch<HalfEdgeData> HalfEdges { get; }
    internal TablePatch<FaceData> Faces { get; }
    internal long RevisionBefore { get; }
    internal long RevisionAfter { get; }

    public int ByteSize => Vertices.ByteSize + HalfEdges.ByteSize + Faces.ByteSize;
}

public partial class SpatialMesh
{
    // Revisions are unique across meshes, so a patch can only match the mesh state it was recorded on.
    static long s_lastRevision;
    long _revision;

    // Runs operation and returns the patch that reverts it. Running the same deterministic operation
    // again after reverting reproduces the same handles. If operation throws, the mesh is reverted
    // before the exception propagates.
    public MeshPatch Record(Action<SpatialMesh> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (_vertexRecorder.IsRecording)
            throw new InvalidOperationException("A mesh operation is already being recorded.");

        long revisionBefore = _revision;
        _vertexRecorder.Begin();
        _halfEdgeRecorder.Begin();
        _faceRecorder.Begin();
        try
        {
            operation(this);
        }
        catch
        {
            RevertTables(EndRecording(revisionBefore));
            _revision = revisionBefore;
            throw;
        }

        return EndRecording(revisionBefore);
    }

    // Reverts the most recently recorded operation that has not yet been reverted.
    public void Revert(MeshPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        if (_vertexRecorder.IsRecording || _revision != patch.RevisionAfter)
            throw new InvalidOperationException("The patch does not match the current mesh state.");

        RevertTables(patch);
        _revision = patch.RevisionBefore;
    }

    void MarkMutated() => _revision = Interlocked.Increment(ref s_lastRevision);

    MeshPatch EndRecording(long revisionBefore) =>
        new(
            _vertexRecorder.End(),
            _halfEdgeRecorder.End(),
            _faceRecorder.End(),
            revisionBefore,
            _revision
        );

    void RevertTables(MeshPatch patch)
    {
        _vertexRecorder.Revert(patch.Vertices);
        _halfEdgeRecorder.Revert(patch.HalfEdges);
        _faceRecorder.Revert(patch.Faces);
    }
}
