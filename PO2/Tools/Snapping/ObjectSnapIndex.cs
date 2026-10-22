using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using PO2.Core;

namespace PO2.Tools.Snapping
{
    // One PO keeps one local-space feature tree. Root transforms are applied
    // during queries, so ordinary object movement never invalidates the build.
    internal sealed class ObjectSnapIndex : IDisposable
    {
        private const float kTargetTieEpsilon = 0.0001f;
        private const float kSourceTieEpsilon = 0.0000001f;
        private const float kTargetQueryTighteningRatio = 0.75f;
        private const int kMaximumTargetQueryTightenings = 4;

        internal struct Node
        {
            public float3 Minimum;
            public float3 Maximum;
            public int Start;
            public int Count;
            public int Left;
            public int Right;
        }

        internal struct BuildRange
        {
            public int NodeIndex;
            public int Start;
            public int Count;
        }

        [BurstCompile]
        internal struct BuildBvhJob : IJob
        {
            private const int kLeafPrimitiveCount = 8;

            [ReadOnly] public NativeArray<float3> LocalVertices;

            [ReadOnly] public NativeArray<int2> Edges;

            public NativeArray<int> PrimitiveOrder;
            public int PrimitiveCount;
            public NativeArray<Node> Nodes;
            public NativeArray<BuildRange> WorkStack;
            public NativeArray<int> NodeCount;

            public void Execute()
            {
                if (PrimitiveCount <= 0)
                {
                    NodeCount[0] = 0;
                    return;
                }

                int nextNodeIndex = 1;
                int stackCount = 1;

                WorkStack[0] = new BuildRange
                {
                    NodeIndex = 0,
                    Start = 0,
                    Count = PrimitiveCount,
                };

                while (stackCount > 0)
                {
                    BuildRange range = WorkStack[--stackCount];

                    CalculateBounds(range.Start, range.Count, out float3 minimum, out float3 maximum, out float3 centerMinimum, out float3 centerMaximum);

                    if (range.Count <= kLeafPrimitiveCount || nextNodeIndex + 1 >= Nodes.Length)
                    {
                        Nodes[range.NodeIndex] = new Node
                        {
                            Minimum = minimum,
                            Maximum = maximum,
                            Start = range.Start,
                            Count = range.Count,
                            Left = -1,
                            Right = -1,
                        };

                        continue;
                    }

                    float3 centerSize = centerMaximum - centerMinimum;

                    int axis = centerSize.x >= centerSize.y && centerSize.x >= centerSize.z ? 0 : centerSize.y >= centerSize.z ? 1 : 2;

                    int middle = range.Start + range.Count / 2;
                    SelectMedian(range.Start, range.Start + range.Count - 1, middle, axis);

                    int leftNode = nextNodeIndex++;
                    int rightNode = nextNodeIndex++;

                    Nodes[range.NodeIndex] = new Node
                    {
                        Minimum = minimum,
                        Maximum = maximum,
                        Start = 0,
                        Count = 0,
                        Left = leftNode,
                        Right = rightNode,
                    };

                    WorkStack[stackCount++] = new BuildRange
                    {
                        NodeIndex = rightNode,
                        Start = middle,
                        Count = range.Start + range.Count - middle,
                    };

                    WorkStack[stackCount++] = new BuildRange
                    {
                        NodeIndex = leftNode,
                        Start = range.Start,
                        Count = middle - range.Start,
                    };
                }

                NodeCount[0] = nextNodeIndex;
            }

            private void CalculateBounds(int start, int count, out float3 minimum, out float3 maximum, out float3 centerMinimum, out float3 centerMaximum)
            {
                minimum = new float3(float.MaxValue);
                maximum = new float3(float.MinValue);
                centerMinimum = minimum;
                centerMaximum = maximum;

                for (int offset = 0; offset < count; offset++)
                {
                    int featureRef = PrimitiveOrder[start + offset];

                    GetFeaturePoints(featureRef, out float3 featureStart, out float3 featureEnd);

                    float3 featureMinimum = math.min(featureStart, featureEnd);

                    float3 featureMaximum = math.max(featureStart, featureEnd);

                    float3 center = (featureStart + featureEnd) * 0.5f;

                    minimum = math.min(minimum, featureMinimum);
                    maximum = math.max(maximum, featureMaximum);
                    centerMinimum = math.min(centerMinimum, center);
                    centerMaximum = math.max(centerMaximum, center);
                }
            }

