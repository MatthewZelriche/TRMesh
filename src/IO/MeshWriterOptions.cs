namespace TREditorSharp.IO;

/// <summary>
/// Cross-format options for writing a mesh to a <see cref="Stream"/>.
/// </summary>
/// <remarks>
/// <para>
/// For <see cref="ObjMeshWriter"/>, faces with fewer than three boundary vertices, an empty half-edge
/// loop, or failed ear clipping when <see cref="TriangulateFaces"/> is <c>true</c> are omitted; the
/// rest of the mesh is still written.
/// </para>
/// </remarks>
public class MeshWriterOptions
{
    /// <summary>
    /// When <c>true</c> (default), disposing the text writer does not close the destination
    /// stream passed to a mesh writer; the caller owns the stream lifecycle.
    /// </summary>
    public bool LeaveStreamOpen { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, writers that support triangulation (e.g. <see cref="ObjMeshWriter"/>) emit
    /// triangles instead of arbitrary n-gons where applicable. Writers that do not use this flag ignore it.
    /// </summary>
    public bool TriangulateFaces { get; init; }
}
