using System.Runtime.CompilerServices;

namespace TRMesh.Containers;

// Slot storage whose structural changes can be reversed exactly, provided they are undone in strict
// reverse order. Implemented by every generated standalone SoA type.
public interface IRecordableTable<TRow>
    where TRow : unmanaged
{
    int SparseCount { get; }
    TRow Get(int slot);
    void Set(int slot, TRow value);
    int Insert(TRow value);
    int RemoveAt(int slot);
    void UndoInsert(bool grew);
    void UndoRemove(TRow value, int denseIndex);
}

internal readonly record struct SavedRow<TRow>(int Handle, TRow Row)
    where TRow : unmanaged;

// Reverses one recorded operation on one table.
public readonly struct TablePatch<TRow>
    where TRow : unmanaged
{
    // Inserts and removals in execution order. A removal stores the dense index it vacated; a run
    // of consecutive inserts stores ~((count << 1) | grew).
    internal readonly int[] Structure;

    // The value each pre-existing row held before the operation first modified or removed it.
    internal readonly SavedRow<TRow>[] SavedRows;

    internal TablePatch(int[] structure, SavedRow<TRow>[] savedRows)
    {
        Structure = structure;
        SavedRows = savedRows;
    }

    public int ByteSize =>
        Structure.Length * sizeof(int) + SavedRows.Length * Unsafe.SizeOf<SavedRow<TRow>>();
}

// Routes mutations of a table so that, while recording, the table can later be restored exactly.
// Every modification of an existing row must be preceded by Capture.
public sealed class TableRecorder<TRow>
    where TRow : unmanaged
{
    readonly IRecordableTable<TRow> _table;
    readonly List<int> _structure = [];
    readonly List<SavedRow<TRow>> _savedRows = [];
    readonly HashSet<int> _touched = [];

    public TableRecorder(IRecordableTable<TRow> table) => _table = table;

    public bool IsRecording { get; private set; }

    public void Begin()
    {
        if (IsRecording)
            throw new InvalidOperationException("The table is already recording.");
        IsRecording = true;
    }

    public TablePatch<TRow> End()
    {
        var patch = new TablePatch<TRow>(_structure.ToArray(), _savedRows.ToArray());
        _structure.Clear();
        _savedRows.Clear();
        _touched.Clear();
        IsRecording = false;
        return patch;
    }

    public int Insert(TRow value)
    {
        int sparseCount = _table.SparseCount;
        int handle = _table.Insert(value);
        if (IsRecording)
        {
            _touched.Add(handle);
            AppendInsert(_table.SparseCount != sparseCount);
        }
        return handle;
    }

    public void RemoveAt(int handle)
    {
        Capture(handle);
        int denseIndex = _table.RemoveAt(handle);
        if (IsRecording)
            _structure.Add(denseIndex);
    }

    // Saves the row's current value if this is the first time the operation touches it. Rows
    // inserted by the operation are never saved, because reverting removes them.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Capture(int handle)
    {
        if (IsRecording && _touched.Add(handle))
            _savedRows.Add(new SavedRow<TRow>(handle, _table.Get(handle)));
    }

    // Restores the table to its state before the operation that produced patch. Later operations
    // must already have been reverted.
    public void Revert(TablePatch<TRow> patch)
    {
        // Removed rows are reactivated with placeholder values; every pre-existing row among them
        // is then overwritten from SavedRows, and rows inserted by the operation are removed again.
        for (int i = patch.Structure.Length - 1; i >= 0; i--)
        {
            int entry = patch.Structure[i];
            if (entry >= 0)
            {
                _table.UndoRemove(default, entry);
                continue;
            }

            int run = ~entry;
            bool grew = (run & 1) != 0;
            for (int count = run >> 1; count > 0; count--)
                _table.UndoInsert(grew);
        }

        foreach (SavedRow<TRow> saved in patch.SavedRows)
            _table.Set(saved.Handle, saved.Row);
    }

    void AppendInsert(bool grew)
    {
        int grewBit = grew ? 1 : 0;
        int last = _structure.Count - 1;
        if (last >= 0 && _structure[last] < 0 && (~_structure[last] & 1) == grewBit)
            _structure[last] -= 2;
        else
            _structure.Add(~(2 | grewBit));
    }
}