            private void SelectMedian(int left, int right, int median, int axis)
            {
                while (left < right)
                {
                    int pivotIndex = left + (right - left) / 2;

                    pivotIndex = Partition(left, right, pivotIndex, axis);

                    if (median == pivotIndex)
                    {
                        return;
                    }

                    if (median < pivotIndex)
                    {
                        right = pivotIndex - 1;
                    }
                    else
                    {
                        left = pivotIndex + 1;
                    }
                }
            }

            private int Partition(int left, int right, int pivotIndex, int axis)
            {
                int pivotFeature = PrimitiveOrder[pivotIndex];
                float pivotValue = GetFeatureCenterAxis(pivotFeature, axis);

                Swap(pivotIndex, right);
                int storeIndex = left;

                for (int index = left; index < right; index++)
                {
                    int featureRef = PrimitiveOrder[index];
                    float value = GetFeatureCenterAxis(featureRef, axis);

                    if (value < pivotValue || (value == pivotValue && featureRef < pivotFeature))
                    {
                        Swap(index, storeIndex);
                        storeIndex++;
                    }
                }

                Swap(storeIndex, right);
                return storeIndex;
            }

            private float GetFeatureCenterAxis(int featureRef, int axis)
            {
                GetFeaturePoints(featureRef, out float3 start, out float3 end);

                float3 center = (start + end) * 0.5f;

                return axis == 0 ? center.x : axis == 1 ? center.y : center.z;
            }

            private void GetFeaturePoints(int featureRef, out float3 start, out float3 end)
            {
                if (featureRef < LocalVertices.Length)
                {
                    start = LocalVertices[featureRef];
                    end = start;
                    return;
                }

                int edgeIndex = featureRef - LocalVertices.Length;

                int2 edge = Edges[edgeIndex];
                start = LocalVertices[edge.x];
                end = LocalVertices[edge.y];
            }

            private void Swap(int first, int second)
            {
                if (first == second)
                {
                    return;
                }

                int value = PrimitiveOrder[first];
                PrimitiveOrder[first] = PrimitiveOrder[second];
                PrimitiveOrder[second] = value;
            }
        }

        private NativeArray<float3> m_LocalVertices;
        private NativeArray<int2> m_VertexMetadata;
        private NativeArray<int2> m_Edges;
        private NativeArray<int2> m_EdgeMetadata;
        private NativeArray<int> m_PrimitiveOrder;
        private NativeArray<Node> m_Nodes;

        private readonly int m_NodeCount;
        private int[] m_QueryStack = new int[64];
        private int m_LeaseCount;
        private bool m_Retired;
        private bool m_Disposed;

        internal ObjectSnapIndex(NativeArray<float3> localVertices, NativeArray<int2> vertexMetadata, NativeArray<int2> edges, NativeArray<int2> edgeMetadata, NativeArray<int> primitiveOrder, NativeArray<Node> nodes, int nodeCount)
        {
            m_LocalVertices = localVertices;
            m_VertexMetadata = vertexMetadata;
            m_Edges = edges;
            m_EdgeMetadata = edgeMetadata;
            m_PrimitiveOrder = primitiveOrder;
            m_Nodes = nodes;
            m_NodeCount = nodeCount;
        }

        public bool TryFindTarget(Camera camera, ObjectTransform transform, int objectId, in ObjectSnapScreenQuery screenQuery, bool verticesEnabled, bool edgesEnabled, out SnapTarget target)
        {
            return TryFindTarget(camera, transform, objectId, screenQuery, verticesEnabled, edgesEnabled, -1, null, out target);
        }

