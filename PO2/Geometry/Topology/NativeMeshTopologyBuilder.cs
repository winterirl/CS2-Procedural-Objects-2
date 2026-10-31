using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using PO2.Conversion;
using PO2.Geometry;

namespace PO2.Geometry.Topology
{
    // Conversion and restore run in Task.Run(), where Unity jobs cannot be scheduled.
    // Cached Burst function pointers keep the native build on that worker.
    [BurstCompile]
    internal static unsafe partial class NativeMeshTopologyBuilder
    {
        private const int kSortStackCapacity = 128;
        private const int kCompatibilityValidationVertexLimit = 4096;

        [StructLayout(LayoutKind.Sequential)]
        internal struct EdgeTriangleRecord
        {
            public int VertexA;
            public int VertexB;
            public int TriangleId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SortRange
        {
            public int Low;
            public int High;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeTopologyContext
        {
            public float3* Positions;
            public int PositionCount;
            public int* Triangles;
            public int IndexCount;
            public float WeldEpsilon;

            public int* RenderToLogical;
            public float3* LogicalReferencePositions;
            public int* LogicalNext;

            public int3* HashKeys;
            public int* HashHeads;
            public int* HashTails;
            public byte* HashUsed;
            public int HashCapacity;

            public int* VertexTriangleOffsets;
            public int* VertexTriangles;
            public EdgeTriangleRecord* EdgeTriangles;
            public EdgeTriangleRecord* RestoreSortRecords;
            public int* RestoreSortBuckets;
            public int* AdjacentVertexOffsets;
            public int* AdjacentVertices;
            public int* GroupCursors;
            public SortRange* SortStack;
            public int SortStackCapacity;

            public int LogicalVertexCount;
            public int VertexTriangleCount;
            public int EdgeTriangleCount;
            public int EdgeCount;
            public int AdjacencyCount;
            public int ErrorCode;
            public int ErrorItem;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void TopologyKernel(ref NativeTopologyContext context);

        private static readonly object s_KernelLock = new object();
        private static FunctionPointer<TopologyKernel> s_WeldFunction;
        private static FunctionPointer<TopologyKernel> s_AdjacencyRecordsFunction;
        private static FunctionPointer<TopologyKernel> s_EdgeSortFunction;
        private static FunctionPointer<TopologyKernel> s_AdjacencyPackFunction;
        private static TopologyKernel s_WeldKernel;
        private static TopologyKernel s_AdjacencyRecordsKernel;
        private static TopologyKernel s_EdgeSortKernel;
        private static TopologyKernel s_AdjacencyPackKernel;
        private static bool s_KernelInitializationAttempted;
        private static string s_KernelInitializationFailure = string.Empty;

        public static bool TryBuild(MeshData mesh, float weldEpsilon, out MeshTopology topology, out string fallbackReason)
        {
            topology = null;
            fallbackReason = string.Empty;

            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (weldEpsilon <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(weldEpsilon));
            }

            Vector3[] positions = mesh.Vertices;
            int[] triangles = mesh.Triangles;
            if (triangles.Length % 3 != 0)
            {
                throw new InvalidOperationException("Procedural mesh triangle index array length must be " + "divisible by three.");
            }

            for (int i = 0; i < triangles.Length; i++)
            {
                int renderVertexIndex = triangles[i];
                if (renderVertexIndex < 0 || renderVertexIndex >= positions.Length)
                {
                    throw new InvalidOperationException($"Triangle {i / 3} references invalid render vertex " + $"index {renderVertexIndex}. Mesh contains " + $"{positions.Length} render vertices.");
                }
            }

            if (!TryGetKernels(out TopologyKernel weldKernel, out TopologyKernel adjacencyRecordsKernel, out TopologyKernel edgeSortKernel, out TopologyKernel adjacencyPackKernel, out fallbackReason))
            {
                return false;
            }

            using (NativeBuffers buffers = new NativeBuffers(positions, triangles))
            {
                NativeTopologyContext context = buffers.CreateContext(weldEpsilon);
                weldKernel(ref context);

                ThrowIfKernelFailed(context, "position welding");
                adjacencyRecordsKernel(ref context);

                ThrowIfKernelFailed(context, "adjacency record construction");
                edgeSortKernel(ref context);

                ThrowIfKernelFailed(context, "edge sorting");
                adjacencyPackKernel(ref context);

                ThrowIfKernelFailed(context, "adjacency packing");
                topology = MaterializeTopology(buffers, context);
            }

            if (positions.Length <= kCompatibilityValidationVertexLimit)
            {
                MeshTopology reference = MeshTopology.Build(mesh, weldEpsilon);
                ValidateEquivalent(topology, reference);
            }

            return true;
        }

        private static TopologyKernel s_RestoreRecords, s_RestoreSort, s_RestorePack;

        // Restore never runs position welding. Pin only the immutable inputs for
        // the synchronous kernels; every worker owns its adjacency scratch.
        internal static MeshTopology Restore(MeshData mesh, int[] mapping, int logicalCount, bool takeOwnership)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));
            if (mapping.Length != mesh.Vertices.Length || logicalCount < 0 || logicalCount > mapping.Length || (mapping.Length > 0 && logicalCount == 0))
                throw new InvalidOperationException("Invalid persisted render-to-logical mapping size.");
            var represented = new bool[logicalCount];
            foreach (int id in mapping)
            {
                if (id < 0 || id >= logicalCount) throw new InvalidOperationException("Invalid persisted logical vertex ID.");
                represented[id] = true;
            }
            foreach (bool present in represented)
                if (!present) throw new InvalidOperationException("Persisted logical vertex has no render membership.");
            int[] triangles = mesh.Triangles;
            if (triangles.Length % 3 != 0) throw new InvalidOperationException("Invalid triangle index buffer.");
            foreach (int index in triangles)
                if (index < 0 || index >= mapping.Length) throw new InvalidOperationException("Invalid triangle render index.");

