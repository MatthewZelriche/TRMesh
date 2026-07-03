using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TREditorSharp.Storage;

/// <summary>
/// Component column backed by aligned native (non-GC) memory. Restricted to
/// unmanaged element types so the buffer can be moved/zeroed by raw memory ops
/// without involving the GC.
///
/// Storage is dense and swap-packed: live entries occupy <c>[0, Count)</c>.
/// The owning <see cref="SlotPool{TTag}"/> drives <see cref="Add"/> and
/// <see cref="SwapRemoveAt"/> so the column always mirrors the pool's
/// live-entity dense order. Untouched tail bytes are kept zeroed on grow,
/// so newly added entries observe <c>default(T)</c>.
///
/// Must be disposed; a finalizer is provided as a safety net but should never be the primary
/// release path because finalizers run on a background thread.
/// </summary>
public sealed unsafe class NativeColumn<T> : IComponentColumn, IDisposable
    where T : unmanaged
{
    private const nuint Alignment = 64;

    private T* _data;
    private int _capacity;
    private int _count;
    private bool _disposed;

    public Type ElementType => typeof(T);

    public int ElementSize => sizeof(T);

    public int Count => _count;

    public int Capacity => _capacity;

    /// <summary>Raw pointer to the backing buffer. Valid until any grow or Dispose.</summary>
    public T* DataPtr
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _data;
    }

    public NativeColumn() { }

    public NativeColumn(int initialCapacity)
    {
        if (initialCapacity > 0)
            Grow(initialCapacity);
    }

    /// <summary>
    /// Bounds-checked by-value accessor over the live region <c>[0, Count)</c>.
    /// Throws if <paramref name="index"/> is out of range. Reads return a copy;
    /// writes assign the whole value back, matching <see cref="List{T}"/> semantics.
    /// </summary>
    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)_count)
                ThrowOutOfRange(index);
            return *(_data + index);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if ((uint)index >= (uint)_count)
                ThrowOutOfRange(index);
            *(_data + index) = value;
        }
    }

    /// <summary>
    /// <para>
    /// Bounds-check-elided direct reference into the backing buffer at <paramref name="index"/>.
    /// This is the zero-copy counterpart to the <see cref="this[int]"/> indexer.
    /// </para>
    /// <para>
    /// <b>Unsafe lifetime contract (read carefully):</b> the returned <see langword="ref"/>
    /// <typeparamref name="T"/> is valid
    /// only while this column’s backing storage for slot <paramref name="index"/> remains unmutated.
    /// It becomes invalid immediately if any of the following occur before you are done using the ref.
    /// Never modify the backing storage while a ref exists.
    /// </para>
    /// <para>
    /// Caller must guarantee index is in bounds.
    /// </para>
    /// </summary>
    /// <param name="index">Dense index into the live region <c>[0, Count)</c>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T UnsafeRef(int index) => ref Unsafe.AsRef<T>(_data + index);

    /// <summary>
    /// Span over the live region <c>[0, Count)</c>. Becomes invalid after any
    /// grow or Dispose.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> AsSpan() => new(_data, _count);

    public void EnsureCount(int target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (target <= _count)
            return;
        if (target > _capacity)
            Grow(target);
        // The grown tail is already zeroed; just expose it as live.
        _count = target;
    }

    public void Add()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_count == _capacity)
            Grow(_count + 1);
        // New tail slot is already zeroed by Grow / by initial calloc-equivalent.
        _count++;
    }

    public void SwapRemoveAt(int index)
    {
        if ((uint)index >= (uint)_count)
            ThrowOutOfRange(index);
        int last = _count - 1;
        if (index != last)
            *(_data + index) = *(_data + last);
        // Zero the now-dead tail so a future Add observes default(T).
        *(_data + last) = default;
        _count--;
    }

    public void CopyEntryTo(int denseIndex, Span<byte> destination)
    {
        ValidateEntryBuffer(destination.Length, nameof(destination));
        if ((uint)denseIndex >= (uint)_count)
            ThrowOutOfRange(denseIndex);

        new ReadOnlySpan<byte>(_data + denseIndex, sizeof(T)).CopyTo(destination);
    }

    public void RestoreEntryFrom(int denseIndex, ReadOnlySpan<byte> source)
    {
        ValidateEntryBuffer(source.Length, nameof(source));
        if ((uint)denseIndex >= (uint)_count)
            ThrowOutOfRange(denseIndex);

        source.CopyTo(new Span<byte>(_data + denseIndex, sizeof(T)));
    }

    public void Clear()
    {
        if (_count == 0)
            return;
        // Zero the previously live region so a subsequent Add reuses default(T) tails.
        NativeMemory.Clear(_data, (nuint)_count * (nuint)sizeof(T));
        _count = 0;
    }

    /// <summary>
    /// Internal geometric grow. Allocates a fresh aligned buffer with capacity
    /// at least <paramref name="minCapacity"/>, copies live data, frees the
    /// old buffer, and zeroes the freshly added tail.
    /// </summary>
    private void Grow(int minCapacity)
    {
        int newCapacity = _capacity == 0 ? Math.Max(4, minCapacity) : _capacity * 2;
        if (newCapacity < minCapacity)
            newCapacity = minCapacity;

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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_data != null)
        {
            NativeMemory.AlignedFree(_data);
            _data = null;
            _capacity = 0;
            _count = 0;
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

    private static void ValidateEntryBuffer(int length, string parameterName)
    {
        if (length != sizeof(T))
        {
            throw new ArgumentException(
                $"Component entry buffer must be exactly {sizeof(T)} bytes, but was {length}.",
                parameterName
            );
        }
    }
}