        internal bool TryFindTarget(Camera camera, ObjectTransform transform, int objectId, in ObjectSnapScreenQuery screenQuery, bool verticesEnabled, bool edgesEnabled, int excludedPartIndex, MeshSnapTargetFilter filter, out SnapTarget target)
        {
            target = default;

            if (camera == null || (!verticesEnabled && !edgesEnabled) || m_NodeCount <= 0 || screenQuery.MaximumDistancePixels <= 0f)
            {
                return false;
            }

            ObjectSnapLocalQuery localQuery = screenQuery.ToLocal(transform);

            float activeQueryRadiusPixels = screenQuery.MaximumDistancePixels;
            int queryTighteningCount = 0;

            float bestDistanceSquared = screenQuery.MaximumDistancePixels * screenQuery.MaximumDistancePixels;

            int bestFeatureRef = -1;
            SnapTargetKind bestKind = default;
            int bestPartIndex = -1;
            int bestFeatureIndex = -1;
            Vector3 bestPosition = Vector3.zero;

            int stackCount = 0;
            PushNode(0, ref stackCount);

            while (stackCount > 0)
            {
                Node node = m_Nodes[m_QueryStack[--stackCount]];

                if (!localQuery.Intersects(node.Minimum, node.Maximum))
                {
                    continue;
                }

                if (node.Count > 0)
                {
                    bool leafImprovedTarget = false;

                    for (int offset = 0; offset < node.Count; offset++)
                    {
                        int featureRef = m_PrimitiveOrder[node.Start + offset];

                        GetFeature(featureRef, out SnapTargetKind kind, out int partIndex, out int featureIndex, out Vector3 localStart, out Vector3 localEnd);

                        if ((kind == SnapTargetKind.POVertex && !verticesEnabled) || (kind == SnapTargetKind.POEdge && !edgesEnabled) || partIndex == excludedPartIndex || IsFeatureExcluded(kind, partIndex, featureIndex, featureRef, filter))
                        {
                            continue;
                        }

                        Vector3 worldStart = transform.TransformPoint(localStart);

                        Vector3 worldEnd = transform.TransformPoint(localEnd);

                        if (!TryGetCursorDistance(camera, screenQuery.CursorPosition, kind, worldStart, worldEnd, out Vector3 candidatePosition, out float candidateDistanceSquared) || candidateDistanceSquared > bestDistanceSquared)
                        {
                            continue;
                        }

                        bool closer = candidateDistanceSquared < bestDistanceSquared - kTargetTieEpsilon;

                        bool tied = Mathf.Abs(candidateDistanceSquared - bestDistanceSquared) <= kTargetTieEpsilon;

                        if (!closer && !(tied && IsPreferredTie(kind, partIndex, featureIndex, featureRef, bestKind, bestPartIndex, bestFeatureIndex, bestFeatureRef)))
                        {
                            continue;
                        }

                        bestDistanceSquared = candidateDistanceSquared;

                        bestFeatureRef = featureRef;
                        bestKind = kind;
                        bestPartIndex = partIndex;
                        bestFeatureIndex = featureIndex;
                        bestPosition = candidatePosition;
                        leafImprovedTarget |= closer;
                    }

                    if (leafImprovedTarget && queryTighteningCount < kMaximumTargetQueryTightenings)
                    {
                        float tightenedRadiusPixels = Mathf.Sqrt(bestDistanceSquared + kTargetTieEpsilon);

                        if (tightenedRadiusPixels <= activeQueryRadiusPixels * kTargetQueryTighteningRatio && ObjectSnapScreenQuery.TryCreate(camera, screenQuery.CursorPosition, tightenedRadiusPixels, out ObjectSnapScreenQuery tightenedScreenQuery))
                        {
                            localQuery = tightenedScreenQuery.ToLocal(transform);

                            activeQueryRadiusPixels = tightenedRadiusPixels;
                            queryTighteningCount++;
                        }
                    }

                    continue;
                }

                ObjectSnapTraversalPriority leftPriority = localQuery.GetTraversalPriority(m_Nodes[node.Left].Minimum, m_Nodes[node.Left].Maximum);

                ObjectSnapTraversalPriority rightPriority = localQuery.GetTraversalPriority(m_Nodes[node.Right].Minimum, m_Nodes[node.Right].Maximum);

                // This is a stack, so the less useful child goes in first.
                if (leftPriority.IsPreferredTo(rightPriority))
                {
                    PushNode(node.Right, ref stackCount);
                    PushNode(node.Left, ref stackCount);
                }
                else
                {
                    PushNode(node.Left, ref stackCount);
                    PushNode(node.Right, ref stackCount);
                }
            }

            if (bestFeatureRef < 0)
            {
                return false;
            }

            target = new SnapTarget(bestKind, objectId, bestPartIndex, bestFeatureIndex, bestPosition, Mathf.Sqrt(bestDistanceSquared));

            return true;
        }

