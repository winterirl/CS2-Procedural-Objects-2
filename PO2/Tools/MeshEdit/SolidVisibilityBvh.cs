using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;

namespace PO2.Tools.MeshEdit
{
    internal struct SolidBvhTriangle
    {
        public float3 A;
        public float3 B;
        public float3 C;
    }

    internal struct SolidBvhTriangleSource
    {
        public int PartIndex;
        public int VertexA;
        public int VertexB;
        public int VertexC;
    }

    internal struct SolidBvhNode
    {
        public float3 Min;
        public float3 Max;
        public int LeftChild;
        public int RightChild;
        public int FirstTriangle;
        public int TriangleCount;
    }

    // CPU-built, Burst-queried acceleration structure for Mesh Edit Solid
    // visibility. Query targets can be vertices or sampled structural-edge
    // points. This is editor visibility only and never affects the object mesh.
    internal sealed partial class SolidVisibilityBvh : IDisposable
    {
        private const int kLeafTriangleCount = 8;

        private NativeArray<SolidBvhNode> m_Nodes;
        private NativeArray<SolidBvhTriangle> m_Triangles;
        private NativeArray<SolidBvhTriangleSource> m_TriangleSources;
        private NativeArray<float3> m_QueryTargets;
        private NativeArray<byte> m_QueryFacingCandidates;
        private NativeArray<byte> m_QueryResults;
        private int m_QueryCapacity;

        private int m_ObjectId = -1;
        private uint m_GeometryVersion;
        private ProceduralObject m_SourceObject;
        private MeshPart[] m_SourceParts = Array.Empty<MeshPart>();
        private uint[] m_PartGeometryVersions = Array.Empty<uint>();
        private uint[] m_PartTopologyVersions = Array.Empty<uint>();
        private Vector3[][] m_RefitPartVertices = Array.Empty<Vector3[]>();
        private float m_OrthographicRayLength = 10f;
        private bool m_HasBuild;
        internal int BuildSerial { get; private set; }

        public NativeArray<float3> QueryTargets => m_QueryTargets;

        public NativeArray<byte> QueryFacingCandidates =>
            m_QueryFacingCandidates;

        public NativeArray<byte> QueryResults => m_QueryResults;

        public void Invalidate()
        {
            m_ObjectId = -1;
            m_GeometryVersion = 0;
            m_HasBuild = false;
        }

        public bool EnsureBuilt(ProceduralObject proceduralObject)
        {
            if (proceduralObject == null)
            {
                return false;
            }

            bool sameObject = m_HasBuild && m_ObjectId == proceduralObject.Id && ReferenceEquals(m_SourceObject, proceduralObject);

            bool topologyMatches = sameObject && HasMatchingTopology(proceduralObject);

            if (topologyMatches && m_GeometryVersion == proceduralObject.GeometryVersion && HasMatchingGeometry(proceduralObject))
            {
                return true;
            }

            // Vertex movement keeps triangle membership intact, so update leaf
            // bounds in place. Only topology changes pay for sorting/rebuilding.
            if (topologyMatches && CanRefit(proceduralObject))
            {
                Refit(proceduralObject);
                return true;
            }

            BuildTriangle[] buildTriangles = CollectTriangles(proceduralObject);

            List<SolidBvhNode> nodes = new List<SolidBvhNode>(Math.Max(1, buildTriangles.Length * 2));

            if (buildTriangles.Length > 0)
            {
                BuildNode(buildTriangles, 0, buildTriangles.Length, nodes);
            }

            ReplaceBuildBuffers(buildTriangles, nodes);

            m_ObjectId = proceduralObject.Id;
            m_GeometryVersion = proceduralObject.GeometryVersion;
            CaptureTopologyVersions(proceduralObject);
            m_HasBuild = true;

            m_OrthographicRayLength = CalculateOrthographicRayLength(nodes);

            return true;
        }

        private bool HasMatchingTopology(ProceduralObject proceduralObject)
        {
            if (m_PartTopologyVersions.Length != proceduralObject.Parts.Count)
            {
                return false;
            }

            for (int partIndex = 0; partIndex < proceduralObject.Parts.Count; partIndex++)
            {
                if (!ReferenceEquals(m_SourceParts[partIndex], proceduralObject.Parts[partIndex]) || m_PartTopologyVersions[partIndex] != proceduralObject.Parts[partIndex].TopologyVersion)
                {
                    return false;
                }
            }

            return true;
        }

