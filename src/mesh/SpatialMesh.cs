using System.Numerics;
using System.Runtime.CompilerServices;

namespace TRMesh.Mesh;

[SoA]
struct VertexData
{
    public Vertex topology;
    public Vector3 position;
    public Vector2 uv;
}

[SoA]
struct EdgeData
{
    public Edge topology;
}

[SoA]
struct FaceData
{
    public Face topology;
    public Vector3 normal;
}

public partial class SpatialMesh : IDisposable
{
    public const int INVALID_HANDLE = -1;

    readonly VertexDataSoA _vertices = new();
    readonly EdgeDataSoA _edges = new();
    readonly FaceDataSoA _faces = new();

    public int VertexCount => _vertices.Count;
    public int EdgeCount => _edges.Count;
    public int FaceCount => _faces.Count;

    public int AddVertex(Vector3 position, Vector2 uv)
    {
        return _vertices.Insert(
            new VertexData
            {
                topology = new Vertex { OutgoingHalfEdge = INVALID_HANDLE },
                position = position,
                uv = uv,
            }
        );
    }

    // Add a face whose boundary, in CCW order, is given by vertices.
    // Returns the handle of the freshly allocated face.
    //
    // The mesh state is mutated only after a full validation pass succeeds, so a
    // thrown exception leaves the mesh in its previous state prior to this call.
    public int AddFace(ReadOnlySpan<int> vertices)
    {
        int n = vertices.Length;
        if (n < 3)
            throw new ArgumentException(
                "AddFace: Face must have at least 3 vertices.",
                nameof(vertices)
            );

        Span<Vector3> positions = stackalloc Vector3[n];
        var uniqueVertices = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            int vtx = vertices[i];
            if (!_vertices.IsAlive(vtx))
            {
                throw new ArgumentException(
                    $"AddFace: Vertex {vtx} (at index {i}) is not a live handle.",
                    nameof(vertices)
                );
            }

            if (!uniqueVertices.Add(vtx))
            {
                throw new ArgumentException(
                    $"AddFace: Vertex {vtx} occurs more than once in the face boundary.",
                    nameof(vertices)
                );
            }

            // Collect the positions of the vertices during this check, to later compute the
            // face normal.
            positions[i] = _vertices.Get(vtx).position;
        }

        Span<int> scratch = stackalloc int[n * 4];
        Span<int> existingHandles = scratch[..n];
        Span<int> hedges = scratch.Slice(n, n);
        Span<int> oldPrev = scratch.Slice(n * 2, n);
        Span<int> oldNext = scratch.Slice(n * 3, n);
        existingHandles.Fill(INVALID_HANDLE);

        for (int i = 0; i < n; i++)
        {
            int iNext = i + 1;
            if (iNext == n)
                iNext = 0;

            int vtx = vertices[i];
            int vtxNext = vertices[iNext];
            int existing = FindHalfEdgeBetweenUnchecked(vtx, vtxNext);
            if (existing == INVALID_HANDLE)
                continue;

            if (_faces.IsAlive(HalfEdgeRef(existing).AdjacentFace))
            {
                throw new ArgumentException(
                    $"AddFace: An edge from {vtx} to {vtxNext} already exists and is not a boundary edge.",
                    nameof(vertices)
                );
            }

            // Cache existing boundary half-edges.
            existingHandles[i] = existing;
        }

        for (int i = 0; i < n; i++)
        {
            int iPrev = i == 0 ? n - 1 : i - 1;
            bool incomingExists = existingHandles[iPrev] != INVALID_HANDLE;
            bool outgoingExists = existingHandles[i] != INVALID_HANDLE;

            if (
                !incomingExists
                && !outgoingExists
                && _vertices.Get(vertices[i]).topology.OutgoingHalfEdge != INVALID_HANDLE
            )
            {
                throw new ArgumentException(
                    $"AddFace: Vertex {vertices[i]} would have more than one disconnected fan of faces.",
                    nameof(vertices)
                );
            }
        }

        Vector3 normal = Util.Math.ComputeFaceNormal(positions);

        // Validation is complete, mutating the mesh state can safely occur.

        int faceHandle = _faces.Insert(
            new FaceData
            {
                topology = new Face { AdjacentHalfEdge = INVALID_HANDLE },
                normal = normal,
            }
        );