        public bool TryFindNearest(ObjectTransform transform, Vector3 target, out int featureRef, out Vector3 closestPoint, out float distanceSquared)
        {
            return TryFindNearest(transform, target, -1, out featureRef, out closestPoint, out distanceSquared);
        }

        internal bool TryFindNearest(ObjectTransform transform, Vector3 target, int includedPartIndex, out int featureRef, out Vector3 closestPoint, out float distanceSquared)
        {
            featureRef = -1;
            closestPoint = Vector3.zero;
            distanceSquared = float.MaxValue;

            if (m_NodeCount <= 0)
            {
                return false;
            }

            int stackCount = 0;
            PushNode(0, ref stackCount);

            while (stackCount > 0)
            {
                Node node = m_Nodes[m_QueryStack[--stackCount]];

                float nodeDistance = DistanceSquaredToWorldBounds(transform, target, node.Minimum, node.Maximum);

                if (nodeDistance > distanceSquared)
                {
                    continue;
                }

                if (node.Count > 0)
                {
                    for (int offset = 0; offset < node.Count; offset++)
                    {
                        int candidateFeature = m_PrimitiveOrder[node.Start + offset];

                        if (includedPartIndex >= 0 && GetFeaturePartIndex(candidateFeature) != includedPartIndex)
                        {
                            continue;
                        }

                        GetFeatureLocalPoints(candidateFeature, out Vector3 localStart, out Vector3 localEnd);

                        Vector3 worldStart = transform.TransformPoint(localStart);

                        Vector3 worldEnd = transform.TransformPoint(localEnd);

                        Vector3 candidatePoint = ClosestPointOnSegment(target, worldStart, worldEnd);

                        float candidateDistanceSquared = (candidatePoint - target).sqrMagnitude;

                        bool closer = candidateDistanceSquared < distanceSquared;

                        bool tied = Mathf.Abs(candidateDistanceSquared - distanceSquared) <= kSourceTieEpsilon;

                        if (!closer && !(tied && (featureRef < 0 || candidateFeature < featureRef)))
                        {
                            continue;
                        }

                        featureRef = candidateFeature;
                        closestPoint = candidatePoint;
                        distanceSquared = candidateDistanceSquared;
                    }

                    continue;
                }

                float leftDistance = DistanceSquaredToWorldBounds(transform, target, m_Nodes[node.Left].Minimum, m_Nodes[node.Left].Maximum);

                float rightDistance = DistanceSquaredToWorldBounds(transform, target, m_Nodes[node.Right].Minimum, m_Nodes[node.Right].Maximum);

                if (leftDistance <= rightDistance)
                {
                    if (rightDistance <= distanceSquared)
                    {
                        PushNode(node.Right, ref stackCount);
                    }

                    if (leftDistance <= distanceSquared)
                    {
                        PushNode(node.Left, ref stackCount);
                    }
                }
                else
                {
                    if (leftDistance <= distanceSquared)
                    {
                        PushNode(node.Left, ref stackCount);
                    }

                    if (rightDistance <= distanceSquared)
                    {
                        PushNode(node.Right, ref stackCount);
                    }
                }
            }

            return featureRef >= 0;
        }

        public bool TryGetClosestPoint(int featureRef, ObjectTransform transform, Vector3 target, out Vector3 closestPoint, out float distanceSquared)
        {
            return TryGetClosestPoint(featureRef, transform, target, -1, out closestPoint, out distanceSquared);
        }