        private void CaptureTopologyVersions(ProceduralObject proceduralObject)
        {
            int partCount = proceduralObject.Parts.Count;
            m_SourceObject = proceduralObject;
            if (m_SourceParts.Length != partCount) m_SourceParts = new MeshPart[partCount];
            if (m_PartGeometryVersions.Length != partCount) m_PartGeometryVersions = new uint[partCount];
            for (int i = 0; i < partCount; i++)
            {
                m_SourceParts[i] = proceduralObject.Parts[i];
                m_PartGeometryVersions[i] = proceduralObject.Parts[i].GeometryVersion;
            }

            if (m_PartTopologyVersions.Length != partCount)
            {
                m_PartTopologyVersions = new uint[partCount];
            }

            for (int partIndex = 0; partIndex < partCount; partIndex++)
            {
                m_PartTopologyVersions[partIndex] = proceduralObject.Parts[partIndex].TopologyVersion;
            }
        }

        private bool HasMatchingGeometry(ProceduralObject obj)
        {
            for (int i = 0; i < obj.Parts.Count; i++)
                if (m_PartGeometryVersions[i] != obj.Parts[i].GeometryVersion)
                    return false;
            return true;
        }

        private bool CanRefit(ProceduralObject proceduralObject)
        {
            // Matching topology versions keep triangle indices valid. The refit already
            // visits every triangle; repeating validation would double that work.
            return m_Triangles.IsCreated && m_TriangleSources.IsCreated && m_Nodes.IsCreated && m_Triangles.Length == m_TriangleSources.Length;
        }

        private void Refit(ProceduralObject proceduralObject)
        {
            int partCount = proceduralObject.Parts.Count;
            if (m_RefitPartVertices.Length != partCount)
            {
                m_RefitPartVertices = new Vector3[partCount][];
            }

            for (int partIndex = 0; partIndex < partCount; partIndex++)
            {
                m_RefitPartVertices[partIndex] = proceduralObject.Parts[partIndex].Mesh.Vertices;
            }

            for (int triangleIndex = 0; triangleIndex < m_TriangleSources.Length; triangleIndex++)
            {
                SolidBvhTriangleSource source = m_TriangleSources[triangleIndex];
                Vector3[] vertices = m_RefitPartVertices[source.PartIndex];

                m_Triangles[triangleIndex] = new SolidBvhTriangle
                {
                    A = ToFloat3(vertices[source.VertexA]),
                    B = ToFloat3(vertices[source.VertexB]),
                    C = ToFloat3(vertices[source.VertexC]),
                };
            }

            // Children are always appended after parents during BuildNode.
            // Walking backward therefore updates leaves before their parents.
            for (int nodeIndex = m_Nodes.Length - 1; nodeIndex >= 0; nodeIndex--)
            {
                RefitNode(nodeIndex);
            }

            if (m_Nodes.Length > 0)
            {
                SolidBvhNode root = m_Nodes[0];
                float diagonal = math.length(root.Max - root.Min);
                m_OrthographicRayLength = math.max(10f, diagonal * 2f + 10f);
            }

            m_GeometryVersion = proceduralObject.GeometryVersion;
            CaptureTopologyVersions(proceduralObject);
        }

        private void RefitNode(int nodeIndex)
        {
            SolidBvhNode node = m_Nodes[nodeIndex];

            if (node.TriangleCount > 0)
            {
                float3 minimum = new float3(float.MaxValue);
                float3 maximum = new float3(float.MinValue);
                int triangleEnd = node.FirstTriangle + node.TriangleCount;

                for (int triangleIndex = node.FirstTriangle; triangleIndex < triangleEnd; triangleIndex++)
                {
                    SolidBvhTriangle triangle = m_Triangles[triangleIndex];

                    minimum = math.min(minimum, math.min(triangle.A, math.min(triangle.B, triangle.C)));
                    maximum = math.max(maximum, math.max(triangle.A, math.max(triangle.B, triangle.C)));
                }

                node.Min = minimum;
                node.Max = maximum;
            }
            else if (node.LeftChild >= 0 && node.RightChild >= 0)
            {
                SolidBvhNode left = m_Nodes[node.LeftChild];
                SolidBvhNode right = m_Nodes[node.RightChild];

                node.Min = math.min(left.Min, right.Min);
                node.Max = math.max(left.Max, right.Max);
            }

            m_Nodes[nodeIndex] = node;
        }

