using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Persistence;
using Unity.Collections;
using Record = PO2.Geometry.Topology.NativeMeshTopologyBuilder.EdgeTriangleRecord;

internal static partial class LoadingTests
{
    private static unsafe void RestoreSortChecks()
    {
        var random = new Random(114233);
        int[] limits = { 1, 255, 256, 65535, 65536, 16777215, 16777216, int.MaxValue };
        foreach (int count in new[] { 0, 1, 2, 17, 4095, 4096, 4097, 18000 })
        for (int variant = 0; variant < limits.Length; variant++)
        {
            int vertexLimit = limits[variant], triangleLimit = limits[(variant + 3) % limits.Length];
            var records = new Record[count + 2];
            var scratch = new Record[count + 2];
            var buckets = new int[258];
            var guard = new Record { VertexA = -1, VertexB = -2, TriangleId = -3 };
            records[0] = records[count + 1] = scratch[0] = scratch[count + 1] = guard;
            buckets[0] = buckets[257] = -17031;
            for (int i = 1; i <= count; i++)
            {
                records[i] = new Record
                {
                    VertexA = i % 23 == 0 ? vertexLimit : random.Next(vertexLimit),
                    VertexB = i % 17 == 0 ? vertexLimit : random.Next(vertexLimit),
                    TriangleId = i % 19 == 0 ? triangleLimit : random.Next(triangleLimit)
                };
                if (i % 7 == 0) records[i] = records[i - 1];
                if (variant == 0) { records[i].VertexA = 0; records[i].VertexB = 1; }
            }
            var expected = records.Skip(1).Take(count).ToArray();
            Array.Sort(expected, CompareSortRecords);
            if (variant == 1) Array.Copy(expected, 0, records, 1, count);
            if (variant == 2) Array.Copy(expected.Reverse().ToArray(), 0, records, 1, count);
            fixed (Record* values = records)
            fixed (Record* temporary = scratch)
            fixed (int* bins = buckets)
                NativeMeshTopologyBuilder.RadixSortEdgeTriangles(values + 1, temporary + 1, count,
                    (uint)vertexLimit, (uint)triangleLimit, bins + 1);
            for (int i = 0; i < count; i++) Check(CompareSortRecords(records[i + 1], expected[i]) == 0, "radix exact record order");
            Check(records[0].Equals(guard) && records[count + 1].Equals(guard)
                && scratch[0].Equals(guard) && scratch[count + 1].Equals(guard), "radix record buffer guards");
            Check(buckets[0] == -17031 && buckets[257] == -17031, "radix bucket guards");
        }

        int activeBefore = NativeAllocationChecks.Active;
        foreach (int records in new[] { 4095, 4096, 4097, 12000, 90000 })
        {
            // Three records per ordinary triangle, two per collapsed triangle.
            int collapsed = records % 3 == 1 ? 2 : records % 3 == 2 ? 1 : 0;
            int ordinary = (records - collapsed * 2) / 3;
            var triangles = new List<int>();
            for (int i = 0; i < ordinary; i++)
            {
                int a = random.Next(80), b, c;
                do { b = random.Next(80); } while (b == a);
                do { c = random.Next(80); } while (c == a || c == b);
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }
            for (int i = 0; i < collapsed; i++) triangles.AddRange(new[] { 0, 0, 1 });
            var mesh = Mesh(83, triangles.ToArray());
            var mapping = Enumerable.Range(0, 83).ToArray();
            var reference = ReferenceMeshTopology.Restore(mesh, mapping, mapping.Length);
            int allocations = NativeAllocationChecks.Created;
            var actual = MeshTopology.Restore(mesh, mapping, mapping.Length);
            int expectedAllocations = records >= NativeMeshTopologyBuilder.kRestoreRadixThreshold ? 9 : 7;
            Check(NativeAllocationChecks.Created - allocations == expectedAllocations, "restore sort path and scratch allocation");
            CompareLogicalTopology(actual, reference);
            Check(NativeAllocationChecks.Active == activeBefore, "restore scratch disposed");
        }

        var large = MakeSortMesh(22000, 1);
        var identity = Enumerable.Range(0, large.Vertices.Length).ToArray();
        var baseline = ReferenceNativeMeshTopologyBuilder.Restore(large, identity, identity.Length, false);
        foreach (int failAt in new[] { 1, 4, 7, 8, 9 })
        {
            NativeAllocationChecks.FailAfter = failAt;
            try { Throws<OutOfMemoryException>(() => MeshTopology.Restore(large, identity, identity.Length)); }
            finally { NativeAllocationChecks.FailAfter = 0; }
            Check(NativeAllocationChecks.Active == activeBefore, "partial native allocation failure released");
        }
        var parallel = new MeshTopology[8];
        LoadingBatch.RunDetached(parallel.Length, i => parallel[i] = MeshTopology.Restore(large, identity, identity.Length), 8);
        foreach (var topology in parallel) CompareLogicalTopology(topology, baseline);
        foreach (int variant in new[] { 0, 4 })
        {
            var sparse = MakeSortMesh(2500, variant);
            var sparseIdentity = Enumerable.Range(0, sparse.Vertices.Length).ToArray();
            int created = NativeAllocationChecks.Created;
            var topology = MeshTopology.Restore(sparse, sparseIdentity, sparseIdentity.Length);
            Check(NativeAllocationChecks.Created - created == 7, "sparse restore keeps comparison sort without scratch");
            CompareLogicalTopology(topology, ReferenceNativeMeshTopologyBuilder.Restore(sparse, sparseIdentity, sparseIdentity.Length, false));
        }
        var grid = MakeSortMesh(18000, 3);
        var gridIdentity = Enumerable.Range(0, grid.Vertices.Length).ToArray();
        CompareLogicalTopology(MeshTopology.Restore(grid, gridIdentity, gridIdentity.Length),
            ReferenceNativeMeshTopologyBuilder.Restore(grid, gridIdentity, gridIdentity.Length, false));
        var split = Mesh(large.Vertices.Length * 2, (int[])large.Triangles.Clone());
        var welded = Enumerable.Range(0, split.Vertices.Length).Select(i => i % identity.Length).ToArray();
        for (int i = 0; i < split.Vertices.Length; i++) split.Vertices[i] = new UnityEngine.Vector3(i, i % 7, 0);
        for (int i = 0; i < split.Triangles.Length; i += 2) split.Triangles[i] += identity.Length;
        CompareLogicalTopology(MeshTopology.Restore(split, welded, identity.Length),
            ReferenceNativeMeshTopologyBuilder.Restore(split, welded, identity.Length, false));
        Check(NativeAllocationChecks.Active == activeBefore, "concurrent restore scratch independent and released");
    }