            TopologyKernel records, sort, pack;
            lock (s_KernelLock)
            {
                if (s_RestoreRecords == null)
                {
                    // The managed fallback uses exactly the same packing algorithm.
                    s_RestoreRecords = AdjacencyRecordsKernel;
                    s_RestoreSort = RestoreEdgeSortKernel;
                    s_RestorePack = AdjacencyPackKernel;
                    if (BurstCompiler.IsEnabled)
                    {
                        try
                        {
                            var r = BurstCompiler.CompileFunctionPointer<TopologyKernel>(AdjacencyRecordsKernel).Invoke;
                            var s = BurstCompiler.CompileFunctionPointer<TopologyKernel>(RestoreEdgeSortKernel).Invoke;
                            var p = BurstCompiler.CompileFunctionPointer<TopologyKernel>(AdjacencyPackKernel).Invoke;
                            s_RestoreRecords = r;
                            s_RestoreSort = s;
                            s_RestorePack = p;
                        }
                        catch (Exception)
                        {
                            // Compilation is optional; malformed geometry is not.
                        }
                    }
                }
                records = s_RestoreRecords;
                sort = s_RestoreSort;
                pack = s_RestorePack;
            }

            int[] owned = takeOwnership ? mapping : (int[])mapping.Clone();
            using (var buffers = new NativeBuffers(logicalCount, triangles.Length))
            fixed (int* mapped = owned)
            fixed (int* indices = triangles)
            {
                var context = new NativeTopologyContext
                {
                    PositionCount = mapping.Length, IndexCount = triangles.Length,
                    Triangles = indices, RenderToLogical = mapped, LogicalVertexCount = logicalCount,
                    VertexTriangleOffsets = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(buffers.VertexTriangleOffsets),
                    VertexTriangles = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(buffers.VertexTriangles),
                    EdgeTriangles = (EdgeTriangleRecord*)NativeArrayUnsafeUtility.GetUnsafePtr(buffers.EdgeTriangles),
                    AdjacentVertexOffsets = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(buffers.AdjacentVertexOffsets),
                    AdjacentVertices = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(buffers.AdjacentVertices),
                    GroupCursors = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(buffers.GroupCursors),
                    SortStack = (SortRange*)NativeArrayUnsafeUtility.GetUnsafePtr(buffers.SortStack),
                    SortStackCapacity = kSortStackCapacity
                };
                records(ref context);
                ThrowIfKernelFailed(context, "restore records");
                using (var scratch = UseRestoreRadix(ref context)
                    ? new RestoreSortScratch(context.EdgeTriangleCount) : null)
                {
                    try
                    {
                        if (scratch != null)
                        {
                            context.RestoreSortRecords = (EdgeTriangleRecord*)NativeArrayUnsafeUtility.GetUnsafePtr(scratch.Records);
                            context.RestoreSortBuckets = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(scratch.Buckets);
                        }
                        sort(ref context);
                        ThrowIfKernelFailed(context, "restore sort");
                    }
                    finally
                    {
                        context.RestoreSortRecords = null;
                        context.RestoreSortBuckets = null;
                    }
                }
                pack(ref context);
                ThrowIfKernelFailed(context, "restore packing");
                return MaterializeTopology(buffers, context, owned);
            }
        }

        private static void ValidateEquivalent(MeshTopology actual, MeshTopology expected)
        {
            if (actual.LogicalVertexCount != expected.LogicalVertexCount || actual.LogicalEdgeCount != expected.LogicalEdgeCount || actual.RenderVertexCount != expected.RenderVertexCount)
            {
                throw new InvalidOperationException("Burst topology does not match the managed topology counts.");
            }

            for (int renderIndex = 0; renderIndex < actual.RenderVertexCount; renderIndex++)
            {
                if (actual.GetLogicalVertexIdForRenderVertex(renderIndex) != expected.GetLogicalVertexIdForRenderVertex(renderIndex))
                {
                    throw new InvalidOperationException($"Burst topology mapped render vertex {renderIndex} " + "differently from the managed topology builder.");
                }
            }

            for (int vertexId = 0; vertexId < actual.LogicalVertexCount; vertexId++)
            {
                Vertex actualVertex = actual.GetVertex(vertexId);
                Vertex expectedVertex = expected.GetVertex(vertexId);
                ValidateEqualSpan(actualVertex.RenderVertexIndices, expectedVertex.RenderVertexIndices, $"logical vertex {vertexId} render membership");
                ValidateEqualSpan(actualVertex.TriangleIndices, expectedVertex.TriangleIndices, $"logical vertex {vertexId} triangle adjacency");
                ValidateEqualSpan(actualVertex.AdjacentVertexIds, expectedVertex.AdjacentVertexIds, $"logical vertex {vertexId} vertex adjacency");
            }

            for (int edgeId = 0; edgeId < actual.LogicalEdgeCount; edgeId++)
            {
                LogicalEdge actualEdge = actual.GetEdge(edgeId);
                LogicalEdge expectedEdge = expected.GetEdge(edgeId);
                if (actualEdge.VertexA != expectedEdge.VertexA || actualEdge.VertexB != expectedEdge.VertexB)
                {
                    throw new InvalidOperationException($"Burst logical edge {edgeId} has different endpoints " + "from the managed topology builder.");
                }

                ValidateEqualSpan(actualEdge.TriangleIndices, expectedEdge.TriangleIndices, $"logical edge {edgeId} triangle adjacency");
            }
        }