        internal bool TryGetClosestPoint(int featureRef, ObjectTransform transform, Vector3 target, int includedPartIndex, out Vector3 closestPoint, out float distanceSquared)
        {
            closestPoint = Vector3.zero;
            distanceSquared = float.MaxValue;

            if (!IsFeatureRefValid(featureRef) || (includedPartIndex >= 0 && GetFeaturePartIndex(featureRef) != includedPartIndex))
            {
                return false;
            }

            GetFeatureLocalPoints(featureRef, out Vector3 localStart, out Vector3 localEnd);

            Vector3 worldStart = transform.TransformPoint(localStart);

            Vector3 worldEnd = transform.TransformPoint(localEnd);

            closestPoint = ClosestPointOnSegment(target, worldStart, worldEnd);

            distanceSquared = (closestPoint - target).sqrMagnitude;

            return true;
        }

        internal bool TryAcquireLease(out ObjectSnapIndexLease lease)
        {
            lease = null;

            if (m_Retired || m_Disposed)
            {
                return false;
            }

            lease = new ObjectSnapIndexLease(this);
            m_LeaseCount++;
            return true;
        }

        internal void ReleaseLease()
        {
            if (m_LeaseCount <= 0)
            {
                return;
            }

            m_LeaseCount--;

            if (m_Retired && m_LeaseCount == 0)
            {
                DisposeStorage();
            }
        }

        public void Dispose()
        {
            Retire();
        }

        internal void Retire()
        {
            if (m_Retired)
            {
                return;
            }

            m_Retired = true;

            if (m_LeaseCount == 0)
            {
                DisposeStorage();
            }
        }

        private void DisposeStorage()
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;
            DisposeArray(ref m_LocalVertices);
            DisposeArray(ref m_VertexMetadata);
            DisposeArray(ref m_Edges);
            DisposeArray(ref m_EdgeMetadata);
            DisposeArray(ref m_PrimitiveOrder);
            DisposeArray(ref m_Nodes);
        }

        private void GetFeature(int featureRef, out SnapTargetKind kind, out int partIndex, out int featureIndex, out Vector3 localStart, out Vector3 localEnd)
        {
            if (featureRef < m_LocalVertices.Length)
            {
                int2 metadata = m_VertexMetadata[featureRef];
                kind = SnapTargetKind.POVertex;
                partIndex = metadata.x;
                featureIndex = metadata.y;

                localStart = ToVector3(m_LocalVertices[featureRef]);

                localEnd = localStart;
                return;
            }

            int edgeIndex = featureRef - m_LocalVertices.Length;

            int2 edgeMetadata = m_EdgeMetadata[edgeIndex];
            int2 edge = m_Edges[edgeIndex];

            kind = SnapTargetKind.POEdge;
            partIndex = edgeMetadata.x;
            featureIndex = edgeMetadata.y;
            localStart = ToVector3(m_LocalVertices[edge.x]);
            localEnd = ToVector3(m_LocalVertices[edge.y]);
        }

        private void GetFeatureLocalPoints(int featureRef, out Vector3 localStart, out Vector3 localEnd)
        {
            if (featureRef < m_LocalVertices.Length)
            {
                localStart = ToVector3(m_LocalVertices[featureRef]);

                localEnd = localStart;
                return;
            }

            int edgeIndex = featureRef - m_LocalVertices.Length;

            int2 edge = m_Edges[edgeIndex];
            localStart = ToVector3(m_LocalVertices[edge.x]);
            localEnd = ToVector3(m_LocalVertices[edge.y]);
        }

        private int GetFeaturePartIndex(int featureRef)
        {
            if (featureRef < m_LocalVertices.Length)
            {
                return m_VertexMetadata[featureRef].x;
            }

            int edgeIndex = featureRef - m_LocalVertices.Length;

            return edgeIndex >= 0 && edgeIndex < m_EdgeMetadata.Length ? m_EdgeMetadata[edgeIndex].x : -1;
        }

        private bool IsFeatureExcluded(SnapTargetKind kind, int partIndex, int featureIndex, int featureRef, MeshSnapTargetFilter filter)
        {
            if (filter == null)
            {
                return false;
            }

            if (kind == SnapTargetKind.POVertex)
            {
                return filter.ExcludesVertex(partIndex, featureIndex);
            }

            int edgeIndex = featureRef - m_LocalVertices.Length;

            if (edgeIndex < 0 || edgeIndex >= m_Edges.Length)
            {
                return false;
            }

            int2 edge = m_Edges[edgeIndex];

            return IsVertexExcluded(edge.x, filter) || IsVertexExcluded(edge.y, filter);
        }

