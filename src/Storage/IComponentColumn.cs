namespace TREditorSharp.Storage;

/// <summary>
/// Common contract for a typed, dense, swap-and-pop erased component column.
///
/// The owning <see cref="SlotPool{TTag}"/> drives the column lifecycle by
/// calling <see cref="Add"/> on every entity allocation and
/// <see cref="SwapRemoveAt"/> on every free. A column's <see cref="Count"/>
/// therefore mirrors the pool's live count exactly, and its element at dense
/// index <c>i</c> is the data for the live entity at dense index <c>i</c>.
///
/// Late registration is supported via <see cref="EnsureCount"/>: the pool
/// fills the column up to its current live count with default-initialized
/// entries before the column starts receiving Add/SwapRemoveAt calls.
///
/// The current implementation is <see cref="NativeColumn{T}"/>; additional
/// backends can implement this interface later. Implementations may dispose
/// native resources via <see cref="IDisposable"/>; storages will dispose any
/// column that implements it.
/// </summary>
public interface IComponentColumn
{
    /// <summary>The element type this column stores.</summary>
    Type ElementType { get; }

    /// <summary>Size in bytes of one component entry.</summary>
    int ElementSize { get; }

    /// <summary>Number of currently live entries (always equal to the pool's live count).</summary>
    int Count { get; }

    /// <summary>Backing buffer size in elements. Always >= Count.</summary>
    int Capacity { get; }

    /// <summary>
    /// Grow <see cref="Count"/> up to <paramref name="target"/>, default-initializing
    /// any newly added entries. No-op if already at or above <paramref name="target"/>.
    /// Used by the pool when registering a column after some allocations have
    /// already occurred, so that all columns have their size in sync.
    /// </summary>
    void EnsureCount(int target);

    /// <summary>
    /// Append a single default-initialized live entry. Increments
    /// <see cref="Count"/> by one; grows the backing buffer if needed.
    /// </summary>
    void Add();

    /// <summary>
    /// Remove the entry at <paramref name="index"/> by swapping the last live
    /// entry into its slot and then decrementing <see cref="Count"/>. The
    /// entry that was at <c>Count - 1</c> now lives at <paramref name="index"/>.
    /// </summary>
    void SwapRemoveAt(int index);

    /// <summary>
    /// Copy the component entry at <paramref name="denseIndex"/> into
    /// <paramref name="destination"/>. The destination length must exactly equal
    /// <see cref="ElementSize"/>.
    /// </summary>
    void CopyEntryTo(int denseIndex, Span<byte> destination);

    /// <summary>
    /// Restore the component entry at <paramref name="denseIndex"/> from
    /// <paramref name="source"/>. The source length must exactly equal
    /// <see cref="ElementSize"/>.
    /// </summary>
    void RestoreEntryFrom(int denseIndex, ReadOnlySpan<byte> source);

    /// <summary>
    /// Reset <see cref="Count"/> to zero. The backing buffer is retained.
    /// </summary>
    void Clear();
}