        public void EnsureQueryCapacity(int requiredCount)
        {
            if (requiredCount <= m_QueryCapacity)
            {
                return;
            }

            int newCapacity = Math.Max(16, m_QueryCapacity == 0 ? requiredCount : m_QueryCapacity);

            while (newCapacity < requiredCount)
            {
                newCapacity *= 2;
            }

            DisposeQueryBuffers();

            m_QueryTargets = new NativeArray<float3>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            m_QueryFacingCandidates = new NativeArray<byte>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            m_QueryResults = new NativeArray<byte>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            m_QueryCapacity = newCapacity;
        }

        public JobHandle ScheduleVisibility(int queryCount, float3 perspectiveCameraOrigin, float3 orthographicDirection, bool orthographic)
        {
            SolidVisibilityJob job = new SolidVisibilityJob
            {
                Nodes = m_Nodes,
                Triangles = m_Triangles,
                Targets = m_QueryTargets,
                FacingCandidates = m_QueryFacingCandidates,
                Results = m_QueryResults,
                PerspectiveCameraOrigin = perspectiveCameraOrigin,
                OrthographicDirection = orthographicDirection,
                OrthographicRayLength = m_OrthographicRayLength,
                IsOrthographic = orthographic,
            };

            return job.Schedule(queryCount, 64);
        }

        public void Dispose()
        {
            DisposeBuildBuffers();
            DisposeQueryBuffers();
            Invalidate();
        }