        private static void ValidateEqualSpan(IndexSpan actual, IndexSpan expected, string label)
        {
            if (actual.Count != expected.Count)
            {
                throw new InvalidOperationException($"Burst topology {label} has {actual.Count} entries; " + $"managed topology has {expected.Count}.");
            }

            for (int i = 0; i < actual.Count; i++)
            {
                if (actual[i] != expected[i])
                {
                    throw new InvalidOperationException($"Burst topology {label} differs at entry {i}.");
                }
            }
        }

        private static bool TryGetKernels(out TopologyKernel weldKernel, out TopologyKernel adjacencyRecordsKernel, out TopologyKernel edgeSortKernel, out TopologyKernel adjacencyPackKernel, out string failureReason)
        {
            weldKernel = null;
            adjacencyRecordsKernel = null;
            edgeSortKernel = null;
            adjacencyPackKernel = null;
            failureReason = string.Empty;

            if (!BurstCompiler.IsEnabled)
            {
                failureReason = "Burst compilation is disabled.";
                return false;
            }

            lock (s_KernelLock)
            {
                if (!s_KernelInitializationAttempted)
                {
                    s_KernelInitializationAttempted = true;
                    try
                    {
                        s_WeldFunction = BurstCompiler.CompileFunctionPointer<TopologyKernel>(new TopologyKernel(WeldKernel));
                        s_AdjacencyRecordsFunction = BurstCompiler.CompileFunctionPointer<TopologyKernel>(new TopologyKernel(AdjacencyRecordsKernel));
                        s_EdgeSortFunction = BurstCompiler.CompileFunctionPointer<TopologyKernel>(new TopologyKernel(EdgeSortKernel));
                        s_AdjacencyPackFunction = BurstCompiler.CompileFunctionPointer<TopologyKernel>(new TopologyKernel(AdjacencyPackKernel));
                        s_WeldKernel = s_WeldFunction.Invoke;
                        s_AdjacencyRecordsKernel = s_AdjacencyRecordsFunction.Invoke;
                        s_EdgeSortKernel = s_EdgeSortFunction.Invoke;
                        s_AdjacencyPackKernel = s_AdjacencyPackFunction.Invoke;
                    }
                    catch (Exception exception)
                    {
                        s_KernelInitializationFailure = $"Burst topology kernels could not compile: " + exception.Message;
                    }
                }

                if (s_WeldKernel == null || s_AdjacencyRecordsKernel == null || s_EdgeSortKernel == null || s_AdjacencyPackKernel == null)
                {
                    failureReason = string.IsNullOrWhiteSpace(s_KernelInitializationFailure) ? "Burst topology kernels are unavailable." : s_KernelInitializationFailure;
                    return false;
                }

                weldKernel = s_WeldKernel;
                adjacencyRecordsKernel = s_AdjacencyRecordsKernel;
                edgeSortKernel = s_EdgeSortKernel;
                adjacencyPackKernel = s_AdjacencyPackKernel;
                return true;
            }
        }

        private static void ThrowIfKernelFailed(NativeTopologyContext context, string stage)
        {
            if (context.ErrorCode == 0)
            {
                return;
            }

            string detail;
            switch (context.ErrorCode)
            {
                case 1:
                    detail = "the native spatial hash ran out of capacity";
                    break;
                case 2:
                    detail = "the native sort stack ran out of capacity";
                    break;
                case 3:
                    detail = $"triangle {context.ErrorItem} referenced invalid geometry";
                    break;
                case 4:
                    detail = "the packed adjacency buffers were inconsistent";
                    break;
                case 5:
                    detail = "the restore sort scratch was missing";
                    break;
                default:
                    detail = $"native error {context.ErrorCode}";
                    break;
            }

            throw new InvalidOperationException($"Burst topology {stage} failed because {detail}.");
        }

