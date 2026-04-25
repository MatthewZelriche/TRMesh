using System.Numerics;
using System.Runtime.CompilerServices;

namespace TREditorSharp;

/// <summary>
/// Generic slot allocator providing O(1) Allocate/Free with stable, generationally-versioned
/// keys. Storage grows geometrically; free slots are tracked by an intrusive integer free-list,
/// and live slots are tracked in a packed bitset for fast skip-during-iterate.
///
/// The <typeparamref name="TTag"/> phantom parameter makes <see cref="SlotPool{TTag}"/>
/// instantiations distinct types so that pools for different entity kinds cannot be mixed
/// up at the type system level. Returned handles are <see cref="Handle{TTag}"/>.
/// </summary>
internal sealed class SlotPool<TTag> where TTag : unmanaged
{
    private const int DefaultInitialCapacity = 16;

    private int[] _generations;
    private ulong[] _aliveBits;
    private int[] _nextFree;
    private readonly List<IComponentColumn> _columns = new();

    private int _freeHead = -1;
    private int _highWater;

    public SlotPool(int initialCapacity = DefaultInitialCapacity)
    {
        if (initialCapacity < 1) initialCapacity = 1;
        _generations = new int[initialCapacity];
        _aliveBits = new ulong[(initialCapacity + 63) >> 6];
        _nextFree = new int[initialCapacity];
    }

    /// <summary>Number of currently live slots.</summary>
    public int LiveCount { get; private set; }

    /// <summary>Total slot capacity (live + free + unused-tail).</summary>
    public int Capacity => _generations.Length;

    /// <summary>
    /// One past the highest slot index that has ever been issued. Iteration only needs
    /// to scan up to this, not to <see cref="Capacity"/>.
    /// </summary>
    public int HighWater => _highWater;

    /// <summary>
    /// Register a component column with this pool. The column will be resized to the
    /// pool's current capacity immediately, and again whenever the pool grows. The column
    /// will receive <see cref="IComponentColumn.OnSlotFreed"/> callbacks during Free.
    /// </summary>
    public void RegisterColumn(IComponentColumn column)
    {
        column.Resize(_generations.Length);
        _columns.Add(column);
    }

    /// <summary>Allocate a fresh slot. O(1).</summary>
    public Handle<TTag> Allocate()
    {
        int index;
        if (_freeHead >= 0)
        {
            index = _freeHead;
            _freeHead = _nextFree[index];
        }
        else
        {
            if (_highWater == _generations.Length) Grow();
            index = _highWater++;
        }

        // Generation parity: even (incl. 0) = dead, odd = live. Bumping flips parity.
        int gen = ++_generations[index];
        SetAliveBit(index, true);
        LiveCount++;
        return new Handle<TTag>(index, gen);
    }

    /// <summary>
    /// Free a previously allocated slot. The handle's generation must match. Always
    /// validated (regardless of MESH_VALIDATE) to prevent double-free corruption of
    /// the free list.
    /// </summary>
    public void Free(Handle<TTag> k)
    {
        ThrowIfNotLive(k);

        // Notify columns. Done before invalidating so the column can read its old value
        // if it needs to (e.g. for managed-reference clearing).
        var cols = _columns;
        for (int i = 0; i < cols.Count; i++) cols[i].OnSlotFreed(k.Index);

        _generations[k.Index]++; // flip parity to dead and invalidate any extant handles
        SetAliveBit(k.Index, false);
        _nextFree[k.Index] = _freeHead;
        _freeHead = k.Index;
        LiveCount--;
    }

    /// <summary>
    /// Returns true iff <paramref name="k"/> refers to a currently live slot whose
    /// generation matches the slot's current generation. O(1) and never throws.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAlive(Handle<TTag> k)
    {
        if ((uint)k.Index >= (uint)_highWater) return false;
        return _generations[k.Index] == k.Generation && GetAliveBit(k.Index);
    }

    /// <summary>
    /// Validate the handle. In a MESH_VALIDATE build (Debug by default) throws if the
    /// handle is null, out of range, stale, or refers to a freed slot. Compiled out in
    /// release builds for max throughput.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ValidateLive(Handle<TTag> k)
    {
#if MESH_VALIDATE
        ThrowIfNotLive(k);
#endif
    }