        private bool IsVertexExcluded(int vertexIndex, MeshSnapTargetFilter filter)
        {
            if (vertexIndex < 0 || vertexIndex >= m_VertexMetadata.Length)
            {
                return false;
            }

            int2 metadata = m_VertexMetadata[vertexIndex];

            return filter.ExcludesVertex(metadata.x, metadata.y);
        }

        private bool IsFeatureRefValid(int featureRef)
        {
            if (featureRef < 0)
            {
                return false;
            }

            if (featureRef < m_LocalVertices.Length)
            {
                return true;
            }

            int edgeIndex = featureRef - m_LocalVertices.Length;

            return edgeIndex >= 0 && edgeIndex < m_Edges.Length;
        }

        private void PushNode(int nodeIndex, ref int stackCount)
        {
            if (nodeIndex < 0)
            {
                return;
            }

            if (stackCount >= m_QueryStack.Length)
            {
                Array.Resize(ref m_QueryStack, m_QueryStack.Length * 2);
            }

            m_QueryStack[stackCount++] = nodeIndex;
        }

        private static bool TryGetCursorDistance(Camera camera, Vector2 cursorPosition, SnapTargetKind kind, Vector3 worldStart, Vector3 worldEnd, out Vector3 worldPosition, out float distanceSquared)
        {
            worldPosition = Vector3.zero;
            distanceSquared = float.MaxValue;

            Vector3 screenStart3 = camera.WorldToScreenPoint(worldStart);

            if (screenStart3.z <= 0f)
            {
                return false;
            }

            Vector2 screenStart = new Vector2(screenStart3.x, screenStart3.y);

            if (kind == SnapTargetKind.POVertex)
            {
                worldPosition = worldStart;
                distanceSquared = (cursorPosition - screenStart).sqrMagnitude;
                return true;
            }

            Vector3 screenEnd3 = camera.WorldToScreenPoint(worldEnd);

            if (screenEnd3.z <= 0f)
            {
                return false;
            }

            Vector2 screenEnd = new Vector2(screenEnd3.x, screenEnd3.y);

            Vector2 screenSegment = screenEnd - screenStart;

            float screenLengthSquared = screenSegment.sqrMagnitude;

            float screenT = screenLengthSquared > 0.000001f ? Mathf.Clamp01(Vector2.Dot(cursorPosition - screenStart, screenSegment) / screenLengthSquared) : 0f;

            Vector2 closestScreen = screenStart + screenSegment * screenT;

            distanceSquared = (cursorPosition - closestScreen).sqrMagnitude;

            float worldT = screenT;

            if (!camera.orthographic)
            {
                float denominator = (1f - screenT) / screenStart3.z + screenT / screenEnd3.z;

                if (Mathf.Abs(denominator) > 0.000001f)
                {
                    worldT = (screenT / screenEnd3.z) / denominator;
                }
            }

            worldPosition = Vector3.Lerp(worldStart, worldEnd, Mathf.Clamp01(worldT));

            return IsFinite(worldPosition);
        }

        private static float DistanceSquaredToWorldBounds(ObjectTransform transform, Vector3 point, float3 minimum, float3 maximum)
        {
            Vector3 localMinimum = ToVector3(minimum);
            Vector3 localMaximum = ToVector3(maximum);
            Vector3 localCenter = (localMinimum + localMaximum) * 0.5f;

            Vector3 localExtents = (localMaximum - localMinimum) * 0.5f;

            Vector3 worldCenter = transform.TransformPoint(localCenter);

            Vector3 worldX = transform.TransformVector(new Vector3(localExtents.x, 0f, 0f));

            Vector3 worldY = transform.TransformVector(new Vector3(0f, localExtents.y, 0f));

            Vector3 worldZ = transform.TransformVector(new Vector3(0f, 0f, localExtents.z));

            Vector3 worldExtents = new Vector3(Mathf.Abs(worldX.x) + Mathf.Abs(worldY.x) + Mathf.Abs(worldZ.x), Mathf.Abs(worldX.y) + Mathf.Abs(worldY.y) + Mathf.Abs(worldZ.y), Mathf.Abs(worldX.z) + Mathf.Abs(worldY.z) + Mathf.Abs(worldZ.z));

            return DistanceSquaredToBounds(point, worldCenter - worldExtents, worldCenter + worldExtents);
        }

