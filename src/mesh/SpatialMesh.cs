using System.Numerics;
using System.Runtime.CompilerServices;
using TRMesh.Containers;

namespace TRMesh.Mesh;

[SoA]
public struct VertexData
{
    public Vertex topology;
    public Vector3 position;
}

[SoA]
public struct HalfEdgeData
{
    public HalfEdge topology;
    public Vector2 UV;
}

[SoA]
public struct FaceData
{
    public Face topology;
    public Vector3 normal;
}

public unsafe partial class SpatialMesh : IDisposable
{
    public const int INVALID_HANDLE = -1;

    readonly VertexDataSoA _vertices = new();
    readonly HalfEdgeDataSoA _halfEdges = new();
    readonly FaceDataSoA _faces = new();

    // All mutation goes through these recorders, or through the *Mut accessors which capture first.
    readonly TableRecorder<VertexData> _vertexRecorder;
    readonly TableRecorder<HalfEdgeData> _halfEdgeRecorder;
    readonly TableRecorder<FaceData> _faceRecorder;

    public SpatialMesh()
    {
        _vertexRecorder = new(_vertices);
        _halfEdgeRecorder = new(_halfEdges);
        _faceRecorder = new(_faces);
    }

    public int VertexCount => _vertices.Count;
    public int HalfEdgeCount => _halfEdges.Count;
    public int EdgeCount => _halfEdges.Count / 2;
    public int FaceCount => _faces.Count;

    public int AddVertex(Vector3 position)
    {
        MarkMutated();
        return _vertexRecorder.Insert(
            new VertexData
            {
                topology = new Vertex { OutgoingHalfEdge = INVALID_HANDLE },
                position = position,
            }
        );
    }

    // Add a face whose boundary, in CCW order, is given by vertices.
    // cornerUvs[i] belongs to vertices[i] in this face.
    // Returns the handle of the freshly allocated face.
    //
    // The mesh state is mutated only after a full validation pass succeeds, so a
    // thrown exception leaves the mesh in its previous state prior to this call.
    public int AddFace(ReadOnlySpan<int> vertices, ReadOnlySpan<Vector2> cornerUvs)
    {
        int n = vertices.Length;
        if (n < 3)
            throw new ArgumentException(
                "AddFace: Face must have at least 3 vertices.",
                nameof(vertices)
            );

        if (cornerUvs.Length != n)
        {
            throw new ArgumentException(
                "AddFace: Corner UV count must match the face vertex count.",
                nameof(cornerUvs)
            );
        }

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

        MarkMutated();
        int faceHandle = _faceRecorder.Insert(
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
            int heHandle;
            if (existing != INVALID_HANDLE)
            {
                HalfEdgeMut(existing).AdjacentFace = faceHandle;
                heHandle = existing;
            }
            else
            {
                int nextVertex = (i + 1) % n;
                heHandle = ConstructEdge(vertices[i], vertices[nextVertex]);
                HalfEdgeMut(heHandle).AdjacentFace = faceHandle;
            }

            hedges[i] = heHandle;
            HalfEdgeUvMut(heHandle) = cornerUvs[i];
        }

        FaceMut(faceHandle).AdjacentHalfEdge = hedges[0];

        // Perform the miserable task of linking up all the half-edges correctly - in two passes.
        for (int i = 0; i < n; i++)
        {
            int existing = existingHandles[i];
            if (existing == INVALID_HANDLE)
                continue;

            ref readonly HalfEdge existingHe = ref HalfEdgeRef(existing);
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
            int heTwinHandle = HalfEdgeRef(heHandle).TwinHalfEdge;
            int heNextTwinHandle = HalfEdgeRef(heNextHandle).TwinHalfEdge;

            bool currExisted = existingHandles[i] != INVALID_HANDLE;
            bool nextExisted = existingHandles[iNext] != INVALID_HANDLE;

            int outgoing = currExisted ? oldNext[i] : heTwinHandle;
            int incoming = nextExisted ? oldPrev[iNext] : heNextTwinHandle;

            HalfEdgeMut(incoming).NextHalfEdge = outgoing;
            HalfEdgeMut(outgoing).PrevHalfEdge = incoming;

            HalfEdgeMut(heHandle).NextHalfEdge = heNextHandle;
            HalfEdgeMut(heNextHandle).PrevHalfEdge = heHandle;
        }

        return faceHandle;
    }