    /// <summary>Reset the pool to empty without freeing the underlying buffers.</summary>
    public void Clear()
    {
        Array.Clear(_aliveBits);
        // Bumping every generation to even invalidates all handles.
        for (int i = 0; i < _highWater; i++)
        {
            int g = _generations[i];
            if ((g & 1) == 1) _generations[i] = g + 1;
        }
        _freeHead = -1;
        _highWater = 0;
        LiveCount = 0;
    }

    /// <summary>
    /// Iterate live slots in ascending index order. Uses bit-tricks to skip whole
    /// 64-slot blocks of dead slots in O(1).
    /// </summary>
    public LiveEnumerator GetEnumerator() => new(this);

    public LiveEnumerable Live => new(this);

    private void ThrowIfNotLive(Handle<TTag> k)
    {
        if (k.IsNull)
            throw new ArgumentException("Null handle is not valid.");
        if ((uint)k.Index >= (uint)_highWater)
            throw new ArgumentOutOfRangeException(
                nameof(k), $"Handle index {k.Index} out of range (high water = {_highWater}).");
        if (_generations[k.Index] != k.Generation)
            throw new InvalidOperationException(
                $"Stale handle: slot {k.Index} handle gen {k.Generation} but current gen is {_generations[k.Index]}.");
        if (!GetAliveBit(k.Index))
            throw new InvalidOperationException(
                $"Handle refers to a freed slot {k.Index}.");
    }

    private void Grow()
    {
        int oldCap = _generations.Length;
        int newCap = oldCap * 2;
        Array.Resize(ref _generations, newCap);
        Array.Resize(ref _nextFree, newCap);
        int newWords = (newCap + 63) >> 6;
        if (newWords != _aliveBits.Length)
            Array.Resize(ref _aliveBits, newWords);

        var cols = _columns;
        for (int i = 0; i < cols.Count; i++) cols[i].Resize(newCap);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetAliveBit(int index, bool value)
    {
        ref ulong word = ref _aliveBits[index >> 6];
        ulong mask = 1UL << (index & 63);
        if (value) word |= mask;
        else word &= ~mask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool GetAliveBit(int index) =>
        (_aliveBits[index >> 6] & (1UL << (index & 63))) != 0;

    /// <summary>
    /// Lightweight wrapper exposing the enumerator via a foreach-friendly Live property.
    /// </summary>
    public readonly ref struct LiveEnumerable
    {
        private readonly SlotPool<TTag> _pool;
        internal LiveEnumerable(SlotPool<TTag> pool) { _pool = pool; }
        public LiveEnumerator GetEnumerator() => new(_pool);
    }

    /// <summary>
    /// Ref-struct enumerator over live slots. Stack-only; never allocates.
    /// </summary>
    public ref struct LiveEnumerator
    {
        private readonly ulong[] _bits;
        private readonly int[] _generations;
        private readonly int _highWater;
        private readonly int _wordCount;
        private int _wordIdx;
        private ulong _word;
        private Handle<TTag> _current;

        internal LiveEnumerator(SlotPool<TTag> pool)
        {
            _bits = pool._aliveBits;
            _generations = pool._generations;
            _highWater = pool._highWater;
            _wordCount = _bits.Length;
            _wordIdx = -1;
            _word = 0;
            _current = default;
        }

        public Handle<TTag> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _current;
        }

        public bool MoveNext()
        {
            while (true)
            {
                if (_word != 0)
                {
                    int bit = BitOperations.TrailingZeroCount(_word);
                    int idx = (_wordIdx << 6) + bit;
                    if (idx >= _highWater) return false;
                    _word &= _word - 1; // clear lowest set bit
                    _current = new Handle<TTag>(idx, _generations[idx]);
                    return true;
                }

                _wordIdx++;
                if (_wordIdx >= _wordCount) return false;
                int firstIdx = _wordIdx << 6;
                if (firstIdx >= _highWater) return false;
                _word = _bits[_wordIdx];
            }
        }
    }
}