        private void ReplaceBuildBuffers(BuildTriangle[] buildTriangles, List<SolidBvhNode> nodes)
        {
            DisposeBuildBuffers();
            BuildSerial++;

            if (buildTriangles.Length > 0)
            {
                m_Triangles = new NativeArray<SolidBvhTriangle>(buildTriangles.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                m_TriangleSources = new NativeArray<SolidBvhTriangleSource>(buildTriangles.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                for (int i = 0; i < buildTriangles.Length; i++)
                {
                    m_Triangles[i] = buildTriangles[i].Triangle;
                    m_TriangleSources[i] = buildTriangles[i].Source;
                }
            }

            if (nodes.Count > 0)
            {
                m_Nodes = new NativeArray<SolidBvhNode>(nodes.Count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                for (int i = 0; i < nodes.Count; i++)
                {
                    m_Nodes[i] = nodes[i];
                }
            }
        }

        private static BuildTriangle[] CollectTriangles(ProceduralObject proceduralObject)
        {
            List<BuildTriangle> triangles = new List<BuildTriangle>();

            for (int partIndex = 0; partIndex < proceduralObject.Parts.Count; partIndex++)
            {
                MeshPart part = proceduralObject.Parts[partIndex];

                Vector3[] vertices = part.Mesh.Vertices;
                int[] indices = part.Mesh.Triangles;

                for (int index = 0; index + 2 < indices.Length; index += 3)
                {
                    int indexA = indices[index];
                    int indexB = indices[index + 1];
                    int indexC = indices[index + 2];

                    if (indexA < 0 || indexB < 0 || indexC < 0 || indexA >= vertices.Length || indexB >= vertices.Length || indexC >= vertices.Length)
                    {
                        continue;
                    }

                    float3 a = ToFloat3(vertices[indexA]);
                    float3 b = ToFloat3(vertices[indexB]);
                    float3 c = ToFloat3(vertices[indexC]);

                    float3 minimum = math.min(a, math.min(b, c));
                    float3 maximum = math.max(a, math.max(b, c));

                    triangles.Add(new BuildTriangle
                    {
                        Triangle = new SolidBvhTriangle
                        {
                            A = a,
                            B = b,
                            C = c,
                        },
                        Source = new SolidBvhTriangleSource
                        {
                            PartIndex = partIndex,
                            VertexA = indexA,
                            VertexB = indexB,
                            VertexC = indexC,
                        },
                        Min = minimum,
                        Max = maximum,
                        Centroid = (a + b + c) / 3f,
                    });
                }
            }

            return triangles.ToArray();
        }

        private static int BuildNode(BuildTriangle[] triangles, int first, int count, List<SolidBvhNode> nodes, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            int nodeIndex = nodes.Count;
            nodes.Add(default);

            float3 minimum = new float3(float.MaxValue);
            float3 maximum = new float3(float.MinValue);
            float3 centroidMinimum = new float3(float.MaxValue);
            float3 centroidMaximum = new float3(float.MinValue);

            int end = first + count;
            for (int i = first; i < end; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                BuildTriangle triangle = triangles[i];
                minimum = math.min(minimum, triangle.Min);
                maximum = math.max(maximum, triangle.Max);
                centroidMinimum = math.min(centroidMinimum, triangle.Centroid);
                centroidMaximum = math.max(centroidMaximum, triangle.Centroid);
            }

            // Median split on the widest centroid axis. It is not a perfect SAH
            // tree, but build cost is predictable and good enough for edit mode.
            if (count <= kLeafTriangleCount)
            {
                nodes[nodeIndex] = new SolidBvhNode
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

            float3 centroidExtent = centroidMaximum - centroidMinimum;

            int axis = 0;
            if (centroidExtent.y > centroidExtent.x && centroidExtent.y >= centroidExtent.z)
            {
                axis = 1;
            }
            else if (centroidExtent.z > centroidExtent.x && centroidExtent.z >= centroidExtent.y)
            {
                axis = 2;
            }

            Array.Sort(triangles, first, count, new BuildTriangleComparer(axis));

            int leftCount = count / 2;
            int rightCount = count - leftCount;
            int leftChild = BuildNode(triangles, first, leftCount, nodes, token);
            int rightChild = BuildNode(triangles, first + leftCount, rightCount, nodes, token);

            nodes[nodeIndex] = new SolidBvhNode
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

        private static float CalculateOrthographicRayLength(List<SolidBvhNode> nodes)
        {
            if (nodes.Count == 0)
            {
                return 10f;
            }

            SolidBvhNode root = nodes[0];
            float diagonal = math.length(root.Max - root.Min);
            return math.max(10f, diagonal * 2f + 10f);
        }

        private void DisposeBuildBuffers()
        {
            if (m_Nodes.IsCreated)
            {
                m_Nodes.Dispose();
            }

            if (m_Triangles.IsCreated)
            {
                m_Triangles.Dispose();
            }

            if (m_TriangleSources.IsCreated)
            {
                m_TriangleSources.Dispose();
            }
        }

        private void DisposeQueryBuffers()
        {
            if (m_QueryTargets.IsCreated)
            {
                m_QueryTargets.Dispose();
            }

            if (m_QueryFacingCandidates.IsCreated)
            {
                m_QueryFacingCandidates.Dispose();
            }

            if (m_QueryResults.IsCreated)
            {
                m_QueryResults.Dispose();
            }

            m_QueryCapacity = 0;
        }

        private static float3 ToFloat3(Vector3 value)
        {
            return new float3(value.x, value.y, value.z);
        }

        private struct BuildTriangle
        {
            public SolidBvhTriangle Triangle;
            public SolidBvhTriangleSource Source;
            public float3 Min;
            public float3 Max;
            public float3 Centroid;
        }

        private sealed class BuildTriangleComparer : IComparer<BuildTriangle>
        {
            private readonly int m_Axis;

            public BuildTriangleComparer(int axis)
            {
                m_Axis = axis;
            }

            public int Compare(BuildTriangle left, BuildTriangle right)
            {
                float leftValue = left.Centroid[m_Axis];
                float rightValue = right.Centroid[m_Axis];
                return leftValue.CompareTo(rightValue);
            }
        }

        [BurstCompile]
        private struct SolidVisibilityJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<SolidBvhNode> Nodes;

            [ReadOnly] public NativeArray<SolidBvhTriangle> Triangles;

            [ReadOnly] public NativeArray<float3> Targets;

            [ReadOnly] public NativeArray<byte> FacingCandidates;

            [WriteOnly] public NativeArray<byte> Results;

            public float3 PerspectiveCameraOrigin;
            public float3 OrthographicDirection;
            public float OrthographicRayLength;
            public bool IsOrthographic;

            public void Execute(int index)
            {
                if (FacingCandidates[index] == 0)
                {
                    Results[index] = 0;
                    return;
                }

                float3 target = Targets[index];
                float3 origin;
                float3 direction;
                float targetDistance;

                // Perspective rays start at the camera. Orthographic rays all
                // share a direction, so start far enough behind each target to
                // cross the complete object bounds.
                if (IsOrthographic)
                {
                    direction = math.normalizesafe(OrthographicDirection, new float3(0f, 0f, 1f));
                    targetDistance = OrthographicRayLength;
                    origin = target - direction * targetDistance;
                }
                else
                {
                    origin = PerspectiveCameraOrigin;
                    float3 delta = target - origin;
                    targetDistance = math.length(delta);

                    if (targetDistance <= 0.000001f)
                    {
                        Results[index] = 1;
                        return;
                    }

                    direction = delta / targetDistance;
                }

                // Stop just before the target so triangles incident to a
                // vertex/edge sample do not classify the target as occluding
                // itself.
                float endpointEpsilon = math.max(0.001f, targetDistance * 0.00001f);
                float maxHitDistance = targetDistance - endpointEpsilon;

                if (maxHitDistance <= endpointEpsilon || Nodes.Length == 0 || Triangles.Length == 0)
                {
                    Results[index] = 1;
                    return;
                }

                bool occluded = IsOccluded(origin, direction, endpointEpsilon, maxHitDistance);

                Results[index] = occluded ? (byte)0 : (byte)1;
            }

            private bool IsOccluded(float3 origin, float3 direction, float minimumDistance, float maximumDistance)
            {
                // FixedList keeps traversal allocation-free inside the Burst
                // job. This tree is binary and shallow for any sane PO mesh.
                FixedList512Bytes<int> stack = default;
                stack.Add(0);

                while (stack.Length > 0)
                {
                    int lastIndex = stack.Length - 1;
                    int nodeIndex = stack[lastIndex];
                    stack.RemoveAt(lastIndex);

                    SolidBvhNode node = Nodes[nodeIndex];
                    if (!IntersectsBounds(origin, direction, minimumDistance, maximumDistance, node.Min, node.Max))
                    {
                        continue;
                    }

                    if (node.TriangleCount > 0)
                    {
                        int triangleEnd = node.FirstTriangle + node.TriangleCount;

                        for (int triangleIndex = node.FirstTriangle; triangleIndex < triangleEnd; triangleIndex++)
                        {
                            if (IntersectsTriangle(origin, direction, minimumDistance, maximumDistance, Triangles[triangleIndex]))
                            {
                                return true;
                            }
                        }

                        continue;
                    }

                    if (node.LeftChild >= 0)
                    {
                        stack.Add(node.LeftChild);
                    }

                    if (node.RightChild >= 0)
                    {
                        stack.Add(node.RightChild);
                    }
                }

                return false;
            }

            private static bool IntersectsBounds(float3 origin, float3 direction, float minimumDistance, float maximumDistance, float3 minimum, float3 maximum)
            {
                float tMin = minimumDistance;
                float tMax = maximumDistance;

                if (!IntersectAxis(origin.x, direction.x, minimum.x, maximum.x, ref tMin, ref tMax) || !IntersectAxis(origin.y, direction.y, minimum.y, maximum.y, ref tMin, ref tMax) || !IntersectAxis(origin.z, direction.z, minimum.z, maximum.z, ref tMin, ref tMax))
                {
                    return false;
                }

                return tMax >= tMin;
            }

            private static bool IntersectAxis(float origin, float direction, float minimum, float maximum, ref float tMin, ref float tMax)
            {
                const float directionEpsilon = 0.0000001f;

                if (math.abs(direction) <= directionEpsilon)
                {
                    return origin >= minimum && origin <= maximum;
                }

                float inverse = 1f / direction;
                float axisMin = (minimum - origin) * inverse;
                float axisMax = (maximum - origin) * inverse;

                if (axisMin > axisMax)
                {
                    float swap = axisMin;
                    axisMin = axisMax;
                    axisMax = swap;
                }

                tMin = math.max(tMin, axisMin);
                tMax = math.min(tMax, axisMax);

                return tMax >= tMin;
            }

            private static bool IntersectsTriangle(float3 origin, float3 direction, float minimumDistance, float maximumDistance, SolidBvhTriangle triangle)
            {
                const float determinantEpsilon = 0.0000001f;

                float3 edge1 = triangle.B - triangle.A;
                float3 edge2 = triangle.C - triangle.A;
                float3 p = math.cross(direction, edge2);
                float determinant = math.dot(edge1, p);

                if (math.abs(determinant) <= determinantEpsilon)
                {
                    return false;
                }

                float inverseDeterminant = 1f / determinant;
                float3 t = origin - triangle.A;
                float u = math.dot(t, p) * inverseDeterminant;

                if (u < 0f || u > 1f)
                {
                    return false;
                }

                float3 q = math.cross(t, edge1);
                float v = math.dot(direction, q) * inverseDeterminant;

                if (v < 0f || u + v > 1f)
                {
                    return false;
                }

                float distance = math.dot(edge2, q) * inverseDeterminant;

                return distance > minimumDistance && distance < maximumDistance;
            }
        }
    }
}