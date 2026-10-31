using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace PO2.Geometry.Topology
{
    internal static unsafe partial class NativeMeshTopologyBuilder
    {
        // Small inputs cost less to sort in place. This chooses an algorithm;
        // it does not limit the size of a part or the number of restore workers.
        internal const int kRestoreRadixThreshold = 4096;

        private static bool UseRestoreRadix(ref NativeTopologyContext context)
        {
            // Sparse/disconnected layouts did not repay the extra memory passes
            // in the host comparisons. Keep their existing in-place sort. These
            // counts are already available from adjacency record construction.
            return context.EdgeTriangleCount >= kRestoreRadixThreshold
                && context.VertexTriangleCount > context.LogicalVertexCount;
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        private static void RestoreEdgeSortKernel(ref NativeTopologyContext context)
        {
            if (UseRestoreRadix(ref context))
            {
                if (context.RestoreSortRecords == null || context.RestoreSortBuckets == null)
                {
                    context.ErrorCode = 5;
                    return;
                }
                RadixSortEdgeTriangles(context.EdgeTriangles, context.RestoreSortRecords,
                    context.EdgeTriangleCount, (uint)(context.LogicalVertexCount - 1),
                    (uint)(context.IndexCount / 3 - 1), context.RestoreSortBuckets);
            }
            else if (!SortEdgeTriangles(context.EdgeTriangles, context.EdgeTriangleCount,
                context.SortStack, context.SortStackCapacity))
            {
                context.ErrorCode = 2;
                return;
            }
            FinishEdgeSort(ref context);
        }

        // Inputs have nonnegative, validated keys and two separate count-sized
        // buffers. Stable byte passes preserve the full (A, B, triangle) order.
        internal static void RadixSortEdgeTriangles(EdgeTriangleRecord* records,
            EdgeTriangleRecord* scratch, int count, uint maximumVertexId,
            uint maximumTriangleId, int* buckets)
        {
            if (count < 2) return;
            EdgeTriangleRecord* source = records;
            EdgeTriangleRecord* destination = scratch;
            for (int key = 0; key < 3; key++)
            {
                uint remaining = key == 0 ? maximumTriangleId : maximumVertexId;
                int shift = 0;
                do
                {
                    for (int i = 0; i < 256; i++) buckets[i] = 0;
                    for (int i = 0; i < count; i++)
                    {
                        uint value = (uint)(key == 0 ? source[i].TriangleId
                            : key == 1 ? source[i].VertexB : source[i].VertexA);
                        buckets[(value >> shift) & 255]++;
                    }
                    int offset = 0;
                    for (int i = 0; i < 256; i++)
                    {
                        int size = buckets[i];
                        buckets[i] = offset;
                        offset += size;
                    }
                    for (int i = 0; i < count; i++)
                    {
                        EdgeTriangleRecord record = source[i];
                        uint value = (uint)(key == 0 ? record.TriangleId
                            : key == 1 ? record.VertexB : record.VertexA);
                        destination[buckets[(value >> shift) & 255]++] = record;
                    }
                    EdgeTriangleRecord* swap = source;
                    source = destination;
                    destination = swap;
                    shift += 8;
                    remaining >>= 8;
                }
                while (remaining != 0);
            }
            // An odd number of byte passes leaves the answer in scratch.
            // Copy it back before the scratch owner can release that buffer.
            if (source != records)
                UnsafeUtility.MemCpy(records, source, (long)count * sizeof(EdgeTriangleRecord));
        }

        private sealed class RestoreSortScratch : IDisposable
        {
            internal NativeArray<EdgeTriangleRecord> Records;
            internal NativeArray<int> Buckets;

            internal RestoreSortScratch(int count)
            {
                try
                {
                    Records = new NativeArray<EdgeTriangleRecord>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    Buckets = new NativeArray<int>(256, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                if (Buckets.IsCreated)
                {
                    Buckets.Dispose();
                    Buckets = default;
                }
                if (Records.IsCreated)
                {
                    Records.Dispose();
                    Records = default;
                }
            }
        }
    }
}
