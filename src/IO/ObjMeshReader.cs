using System.Globalization;
using System.Numerics;
using System.Text;

namespace TREditorSharp.IO;

/// <summary>
/// Reads a <see cref="SpatialMesh"/> from a Wavefront OBJ stream produced by
/// <see cref="ObjMeshWriter"/> (a strict subset of the OBJ format).
/// </summary>
///
/// Unsupported tokens throw <see cref="NotSupportedException"/>.
public sealed class ObjMeshReader
{
    /// <summary>
    /// Read the OBJ document from <paramref name="source"/> and return a freshly populated
    /// <see cref="SpatialMesh"/>.
    /// </summary>
    /// <param name="source">Stream positioned at the start of the OBJ document.</param>
    /// <param name="leaveOpen">When <c>true</c>, the stream is not closed by the reader.</param>
    /// <exception cref="NotSupportedException">An unsupported token is encountered.</exception>
    /// <exception cref="FormatException">A line is malformed (wrong field count, parse failure, out-of-range index).</exception>
    public SpatialMesh Read(Stream source, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(source);

        var mesh = new SpatialMesh();
        var verts = new List<VertexHandle>(256);
        var faceScratch = new List<VertexHandle>(8);

        using var reader = new StreamReader(
            source,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 65_536,
            leaveOpen: leaveOpen
        );

        var inv = CultureInfo.InvariantCulture;
        int lineNumber = 0;
        try
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                lineNumber++;
                ParseLine(line.AsSpan(), lineNumber, mesh, verts, faceScratch, inv);
            }
        }
        catch
        {
            mesh.Dispose();
            throw;
        }

        return mesh;
    }

    private static void ParseLine(
        ReadOnlySpan<char> raw,
        int lineNumber,
        SpatialMesh mesh,
        List<VertexHandle> verts,
        List<VertexHandle> faceScratch,
        IFormatProvider inv
    )
    {
        var line = raw.Trim();
        if (line.IsEmpty || line[0] == '#')
            return;

        // Token = first whitespace-delimited word.
        int tokenEnd = 0;
        while (tokenEnd < line.Length && !char.IsWhiteSpace(line[tokenEnd]))
            tokenEnd++;
        var token = line[..tokenEnd];
        var rest = line[tokenEnd..];

        if (token.SequenceEqual("o"))
            return;

        if (token.SequenceEqual("v"))
        {
            ParseVertex(rest, lineNumber, mesh, verts, inv);
            return;
        }

        if (token.SequenceEqual("f"))
        {
            ParseFace(rest, lineNumber, mesh, verts, faceScratch);
            return;
        }

        throw new NotSupportedException(
            $"ObjMeshReader: unsupported token '{token.ToString()}' on line {lineNumber}."
        );
    }

    private static void ParseVertex(
        ReadOnlySpan<char> rest,
        int lineNumber,
        SpatialMesh mesh,
        List<VertexHandle> verts,
        IFormatProvider inv
    )
    {
        Span<float> coords = stackalloc float[3];
        int filled = 0;
        var remaining = rest;
        while (TryNextToken(ref remaining, out var token))
        {
            if (filled >= 3)
                throw new FormatException(
                    $"ObjMeshReader: vertex on line {lineNumber} has more than 3 components."
                );
            if (!float.TryParse(token, NumberStyles.Float, inv, out var value))
                throw new FormatException(
                    $"ObjMeshReader: cannot parse '{token.ToString()}' as float on line {lineNumber}."
                );
            coords[filled++] = value;
        }

        if (filled != 3)
            throw new FormatException(
                $"ObjMeshReader: vertex on line {lineNumber} has {filled} components; expected 3."
            );

        verts.Add(mesh.AddVertex(new Vector3(coords[0], coords[1], coords[2])));
    }

    private static void ParseFace(
        ReadOnlySpan<char> rest,
        int lineNumber,
        SpatialMesh mesh,
        List<VertexHandle> verts,
        List<VertexHandle> scratch
    )
    {
        scratch.Clear();
        var remaining = rest;
        while (TryNextToken(ref remaining, out var token))
        {
            if (token.IndexOf('/') >= 0)
                throw new NotSupportedException(
                    $"ObjMeshReader: slashed face reference '{token.ToString()}' on line {lineNumber} is not supported."
                );
            if (token.Length > 0 && token[0] == '-')
                throw new NotSupportedException(
                    $"ObjMeshReader: negative face index '{token.ToString()}' on line {lineNumber} is not supported."
                );
            if (
                !int.TryParse(
                    token,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var index1
                )
            )
                throw new FormatException(
                    $"ObjMeshReader: cannot parse '{token.ToString()}' as integer on line {lineNumber}."
                );
            if (index1 < 1 || index1 > verts.Count)
                throw new FormatException(
                    $"ObjMeshReader: face index {index1} on line {lineNumber} is out of range [1, {verts.Count}]."
                );
            scratch.Add(verts[index1 - 1]);
        }

        if (scratch.Count < 3)
            throw new FormatException(
                $"ObjMeshReader: face on line {lineNumber} has {scratch.Count} corners; expected at least 3."
            );

        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(scratch);
        mesh.AddFace(span);
    }

    private static bool TryNextToken(ref ReadOnlySpan<char> rest, out ReadOnlySpan<char> token)
    {
        int i = 0;
        while (i < rest.Length && char.IsWhiteSpace(rest[i]))
            i++;
        if (i >= rest.Length)
        {
            token = default;
            rest = default;
            return false;
        }
        int start = i;
        while (i < rest.Length && !char.IsWhiteSpace(rest[i]))
            i++;
        token = rest[start..i];
        rest = rest[i..];
        return true;
    }
}
