using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using TRMesh.Containers;
using TRMesh.Mesh;

namespace tr_mesh.Tests;

public class RecordingTests
{
    static readonly Vector2[] QuadUvs = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Table_RandomOperations_RevertExactlyAndReplayIdentically(int seed)
    {
        using var table = new TestEntitySoA();
        var recorder = new TableRecorder<TestEntity>(table);
        RandomOperation(recorder, table, new Random(seed), steps: 40);

        const int operationCount = 8;
        var states = new List<byte[]> { RawState(table) };
        var patches = new List<TablePatch<TestEntity>>();
        for (int op = 0; op < operationCount; op++)
        {
            recorder.Begin();
            RandomOperation(recorder, table, new Random(seed * 100 + op), steps: 30);
            patches.Add(recorder.End());
            states.Add(RawState(table));
        }

        for (int op = operationCount - 1; op >= 0; op--)
        {
            recorder.Revert(patches[op]);
            Assert.Equal(states[op], RawState(table));
        }

        for (int op = 0; op < operationCount; op++)
        {
            RandomOperation(recorder, table, new Random(seed * 100 + op), steps: 30);
            Assert.Equal(states[op + 1], RawState(table));
        }
    }

    [Fact]
    public void Table_ReinsertingGrownHandleWithinOperation_RevertsExactly()
    {
        using var table = new TestEntitySoA();
        var recorder = new TableRecorder<TestEntity>(table);
        table.Insert(new TestEntity { Id = 1 });
        byte[] before = RawState(table);

        recorder.Begin();
        int grown = recorder.Insert(new TestEntity { Id = 2 });
        recorder.Insert(new TestEntity { Id = 3 });
        recorder.RemoveAt(grown);
        Assert.Equal(grown, recorder.Insert(new TestEntity { Id = 4 }));
        recorder.Revert(recorder.End());

        Assert.Equal(before, RawState(table));
    }

    [Fact]
    public void Table_RepeatedWritesAndRemovalOfOneRow_SaveItOnce()
    {
        using var table = new TestEntitySoA();
        var recorder = new TableRecorder<TestEntity>(table);
        int slot = table.Insert(new TestEntity { Id = 1, Health = 1f });

        recorder.Begin();
        for (int i = 0; i < 5; i++)
        {
            recorder.Capture(slot);
            table.SetHealth(slot, i);
        }
        recorder.RemoveAt(slot);
        TablePatch<TestEntity> patch = recorder.End();

        Assert.Single(patch.SavedRows);
        Assert.Equal(1f, patch.SavedRows[0].Row.Health);
        Assert.Single(patch.Structure);
    }

    [Fact]
    public void Mesh_RecordedOperations_RevertExactlyAndReplayIdentically()
    {
        using var mesh = new SpatialMesh();
        int[] g = AddGrid(mesh);
        mesh.AddFace([g[0], g[1], g[4], g[3]], QuadUvs);

        Action<SpatialMesh>[] operations =
        [
            m => m.AddFace([g[1], g[2], g[5], g[4]], QuadUvs),
            m => m.AddFace([g[4], g[5], g[8], g[7]], QuadUvs),
            m => m.AddFace([g[3], g[4], g[7], g[6]], QuadUvs),
            m => m.SetVertexPositions([g[4]], [new Vector3(1, 1, 0.5f)]),
            m =>
            {
                int apex = m.AddVertex(new Vector3(0.5f, -1, 0));
                m.AddFace([g[1], g[0], apex], [Vector2.Zero, Vector2.UnitX, Vector2.One]);
            },
        ];

        var states = new List<byte[]> { RawState(mesh) };
        var patches = new List<MeshPatch>();
        foreach (Action<SpatialMesh> operation in operations)
        {
            patches.Add(mesh.Record(operation));
            states.Add(RawState(mesh));
        }

        for (int op = operations.Length - 1; op >= 0; op--)
        {
            mesh.Revert(patches[op]);
            Assert.Equal(states[op], RawState(mesh));
        }

        for (int op = 0; op < operations.Length; op++)
        {
            patches[op] = mesh.Record(operations[op]);
            Assert.Equal(states[op + 1], RawState(mesh));
        }
    }

    [Fact]
    public void Mesh_ThrowingOperation_IsRolledBack()
    {
        using var mesh = new SpatialMesh();
        int[] g = AddGrid(mesh);
        mesh.AddFace([g[0], g[1], g[4], g[3]], QuadUvs);
        byte[] before = RawState(mesh);

        Assert.Throws<InvalidOperationException>(() =>
            mesh.Record(m =>
            {
                m.AddFace([g[1], g[2], g[5], g[4]], QuadUvs);
                m.SetVertexPositions([g[4]], [Vector3.One]);
                throw new InvalidOperationException();
            })
        );

        Assert.Equal(before, RawState(mesh));
    }