        private static float DistanceSquaredToBounds(Vector3 point, Vector3 minimum, Vector3 maximum)
        {
            float dx = point.x < minimum.x ? minimum.x - point.x : point.x > maximum.x ? point.x - maximum.x : 0f;

            float dy = point.y < minimum.y ? minimum.y - point.y : point.y > maximum.y ? point.y - maximum.y : 0f;

            float dz = point.z < minimum.z ? minimum.z - point.z : point.z > maximum.z ? point.z - maximum.z : 0f;

            return dx * dx + dy * dy + dz * dz;
        }

        private static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;

            if (lengthSquared <= 0.0000001f)
            {
                return start;
            }

            float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);

            return start + segment * t;
        }

        private static bool IsPreferredTie(SnapTargetKind candidateKind, int candidatePartIndex, int candidateFeatureIndex, int candidateFeatureRef, SnapTargetKind bestKind, int bestPartIndex, int bestFeatureIndex, int bestFeatureRef)
        {
            if (bestFeatureRef < 0)
            {
                return true;
            }

            if (candidateKind != bestKind)
            {
                return candidateKind == SnapTargetKind.POVertex;
            }

            if (candidatePartIndex != bestPartIndex)
            {
                return candidatePartIndex < bestPartIndex;
            }

            if (candidateFeatureIndex != bestFeatureIndex)
            {
                return candidateFeatureIndex < bestFeatureIndex;
            }

            return candidateFeatureRef < bestFeatureRef;
        }

        private static Vector3 ToVector3(float3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
        }

        private static void DisposeArray<T>(ref NativeArray<T> array) where T : struct
        {
            if (!array.IsCreated)
            {
                return;
            }

            array.Dispose();
            array = default;
        }
    }

    internal sealed class ObjectSnapIndexLease : IDisposable
    {
        private ObjectSnapIndex m_Index;

        internal ObjectSnapIndex Index => m_Index;

        internal ObjectSnapIndexLease(ObjectSnapIndex index)
        {
            m_Index = index;
        }

        public void Dispose()
        {
            ObjectSnapIndex index = m_Index;
            if (index == null)
            {
                return;
            }

            m_Index = null;
            index.ReleaseLease();
        }
    }

    // The drag keeps only this lightweight view. Its transform is the pose
    // captured at mouse-down, while the shared local tree stays unchanged.
    internal sealed class IndexedTranslationSnapSource : ITranslationSnapSource
    {
        private ObjectSnapIndexLease m_Lease;
        private readonly ObjectTransform m_Transform;
        private readonly int m_IncludedPartIndex;

        public IndexedTranslationSnapSource(ObjectSnapIndexLease lease, ObjectTransform transform, int includedPartIndex = -1)
        {
            m_Lease = lease;
            m_Transform = transform;
            m_IncludedPartIndex = includedPartIndex;
        }

        public bool TryFindNearest(Vector3 target, out int primitiveIndex, out Vector3 closestPoint, out float distanceSquared)
        {
            ObjectSnapIndex index = m_Lease?.Index;

            if (index == null)
            {
                primitiveIndex = -1;
                closestPoint = Vector3.zero;
                distanceSquared = float.MaxValue;
                return false;
            }

            return index.TryFindNearest(m_Transform, target, m_IncludedPartIndex, out primitiveIndex, out closestPoint, out distanceSquared);
        }

        public bool TryGetClosestPoint(int primitiveIndex, Vector3 target, out Vector3 closestPoint, out float distanceSquared)
        {
            ObjectSnapIndex index = m_Lease?.Index;

            if (index == null)
            {
                closestPoint = Vector3.zero;
                distanceSquared = float.MaxValue;
                return false;
            }

            return index.TryGetClosestPoint(primitiveIndex, m_Transform, target, m_IncludedPartIndex, out closestPoint, out distanceSquared);
        }

        public void Dispose()
        {
            m_Lease?.Dispose();
            m_Lease = null;
        }
    }
}