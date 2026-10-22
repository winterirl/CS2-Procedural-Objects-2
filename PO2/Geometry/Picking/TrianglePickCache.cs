using System;
using System.Collections.Generic;
using UnityEngine;

namespace PO2.Geometry.Picking
{
    // Shared by face tools and object selection. The caller owns cache lifetime;
    // small meshes scan directly and larger meshes use the same BVH traversal.
    internal sealed class TrianglePickCache
    {
        private const int kDirectScanTriangleLimit = 64;
        private const int kLeafTriangleCount = 8;
        private const float kIntersectionEpsilon = 0.000001f;

        private readonly Vector3[] m_Vertices;
        private readonly int[] m_Indices;
        private readonly PickTriangle[] m_BvhTriangles;
        private readonly PickNode[] m_BvhNodes;
        private readonly int[] m_TraversalStack;

        internal TrianglePickCache(Vector3[] vertices, int[] indices)
        {
            m_Vertices = vertices;
            m_Indices = indices;
            if (vertices == null || indices == null || indices.Length % 3 != 0)
            {
                throw new ArgumentException("Invalid picking mesh.");
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                if (!IsFinite(vertices[i]))
                {
                    throw new ArgumentException("Non-finite picking vertex.");
                }
            }

            for (int i = 0; i < indices.Length; i++)
            {
                if (indices[i] < 0 || indices[i] >= vertices.Length)
                {
                    throw new ArgumentException("Invalid picking index.");
                }
            }

            int triangleCount = indices.Length / 3;
            if (triangleCount <= kDirectScanTriangleLimit)
            {
                m_BvhTriangles = Array.Empty<PickTriangle>();
                m_BvhNodes = Array.Empty<PickNode>();
                m_TraversalStack = Array.Empty<int>();
                return;
            }

            PickTriangle[] triangles = new PickTriangle[triangleCount];
            for (int triangleId = 0; triangleId < triangleCount; triangleId++)
            {
                int offset = triangleId * 3;
                Vector3 a = m_Vertices[m_Indices[offset]];
                Vector3 b = m_Vertices[m_Indices[offset + 1]];
                Vector3 c = m_Vertices[m_Indices[offset + 2]];

                triangles[triangleId] = new PickTriangle
                {
                    TriangleId = triangleId,
                    Min = Vector3.Min(a, Vector3.Min(b, c)),
                    Max = Vector3.Max(a, Vector3.Max(b, c)),
                    Centroid = (a + b + c) / 3f,
                };
            }

            List<PickNode> nodes = new List<PickNode>(Math.Max(1, triangleCount / 2));
            BuildNode(triangles, 0, triangleCount, nodes);

            m_BvhTriangles = triangles;
            m_BvhNodes = nodes.ToArray();
            m_TraversalStack = new int[64];
        }

        internal long RetainedBytes =>
            (long)m_Vertices.Length * 12 + (long)m_Indices.Length * 4 + (long)m_BvhTriangles.Length * 40 + (long)m_BvhNodes.Length * 40 + (long)m_TraversalStack.Length * 4;

        public bool TryPick(Vector3 origin, Vector3 direction, out int triangleId, out float distance, float maximumDistance = float.PositiveInfinity)
        {
            triangleId = -1;
            distance = maximumDistance;
            if (!IsFinite(origin) || !IsFinite(direction) || direction.sqrMagnitude <= 0f || float.IsNaN(maximumDistance))
            {
                return false;
            }

            triangleId = m_BvhNodes.Length == 0 ? PickDirect(origin, direction, ref distance) : PickBvh(origin, direction, ref distance);
            return triangleId >= 0;
        }

        private int PickDirect(Vector3 origin, Vector3 direction, ref float bestT)
        {
            int bestTriangleId = -1;

            for (int triangleId = 0; triangleId < m_Indices.Length / 3; triangleId++)
            {
                int offset = triangleId * 3;
                if (TryIntersectTriangle(origin, direction, m_Vertices[m_Indices[offset]], m_Vertices[m_Indices[offset + 1]], m_Vertices[m_Indices[offset + 2]], out float t) && t <= bestT + HitTolerance(bestT) && IsBetterHit(t, triangleId, bestT, bestTriangleId))
                {
                    bestT = t;
                    bestTriangleId = triangleId;
                }
            }

            return bestTriangleId;
        }

