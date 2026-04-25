using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TREditorSharp;

/// <summary>
/// Component column backed by aligned native (non-GC) memory. Restricted to
/// unmanaged element types so the buffer can be moved/zeroed by raw memory ops
/// without involving the GC. Storage is allocated via
/// <see cref="NativeMemory.AlignedAlloc"/> at a 64-byte alignment, suitable for
/// AVX-512 SIMD loads.
///
/// Must be disposed; ownership flows through <see cref="TopologyStorage{TTag}"/>
/// up to <see cref="HalfEdgeMesh"/>, which is itself <see cref="IDisposable"/>.
/// A finalizer is provided as a safety net but should never be the primary
/// release path because finalizers run on a background thread.
/// </summary>
public sealed unsafe class NativeColumn<T> : IComponentColumn, IDisposable
    where T : unmanaged
{
    private const nuint Alignment = 64;

    private T* _data;
    private int _capacity;
    private bool _disposed;

    public Type ElementType => typeof(T);

    public int Capacity => _capacity;

    /// <summary>Raw pointer to the backing buffer. Valid until Resize or Dispose.</summary>
    public T* DataPtr
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _data;
    }

    public NativeColumn() { }

    public NativeColumn(int initialCapacity)
    {
        if (initialCapacity > 0) Resize(initialCapacity);
    }

    /// <summary>
    /// Bounds-checked ref accessor. Throws if <paramref name="index"/> is out of range.
    /// </summary>
    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)_capacity)
                ThrowOutOfRange(index);
            return ref Unsafe.AsRef<T>(_data + index);
        }
    }

    /// <summary>
    /// Bounds-check-elided ref accessor. Caller must guarantee
    /// <c>0 &lt;= index &lt; Capacity</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T UnsafeRef(int index) => ref Unsafe.AsRef<T>(_data + index);

    /// <summary>
    /// Span over the entire backing buffer. Becomes invalid after Resize/Dispose.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> AsSpan() => new(_data, _capacity);

    public void Resize(int newCapacity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (newCapacity <= _capacity) return;

        nuint newBytes = (nuint)newCapacity * (nuint)sizeof(T);
        T* newPtr = (T*)NativeMemory.AlignedAlloc(newBytes, Alignment);

        if (_capacity > 0)
        {
            nuint oldBytes = (nuint)_capacity * (nuint)sizeof(T);
            NativeMemory.Copy(_data, newPtr, oldBytes);
            NativeMemory.AlignedFree(_data);
        }

        // Zero the freshly added tail so newly grown slots read as default(T).
        nuint tailBytes = (nuint)(newCapacity - _capacity) * (nuint)sizeof(T);
        NativeMemory.Clear(newPtr + _capacity, tailBytes);

        _data = newPtr;
        _capacity = newCapacity;
    }

    public void OnSlotFreed(int index)
    {
        // Unmanaged data has no GC references to clear. Stale slot bytes are never
        // read while dead (handle generation enforces this). Skip writes for perf.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_data != null)
        {
            NativeMemory.AlignedFree(_data);
            _data = null;
            _capacity = 0;
        }
        GC.SuppressFinalize(this);
    }

    ~NativeColumn()
    {
        if (!_disposed && _data != null)
        {
            NativeMemory.AlignedFree(_data);
            _data = null;
        }
    }

    private static void ThrowOutOfRange(int index) =>
        throw new ArgumentOutOfRangeException(nameof(index), index, "Index out of range.");
}
