using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Geometry;

namespace PO2.Geometry.Topology
{
    // The editable topology derived from the render mesh. 
    // Render vertices that occupy the same point are normally grouped into one logical vertex, 
    // then triangle adjacency is rebuilt around those logical IDs.
    // 
    // There are separate construction paths on purpose. 
    // Build() may infer groups by position for ordinary new mesh data.
    // Converted assets may use the equivalent native builder through CreateFromSortedData().
    // Restore() must use the saved mapping.
    // Do NOT replace Restore() with Build() even though it looks simpler.
    public sealed partial class MeshTopology
    {
        // This is only used while initially inferring logical vertices.
        // It is not a rule that render vertices must stay within this distance later.
        // TODO: If I'm gonna support splitting vertices is can't be a hard rule later.
        public const float kDefaultWeldEpsilon = 0.0001f;

        private readonly Vertex[] m_Vertices;
        private readonly LogicalEdge[] m_Edges;
        private readonly int[] m_RenderVertexToLogicalVertex;
        private readonly Dictionary<LogicalEdgeKey, int> m_EdgeLookup;

        public IReadOnlyList<Vertex> Vertices =>
            m_Vertices;

        public IReadOnlyList<LogicalEdge> Edges =>
            m_Edges;

        public int LogicalVertexCount =>
            m_Vertices.Length;

        public int LogicalEdgeCount =>
            m_Edges.Length;

        public int RenderVertexCount =>
            m_RenderVertexToLogicalVertex.Length;

        private MeshTopology(Vertex[] vertices, LogicalEdge[] edges, int[] renderVertexToLogicalVertex, Dictionary<LogicalEdgeKey, int> edgeLookup)
        {
            m_Vertices = vertices;
            m_Edges = edges;
            m_RenderVertexToLogicalVertex = renderVertexToLogicalVertex;
            m_EdgeLookup = edgeLookup;
        }

        // The conversion-only native builder already emits edges in endpoint order.
        // It can skip constructing another giant managed dictionary and use the binary-search fallback in TryGetEdge().
        internal static MeshTopology CreateFromSortedData(Vertex[] vertices, LogicalEdge[] edges, int[] renderVertexToLogicalVertex)
        {
            if (vertices == null)
            {
                throw new ArgumentNullException(nameof(vertices));
            }

            if (edges == null)
            {
                throw new ArgumentNullException(nameof(edges));
            }

            if (renderVertexToLogicalVertex == null)
            {
                throw new ArgumentNullException(nameof(renderVertexToLogicalVertex));
            }

            return new MeshTopology(vertices, edges, renderVertexToLogicalVertex, null);
        }

        public static MeshTopology Build(MeshData mesh, float weldEpsilon = kDefaultWeldEpsilon)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (weldEpsilon <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(weldEpsilon), "Weld epsilon must be greater than zero.");
            }

            Vector3[] positions = mesh.Vertices;
            int[] triangles = mesh.Triangles;

            // Render vertices split by a UV seam or hard normal still need to move together in Mesh Edit.
            // Build the mapping once then use logical IDs everywhere else in the editing model.
            int[] renderToLogical = new int[positions.Length];

            List<LogicalVertexBuilder> logicalVertices = new List<LogicalVertexBuilder>();

            Dictionary<QuantizedPosition, List<int>> spatialBuckets = new Dictionary<QuantizedPosition, List<int>>();

            for (int renderIndex = 0; renderIndex < positions.Length; renderIndex++)
            {
                Vector3 position = positions[renderIndex];

                int logicalVertexId = FindMatchingLogicalVertex(position, weldEpsilon, spatialBuckets, logicalVertices);

                if (logicalVertexId < 0)
                {
                    logicalVertexId = logicalVertices.Count;

                    LogicalVertexBuilder builder = new LogicalVertexBuilder(logicalVertexId, position);

                    logicalVertices.Add(builder);

                    QuantizedPosition cell = QuantizedPosition.FromPosition(position, weldEpsilon);

                    if (!spatialBuckets.TryGetValue(cell, out List<int> bucket))
                    {
                        bucket = new List<int>();
                        spatialBuckets.Add(cell, bucket);
                    }

                    bucket.Add(logicalVertexId);
                }

                logicalVertices[logicalVertexId].RenderVertexIndices.Add(renderIndex);

                renderToLogical[renderIndex] = logicalVertexId;
            }

