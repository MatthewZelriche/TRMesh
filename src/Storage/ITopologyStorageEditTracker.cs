namespace TREditorSharp.Storage;

/// <summary>
/// Receives entity lifecycle changes while a topology storage participates in an edit scope.
/// </summary>
internal interface ITopologyStorageEditTracker<TTag>
    where TTag : unmanaged
{
    void OnAllocated(Handle<TTag> handle);

    void OnReserved(EntitySnapshot<TTag> snapshot);
}