    public void Dispose()
    {
        _vertices.Dispose();
        _halfEdges.Dispose();
        _faces.Dispose();
        GC.SuppressFinalize(this);
    }

    // Finds a half-edge between two given vertices identified by their handles.
    int FindHalfEdgeBetweenUnchecked(int from, int to)
    {
        foreach (int he in new HalfEdgesAroundVertex(this, from))
        {
            if (HalfEdgeRef(HalfEdgeRef(he).TwinHalfEdge).SourceVertex == to)
                return he;
        }

        return INVALID_HANDLE;
    }

    int ConstructEdge(int from, int to)
    {
        int he = _halfEdgeRecorder.Insert(default);
        int twin = _halfEdgeRecorder.Insert(default);

        HalfEdgeMut(he) = new HalfEdge
        {
            SourceVertex = from,
            TwinHalfEdge = twin,
            NextHalfEdge = twin,
            PrevHalfEdge = twin,
            AdjacentFace = INVALID_HANDLE,
        };
        HalfEdgeMut(twin) = new HalfEdge
        {
            SourceVertex = to,
            TwinHalfEdge = he,
            NextHalfEdge = he,
            PrevHalfEdge = he,
            AdjacentFace = INVALID_HANDLE,
        };

        if (VertexRef(from).OutgoingHalfEdge == INVALID_HANDLE)
            VertexMut(from).OutgoingHalfEdge = he;

        if (VertexRef(to).OutgoingHalfEdge == INVALID_HANDLE)
            VertexMut(to).OutgoingHalfEdge = twin;

        return he;
    }

    // Read accessors never record. Writes must go through the *Mut accessors, which capture the row
    // for an active recording before returning it. The returned refs alias native storage and are
    // invalidated by structural modification, same as GetPointers.
    //
    // The unused void* parameter is intentional: an optional pointer in the signature makes every
    // call site require an unsafe context, even when the argument is omitted. This is to ensure these calls
    // are treated with the proper safety respect they deserve despite appearing to return "safe" references.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ref readonly Vertex VertexRef(int handle, void* enforceUnsafe = null)
    {
        _vertices.GetPointers(handle, out Vertex* topology, out _);
        return ref *topology;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ref readonly HalfEdge HalfEdgeRef(int handle, void* enforceUnsafe = null)
    {
        _halfEdges.GetPointers(handle, out HalfEdge* topology, out _);
        return ref *topology;
    }

    ref Vertex VertexMut(int handle, void* enforceUnsafe = null)
    {
        _vertexRecorder.Capture(handle);
        _vertices.GetPointers(handle, out Vertex* topology, out _);
        return ref *topology;
    }

    ref HalfEdge HalfEdgeMut(int handle, void* enforceUnsafe = null)
    {
        _halfEdgeRecorder.Capture(handle);
        _halfEdges.GetPointers(handle, out HalfEdge* topology, out _);
        return ref *topology;
    }

    ref Vector2 HalfEdgeUvMut(int handle, void* enforceUnsafe = null)
    {
        _halfEdgeRecorder.Capture(handle);
        _halfEdges.GetPointers(handle, out _, out Vector2* uv);
        return ref *uv;
    }

    ref Face FaceMut(int handle, void* enforceUnsafe = null)
    {
        _faceRecorder.Capture(handle);
        _faces.GetPointers(handle, out Face* topology, out _);
        return ref *topology;
    }
}
