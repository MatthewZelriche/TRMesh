using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TREditorSharp;

/// <summary>
/// Stable, generationally-versioned handle to a slot in a pool keyed by the phantom
/// type parameter <typeparamref name="TKind"/>.
///
/// Eight bytes (two int fields; the kind parameter is not stored at runtime).
/// <typeparamref name="TKind"/> must be <c>unmanaged</c> (mesh tags are zero-size
/// <c>struct</c> markers). A default-valued handle is the null handle
/// (<see cref="Generation"/> == 0).
///
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Handle<TKind>(int Index, int Generation) where TKind : unmanaged
{
    public static Handle<TKind> Null => default;

    public bool IsNull
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Generation == 0;
    }

    public override string ToString()
    {
        if (IsNull) return $"Handle<{typeof(TKind).Name}>.Null";
        char prefix = typeof(TKind).Name switch
        {
            nameof(VertexTag) => 'V',
            nameof(HalfEdgeTag) => 'H',
            nameof(FaceTag) => 'F',
            _ => '?',
        };
        return $"{prefix}[{Index}:g{Generation}]";
    }
}