    [Fact]
    public void Mesh_RevertOutOfOrder_IsRejected()
    {
        using var mesh = new SpatialMesh();
        int[] g = AddGrid(mesh);
        MeshPatch first = mesh.Record(m => m.AddFace([g[0], g[1], g[4], g[3]], QuadUvs));
        mesh.Record(m => m.AddFace([g[1], g[2], g[5], g[4]], QuadUvs));

        Assert.Throws<InvalidOperationException>(() => mesh.Revert(first));
    }

    [Fact]
    public void Mesh_MovingOneVertex_SavesOnlyThatVertexAndItsFaces()
    {
        using SpatialMesh mesh = SpatialMeshBuilder.CreateCylinder(2f, 2f, 64);

        MeshPatch patch = mesh.Record(m => m.SetVertexPositions([0], [new Vector3(2, -1, 0)]));

        // One vertex row and the three faces around a cylinder corner, 20 bytes each.
        Assert.Equal(4 * 20, patch.ByteSize);
    }

    static int[] AddGrid(SpatialMesh mesh)
    {
        var grid = new int[9];
        for (int i = 0; i < grid.Length; i++)
            grid[i] = mesh.AddVertex(new Vector3(i % 3, i / 3, 0));
        return grid;
    }

    static void RandomOperation(
        TableRecorder<TestEntity> recorder,
        TestEntitySoA table,
        Random random,
        int steps
    )
    {
        for (int step = 0; step < steps; step++)
        {
            var live = new List<int>();
            var enumerator = table.GetEnumerator();
            while (enumerator.MoveNext())
                live.Add(enumerator.CurrentSlot);

            int choice = random.Next(3);
            if (choice == 0 || live.Count == 0)
            {
                recorder.Insert(
                    new TestEntity
                    {
                        Id = random.Next(),
                        Health = random.NextSingle(),
                        Flags = (byte)random.Next(256),
                    }
                );
            }
            else if (choice == 1)
            {
                recorder.RemoveAt(live[random.Next(live.Count)]);
            }
            else
            {
                int slot = live[random.Next(live.Count)];
                recorder.Capture(slot);
                table.SetHealth(slot, random.NextSingle());
            }
        }
    }

    // Every dense column in dense order, followed by each slot map's sparse array and free stack.
    static byte[] RawState(TestEntitySoA table)
    {
        var bytes = new List<byte>();
        table.WithReadOnlyColumns(
            (slots, ids, health, flags) =>
            {
                Append(bytes, slots);
                Append(bytes, ids);
                Append(bytes, health);
                Append(bytes, flags);
            }
        );
        Append<int>(bytes, Allocator<int>(table));
        return bytes.ToArray();
    }

    static byte[] RawState(SpatialMesh mesh)
    {
        var bytes = new List<byte>();
        mesh.WithVertices(
            (slots, topology, positions) =>
            {
                Append(bytes, slots);
                Append(bytes, topology);
                Append(bytes, positions);
            }
        );
        mesh.WithHalfEdges(
            (slots, topology, uvs) =>
            {
                Append(bytes, slots);
                Append(bytes, topology);
                Append(bytes, uvs);
            }
        );
        mesh.WithFaces(
            (slots, topology, normals) =>
            {
                Append(bytes, slots);
                Append(bytes, topology);
                Append(bytes, normals);
            }
        );
        Append<int>(bytes, Allocator<Vertex>(Storage(mesh, "_vertices")));
        Append<int>(bytes, Allocator<HalfEdge>(Storage(mesh, "_halfEdges")));
        Append<int>(bytes, Allocator<Face>(Storage(mesh, "_faces")));
        return bytes.ToArray();
    }

    static void Append<T>(List<byte> bytes, ReadOnlySpan<T> values)
        where T : unmanaged
    {
        bytes.AddRange(BitConverter.GetBytes(values.Length));
        bytes.AddRange(MemoryMarshal.AsBytes(values).ToArray());
    }

    static object Storage(SpatialMesh mesh, string name) =>
        typeof(SpatialMesh)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(mesh)!;

    static int[] Allocator<TMaster>(object soa)
        where TMaster : unmanaged =>
        ((UnsafeSlotMap<TMaster>)
            soa.GetType()
                .GetField("_field0", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(soa)!).CopyAllocatorState();
}