        private int PickBvh(Vector3 origin, Vector3 direction, ref float bestT)
        {
            int bestTriangleId = -1;
            int stackCount = 0;
            m_TraversalStack[stackCount++] = 0;

            while (stackCount > 0)
            {
                int nodeIndex = m_TraversalStack[--stackCount];
                PickNode node = m_BvhNodes[nodeIndex];
                if (!TryIntersectBounds(origin, direction, node.Min, node.Max, bestT + HitTolerance(bestT), out float _))
                {
                    continue;
                }

                if (node.TriangleCount > 0)
                {
                    int end = node.FirstTriangle + node.TriangleCount;
                    for (int i = node.FirstTriangle; i < end; i++)
                    {
                        PickTriangle triangle = m_BvhTriangles[i];
                        int offset = triangle.TriangleId * 3;
                        if (TryIntersectTriangle(origin, direction, m_Vertices[m_Indices[offset]], m_Vertices[m_Indices[offset + 1]], m_Vertices[m_Indices[offset + 2]], out float t) && t <= bestT + HitTolerance(bestT) && IsBetterHit(t, triangle.TriangleId, bestT, bestTriangleId))
                        {
                            bestT = t;
                            bestTriangleId = triangle.TriangleId;
                        }
                    }

                    continue;
                }

                PickNode left = m_BvhNodes[node.LeftChild];
                PickNode right = m_BvhNodes[node.RightChild];
                bool hitLeft = TryIntersectBounds(origin, direction, left.Min, left.Max, bestT + HitTolerance(bestT), out float leftT);
                bool hitRight = TryIntersectBounds(origin, direction, right.Min, right.Max, bestT + HitTolerance(bestT), out float rightT);

                if (hitLeft && hitRight)
                {
                    if (leftT <= rightT)
                    {
                        m_TraversalStack[stackCount++] = node.RightChild;
                        m_TraversalStack[stackCount++] = node.LeftChild;
                    }
                    else
                    {
                        m_TraversalStack[stackCount++] = node.LeftChild;
                        m_TraversalStack[stackCount++] = node.RightChild;
                    }
                }
                else if (hitLeft)
                {
                    m_TraversalStack[stackCount++] = node.LeftChild;
                }
                else if (hitRight)
                {
                    m_TraversalStack[stackCount++] = node.RightChild;
                }
            }

            return bestTriangleId;
        }

        private static int BuildNode(PickTriangle[] triangles, int first, int count, List<PickNode> nodes)
        {
            int nodeIndex = nodes.Count;
            nodes.Add(default);

            Vector3 minimum = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 maximum = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            Vector3 centroidMinimum = minimum;
            Vector3 centroidMaximum = maximum;

            int end = first + count;
            for (int i = first; i < end; i++)
            {
                PickTriangle triangle = triangles[i];
                minimum = Vector3.Min(minimum, triangle.Min);
                maximum = Vector3.Max(maximum, triangle.Max);
                centroidMinimum = Vector3.Min(centroidMinimum, triangle.Centroid);
                centroidMaximum = Vector3.Max(centroidMaximum, triangle.Centroid);
            }

            if (count <= kLeafTriangleCount)
            {
                nodes[nodeIndex] = new PickNode
                {
                    Min = minimum,
                    Max = maximum,
                    LeftChild = -1,
                    RightChild = -1,
                    FirstTriangle = first,
                    TriangleCount = count,
                };
                return nodeIndex;
            }

            Vector3 extent = centroidMaximum - centroidMinimum;
            int axis = 0;
            if (extent.y > extent.x && extent.y >= extent.z)
            {
                axis = 1;
            }
            else if (extent.z > extent.x && extent.z >= extent.y)
            {
                axis = 2;
            }

            PartitionMedian(triangles, first, first + count - 1, first + count / 2, axis);

            int leftCount = count / 2;
            int leftChild = BuildNode(triangles, first, leftCount, nodes);
            int rightChild = BuildNode(triangles, first + leftCount, count - leftCount, nodes);

            nodes[nodeIndex] = new PickNode
            {
                Min = minimum,
                Max = maximum,
                LeftChild = leftChild,
                RightChild = rightChild,
                FirstTriangle = -1,
                TriangleCount = 0,
            };
            return nodeIndex;
        }

        internal static bool TryIntersectBounds(Vector3 origin, Vector3 direction, Vector3 minimum, Vector3 maximum, float maximumT, out float entryT)
        {
            float near = 0f;
            float far = maximumT;

            for (int axis = 0; axis < 3; axis++)
            {
                float axisDirection = direction[axis];
                float axisOrigin = origin[axis];
                if (axisDirection == 0f)
                {
                    if (axisOrigin < minimum[axis] || axisOrigin > maximum[axis])
                    {
                        entryT = 0f;
                        return false;
                    }

                    continue;
                }

                float inverse = 1f / axisDirection;
                float axisNear = (minimum[axis] - axisOrigin) * inverse;
                float axisFar = (maximum[axis] - axisOrigin) * inverse;
                if (axisNear > axisFar)
                {
                    float swap = axisNear;
                    axisNear = axisFar;
                    axisFar = swap;
                }

                near = Mathf.Max(near, axisNear);
                far = Mathf.Min(far, axisFar);
                if (far < near)
                {
                    entryT = 0f;
                    return false;
                }
            }

            entryT = near;
            return far >= kIntersectionEpsilon;
        }

        private static bool TryIntersectTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < kIntersectionEpsilon)
            {
                return false;
            }