        // Construct new edges for the face and set face handles.
        for (int i = 0; i < n; i++)
        {
            int existing = existingHandles[i];
            if (existing != INVALID_HANDLE)
            {
                HalfEdgeRef(existing).AdjacentFace = faceHandle;
                hedges[i] = existing;
                continue;
            }

            int iNext = i + 1;
            if (iNext == n)
                iNext = 0;

            int heHandle = ConstructEdge(vertices[i], vertices[iNext]);
            HalfEdgeRef(heHandle).AdjacentFace = faceHandle;
            hedges[i] = heHandle;
        }

        _faces.Get(faceHandle).topology.AdjacentHalfEdge = hedges[0];

        // Perform the miserable task of linking up all the half-edges correctly - in two passes.
        for (int i = 0; i < n; i++)
        {
            int existing = existingHandles[i];
            if (existing == INVALID_HANDLE)
                continue;

            ref HalfEdge existingHe = ref HalfEdgeRef(existing);
            oldPrev[i] = existingHe.PrevHalfEdge;
            oldNext[i] = existingHe.NextHalfEdge;
        }
        for (int i = 0; i < n; i++)
        {
            int iNext = i + 1;
            if (iNext == n)
                iNext = 0;

            int heHandle = hedges[i];
            int heNextHandle = hedges[iNext];
            int heTwinHandle = Twin(heHandle);
            int heNextTwinHandle = Twin(heNextHandle);

            bool currExisted = existingHandles[i] != INVALID_HANDLE;
            bool nextExisted = existingHandles[iNext] != INVALID_HANDLE;

            int outgoing = currExisted ? oldNext[i] : heTwinHandle;
            int incoming = nextExisted ? oldPrev[iNext] : heNextTwinHandle;

            HalfEdgeRef(incoming).NextHalfEdge = outgoing;
            HalfEdgeRef(outgoing).PrevHalfEdge = incoming;

            ref HalfEdge heW = ref HalfEdgeRef(heHandle);
            ref HalfEdge heNextW = ref HalfEdgeRef(heNextHandle);
            heW.NextHalfEdge = heNextHandle;
            heNextW.PrevHalfEdge = heHandle;
        }

        return faceHandle;
    }

    public void Dispose()
    {
        _vertices.Dispose();
        _edges.Dispose();
        _faces.Dispose();
        GC.SuppressFinalize(this);
    }

    // Finds a half-edge between two given vertices identified by their handles.
    int FindHalfEdgeBetweenUnchecked(int from, int to)
    {
        foreach (int he in new HalfEdgesAroundVertex(this, from))
        {
            if (HalfEdgeRef(Twin(he)).SourceVertex == to)
                return he;
        }

        return INVALID_HANDLE;
    }

    int ConstructEdge(int from, int to)
    {
        int slot = _edges.Insert();
        int he = slot << 1;
        int twin = Twin(he);

        ref Edge edge = ref _edges.Get(slot).topology;
        edge.halfEdge = new HalfEdge
        {
            SourceVertex = from,
            NextHalfEdge = twin,
            PrevHalfEdge = twin,
            AdjacentFace = INVALID_HANDLE,
        };
        edge.twin = new HalfEdge
        {
            SourceVertex = to,
            NextHalfEdge = he,
            PrevHalfEdge = he,
            AdjacentFace = INVALID_HANDLE,
        };

        ref Vertex fromVertex = ref _vertices.Get(from).topology;
        if (fromVertex.OutgoingHalfEdge == INVALID_HANDLE)
            fromVertex.OutgoingHalfEdge = he;

        ref Vertex toVertex = ref _vertices.Get(to).topology;
        if (toVertex.OutgoingHalfEdge == INVALID_HANDLE)
            toVertex.OutgoingHalfEdge = twin;

        return he;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Twin(int halfEdge) => halfEdge ^ 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ref HalfEdge HalfEdgeRef(int handle)
    {
        var view = _edges.Get(handle >> 1);
        return ref Unsafe.Add(ref view.topology.halfEdge, handle & 1);
    }

    internal ref HalfEdge GetHalfEdge(int handle) => ref HalfEdgeRef(handle);

    internal ref Vertex GetVertex(int slot) => ref _vertices.Get(slot).topology;

    internal ref Face GetFace(int slot) => ref _faces.Get(slot).topology;

    internal Vector3 GetFaceNormal(int slot) => _faces.Get(slot).normal;
}