    private static int CompareSortRecords(Record a, Record b)
    {
        int result = a.VertexA.CompareTo(b.VertexA);
        if (result == 0) result = a.VertexB.CompareTo(b.VertexB);
        if (result == 0) result = a.TriangleId.CompareTo(b.TriangleId);
        return result;
    }

    private static void CompareLogicalTopology(MeshTopology actual, MeshTopology expected)
    {
        Check(actual.LogicalVertexCount == expected.LogicalVertexCount
            && actual.LogicalEdgeCount == expected.LogicalEdgeCount
            && actual.RenderVertexCount == expected.RenderVertexCount, "complete logical topology counts");
        for (int i = 0; i < actual.RenderVertexCount; i++)
            Check(actual.GetLogicalVertexIdForRenderVertex(i) == expected.GetLogicalVertexIdForRenderVertex(i), "saved logical mapping");
        for (int i = 0; i < actual.LogicalVertexCount; i++)
        {
            var a = actual.GetVertex(i); var b = expected.GetVertex(i);
            Same(a.RenderVertexIndices, b.RenderVertexIndices, "logical render membership");
            Same(a.TriangleIndices, b.TriangleIndices, "logical triangle membership");
            Same(a.AdjacentVertexIds, b.AdjacentVertexIds, "logical neighbor order");
        }
        for (int i = 0; i < actual.LogicalEdgeCount; i++)
        {
            var a = actual.GetEdge(i); var b = expected.GetEdge(i);
            Check(a.Id == b.Id && a.VertexA == b.VertexA && a.VertexB == b.VertexB, "complete edge order");
            Same(a.TriangleIndices, b.TriangleIndices, "complete edge triangle order");
            Check(actual.TryGetEdge(b.VertexB, b.VertexA, out var found) && found.Id == i, "restored binary edge lookup");
        }
    }

