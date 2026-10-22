using System;
using UnityEngine;

namespace PO2.Tools.Snapping
{
    // Each drag has its own query stack. Resetting it leaves the prepared tree
    // available for another drag while the selection and geometry still match.
    internal sealed class TranslationSnapSource : ITranslationSnapSource
    {
        private const float kDistanceTieEpsilon = 0.0000001f;
        private TranslationSnapSourceData m_Data;
        private int[] m_QueryStack = new int[64];

        internal TranslationSnapSource(TranslationSnapSourceData data)
        {
            m_Data = data ?? throw new ArgumentNullException(nameof(data));
            m_Data.Retain();
        }

        public bool TryFindNearest(Vector3 target, out int primitiveIndex, out Vector3 closestPoint, out float distanceSquared)
        {
            primitiveIndex = -1;
            closestPoint = Vector3.zero;
            distanceSquared = float.MaxValue;

            if (m_Data == null || m_Data.NodeCount == 0)
            {
                return false;
            }

            int stackCount = 0;
            PushNode(0, ref stackCount);

            while (stackCount > 0)
            {
                int nodeIndex = m_QueryStack[--stackCount];
                TranslationSnapSourceData.Node node = m_Data.Nodes[nodeIndex];

                if (DistanceSquaredToBounds(target, node.Minimum, node.Maximum) > distanceSquared)
                {
                    continue;
                }

                if (node.Count > 0)
                {
                    for (int offset = 0; offset < node.Count; offset++)
                    {
                        int candidateIndex = m_Data.PrimitiveIndices[node.Start + offset];

                        TranslationSnapSourceData.Primitive primitive = m_Data.Primitives[candidateIndex];

                        Vector3 candidatePoint = ClosestPointOnSegment(target, primitive.Start, primitive.End);

                        float candidateDistanceSquared = (candidatePoint - target).sqrMagnitude;

                        bool closer = candidateDistanceSquared < distanceSquared;

                        bool tied = Mathf.Abs(candidateDistanceSquared - distanceSquared) <= kDistanceTieEpsilon;

                        if (!closer && !(tied && (primitiveIndex < 0 || candidateIndex < primitiveIndex)))
                        {
                            continue;
                        }

                        primitiveIndex = candidateIndex;
                        closestPoint = candidatePoint;
                        distanceSquared = candidateDistanceSquared;
                    }

                    continue;
                }

                float leftDistance = DistanceSquaredToBounds(target, m_Data.Nodes[node.Left].Minimum, m_Data.Nodes[node.Left].Maximum);

                float rightDistance = DistanceSquaredToBounds(target, m_Data.Nodes[node.Right].Minimum, m_Data.Nodes[node.Right].Maximum);

                // Push the farther node first so the nearer one tightens the
                // distance limit before the other branch is considered.
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

            return primitiveIndex >= 0;
        }

        public bool TryGetClosestPoint(int primitiveIndex, Vector3 target, out Vector3 closestPoint, out float distanceSquared)
        {
            closestPoint = Vector3.zero;
            distanceSquared = float.MaxValue;

            if (primitiveIndex < 0 || m_Data == null || primitiveIndex >= m_Data.PrimitiveCount)
            {
                return false;
            }

            TranslationSnapSourceData.Primitive primitive = m_Data.Primitives[primitiveIndex];
            closestPoint = ClosestPointOnSegment(target, primitive.Start, primitive.End);

            distanceSquared = (closestPoint - target).sqrMagnitude;

            return true;
        }

        public void Dispose()
        {
            var data = m_Data;
            m_Data = null;
            data?.Release();
        }

        private void PushNode(int nodeIndex, ref int stackCount)
        {
            if (stackCount >= m_QueryStack.Length)
            {
                Array.Resize(ref m_QueryStack, m_QueryStack.Length * 2);
            }

            m_QueryStack[stackCount++] = nodeIndex;
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

        private static float DistanceSquaredToBounds(Vector3 point, Vector3 minimum, Vector3 maximum)
        {
            float dx = point.x < minimum.x ? minimum.x - point.x : point.x > maximum.x ? point.x - maximum.x : 0f;

            float dy = point.y < minimum.y ? minimum.y - point.y : point.y > maximum.y ? point.y - maximum.y : 0f;

            float dz = point.z < minimum.z ? minimum.z - point.z : point.z > maximum.z ? point.z - maximum.z : 0f;

            return dx * dx + dy * dy + dz * dz;
        }
    }
}