        private static MeshTopology MaterializeTopology(NativeBuffers buffers, NativeTopologyContext context, int[] ownedMapping = null)
        {
            int logicalVertexCount = context.LogicalVertexCount;
            int renderVertexCount = context.PositionCount;

            if (logicalVertexCount < 0 || logicalVertexCount > renderVertexCount || (renderVertexCount > 0 && logicalVertexCount == 0))
            {
                throw new InvalidOperationException("Burst topology produced an invalid logical vertex count.");
            }

            if (context.VertexTriangleCount < 0 || context.VertexTriangleCount > context.IndexCount || context.EdgeTriangleCount < 0 || context.EdgeTriangleCount > context.IndexCount || context.EdgeCount < 0 || context.EdgeCount > context.EdgeTriangleCount || context.AdjacencyCount != context.EdgeCount * 2 || context.AdjacencyCount > buffers.AdjacentVertices.Length)
            {
                throw new InvalidOperationException("Burst topology produced invalid adjacency counts.");
            }

            ValidatePackedOffsets(buffers.VertexTriangleOffsets, logicalVertexCount, context.VertexTriangleCount, "vertex/triangle");
            ValidatePackedOffsets(buffers.AdjacentVertexOffsets, logicalVertexCount, context.AdjacencyCount, "vertex adjacency");
            int[] renderToLogical = ownedMapping ?? CopyNativeInts(buffers.RenderToLogical, renderVertexCount);
            int[] renderCounts = new int[logicalVertexCount];
            for (int renderIndex = 0; renderIndex < renderVertexCount; renderIndex++)
            {
                int logicalId = renderToLogical[renderIndex];
                if (logicalId < 0 || logicalId >= logicalVertexCount)
                {
                    throw new InvalidOperationException($"Burst topology mapped render vertex {renderIndex} " + $"to invalid logical vertex {logicalId}.");
                }

                renderCounts[logicalId]++;
            }

            int[] renderOffsets = BuildOffsets(renderCounts);
            int[] renderCursors = (int[])renderOffsets.Clone();
            int[] renderMembership = new int[renderVertexCount];
            for (int renderIndex = 0; renderIndex < renderVertexCount; renderIndex++)
            {
                int logicalId = renderToLogical[renderIndex];
                renderMembership[renderCursors[logicalId]++] = renderIndex;
            }

            int[] vertexTriangles = CopyNativeInts(buffers.VertexTriangles, context.VertexTriangleCount);
            int[] adjacentVertices = CopyNativeInts(buffers.AdjacentVertices, context.AdjacencyCount);
            Vertex[] vertices = new Vertex[logicalVertexCount];
            for (int logicalId = 0; logicalId < logicalVertexCount; logicalId++)
            {
                if (renderCounts[logicalId] <= 0)
                {
                    throw new InvalidOperationException($"Burst logical vertex {logicalId} has no render " + "vertex membership.");
                }

                vertices[logicalId] = new Vertex(logicalId, renderMembership, renderOffsets[logicalId], renderCounts[logicalId], vertexTriangles, buffers.VertexTriangleOffsets[logicalId], buffers.VertexTriangleOffsets[logicalId + 1] - buffers.VertexTriangleOffsets[logicalId], adjacentVertices, buffers.AdjacentVertexOffsets[logicalId], buffers.AdjacentVertexOffsets[logicalId + 1] - buffers.AdjacentVertexOffsets[logicalId]);
            }

            LogicalEdge[] edges = MaterializeEdges(buffers, context);

            return MeshTopology.CreateFromSortedData(vertices, edges, renderToLogical);
        }

        private static void ValidatePackedOffsets(NativeArray<int> offsets, int groupCount, int itemCount, string label)
        {
            if (groupCount < 0 || groupCount >= offsets.Length || offsets[0] != 0)
            {
                throw new InvalidOperationException($"Burst topology produced invalid {label} offsets.");
            }

            int previous = 0;
            for (int i = 1; i <= groupCount; i++)
            {
                int current = offsets[i];
                if (current < previous || current > itemCount)
                {
                    throw new InvalidOperationException($"Burst topology produced invalid {label} offsets.");
                }

                previous = current;
            }

            if (previous != itemCount)
            {
                throw new InvalidOperationException($"Burst topology {label} offsets end at {previous}; " + $"expected {itemCount}.");
            }
        }