            Dictionary<LogicalEdgeKey, LogicalEdgeBuilder> logicalEdges = new Dictionary<LogicalEdgeKey, LogicalEdgeBuilder>();

            BuildAdjacency(triangles, renderToLogical, logicalVertices, logicalEdges);

            Vertex[] vertices = new Vertex[logicalVertices.Count];

            for (int i = 0; i < logicalVertices.Count; i++)
            {
                LogicalVertexBuilder builder = logicalVertices[i];

                int[] adjacentTriangles = new int[builder.TriangleIndices.Count];

                builder.TriangleIndices.CopyTo(adjacentTriangles);

                Array.Sort(adjacentTriangles);

                int[] adjacentVertices = new int[builder.AdjacentVertexIds.Count];

                builder.AdjacentVertexIds.CopyTo(adjacentVertices);

                Array.Sort(adjacentVertices);

                vertices[i] = new Vertex(builder.Id, builder.RenderVertexIndices.ToArray(), adjacentTriangles, adjacentVertices);
            }

            // Dictionary order is NOT an ID scheme.
            // Sort endpoints so the same mesh produces the same logical edge IDs every time it is rebuilt.
            List<LogicalEdgeBuilder> sortedEdgeBuilders = new List<LogicalEdgeBuilder>(logicalEdges.Values);

            sortedEdgeBuilders.Sort((left, right) =>
            {
                int compareA = left.Key.VertexA.CompareTo(right.Key.VertexA);

                return compareA != 0 ? compareA : left.Key.VertexB.CompareTo(right.Key.VertexB);
            });

            LogicalEdge[] edges = new LogicalEdge[sortedEdgeBuilders.Count];

            Dictionary<LogicalEdgeKey, int> edgeLookup = new Dictionary<LogicalEdgeKey, int>(sortedEdgeBuilders.Count);

            for (int i = 0; i < sortedEdgeBuilders.Count; i++)
            {
                LogicalEdgeBuilder builder = sortedEdgeBuilders[i];

                int[] adjacentTriangles = new int[builder.TriangleIndices.Count];

                builder.TriangleIndices.CopyTo(adjacentTriangles);
                Array.Sort(adjacentTriangles);

                edges[i] = new LogicalEdge(i, builder.Key.VertexA, builder.Key.VertexB, adjacentTriangles);

                edgeLookup.Add(builder.Key, i);
            }

