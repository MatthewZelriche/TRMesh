namespace TREditorSharp;

/// <summary>
/// Common contract for a typed parallel array indexed by a slot index. Each
/// column is one "row" in the user's mental table model: every entity has one
/// element, and the element at index <c>i</c> belongs to the slot at index <c>i</c>.
///
/// Concrete implementations are <see cref="ManagedColumn{T}"/> and
/// <see cref="NativeColumn{T}"/>. The slot pool drives the column lifecycle by
/// calling <see cref="Resize"/> on grow and <see cref="OnSlotFreed"/> when a
/// slot is released. Implementations may dispose native resources via
/// <see cref="IDisposable"/>; storages will dispose any column that implements it.
/// </summary>
public interface IComponentColumn
{
    /// <summary>The element type this column stores.</summary>
    Type ElementType { get; }

    /// <summary>Current backing capacity (in number of elements).</summary>
    int Capacity { get; }

    /// <summary>
    /// Grow the backing storage to at least <paramref name="newCapacity"/> elements.
    /// Existing elements at lower indices must be preserved. Newly added elements
    /// must be zero-initialized. Shrinking is not supported.
    /// </summary>
    void Resize(int newCapacity);

    /// <summary>
    /// Notification that the slot at <paramref name="index"/> has been freed by
    /// the slot pool. Implementations should clear any GC references in the
    /// element so the GC can collect them; pure-unmanaged columns may no-op.
    /// </summary>
    void OnSlotFreed(int index);
}
