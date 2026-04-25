using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TREditorSharp;

/// <summary>
/// Component column backed by a managed <c>T[]</c>. Works for any element type,
/// including value types containing managed references (which are cleared on
/// slot free so the GC can reclaim them).
///
/// The hot path is <c>column.Data[index]</c> or <c>ref column[index]</c>; both
/// return a ref to the element. The exposed <see cref="Data"/> array reference
/// becomes stale after a Grow, so callers iterating in a tight loop should
/// re-read it (or call <see cref="AsSpan"/>) after any operation that may
/// trigger growth.
/// </summary>
public sealed class ManagedColumn<T> : IComponentColumn
{
    /// <summary>
    /// Backing array. Indexed by slot index. Reference may be replaced by Resize.
    /// </summary>
    public T[] Data { get; private set; } = Array.Empty<T>();

    public Type ElementType => typeof(T);

    public int Capacity => Data.Length;

    public ManagedColumn() { }

    public ManagedColumn(int initialCapacity)
    {
        if (initialCapacity > 0) Data = new T[initialCapacity];
    }

    /// <summary>
    /// Get a ref to the element at the given slot index. No generation check.
    /// </summary>
    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref Data[index];
    }

    /// <summary>
    /// Bounds-check-elided ref accessor. Caller must guarantee
    /// <c>0 &lt;= index &lt; Capacity</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T UnsafeRef(int index) =>
        ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(Data), index);

    /// <summary>Span over the entire backing array (live and dead slots alike).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> AsSpan() => Data.AsSpan();

    public void Resize(int newCapacity)
    {
        if (newCapacity <= Data.Length) return;
        var arr = Data;
        Array.Resize(ref arr, newCapacity);
        Data = arr;
    }

    public void OnSlotFreed(int index)
    {
        // Clear the element so the GC can reclaim referenced data. For pure-blittable
        // types (no GC refs) the JIT elides this branch entirely as a constant.
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Data[index] = default!;
        }
    }
}