            return new MeshTopology(vertices, edges, renderToLogical, edgeLookup);
        }

        internal static MeshTopology Restore(MeshData mesh, int[] renderToLogical, int logicalVertexCount)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (renderToLogical == null)
            {
                throw new ArgumentNullException(nameof(renderToLogical));
            }

            if (renderToLogical.Length != mesh.Vertices.Length)
            {
                throw new InvalidOperationException($"Persisted render-to-logical mapping contains " + $"{renderToLogical.Length} entries for " + $"{mesh.Vertices.Length} render vertices.");
            }

            if (logicalVertexCount < 0 || (mesh.Vertices.Length > 0 && logicalVertexCount == 0))
            {
                throw new InvalidOperationException($"Invalid persisted logical vertex count " + $"{logicalVertexCount}.");
            }

            // The mapping is persisted because position welding loses intent.
            // Two vertices may be split but overlapping, or welded and later moved together.
            // Re-running Build() here would undo that history. Taht would be bad.
            List<LogicalVertexBuilder> logicalVertices = new List<LogicalVertexBuilder>(logicalVertexCount);

            for (int logicalId = 0; logicalId < logicalVertexCount; logicalId++)
            {
                logicalVertices.Add(new LogicalVertexBuilder(logicalId, Vector3.zero));
            }

            // Empty logical IDs are not allowed.
            // A sparse or broken mapping would make vertex IDs unstable
            // that would break every saved reference to topology derived from them.
            bool[] represented = new bool[logicalVertexCount];
            int[] mapping = (int[])renderToLogical.Clone();

            for (int renderIndex = 0; renderIndex < mapping.Length; renderIndex++)
            {
                int logicalId = mapping[renderIndex];
                if (logicalId < 0 || logicalId >= logicalVertexCount)
                {
                    throw new InvalidOperationException($"Persisted render vertex {renderIndex} references " + $"invalid logical vertex {logicalId}; logical count is " + $"{logicalVertexCount}.");
                }

                logicalVertices[logicalId].RenderVertexIndices.Add(renderIndex);
                represented[logicalId] = true;
            }

            for (int logicalId = 0; logicalId < represented.Length; logicalId++)
            {
                if (!represented[logicalId])
                {
                    throw new InvalidOperationException($"Persisted logical vertex {logicalId} has no render " + "vertex membership.");
                }
            }

            Dictionary<LogicalEdgeKey, LogicalEdgeBuilder> logicalEdges = new Dictionary<LogicalEdgeKey, LogicalEdgeBuilder>();

            BuildAdjacency(mesh.Triangles, mapping, logicalVertices, logicalEdges);

            Vertex[] vertices = new Vertex[logicalVertices.Count];

            for (int i = 0; i < logicalVertices.Count; i++)
            {
                LogicalVertexBuilder builder = logicalVertices[i];

                int[] adjacentTriangles = new int[builder.TriangleIndices.Count];
                builder.TriangleIndices.CopyTo(adjacentTriangles);
                Array.Sort(adjacentTriangles);

                int[] adjacentVertices = new int[builder.AdjacentVertexIds.Count];
                builder.AdjacentVertexIds.CopyTo(adjacentVertices);
                Array.Sort(adjacentVertices);

                vertices[i] = new Vertex(builder.Id, builder.RenderVertexIndices.ToArray(), adjacentTriangles, adjacentVertices);
            }

            List<LogicalEdgeBuilder> sortedEdgeBuilders = new List<LogicalEdgeBuilder>(logicalEdges.Values);
            sortedEdgeBuilders.Sort((left, right) =>
            {
                int compareA = left.Key.VertexA.CompareTo(right.Key.VertexA);
                return compareA != 0 ? compareA : left.Key.VertexB.CompareTo(right.Key.VertexB);
            });

            LogicalEdge[] edges = new LogicalEdge[sortedEdgeBuilders.Count];
            Dictionary<LogicalEdgeKey, int> edgeLookup = new Dictionary<LogicalEdgeKey, int>(sortedEdgeBuilders.Count);

            for (int i = 0; i < sortedEdgeBuilders.Count; i++)
            {
                LogicalEdgeBuilder builder = sortedEdgeBuilders[i];
                int[] adjacentTriangles = new int[builder.TriangleIndices.Count];
                builder.TriangleIndices.CopyTo(adjacentTriangles);
                Array.Sort(adjacentTriangles);

                edges[i] = new LogicalEdge(i, builder.Key.VertexA, builder.Key.VertexB, adjacentTriangles);
                edgeLookup.Add(builder.Key, i);
            }

            return new MeshTopology(vertices, edges, mapping, edgeLookup);
        }

        public Vertex GetVertex(int logicalVertexId)
        {
            if (logicalVertexId < 0 || logicalVertexId >= m_Vertices.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(logicalVertexId));
            }

            return m_Vertices[logicalVertexId];
        }

        public LogicalEdge GetEdge(int logicalEdgeId)
        {
            if (logicalEdgeId < 0 || logicalEdgeId >= m_Edges.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(logicalEdgeId));
            }

            return m_Edges[logicalEdgeId];
        }

        public bool TryGetEdge(int vertexA, int vertexB, out LogicalEdge edge)
        {
            edge = default(LogicalEdge);

            if (vertexA < 0 || vertexB < 0 || vertexA == vertexB)
            {
                return false;
            }

            LogicalEdgeKey key = new LogicalEdgeKey(vertexA, vertexB);

            if (m_EdgeLookup != null)
            {
                if (!m_EdgeLookup.TryGetValue(key, out int edgeId))
                {
                    return false;
                }

                edge = m_Edges[edgeId];
                return true;
            }

            // Native edges are already sorted by endpoint. Reuse that order
            // instead of allocating another full edge lookup.
            int low = 0;
            int high = m_Edges.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                LogicalEdge candidate = m_Edges[middle];
                int compareA = candidate.VertexA.CompareTo(key.VertexA);
                int compare = compareA != 0 ? compareA : candidate.VertexB.CompareTo(key.VertexB);

                if (compare == 0)
                {
                    edge = candidate;
                    return true;
                }

                if (compare < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return false;
        }

        public int GetLogicalVertexIdForRenderVertex(int renderVertexIndex)
        {
            if (renderVertexIndex < 0 || renderVertexIndex >= m_RenderVertexToLogicalVertex.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(renderVertexIndex));
            }

            return m_RenderVertexToLogicalVertex[renderVertexIndex];
        }

        private static int FindMatchingLogicalVertex(Vector3 position, float weldEpsilon, Dictionary<QuantizedPosition, List<int>> spatialBuckets, List<LogicalVertexBuilder> logicalVertices)
        {
            QuantizedPosition centerCell = QuantizedPosition.FromPosition(position, weldEpsilon);

            float epsilonSquared = weldEpsilon * weldEpsilon;

            // Searching only the center bucket is not enough.
            // Two points can be within epsilon and still land on opposite sides of a bucket edge.
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        QuantizedPosition cell = new QuantizedPosition(centerCell.X + x, centerCell.Y + y, centerCell.Z + z);

                        if (!spatialBuckets.TryGetValue(cell, out List<int> candidates))
                        {
                            continue;
                        }

                        for (int i = 0; i < candidates.Count; i++)
                        {
                            int candidateId = candidates[i];

                            Vector3 candidatePosition = logicalVertices[candidateId].ReferencePosition;

                            if ((candidatePosition - position).sqrMagnitude <= epsilonSquared)
                            {
                                return candidateId;
                            }
                        }
                    }
                }
            }

            return -1;
        }

        private static void BuildAdjacency(int[] triangles, int[] renderToLogical, List<LogicalVertexBuilder> logicalVertices, Dictionary<LogicalEdgeKey, LogicalEdgeBuilder> logicalEdges)
        {
            if (triangles.Length % 3 != 0)
            {
                throw new InvalidOperationException("Procedural mesh triangle index array length " + "must be divisible by three.");
            }

            // Triangle adjacency includes quad diagonals. StructuralTopology filters those
            // out for the modeling tools.
            for (int index = 0; index < triangles.Length; index += 3)
            {
                int triangleId = index / 3;

                int logicalA = GetLogicalVertexForTriangleIndex(triangles[index], triangleId, renderToLogical);

                int logicalB = GetLogicalVertexForTriangleIndex(triangles[index + 1], triangleId, renderToLogical);

                int logicalC = GetLogicalVertexForTriangleIndex(triangles[index + 2], triangleId, renderToLogical);

                logicalVertices[logicalA].TriangleIndices.Add(triangleId);
                logicalVertices[logicalB].TriangleIndices.Add(triangleId);
                logicalVertices[logicalC].TriangleIndices.Add(triangleId);

                AddLogicalEdge(logicalA, logicalB, triangleId, logicalVertices, logicalEdges);

                AddLogicalEdge(logicalB, logicalC, triangleId, logicalVertices, logicalEdges);

                AddLogicalEdge(logicalC, logicalA, triangleId, logicalVertices, logicalEdges);
            }
        }

        private static int GetLogicalVertexForTriangleIndex(int renderVertexIndex, int triangleId, int[] renderToLogical)
        {
            if (renderVertexIndex < 0 || renderVertexIndex >= renderToLogical.Length)
            {
                throw new InvalidOperationException($"Triangle {triangleId} references invalid " + $"render vertex index {renderVertexIndex}. " + $"Mesh contains {renderToLogical.Length} " + "render vertices.");
            }

            return renderToLogical[renderVertexIndex];
        }

        private static void AddLogicalEdge(int a, int b, int triangleId, List<LogicalVertexBuilder> logicalVertices, Dictionary<LogicalEdgeKey, LogicalEdgeBuilder> logicalEdges)
        {
            if (a == b)
            {
                // Welding can collapse a degenerate triangle side to one logical point.
                // It is still render data, but it is not an editable edge.
                return;
            }

            logicalVertices[a].AdjacentVertexIds.Add(b);
            logicalVertices[b].AdjacentVertexIds.Add(a);

            LogicalEdgeKey key = new LogicalEdgeKey(a, b);

            if (!logicalEdges.TryGetValue(key, out LogicalEdgeBuilder edge))
            {
                edge = new LogicalEdgeBuilder(key);
                logicalEdges.Add(key, edge);
            }

            edge.TriangleIndices.Add(triangleId);
        }

        private sealed class LogicalVertexBuilder
        {
            public int Id { get; }
            public Vector3 ReferencePosition { get; }

            public List<int> RenderVertexIndices { get; } = new List<int>();

            public HashSet<int> TriangleIndices { get; } = new HashSet<int>();

            public HashSet<int> AdjacentVertexIds { get; } = new HashSet<int>();

            public LogicalVertexBuilder(int id, Vector3 referencePosition)
            {
                Id = id;
                ReferencePosition = referencePosition;
            }
        }

        private sealed class LogicalEdgeBuilder
        {
            public LogicalEdgeKey Key { get; }

            public HashSet<int> TriangleIndices { get; } = new HashSet<int>();

            public LogicalEdgeBuilder(LogicalEdgeKey key)
            {
                Key = key;
            }
        }

        private readonly struct LogicalEdgeKey : IEquatable<LogicalEdgeKey>
        {
            public int VertexA { get; }
            public int VertexB { get; }

            public LogicalEdgeKey(int vertexA, int vertexB)
            {
                // Edge lookup is undirected throughout this topology.
                VertexA = Math.Min(vertexA, vertexB);
                VertexB = Math.Max(vertexA, vertexB);
            }

            public bool Equals(LogicalEdgeKey other)
            {
                return VertexA == other.VertexA && VertexB == other.VertexB;
            }

            public override bool Equals(object obj)
            {
                return obj is LogicalEdgeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (VertexA * 397) ^ VertexB;
                }
            }
        }

        private readonly struct QuantizedPosition : IEquatable<QuantizedPosition>
        {
            public int X { get; }
            public int Y { get; }
            public int Z { get; }

            public QuantizedPosition(int x, int y, int z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public static QuantizedPosition FromPosition(Vector3 position, float epsilon)
            {
                float inverseEpsilon = 1f / epsilon;

                // The quantized cell only narrows the candidate search.
                // The actual weld decision still uses squared distance above.
                return new QuantizedPosition(Mathf.RoundToInt(position.x * inverseEpsilon), Mathf.RoundToInt(position.y * inverseEpsilon), Mathf.RoundToInt(position.z * inverseEpsilon));
            }

            public bool Equals(QuantizedPosition other)
            {
                return X == other.X && Y == other.Y && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is QuantizedPosition other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + X;
                    hash = hash * 31 + Y;
                    hash = hash * 31 + Z;
                    return hash;
                }
            }
        }
    }
}