            float inverse = 1f / determinant;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, p) * inverse;
            if (u < 0f || u > 1f)
            {
                return false;
            }

            Vector3 q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < 0f || u + v > 1f)
            {
                return false;
            }

            t = Vector3.Dot(edge2, q) * inverse;
            return t > kIntersectionEpsilon && !float.IsInfinity(t) && !float.IsNaN(t);
        }

        internal static float HitTolerance(float distance)
        {
            return float.IsInfinity(distance) ? 0f : kIntersectionEpsilon * Mathf.Max(1f, Mathf.Abs(distance));
        }

        // Refit a detached copy so a worker never changes a published cache.
        internal TrianglePickCache RefitCopy(Vector3[] vertices)
        {
            if (vertices.Length != m_Vertices.Length)
            {
                throw new ArgumentException("Picking vertex count changed.");
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                if (!IsFinite(vertices[i]))
                {
                    throw new ArgumentException("Non-finite picking vertex.");
                }
            }

            if (m_BvhNodes.Length == 0)
            {
                return new TrianglePickCache(vertices, m_Indices);
            }

            PickTriangle[] triangles = (PickTriangle[])m_BvhTriangles.Clone();
            PickNode[] nodes = (PickNode[])m_BvhNodes.Clone();
            for (int i = 0; i < triangles.Length; i++)
            {
                PickTriangle triangle = triangles[i];
                int offset = triangle.TriangleId * 3;
                Vector3 a = vertices[m_Indices[offset]];
                Vector3 b = vertices[m_Indices[offset + 1]];
                Vector3 c = vertices[m_Indices[offset + 2]];
                triangle.Min = Vector3.Min(a, Vector3.Min(b, c));
                triangle.Max = Vector3.Max(a, Vector3.Max(b, c));
                triangle.Centroid = (a + b + c) / 3f;
                triangles[i] = triangle;
            }

            for (int i = nodes.Length - 1; i >= 0; i--)
            {
                PickNode node = nodes[i];
                if (node.TriangleCount > 0)
                {
                    node.Min = triangles[node.FirstTriangle].Min;
                    node.Max = triangles[node.FirstTriangle].Max;
                    for (int j = 1; j < node.TriangleCount; j++)
                    {
                        PickTriangle triangle = triangles[node.FirstTriangle + j];
                        node.Min = Vector3.Min(node.Min, triangle.Min);
                        node.Max = Vector3.Max(node.Max, triangle.Max);
                    }
                }
                else
                {
                    node.Min = Vector3.Min(nodes[node.LeftChild].Min, nodes[node.RightChild].Min);
                    node.Max = Vector3.Max(nodes[node.LeftChild].Max, nodes[node.RightChild].Max);
                }

                nodes[i] = node;
            }

            return new TrianglePickCache(vertices, m_Indices, triangles, nodes);
        }

        private TrianglePickCache(Vector3[] vertices, int[] indices, PickTriangle[] triangles, PickNode[] nodes)
        {
            m_Vertices = vertices;
            m_Indices = indices;
            m_BvhTriangles = triangles;
            m_BvhNodes = nodes;
            m_TraversalStack = new int[64];
        }

        private static bool IsBetterHit(float candidateT, int candidateTriangleId, float bestT, int bestTriangleId)
        {
            if (bestTriangleId < 0)
            {
                return true;
            }

            float tieEpsilon = kIntersectionEpsilon * Mathf.Max(1f, Mathf.Max(Mathf.Abs(candidateT), Mathf.Abs(bestT)));

            return candidateT < bestT - tieEpsilon || (Mathf.Abs(candidateT - bestT) <= tieEpsilon && candidateTriangleId < bestTriangleId);
        }

        internal static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private struct PickTriangle
        {
            public int TriangleId;
            public Vector3 Min;
            public Vector3 Max;
            public Vector3 Centroid;
        }

        private struct PickNode
        {
            public Vector3 Min;
            public Vector3 Max;
            public int LeftChild;
            public int RightChild;
            public int FirstTriangle;
            public int TriangleCount;
        }

        // Only the median split is needed, not a complete sort at every level.
        private static void PartitionMedian(PickTriangle[] triangles, int left, int right, int middle, int axis)
        {
            while (left < right)
            {
                PickTriangle pivot = triangles[left + (right - left) / 2];
                int i = left;
                int j = right;
                while (i <= j)
                {
                    while (Compare(triangles[i], pivot, axis) < 0) i++;
                    while (Compare(triangles[j], pivot, axis) > 0) j--;
                    if (i <= j)
                    {
                        PickTriangle swap = triangles[i];
                        triangles[i++] = triangles[j];
                        triangles[j--] = swap;
                    }
                }

                if (middle <= j) right = j;
                else if (middle >= i) left = i;
                else return;
            }
        }

        private static int Compare(PickTriangle left, PickTriangle right, int axis)
        {
            int result = left.Centroid[axis].CompareTo(right.Centroid[axis]);
            return result != 0 ? result : left.TriangleId.CompareTo(right.TriangleId);
        }
    }
}