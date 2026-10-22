using System;
using System.Collections.Generic;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Rendering.Mapping
{
    // Just enough triangle adjacency for generated UV charts.
    // This is separate from the editable structural topology because imported meshes
    // may have no useful quads at all and generated mapping still needs to work.
    internal sealed class SurfaceTopology
    {
        private readonly TriangleData[] m_Triangles;

        public int TriangleCount => m_Triangles.Length;

        public TriangleData GetTriangle(int triangleId)
        {
            return m_Triangles[triangleId];
        }

        private SurfaceTopology(TriangleData[] triangles)
        {
            m_Triangles = triangles;
        }

        public static SurfaceTopology Build(MeshData mesh, MeshTopology topology)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            if (topology == null)
            {
                throw new ArgumentNullException(nameof(topology));
            }

            int[] indices = mesh.Triangles;

            if (indices.Length % 3 != 0)
            {
                throw new InvalidOperationException("Procedural mesh triangle index array length must be divisible by three.");
            }

            int triangleCount = indices.Length / 3;
            TriangleData[] triangles = new TriangleData[triangleCount];

            // Adjacency uses logical edges so triangles can meet across split render vertices.
            // User-authored UV/normal splits are handled later as chart seam hints,
            // not mistaken for holes in the mesh.
            Dictionary<EdgeKey, EdgeUse> firstUses = new Dictionary<EdgeKey, EdgeUse>(triangleCount * 2);

            HashSet<EdgeKey> nonManifoldEdges = new HashSet<EdgeKey>();

            for (int triangleId = 0; triangleId < triangleCount; triangleId++)
            {
                int baseIndex = triangleId * 3;

                int renderA = indices[baseIndex];
                int renderB = indices[baseIndex + 1];
                int renderC = indices[baseIndex + 2];

                int logicalA = topology.GetLogicalVertexIdForRenderVertex(renderA);
                int logicalB = topology.GetLogicalVertexIdForRenderVertex(renderB);
                int logicalC = topology.GetLogicalVertexIdForRenderVertex(renderC);

                triangles[triangleId] = new TriangleData(renderA, renderB, renderC, logicalA, logicalB, logicalC);

                AddEdgeUse(new EdgeKey(logicalA, logicalB), new EdgeUse(triangleId, 0), firstUses, nonManifoldEdges, triangles);

                AddEdgeUse(new EdgeKey(logicalB, logicalC), new EdgeUse(triangleId, 1), firstUses, nonManifoldEdges, triangles);

                AddEdgeUse(new EdgeKey(logicalC, logicalA), new EdgeUse(triangleId, 2), firstUses, nonManifoldEdges, triangles);
            }

            // Edges used by more than two triangles are intentionally not traversable for chart building.
            // Remove the tentative pairing that was created when the second use was encountered.
            foreach (EdgeKey edge in nonManifoldEdges)
            {
                if (!firstUses.TryGetValue(edge, out EdgeUse first))
                {
                    continue;
                }

                TriangleData triangle = triangles[first.TriangleId];
                int neighbor = triangle.GetNeighbor(first.EdgeIndex);

                triangle.SetNeighbor(first.EdgeIndex, -1);

                if (neighbor >= 0)
                {
                    TriangleData neighborTriangle = triangles[neighbor];
                    int neighborEdge = neighborTriangle.FindNeighborEdge(first.TriangleId);

                    if (neighborEdge >= 0)
                    {
                        neighborTriangle.SetNeighbor(neighborEdge, -1);
                    }
                }
            }

            return new SurfaceTopology(triangles);
        }

        private static void AddEdgeUse(EdgeKey edge, EdgeUse use, Dictionary<EdgeKey, EdgeUse> firstUses, HashSet<EdgeKey> nonManifoldEdges, TriangleData[] triangles)
        {
            if (nonManifoldEdges.Contains(edge))
            {
                return;
            }

            // First use waits for a partner, second creates the pair,
            // and a third marks the edge non-manifold so chart traversal stops there.
            if (!firstUses.TryGetValue(edge, out EdgeUse first))
            {
                firstUses.Add(edge, use);
                return;
            }

            TriangleData firstTriangle = triangles[first.TriangleId];

            if (firstTriangle.GetNeighbor(first.EdgeIndex) >= 0)
            {
                nonManifoldEdges.Add(edge);
                return;
            }

            firstTriangle.SetNeighbor(first.EdgeIndex, use.TriangleId);
            triangles[use.TriangleId].SetNeighbor(use.EdgeIndex, first.TriangleId);
        }

        internal sealed class TriangleData
        {
            private readonly int[] m_RenderVertices = new int[3];
            private readonly int[] m_LogicalVertices = new int[3];
            private readonly int[] m_Neighbors = { -1, -1, -1 };

            public TriangleData(int renderA, int renderB, int renderC, int logicalA, int logicalB, int logicalC)
            {
                m_RenderVertices[0] = renderA;
                m_RenderVertices[1] = renderB;
                m_RenderVertices[2] = renderC;

                m_LogicalVertices[0] = logicalA;
                m_LogicalVertices[1] = logicalB;
                m_LogicalVertices[2] = logicalC;
            }

            public int GetRenderVertex(int corner)
            {
                return m_RenderVertices[corner];
            }

            public int GetLogicalVertex(int corner)
            {
                return m_LogicalVertices[corner];
            }

            public int GetNeighbor(int edge)
            {
                return m_Neighbors[edge];
            }

            public void SetNeighbor(int edge, int triangleId)
            {
                m_Neighbors[edge] = triangleId;
            }

            public int FindNeighborEdge(int triangleId)
            {
                for (int edge = 0; edge < 3; edge++)
                {
                    if (m_Neighbors[edge] == triangleId)
                    {
                        return edge;
                    }
                }

                return -1;
            }

            public void GetEdgeLogicalVertices(int edge, out int logicalA, out int logicalB)
            {
                int firstCorner = edge;
                int secondCorner = (edge + 1) % 3;

                logicalA = m_LogicalVertices[firstCorner];
                logicalB = m_LogicalVertices[secondCorner];
            }

            public bool TryGetRenderVertexForLogical(int logicalVertexId, out int renderVertexIndex)
            {
                for (int corner = 0; corner < 3; corner++)
                {
                    if (m_LogicalVertices[corner] == logicalVertexId)
                    {
                        renderVertexIndex = m_RenderVertices[corner];
                        return true;
                    }
                }

                renderVertexIndex = -1;
                return false;
            }
        }

        private readonly struct EdgeUse
        {
            public int TriangleId { get; }
            public int EdgeIndex { get; }

            public EdgeUse(int triangleId, int edgeIndex)
            {
                TriangleId = triangleId;
                EdgeIndex = edgeIndex;
            }
        }

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            private readonly int m_A;
            private readonly int m_B;

            public EdgeKey(int a, int b)
            {
                if (a <= b)
                {
                    m_A = a;
                    m_B = b;
                }
                else
                {
                    m_A = b;
                    m_B = a;
                }
            }

            public bool Equals(EdgeKey other)
            {
                return m_A == other.m_A && m_B == other.m_B;
            }

            public override bool Equals(object obj)
            {
                return obj is EdgeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (m_A * 397) ^ m_B;
                }
            }
        }
    }
}