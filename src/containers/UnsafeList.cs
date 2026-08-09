using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TRMesh.Containers;

// Contiguous unmanaged list. Owns a native buffer via fields stored directly on the struct
// (no heap control block). Copying the struct aliases the same buffer — do not copy;
// transfer ownership explicitly or dispose only once.
public unsafe struct UnsafeList<T> : IDisposable
    where T : unmanaged
{
    public T* Ptr;
    public int Count;
    public int Capacity;

    public UnsafeList(int initialCapacity)
    {
        Ptr = null;
        Count = 0;
        Capacity = 0;
        if (initialCapacity > 0)
            SetCapacity(initialCapacity);
    }

    public readonly ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Debug.Assert((uint)index < (uint)Count);
            return ref Ptr[index];
        }
    }

    // Sets Count. Grows capacity when needed. Newly exposed elements are zeroed.
    public void Resize(int newCount)
    {
        Debug.Assert(newCount >= 0);

        if (newCount > Capacity)
            SetCapacity(newCount);

        if (newCount > Count)
            NativeMemory.Clear(Ptr + Count, (nuint)((newCount - Count) * sizeof(T)));

        Count = newCount;
    }

    public void Add(T item)
    {
        if (Count >= Capacity)
            SetCapacity(Capacity == 0 ? 4 : Capacity * 2);

        Ptr[Count++] = item;
    }

    // Removes the element at index, shifting the tail down to preserve order.
    public void RemoveAt(int index)
    {
        Debug.Assert((uint)index < (uint)Count);

        Count--;
        if (index < Count)
        {
            var bytes = (nuint)((Count - index) * sizeof(T));
            Buffer.MemoryCopy(Ptr + index + 1, Ptr + index, bytes, bytes);
        }
    }

    public void SetCapacity(int newCapacity)
    {
        Debug.Assert(newCapacity > 0);
        Debug.Assert(newCapacity >= Count);
        if (newCapacity == Capacity)
            return;

        var byteCount = (nuint)newCapacity * (nuint)sizeof(T);
        Ptr =
            Ptr == null
                ? (T*)NativeMemory.Alloc(byteCount)
                : (T*)NativeMemory.Realloc(Ptr, byteCount);
        Capacity = newCapacity;
    }

    public void Dispose()
    {
        if (Ptr != null)
        {
            NativeMemory.Free(Ptr);
            Ptr = null;
        }

        Count = 0;
        Capacity = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Enumerator GetEnumerator() => new(Ptr, Count);

    public ref struct Enumerator
    {
        readonly T* _ptr;
        readonly int _count;
        int _index;

        internal Enumerator(T* ptr, int count)
        {
            _ptr = ptr;
            _count = count;
            _index = -1;
        }

        public ref T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _ptr[_index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext() => ++_index < _count;
    }
}