        private static int[] CopyNativeInts(NativeArray<int> source, int count)
        {
            if (count < 0 || count > source.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            int[] result = new int[count];
            if (count == 0)
            {
                return result;
            }

            fixed (int* destination = result)
            {
                UnsafeUtility.MemCpy(destination, NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(source), checked((long)count * sizeof(int)));
            }

            return result;
        }

        private static LogicalEdge[] MaterializeEdges(NativeBuffers buffers, NativeTopologyContext context)
        {
            int edgeCount = context.EdgeCount;
            int edgeTriangleCount = context.EdgeTriangleCount;
            LogicalEdge[] edges = new LogicalEdge[edgeCount];
            int[] edgeTriangles = new int[edgeTriangleCount];

            if (edgeCount == 0)
            {
                if (edgeTriangleCount != 0)
                {
                    throw new InvalidOperationException("Burst topology returned edge triangles without edges.");
                }

                return edges;
            }

            int edgeIndex = -1;
            int edgeStart = 0;
            int vertexA = -1;
            int vertexB = -1;

            for (int i = 0; i < edgeTriangleCount; i++)
            {
                EdgeTriangleRecord record = buffers.EdgeTriangles[i];
                if (record.VertexA < 0 || record.VertexB <= record.VertexA || record.VertexB >= context.LogicalVertexCount || record.TriangleId < 0 || record.TriangleId >= context.IndexCount / 3 || (edgeIndex >= 0 && (record.VertexA < vertexA || (record.VertexA == vertexA && record.VertexB < vertexB))) || (edgeIndex >= 0 && record.VertexA == vertexA && record.VertexB == vertexB && record.TriangleId <= edgeTriangles[i - 1]))
                {
                    throw new InvalidOperationException("Burst topology produced an invalid edge/triangle " + "adjacency record.");
                }

                bool startsEdge = edgeIndex < 0 || record.VertexA != vertexA || record.VertexB != vertexB;

                if (startsEdge)
                {
                    if (edgeIndex >= 0)
                    {
                        edges[edgeIndex] = new LogicalEdge(edgeIndex, vertexA, vertexB, edgeTriangles, edgeStart, i - edgeStart);
                    }

                    edgeIndex++;
                    edgeStart = i;
                    vertexA = record.VertexA;
                    vertexB = record.VertexB;
                }

                edgeTriangles[i] = record.TriangleId;
            }

            if (edgeIndex >= 0)
            {
                edges[edgeIndex] = new LogicalEdge(edgeIndex, vertexA, vertexB, edgeTriangles, edgeStart, edgeTriangleCount - edgeStart);
            }

            if (edgeIndex + 1 != edgeCount)
            {
                throw new InvalidOperationException($"Burst topology reported {edgeCount} edges but " + $"materialized {edgeIndex + 1}.");
            }

            return edges;
        }

        private static int[] BuildOffsets(int[] counts)
        {
            int[] offsets = new int[counts.Length + 1];
            int offset = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                offsets[i] = offset;
                offset = checked(offset + counts[i]);
            }

            offsets[counts.Length] = offset;
            return offsets;
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        private static void WeldKernel(ref NativeTopologyContext context)
        {
            int logicalVertexCount = 0;
            float epsilonSquared = context.WeldEpsilon * context.WeldEpsilon;
            float inverseEpsilon = 1f / context.WeldEpsilon;

            for (int renderIndex = 0; renderIndex < context.PositionCount; renderIndex++)
            {
                float3 position = context.Positions[renderIndex];
                float3 scaledPosition = position * inverseEpsilon;
                int3 centerCell = new int3(RoundToInt(scaledPosition.x), RoundToInt(scaledPosition.y), RoundToInt(scaledPosition.z));
                int logicalId = -1;

                // Keep the old x/y/z bucket traversal and per-bucket insertion order.
                // This preserves first-match behavior for weird meshes with several
                // candidates inside the weld radius.
                for (int x = -1; x <= 1 && logicalId < 0; x++)
                {
                    for (int y = -1; y <= 1 && logicalId < 0; y++)
                    {
                        for (int z = -1; z <= 1; z++)
                        {
                            int3 cell = centerCell + new int3(x, y, z);
                            int slot = FindHashSlot(ref context, cell, false);
                            if (slot < 0)
                            {
                                continue;
                            }

                            int candidate = context.HashHeads[slot];
                            while (candidate >= 0)
                            {
                                float3 difference = context.LogicalReferencePositions[candidate] - position;
                                float distanceSquared = difference.x * difference.x;
                                distanceSquared += difference.y * difference.y;
                                distanceSquared += difference.z * difference.z;
                                if (distanceSquared <= epsilonSquared)
                                {
                                    logicalId = candidate;
                                    break;
                                }

                                candidate = context.LogicalNext[candidate];
                            }

                            if (logicalId >= 0)
                            {
                                break;
                            }
                        }
                    }
                }

                if (logicalId < 0)
                {
                    logicalId = logicalVertexCount++;
                    context.LogicalReferencePositions[logicalId] = position;
                    context.LogicalNext[logicalId] = -1;

                    int slot = FindHashSlot(ref context, centerCell, true);
                    if (slot < 0)
                    {
                        context.ErrorCode = 1;
                        context.ErrorItem = renderIndex;
                        return;
                    }

                    int tail = context.HashTails[slot];
                    if (tail < 0)
                    {
                        context.HashHeads[slot] = logicalId;
                    }
                    else
                    {
                        context.LogicalNext[tail] = logicalId;
                    }

                    context.HashTails[slot] = logicalId;
                }

                context.RenderToLogical[renderIndex] = logicalId;
            }

            context.LogicalVertexCount = logicalVertexCount;
        }

        private static int RoundToInt(float value)
        {
            // Mathf.RoundToInt uses midpoint-to-even.
            // Spell it out here rather than depending on a platform intrinsic choosing the same tie rule.
            float lowerFloat = math.floor(value);
            int lower = (int)lowerFloat;
            float fraction = value - lowerFloat;
            if (fraction < 0.5f)
            {
                return lower;
            }

            if (fraction > 0.5f)
            {
                return lower + 1;
            }

            return (lower & 1) == 0 ? lower : lower + 1;
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        private static void AdjacencyRecordsKernel(ref NativeTopologyContext context)
        {
            int vertexTriangleCount = 0;
            int edgeTriangleCount = 0;

            for (int index = 0; index < context.IndexCount; index += 3)
            {
                int triangleId = index / 3;
                int renderA = context.Triangles[index];
                int renderB = context.Triangles[index + 1];
                int renderC = context.Triangles[index + 2];

                if (renderA < 0 || renderA >= context.PositionCount || renderB < 0 || renderB >= context.PositionCount || renderC < 0 || renderC >= context.PositionCount)
                {
                    context.ErrorCode = 3;
                    context.ErrorItem = triangleId;
                    return;
                }

                int logicalA = context.RenderToLogical[renderA];
                int logicalB = context.RenderToLogical[renderB];
                int logicalC = context.RenderToLogical[renderC];
                if (logicalA < 0 || logicalA >= context.LogicalVertexCount || logicalB < 0 || logicalB >= context.LogicalVertexCount || logicalC < 0 || logicalC >= context.LogicalVertexCount)
                {
                    context.ErrorCode = 4;
                    context.ErrorItem = triangleId;
                    return;
                }

                // Welding can collapse a triangle. Count each logical vertex only once.
                context.VertexTriangleOffsets[logicalA + 1]++;
                vertexTriangleCount++;
                if (logicalB != logicalA)
                {
                    context.VertexTriangleOffsets[logicalB + 1]++;
                    vertexTriangleCount++;
                }

                if (logicalC != logicalA && logicalC != logicalB)
                {
                    context.VertexTriangleOffsets[logicalC + 1]++;
                    vertexTriangleCount++;
                }

                AddEdgeRecord(context.EdgeTriangles, ref edgeTriangleCount, logicalA, logicalB, triangleId);
                AddEdgeRecord(context.EdgeTriangles, ref edgeTriangleCount, logicalB, logicalC, triangleId);
                AddEdgeRecord(context.EdgeTriangles, ref edgeTriangleCount, logicalC, logicalA, triangleId);
            }

            context.VertexTriangleCount = vertexTriangleCount;
            context.EdgeTriangleCount = edgeTriangleCount;
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        private static void EdgeSortKernel(ref NativeTopologyContext context)
        {
            int edgeTriangleCount = context.EdgeTriangleCount;
            if (!SortEdgeTriangles(context.EdgeTriangles, edgeTriangleCount, context.SortStack, context.SortStackCapacity))
            {
                context.ErrorCode = 2;
                return;
            }

            FinishEdgeSort(ref context);
        }

        private static void FinishEdgeSort(ref NativeTopologyContext context)
        {
            int edgeTriangleCount = CompactEdgeTriangles(context.EdgeTriangles, context.EdgeTriangleCount);
            int edgeCount = 0;
            int previousA = -1;
            int previousB = -1;
            for (int i = 0; i < edgeTriangleCount; i++)
            {
                EdgeTriangleRecord edge = context.EdgeTriangles[i];
                if (i > 0 && edge.VertexA == previousA && edge.VertexB == previousB)
                {
                    continue;
                }

                previousA = edge.VertexA;
                previousB = edge.VertexB;
                edgeCount++;
                context.AdjacentVertexOffsets[edge.VertexA + 1]++;
                context.AdjacentVertexOffsets[edge.VertexB + 1]++;
            }

            context.EdgeTriangleCount = edgeTriangleCount;
            context.EdgeCount = edgeCount;
            context.AdjacencyCount = edgeCount * 2;
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        private static void AdjacencyPackKernel(ref NativeTopologyContext context)
        {
            BuildOffsetsInPlace(context.VertexTriangleOffsets, context.LogicalVertexCount);
            CopyOffsetsToCursors(context.VertexTriangleOffsets, context.GroupCursors, context.LogicalVertexCount);

            for (int index = 0; index < context.IndexCount; index += 3)
            {
                int triangleId = index / 3;
                int logicalA = context.RenderToLogical[context.Triangles[index]];
                int logicalB = context.RenderToLogical[context.Triangles[index + 1]];
                int logicalC = context.RenderToLogical[context.Triangles[index + 2]];

                AppendPackedItem(context.VertexTriangles, context.GroupCursors, logicalA, triangleId);
                if (logicalB != logicalA)
                {
                    AppendPackedItem(context.VertexTriangles, context.GroupCursors, logicalB, triangleId);
                }

                if (logicalC != logicalA && logicalC != logicalB)
                {
                    AppendPackedItem(context.VertexTriangles, context.GroupCursors, logicalC, triangleId);
                }
            }

            if (!ValidatePackedCursors(context.VertexTriangleOffsets, context.GroupCursors, context.LogicalVertexCount))
            {
                context.ErrorCode = 4;
                return;
            }

            BuildOffsetsInPlace(context.AdjacentVertexOffsets, context.LogicalVertexCount);
            CopyOffsetsToCursors(context.AdjacentVertexOffsets, context.GroupCursors, context.LogicalVertexCount);

            int previousA = -1;
            int previousB = -1;
            for (int i = 0; i < context.EdgeTriangleCount; i++)
            {
                EdgeTriangleRecord edge = context.EdgeTriangles[i];
                if (i > 0 && edge.VertexA == previousA && edge.VertexB == previousB)
                {
                    continue;
                }

                previousA = edge.VertexA;
                previousB = edge.VertexB;

                // Edges are sorted by (A, B).
                // For one vertex, reverse edges contribute every lower neighbor first
                // and forward edges then contribute every higher neighbor, both in ascending order.
                AppendPackedItem(context.AdjacentVertices, context.GroupCursors, edge.VertexA, edge.VertexB);
                AppendPackedItem(context.AdjacentVertices, context.GroupCursors, edge.VertexB, edge.VertexA);
            }

            if (!ValidatePackedCursors(context.AdjacentVertexOffsets, context.GroupCursors, context.LogicalVertexCount))
            {
                context.ErrorCode = 4;
            }
        }

        private static void BuildOffsetsInPlace(int* offsets, int groupCount)
        {
            for (int i = 1; i <= groupCount; i++)
            {
                offsets[i] += offsets[i - 1];
            }
        }

        private static void CopyOffsetsToCursors(int* offsets, int* cursors, int groupCount)
        {
            for (int i = 0; i < groupCount; i++)
            {
                cursors[i] = offsets[i];
            }
        }

        private static void AppendPackedItem(int* items, int* cursors, int groupId, int value)
        {
            items[cursors[groupId]++] = value;
        }

        private static bool ValidatePackedCursors(int* offsets, int* cursors, int groupCount)
        {
            for (int i = 0; i < groupCount; i++)
            {
                if (cursors[i] != offsets[i + 1])
                {
                    return false;
                }
            }

            return true;
        }

        private static int FindHashSlot(ref NativeTopologyContext context, int3 key, bool create)
        {
            int mask = context.HashCapacity - 1;
            uint hash = HashCell(key);
            int slot = (int)(hash & (uint)mask);

            for (int probe = 0; probe < context.HashCapacity; probe++)
            {
                if (context.HashUsed[slot] == 0)
                {
                    if (!create)
                    {
                        return -1;
                    }

                    context.HashUsed[slot] = 1;
                    context.HashKeys[slot] = key;
                    context.HashHeads[slot] = -1;
                    context.HashTails[slot] = -1;
                    return slot;
                }

                if (math.all(context.HashKeys[slot] == key))
                {
                    return slot;
                }

                slot = (slot + 1) & mask;
            }

            return -1;
        }

        private static uint HashCell(int3 cell)
        {
            uint hash = (uint)cell.x * 0x8da6b343u;
            hash ^= (uint)cell.y * 0xd8163841u;
            hash ^= (uint)cell.z * 0xcb1ab31fu;
            hash ^= hash >> 16;
            hash *= 0x7feb352du;
            hash ^= hash >> 15;
            return hash;
        }

        private static void AddEdgeRecord(EdgeTriangleRecord* records, ref int count, int vertexA, int vertexB, int triangleId)
        {
            if (vertexA == vertexB)
            {
                return;
            }

            records[count++] = new EdgeTriangleRecord
            {
                VertexA = math.min(vertexA, vertexB),
                VertexB = math.max(vertexA, vertexB),
                TriangleId = triangleId,
            };
        }

        private static int CompactEdgeTriangles(EdgeTriangleRecord* records, int count)
        {
            int write = 0;
            for (int read = 0; read < count; read++)
            {
                EdgeTriangleRecord current = records[read];
                if (write > 0)
                {
                    EdgeTriangleRecord previous = records[write - 1];
                    if (current.VertexA == previous.VertexA && current.VertexB == previous.VertexB && current.TriangleId == previous.TriangleId)
                    {
                        continue;
                    }
                }

                records[write++] = current;
            }

            return write;
        }

        private static bool SortEdgeTriangles(EdgeTriangleRecord* items, int count, SortRange* stack, int stackCapacity)
        {
            if (count < 2)
            {
                return true;
            }

            int stackCount = 0;
            int low = 0;
            int high = count - 1;

            while (true)
            {
                while (high - low > 16)
                {
                    EdgeTriangleRecord pivot = items[low + ((high - low) >> 1)];
                    int left = low;
                    int right = high;

                    while (left <= right)
                    {
                        while (Compare(items[left], pivot) < 0)
                        {
                            left++;
                        }

                        while (Compare(items[right], pivot) > 0)
                        {
                            right--;
                        }

                        if (left <= right)
                        {
                            EdgeTriangleRecord swap = items[left];
                            items[left] = items[right];
                            items[right] = swap;
                            left++;
                            right--;
                        }
                    }

                    if (right - low < high - left)
                    {
                        if (left < high && !PushRange(stack, stackCapacity, ref stackCount, left, high))
                        {
                            return false;
                        }

                        high = right;
                    }
                    else
                    {
                        if (low < right && !PushRange(stack, stackCapacity, ref stackCount, low, right))
                        {
                            return false;
                        }

                        low = left;
                    }
                }

                InsertionSort(items, low, high);
                if (stackCount == 0)
                {
                    return true;
                }

                SortRange range = stack[--stackCount];
                low = range.Low;
                high = range.High;
            }
        }

        private static bool PushRange(SortRange* stack, int capacity, ref int count, int low, int high)
        {
            if (count >= capacity)
            {
                return false;
            }

            stack[count++] = new SortRange
            {
                Low = low,
                High = high,
            };
            return true;
        }

        private static void InsertionSort(EdgeTriangleRecord* items, int low, int high)
        {
            for (int i = low + 1; i <= high; i++)
            {
                EdgeTriangleRecord value = items[i];
                int cursor = i - 1;
                while (cursor >= low && Compare(items[cursor], value) > 0)
                {
                    items[cursor + 1] = items[cursor--];
                }

                items[cursor + 1] = value;
            }
        }

        private static int Compare(EdgeTriangleRecord left, EdgeTriangleRecord right)
        {
            if (left.VertexA != right.VertexA)
            {
                return left.VertexA < right.VertexA ? -1 : 1;
            }

            if (left.VertexB != right.VertexB)
            {
                return left.VertexB < right.VertexB ? -1 : 1;
            }

            if (left.TriangleId != right.TriangleId)
            {
                return left.TriangleId < right.TriangleId ? -1 : 1;
            }

            return 0;
        }

        private sealed class NativeBuffers : IDisposable
        {
            public NativeArray<float3> Positions;
            public NativeArray<int> Triangles;
            public NativeArray<int> RenderToLogical;
            public NativeArray<float3> LogicalReferencePositions;
            public NativeArray<int> LogicalNext;
            public NativeArray<int3> HashKeys;
            public NativeArray<int> HashHeads;
            public NativeArray<int> HashTails;
            public NativeArray<byte> HashUsed;
            public NativeArray<int> VertexTriangleOffsets;
            public NativeArray<int> VertexTriangles;
            public NativeArray<EdgeTriangleRecord> EdgeTriangles;
            public NativeArray<int> AdjacentVertexOffsets;
            public NativeArray<int> AdjacentVertices;
            public NativeArray<int> GroupCursors;
            public NativeArray<SortRange> SortStack;

            public NativeBuffers(int logicalCount, int indexCount)
            {
                try
                {
                    VertexTriangleOffsets = CreateArray<int>(checked(logicalCount + 1), NativeArrayOptions.ClearMemory);
                    VertexTriangles = CreateArray<int>(indexCount);
                    EdgeTriangles = CreateArray<EdgeTriangleRecord>(indexCount);
                    AdjacentVertexOffsets = CreateArray<int>(checked(logicalCount + 1), NativeArrayOptions.ClearMemory);
                    AdjacentVertices = CreateArray<int>(checked(indexCount * 2));
                    GroupCursors = CreateArray<int>(logicalCount);
                    SortStack = CreateArray<SortRange>(kSortStackCapacity);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public NativeBuffers(Vector3[] positions, int[] triangles)
            {
                try
                {
                    int positionCount = positions.Length;
                    int indexCount = triangles.Length;
                    int hashCapacity = CalculateHashCapacity(positionCount);
                    int adjacencyCapacity = checked(indexCount * 2);

                    Positions = CreateArray<float3>(positionCount);
                    Triangles = CreateArray<int>(indexCount);
                    RenderToLogical = CreateArray<int>(positionCount);
                    LogicalReferencePositions = CreateArray<float3>(positionCount);
                    LogicalNext = CreateArray<int>(positionCount);
                    HashKeys = CreateArray<int3>(hashCapacity);
                    HashHeads = CreateArray<int>(hashCapacity);
                    HashTails = CreateArray<int>(hashCapacity);
                    HashUsed = CreateArray<byte>(hashCapacity, NativeArrayOptions.ClearMemory);
                    VertexTriangleOffsets = CreateArray<int>(checked(positionCount + 1), NativeArrayOptions.ClearMemory);
                    VertexTriangles = CreateArray<int>(indexCount);
                    EdgeTriangles = CreateArray<EdgeTriangleRecord>(indexCount);
                    AdjacentVertexOffsets = CreateArray<int>(checked(positionCount + 1), NativeArrayOptions.ClearMemory);
                    AdjacentVertices = CreateArray<int>(adjacencyCapacity);
                    GroupCursors = CreateArray<int>(positionCount);
                    SortStack = CreateArray<SortRange>(kSortStackCapacity);

                    for (int i = 0; i < positionCount; i++)
                    {
                        Vector3 position = positions[i];
                        Positions[i] = new float3(position.x, position.y, position.z);
                    }

                    for (int i = 0; i < indexCount; i++)
                    {
                        Triangles[i] = triangles[i];
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public NativeTopologyContext CreateContext(float weldEpsilon)
            {
                return new NativeTopologyContext
                {
                    Positions = (float3*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Positions),
                    PositionCount = Positions.Length,
                    Triangles = (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Triangles),
                    IndexCount = Triangles.Length,
                    WeldEpsilon = weldEpsilon,
                    RenderToLogical = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(RenderToLogical),
                    LogicalReferencePositions = (float3*)NativeArrayUnsafeUtility.GetUnsafePtr(LogicalReferencePositions),
                    LogicalNext = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(LogicalNext),
                    HashKeys = (int3*)NativeArrayUnsafeUtility.GetUnsafePtr(HashKeys),
                    HashHeads = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(HashHeads),
                    HashTails = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(HashTails),
                    HashUsed = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(HashUsed),
                    HashCapacity = HashUsed.Length,
                    VertexTriangleOffsets = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(VertexTriangleOffsets),
                    VertexTriangles = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(VertexTriangles),
                    EdgeTriangles = (EdgeTriangleRecord*)NativeArrayUnsafeUtility.GetUnsafePtr(EdgeTriangles),
                    AdjacentVertexOffsets = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(AdjacentVertexOffsets),
                    AdjacentVertices = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(AdjacentVertices),
                    GroupCursors = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(GroupCursors),
                    SortStack = (SortRange*)NativeArrayUnsafeUtility.GetUnsafePtr(SortStack),
                    SortStackCapacity = SortStack.Length,
                };
            }

            public void Dispose()
            {
                DisposeArray(ref SortStack);
                DisposeArray(ref GroupCursors);
                DisposeArray(ref AdjacentVertices);
                DisposeArray(ref AdjacentVertexOffsets);
                DisposeArray(ref EdgeTriangles);
                DisposeArray(ref VertexTriangles);
                DisposeArray(ref VertexTriangleOffsets);
                DisposeArray(ref HashUsed);
                DisposeArray(ref HashTails);
                DisposeArray(ref HashHeads);
                DisposeArray(ref HashKeys);
                DisposeArray(ref LogicalNext);
                DisposeArray(ref LogicalReferencePositions);
                DisposeArray(ref RenderToLogical);
                DisposeArray(ref Triangles);
                DisposeArray(ref Positions);
            }

            private static NativeArray<T> CreateArray<T>(int length, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory) where T : struct
            {
                return new NativeArray<T>(length, Allocator.Persistent, options);
            }

            private static void DisposeArray<T>(ref NativeArray<T> array) where T : struct
            {
                if (array.IsCreated)
                {
                    array.Dispose();
                    array = default(NativeArray<T>);
                }
            }

            private static int CalculateHashCapacity(int itemCount)
            {
                long required = Math.Max(1L, (long)itemCount * 2L);
                if (required > 1L << 30)
                {
                    throw new InvalidOperationException("Mesh is too large for the native topology spatial hash.");
                }

                int capacity = 1;
                while (capacity < required)
                {
                    capacity <<= 1;
                }

                return capacity;
            }
        }
    }
}
