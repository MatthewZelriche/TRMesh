using System.Buffers.Binary;
using System.Text;

namespace TREditorSharp.IO;

static class BinaryMeshPrimitives
{
    public static void WriteInt32(Stream destination, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        destination.Write(buffer);
    }

    public static int ReadInt32(Stream source)
    {
        Span<byte> buffer = stackalloc byte[4];
        source.ReadExactly(buffer);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    public static void WriteUInt32(Span<byte> destination, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
    }

    public static uint ReadUInt32(ReadOnlySpan<byte> source) =>
        BinaryPrimitives.ReadUInt32LittleEndian(source);

    public static void WriteInt64(Stream destination, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        destination.Write(buffer);
    }

    public static long ReadInt64(Stream source)
    {
        Span<byte> buffer = stackalloc byte[8];
        source.ReadExactly(buffer);
        return BinaryPrimitives.ReadInt64LittleEndian(buffer);
    }

    public static void WriteSingle(Span<byte> destination, float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination, value);
    }

    public static float ReadSingle(ReadOnlySpan<byte> source) =>
        BinaryPrimitives.ReadSingleLittleEndian(source);

    public static void WriteString(Stream destination, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32(destination, bytes.Length);
        destination.Write(bytes);
    }

    public static string ReadString(Stream source, int maximumByteCount = int.MaxValue)
    {
        int byteCount = ReadInt32(source);
        if (byteCount < 0 || byteCount > maximumByteCount)
            throw new FormatException($"Binary mesh string length {byteCount} is invalid.");
        EnsureRemaining(source, byteCount);
        var bytes = new byte[byteCount];
        source.ReadExactly(bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    private static void EnsureRemaining(Stream source, long byteCount)
    {
        if (source.CanSeek && byteCount > source.Length - source.Position)
            throw new EndOfStreamException();
    }
}