    private static void CompareLogicalTopology(MeshTopology actual, ReferenceMeshTopology expected)
    {
        var wrapped = MeshTopology.CreateFromSortedData(
            Enumerable.Range(0, expected.LogicalVertexCount).Select(expected.GetVertex).ToArray(),
            Enumerable.Range(0, expected.LogicalEdgeCount).Select(expected.GetEdge).ToArray(),
            Enumerable.Range(0, expected.RenderVertexCount).Select(expected.GetLogicalVertexIdForRenderVertex).ToArray());
        CompareLogicalTopology(actual, wrapped);
    }

    private static MeshData MakeSortMesh(int triangleCount, int variant)
    {
        int vertexCount = variant == 0 || variant == 4 ? triangleCount * 3 : Math.Max(16, triangleCount / 2);
        int side = (int)Math.Ceiling(Math.Sqrt((triangleCount + 1) / 2));
        if (variant == 3) vertexCount = (side + 1) * (side + 1);
        var indices = new int[triangleCount * 3];
        var random = new Random(771 + triangleCount);
        for (int i = 0; i < triangleCount; i++)
        {
            int a, b, c;
            if (variant == 0 || variant == 4) { a = i * 3; b = a + 1; c = a + 2; }
            else if (variant == 2) { a = 0; b = 1; c = 2 + i % (vertexCount - 2); }
            else if (variant == 3)
            {
                int cell = i / 2;
                a = (cell / side) * (side + 1) + cell % side;
                b = i % 2 == 0 ? a + 1 : a + side + 2;
                c = i % 2 == 0 ? a + side + 2 : a + side + 1;
            }
            else
            {
                a = random.Next(vertexCount);
                do { b = random.Next(vertexCount); } while (b == a);
                do { c = random.Next(vertexCount); } while (c == a || c == b);
            }
            indices[i * 3] = a; indices[i * 3 + 1] = b; indices[i * 3 + 2] = c;
        }
        if (variant == 4)
            for (int i = indices.Length - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                int value = indices[i]; indices[i] = indices[other]; indices[other] = value;
            }
        return Mesh(vertexCount, indices);
    }

    private static void RestoreSortBenchmark()
    {
        Console.WriteLine("Host-only complete native restore comparison; allocations included, Burst disabled.");
        Console.WriteLine("triangles,distribution,workers,baseline_ms,radix_ms,baseline_over_radix");
        foreach (int count in new[] { 512, 2048, 8192, 32768, 131072 })
        for (int variant = 0; variant < 5; variant++)
            BenchmarkSortCase(count, variant, 1);
        for (int variant = 0; variant < 5; variant++) BenchmarkSortCase(32768, variant, 8);
    }

    private static void BenchmarkSortCase(int count, int variant, int workers)
    {
        var mesh = MakeSortMesh(count, variant);
        var identity = Enumerable.Range(0, mesh.Vertices.Length).ToArray();
        int iterations = count < 10000 ? 10 : 1;
        void Run(bool previous)
        {
            LoadingBatch.RunDetached(workers, _ =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    var value = previous
                        ? ReferenceNativeMeshTopologyBuilder.Restore(mesh, identity, identity.Length, false)
                        : MeshTopology.Restore(mesh, identity, identity.Length);
                    if (value.LogicalVertexCount != identity.Length) throw new Exception("benchmark restore failed");
                }
            }, workers);
        }
        Run(true); Run(false);
        var oldTimes = new double[5]; var newTimes = new double[5];
        for (int i = 0; i < 5; i++)
        for (int j = 0; j < 2; j++)
        {
            bool previous = (i + j) % 2 == 0;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var watch = Stopwatch.StartNew();
            Run(previous);
            watch.Stop();
            (previous ? oldTimes : newTimes)[i] = watch.Elapsed.TotalMilliseconds / iterations;
        }
        Array.Sort(oldTimes); Array.Sort(newTimes);
        Console.WriteLine($"{count},{new[] { "ordered", "random", "shared-edge", "grid", "sparse-random" }[variant]},{workers},{oldTimes[2]:0.###},{newTimes[2]:0.###},{oldTimes[2] / newTimes[2]:0.###}");
    }
}
