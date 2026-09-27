using System.Numerics;
using System.Runtime.CompilerServices;

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

    public int VertexCount => _vertices.Count;
    public int HalfEdgeCount => _halfEdges.Count;
    public int EdgeCount => _halfEdges.Count / 2;
    public int FaceCount => _faces.Count;

    public int AddVertex(Vector3 position)
    {
        return _vertices.Insert(
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

            if (_faces.IsAlive(HalfEdgePtr(existing)->AdjacentFace))
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
            int heHandle;
            if (existing != INVALID_HANDLE)
            {
                HalfEdgePtr(existing)->AdjacentFace = faceHandle;
                heHandle = existing;
            }
            else
            {
                int nextVertex = (i + 1) % n;
                heHandle = ConstructEdge(vertices[i], vertices[nextVertex]);
                HalfEdgePtr(heHandle)->AdjacentFace = faceHandle;
            }

            hedges[i] = heHandle;
            *HalfEdgeUvPtr(heHandle) = cornerUvs[i];
        }

        FacePtr(faceHandle)->AdjacentHalfEdge = hedges[0];

        // Perform the miserable task of linking up all the half-edges correctly - in two passes.
        for (int i = 0; i < n; i++)
        {
            int existing = existingHandles[i];
            if (existing == INVALID_HANDLE)
                continue;

            HalfEdge* existingHe = HalfEdgePtr(existing);
            oldPrev[i] = existingHe->PrevHalfEdge;
            oldNext[i] = existingHe->NextHalfEdge;
        }
        for (int i = 0; i < n; i++)
        {
            int iNext = i + 1;
            if (iNext == n)
                iNext = 0;

            int heHandle = hedges[i];
            int heNextHandle = hedges[iNext];
            int heTwinHandle = HalfEdgePtr(heHandle)->TwinHalfEdge;
            int heNextTwinHandle = HalfEdgePtr(heNextHandle)->TwinHalfEdge;

            bool currExisted = existingHandles[i] != INVALID_HANDLE;
            bool nextExisted = existingHandles[iNext] != INVALID_HANDLE;

            int outgoing = currExisted ? oldNext[i] : heTwinHandle;
            int incoming = nextExisted ? oldPrev[iNext] : heNextTwinHandle;

            HalfEdgePtr(incoming)->NextHalfEdge = outgoing;
            HalfEdgePtr(outgoing)->PrevHalfEdge = incoming;

            HalfEdge* heW = HalfEdgePtr(heHandle);
            HalfEdge* heNextW = HalfEdgePtr(heNextHandle);
            heW->NextHalfEdge = heNextHandle;
            heNextW->PrevHalfEdge = heHandle;
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
            if (HalfEdgePtr(HalfEdgePtr(he)->TwinHalfEdge)->SourceVertex == to)
                return he;
        }

        return INVALID_HANDLE;
    }

    int ConstructEdge(int from, int to)
    {
        int he = _halfEdges.Insert();
        int twin = _halfEdges.Insert();

        *HalfEdgePtr(he) = new HalfEdge
        {
            SourceVertex = from,
            TwinHalfEdge = twin,
            NextHalfEdge = twin,
            PrevHalfEdge = twin,
            AdjacentFace = INVALID_HANDLE,
        };
        *HalfEdgePtr(twin) = new HalfEdge
        {
            SourceVertex = to,
            TwinHalfEdge = he,
            NextHalfEdge = he,
            PrevHalfEdge = he,
            AdjacentFace = INVALID_HANDLE,
        };

        Vertex* fromVertex = VertexPtr(from);
        if (fromVertex->OutgoingHalfEdge == INVALID_HANDLE)
            fromVertex->OutgoingHalfEdge = he;

        Vertex* toVertex = VertexPtr(to);
        if (toVertex->OutgoingHalfEdge == INVALID_HANDLE)
            toVertex->OutgoingHalfEdge = twin;

        return he;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Vertex* VertexPtr(int handle)
    {
        _vertices.GetPointers(handle, out Vertex* topology, out _);
        return topology;
    }

    HalfEdge* HalfEdgePtr(int handle)
    {
        _halfEdges.GetPointers(handle, out HalfEdge* topology, out _);
        return topology;
    }

    Vector2* HalfEdgeUvPtr(int handle)
    {
        _halfEdges.GetPointers(handle, out _, out Vector2* uv);
        return uv;
    }

    Face* FacePtr(int handle)
    {
        _faces.GetPointers(handle, out Face* topology, out _);
        return topology;
    }
}
