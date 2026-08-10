using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TRMesh.Containers;

// A "Colony" data type, similar to plf::colony or a HopSlotMap.
// Because this struct owns its native allocations inline, it is an error to ever copy this struct.
public unsafe struct UnsafeColony<T> : IDisposable
    where T : unmanaged
{
    const int MaxChunkSize = 1 << 15;
    UnsafeChunkedList<T> _items;
    UnsafeList<ushort> _skipField;
    UnsafeList<ChunkData> _chunkData;

    // Top-level of the two-level free list. Stores the index of the first free chunk in _chunkData.
    // "Free Chunk" means a chunk that contains at least one free block.
    int _chunkFreeListHead;

    // Number of elements currently stored in the colony.
    int _count;

    public UnsafeColony()
        : this(0) { }

    public UnsafeColony(int initialCapacity)
    {
        Debug.Assert(initialCapacity >= 0);
        EnsureInitialized(initialCapacity);
    }

    struct FreeNode
    {
        public ushort Previous;
        public ushort Next;
    }

    struct ChunkData
    {
        public int Start;
        public int NextFree;
        public ushort FirstFreeBlock;
    }

    public readonly int Count => _count;
    public readonly int Capacity => _items.Capacity;
    public readonly bool IsEmpty => _count == 0;

    // Retrieves the element at the given slot.
    public ref T this[int slot]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Debug.Assert((uint)slot < (uint)_skipField.Count);
            Debug.Assert(_skipField.Ptr[slot] == 0);
            return ref _items.ElementRef(slot);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Enumerator GetEnumerator() => new(_items, _skipField.Ptr, _skipField.Count);

    // Inserts value into the colony at the returned slot.
    public int Insert(T value)
    {
        if (_count == _skipField.Count)
        {
            // Case 1: We have no "holes" and can simply append the element to the end of the colony.

            // Since this is always the first possible insertion, we must ensure the colony is
            // initialized here, to support default struct initialization.
            EnsureInitialized(0);

            // Store the value
            int slot = _count;
            _items[_count++] = value;

            // Update the skip-field to account for the new element, including ensuring we
            // write in a zero sentinel PAST the last element in the skipField.
            var requiredSkipCapacity = _skipField.Count + 2;
            if (_skipField.Capacity < requiredSkipCapacity)
            {
                _skipField.SetCapacity(Math.Max(requiredSkipCapacity, _items.Capacity + 1));
            }
            _skipField.Add(0);
            _skipField.Ptr[_skipField.Count] = 0;

            // If the call to _items[] above allocated a new chunk, we need to update book-keeping to
            // account for it.
            if (_chunkData.Count != _items.ChunkCount)
            {
                AppendChunkData();
            }

            return slot;
        }
        else
        {
            // Case 2: We have an inactive slot available that can be reused.

            Debug.Assert(_chunkFreeListHead != -1);
            var chunkIndex = _chunkFreeListHead;
            ref var chunkData = ref _chunkData[chunkIndex];
            Debug.Assert(chunkData.FirstFreeBlock != ushort.MaxValue);
            var localIndex = chunkData.FirstFreeBlock;
            var chunkBase = _items.ChunkBase(chunkIndex);
            int slot = chunkData.Start + localIndex;
            var blockLength = _skipField[slot];
            Debug.Assert(blockLength != 0);

            var nodePtr = GetFreeNode(chunkBase, localIndex);
            if (blockLength == 1)
            {
                // Is this the only free block in this entire chunk?
                if (nodePtr->Next == chunkData.FirstFreeBlock)
                {
                    Debug.Assert(nodePtr->Previous == chunkData.FirstFreeBlock);
                    // Promote this block to being fully occupied - that is,
                    // its second-level free-list is now empty.
                    chunkData.FirstFreeBlock = ushort.MaxValue;

                    // Insert always consumes the head free chunk, so the top-level
                    // free-list only needs forward links.
                    Debug.Assert(_chunkFreeListHead == chunkIndex);
                    _chunkFreeListHead = chunkData.NextFree;
                    chunkData.NextFree = -1;
                }
                else
                {
                    // Pop the free block off the second-level free-list
                    chunkData.FirstFreeBlock = UnlinkFreeNode(chunkBase, nodePtr);
                }
            }
            else
            {
                // Shift the freeblock to the right by one position.
                var oldFreeBlockStart = chunkData.FirstFreeBlock;
                var newFreeBlockStart = (ushort)(oldFreeBlockStart + 1);
                var newBlockLength = (ushort)(blockLength - 1);

                RelocateFreeNode(chunkBase, nodePtr, newFreeBlockStart);
                chunkData.FirstFreeBlock = newFreeBlockStart;
                WriteFreeBlockEndpoints(_skipField.Ptr, slot + 1, newBlockLength);
            }

            // ElementRef instead of [] to avoid capacity check.
            _items.ElementRef(chunkBase, localIndex) = value;
            _skipField[slot] = 0;
            _count++;
            return slot;
        }
    }

    // Erases the element at the given slot
    public void RemoveAt(int slot)
    {
        var extent = _skipField.Count;
        var skipField = _skipField.Ptr;
        Debug.Assert((uint)slot < (uint)extent);
        Debug.Assert(skipField[slot] == 0);
        Debug.Assert(_count > 0);

        _items.ResolveIndex(slot, out var chunkIndex, out var localIndex);
        ref var chunkData = ref _chunkData[chunkIndex];
        Debug.Assert(slot == chunkData.Start + localIndex);

        var chunkBase = _items.ChunkBase(chunkIndex);
        var chunkStartIdx = chunkData.Start;
        var chunkEndIdx = Math.Min(chunkStartIdx + _items.GetChunkCapacity(chunkIndex), extent);

        // A neighboring boundary on either side of the current slot already contains its block length.
        // Cache each one once, using zero to represent either an active neighbor or a chunk edge.
        var rightSlot = slot + 1;
        int leftLength = localIndex != 0 ? skipField[slot - 1] : 0;
        int rightLength = rightSlot < chunkEndIdx ? skipField[rightSlot] : 0;
        var blockStart = slot - leftLength;
        var blockLength = leftLength + rightLength + 1;

        if (leftLength == 0)
        {
            if (rightLength == 0)
            {
                // Erasing a single slot with no adjacent free blocks.
                var freeLocal = (ushort)localIndex;
                var nodePtr = GetFreeNode(chunkBase, freeLocal);
                if (chunkData.FirstFreeBlock == ushort.MaxValue)
                {
                    // Case 1: There are no existing free blocks in this chunk.

                    // Mark the second-level free node as a self-loop.
                    nodePtr->Previous = freeLocal;
                    nodePtr->Next = freeLocal;

                    // This new free chunk now becomes the head of the top-level free list.
                    chunkData.FirstFreeBlock = freeLocal;
                    chunkData.NextFree = _chunkFreeListHead;
                    _chunkFreeListHead = chunkIndex;
                }
                else
                {
                    // Case 2: There are existing free blocks in this chunk.

                    // Insert the new singleton free block, making it the new head of the second-level
                    // free list for this chunk.
                    var head = chunkData.FirstFreeBlock;
                    var headPtr = GetFreeNode(chunkBase, head);
                    var tail = headPtr->Previous;
                    var tailPtr = GetFreeNode(chunkBase, tail);
                    nodePtr->Previous = tail;
                    nodePtr->Next = head;
                    tailPtr->Next = freeLocal;
                    headPtr->Previous = freeLocal;
                    chunkData.FirstFreeBlock = freeLocal;
                }

                // Early return - update the skip-field to account for a singleton free block.
                skipField[slot] = 1;
                _count--;
                return;
            }
            else
            {
                // Extend the right free-block left by one slot via relocating its second-level
                // free-list node.
                var oldLocal = (ushort)(localIndex + 1);
                var newLocal = (ushort)localIndex;
                var oldNodePtr = GetFreeNode(chunkBase, oldLocal);
                RelocateFreeNode(chunkBase, oldNodePtr, newLocal);
                var head = chunkData.FirstFreeBlock;
                chunkData.FirstFreeBlock = head == oldLocal ? newLocal : head;
            }
        }
        else if (rightLength != 0)
        {
            // Merge left and right free blocks; drop the right block's freelist node.
            var rightLocal = (ushort)(localIndex + 1);
            var rightNodePtr = GetFreeNode(chunkBase, rightLocal);
            // Left still owns a freelist node, so the right node cannot be a singleton.
            Debug.Assert(rightNodePtr->Next != rightLocal);
            var following = UnlinkFreeNode(chunkBase, rightNodePtr);
            var head = chunkData.FirstFreeBlock;
            chunkData.FirstFreeBlock = head == rightLocal ? following : head;

            // The old block boundaries are already nonzero; only the newly erased
            // interior slot needs an inactive marker.
            skipField[slot] = ushort.MaxValue;
        }

        Debug.Assert(blockLength <= ushort.MaxValue);
        var encodedLength = (ushort)blockLength;
        WriteFreeBlockEndpoints(skipField, blockStart, encodedLength);
        _count--;
    }

    // Clears the colony, retaining all native allocations for subsequent reuse.
    public void Clear()
    {
        _skipField.Resize(0);
        if (_skipField.Ptr != null)
            _skipField.Ptr[0] = 0;

        for (var i = 0; i < _chunkData.Count; i++)
        {
            ref var chunkData = ref _chunkData[i];
            chunkData.NextFree = -1;
            chunkData.FirstFreeBlock = ushort.MaxValue;
        }

        _count = 0;
        _chunkFreeListHead = -1;
    }

    public void Dispose()
    {
        _items.Dispose();
        _skipField.Dispose();
        _chunkData.Dispose();
        _count = 0;
        _chunkFreeListHead = -1;
    }

    // Time complexity of iteration over the entire container:
    // O(N + M)
    // where:
    // - N is the Count, ie the number of active elements.
    // - M is the number of chunks in the container. Chunks can skip over as many as 32,768 inactive
    //   elements at a time, so N realistically dominates the time complexity and M becomes negligible
    //   in the non-degenerate case. A degenerate case of one element per chunk would be the exception
    //   but this is exceedingly rare.
    public ref struct Enumerator
    {
        static readonly ushort* EmptyPtr = (ushort*)NativeMemory.AllocZeroed((nuint)sizeof(ushort));

        UnsafeChunkedList<T> _items;
        readonly ushort* _skipField;
        readonly int _extent;
        int _index;
        int _chunkIndex;
        int _chunkStart;
        int _chunkEnd;
        byte* _chunkBase;

        internal Enumerator(UnsafeChunkedList<T> items, ushort* skipField, int extent)
        {
            _items = items;
            _skipField = skipField != null ? skipField : EmptyPtr;
            _extent = extent;
            _index = -1;
            _chunkIndex = 0;
            _chunkStart = 0;
            _chunkEnd = extent == 0 ? 0 : Math.Min(items.GetChunkCapacity(0), extent);
            _chunkBase = extent == 0 ? null : items.ChunkBase(0);
        }

        public ref T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _items.ElementRef(_chunkBase, _index - _chunkStart);
        }

        // The stable slot occupied by Current, for indexing parallel storage.
        public readonly int CurrentSlot
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            // The skip field has a zero sentinel at _extent, so this read remains
            // valid when incrementing past the final active slot.
            _index++;
            _index += _skipField[_index];

            // The dense same-chunk path only evaluates this boundary branch.
            while (_index >= _chunkEnd)
            {
                // We are jumping past the end of the current chunk...
                // Check if we are at the very end of the container, and handle that case.
                if (_index >= _extent)
                {
                    // Keep subsequent MoveNext calls parked immediately before the
                    // sentinel so they continue to return false safely.
                    _index = _extent - 1;
                    return false;
                }

                // Since we aren't at the very end of the container, jump to the next chunk.
                _chunkStart = _chunkEnd;
                _chunkIndex++;
                _chunkEnd = Math.Min(_chunkEnd + _items.GetChunkCapacity(_chunkIndex), _extent);
                _chunkBase = _items.ChunkBase(_chunkIndex);
                _index += _skipField[_index];
            }

            return true;
        }
    }

    private void AppendChunkData()
    {
        Debug.Assert(_chunkData.Count < _items.ChunkCount);
        var chunkIndex = _chunkData.Count;
        _chunkData.Add(
            new ChunkData
            {
                Start = _items.getChunkBaseIndex(chunkIndex),
                NextFree = -1,
                FirstFreeBlock = ushort.MaxValue,
            }
        );
    }

    // Initialization always ensures _items is constructed with the correct chunk size,
    // while a default-initialized _items would have a MaxChunkSize of 0 by default.
    // So this can be used to test if the colony has been initialized.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Initialized() => _items.MaxChunkSize == MaxChunkSize;

    // Swaps oldNodePtr with newNodePtr, unlinking oldNodePtr in the process.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RelocateFreeNode(byte* chunkBase, FreeNode* oldNodePtr, ushort newLocal)
    {
        var newNodePtr = GetFreeNode(chunkBase, newLocal);
        var previousPtr = GetFreeNode(chunkBase, oldNodePtr->Previous);
        var nextPtr = GetFreeNode(chunkBase, oldNodePtr->Next);

        previousPtr->Next = newLocal;
        nextPtr->Previous = newLocal;

        // Preserve this ordering. When oldNodePtr is a singleton, the writes above
        // change its links to newLocal, producing self-links for the new node.
        newNodePtr->Previous = oldNodePtr->Previous;
        newNodePtr->Next = oldNodePtr->Next;
    }

    // Unlinks nodePtr, returning the index of the next free node in the second-level free list.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ushort UnlinkFreeNode(byte* chunkBase, FreeNode* nodePtr)
    {
        var previous = nodePtr->Previous;
        var following = nodePtr->Next;

        GetFreeNode(chunkBase, previous)->Next = following;
        GetFreeNode(chunkBase, following)->Previous = previous;
        return following;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteFreeBlockEndpoints(
        ushort* skipField,
        int blockStart,
        ushort blockLength
    )
    {
        skipField[blockStart] = blockLength;
        skipField[blockStart + blockLength - 1] = blockLength;
    }

    private void EnsureInitialized(int capacity)
    {
        if (Initialized())
        {
            return;
        }

        _items = new UnsafeChunkedList<T>(capacity, MaxChunkSize, sizeof(FreeNode));
        _skipField = new UnsafeList<ushort>(_items.Capacity + 1);
        _skipField.Ptr[0] = 0;
        _chunkData = new UnsafeList<ChunkData>(_items.ChunkCount);
        _chunkFreeListHead = -1;
        _count = 0;

        while (_chunkData.Count < _items.ChunkCount)
        {
            AppendChunkData();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private FreeNode* GetFreeNode(byte* chunkBase, int localIndex) =>
        (FreeNode*)(chunkBase + (nuint)localIndex * (nuint)_items.ElementStride);

    // For testing purposes only.
    internal readonly bool IsActive(int slot)
    {
        if ((uint)slot >= (uint)_skipField.Count)
            return false;
        return _skipField[slot] == 0;
    }
}
