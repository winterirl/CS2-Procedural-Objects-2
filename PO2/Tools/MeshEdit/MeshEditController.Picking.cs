using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;

namespace PO2.Tools.MeshEdit
{
    public sealed partial class MeshEditController
    {
        private const float kVertexPickRadiusPixels = 10f;
        private const float kEdgePickRadiusPixels = 12f;
        private const float kMouseMovementEpsilonSquared = 0.01f;

        private VertexRef? m_HoveredVertex;
        private StructuralEdgeRef? m_HoveredEdge;
        private SurfaceRef? m_HoveredSurface;

        private Vector2 m_LastXRayEdgePickMousePosition;
        private uint m_LastXRayEdgePickProjectionVersion;
        private int m_LastXRayEdgePickIndex = -1;
        private bool m_HasCachedXRayEdgePick;

        private Vector2 m_LastMousePosition;
        private bool m_HasLastMousePosition;

        private bool m_VertexHoverSuppressed;
        public bool HasHoveredVertex => m_HoveredVertex.HasValue;
        public bool HasHoveredEdge => m_HoveredEdge.HasValue;
        public bool HasHoveredSurface => m_HoveredSurface.HasValue;

        private void ClearTransientHover()
        {
            m_HoveredVertex = null;
            m_HoveredEdge = null;
            m_HoveredSurface = null;
            m_HasLastMousePosition = false;
        }

        public bool SetHoveredEdge(StructuralEdgeRef? edge)
        {
            if (SelectionMode != MeshSelectionMode.Edge)
            {
                edge = null;
            }

            if (m_HoveredEdge == edge)
            {
                return false;
            }

            m_HoveredEdge = edge;
            m_DirtyFlags |= MeshEditCacheDirtyFlags.Hover;
            FlushVisualDirtyFlags();
            return true;
        }

        public bool IsEdgeHovered(StructuralEdgeRef edge)
        {
            return SelectionMode == MeshSelectionMode.Edge && m_HoveredEdge.HasValue && m_HoveredEdge.Value == edge;
        }

        public bool SetHoveredSurface(SurfaceRef? surface)
        {
            if (SelectionMode != MeshSelectionMode.SurfaceArea)
            {
                surface = null;
            }

            if (m_HoveredSurface == surface)
            {
                return false;
            }

            m_HoveredSurface = surface;
            m_DirtyFlags |= MeshEditCacheDirtyFlags.Hover;
            FlushVisualDirtyFlags();
            return true;
        }

        public bool IsSurfaceSelected(SurfaceRef surface)
        {
            return m_SurfaceSelection.Contains(surface);
        }

        public bool IsSurfaceHovered(SurfaceRef surface)
        {
            return SelectionMode == MeshSelectionMode.SurfaceArea && m_HoveredSurface.HasValue && m_HoveredSurface.Value == surface;
        }

        public bool SetVertexHoverSuppressed(bool suppressed)
        {
            if (m_VertexHoverSuppressed == suppressed)
            {
                return false;
            }

            m_VertexHoverSuppressed = suppressed;
            m_VisualVersion++;
            return true;
        }

        public bool ClearHover()
        {
            bool changed = m_HoveredVertex.HasValue || m_HoveredEdge.HasValue || m_HoveredSurface.HasValue;

            m_HoveredVertex = null;
            m_HoveredEdge = null;
            m_HoveredSurface = null;
            m_HasLastMousePosition = false;

            if (changed)
            {
                m_VisualVersion++;
            }

            return changed;
        }

        public bool IsHovered(VertexRef vertex)
        {
            return SelectionMode == MeshSelectionMode.Vertex && !m_VertexHoverSuppressed && m_HoveredVertex.HasValue && m_HoveredVertex.Value == vertex;
        }

        public bool TryPickEdge(Vector2 mousePosition, out StructuralEdgeRef edgeRef)
        {
            edgeRef = default;
            if (UsesGpuMarkers)
            {
                int slot = MeshGpu.HoverSlot;
                if (SelectionMode != MeshSelectionMode.Edge || slot < 0 || slot >= MeshGpu.Geometry.Edges.Length) return false;
                edgeRef = MeshGpu.Geometry.Edges[slot];
                return true;
            }

            if (SelectionMode != MeshSelectionMode.Edge || m_EdgeFrameDataCount <= 0)
            {
                return false;
            }

            if (VisibilityMode == MeshEditVisibilityMode.XRay && HasValidXRayEdgeProjection())
            {
                return TryPickXRayEdge(mousePosition, out edgeRef);
            }

            float bestDistanceSquared = kEdgePickRadiusPixels * kEdgePickRadiusPixels;
            float bestDepth = float.MaxValue;
            int bestFrameIndex = -1;

            for (int i = 0; i < m_EdgeFrameDataCount; i++)
            {
                EdgeFrameData frame = m_EdgeFrameData[i];

                if (frame.CameraDepthStart <= 0f || frame.CameraDepthEnd <= 0f || frame.VisibleIntervalCount <= 0 || frame.VisibleIntervalMask == 0u)
                {
                    continue;
                }

                int intervalCount = frame.VisibleIntervalCount;
                int intervalIndex = 0;

                while (intervalIndex < intervalCount)
                {
                    if ((frame.VisibleIntervalMask & (1u << intervalIndex)) == 0u)
                    {
                        intervalIndex++;
                        continue;
                    }

                    int visibleStart = intervalIndex;
                    intervalIndex++;

                    while (intervalIndex < intervalCount && (frame.VisibleIntervalMask & (1u << intervalIndex)) != 0u)
                    {
                        intervalIndex++;
                    }

                    float startT = visibleStart / (float)intervalCount;
                    float endT = intervalIndex / (float)intervalCount;

                    Vector2 screenStart = Vector2.Lerp(frame.ScreenStart, frame.ScreenEnd, startT);
                    Vector2 screenEnd = Vector2.Lerp(frame.ScreenStart, frame.ScreenEnd, endT);

                    float segmentT;
                    float distanceSquared = DistanceSquaredToScreenSegment(mousePosition, screenStart, screenEnd, out segmentT);

                    float depthStart = Mathf.Lerp(frame.CameraDepthStart, frame.CameraDepthEnd, startT);
                    float depthEnd = Mathf.Lerp(frame.CameraDepthStart, frame.CameraDepthEnd, endT);
                    float depth = Mathf.Lerp(depthStart, depthEnd, segmentT);

                    bool closerInScreenSpace = distanceSquared < bestDistanceSquared;
                    bool sameScreenDistance = Mathf.Abs(distanceSquared - bestDistanceSquared) < 0.0001f;

                    if (!closerInScreenSpace && !(sameScreenDistance && depth < bestDepth))
                    {
                        continue;
                    }

                    bestDistanceSquared = distanceSquared;
                    bestDepth = depth;
                    bestFrameIndex = i;
                }
            }

            if (bestFrameIndex < 0)
            {
                return false;
            }

            edgeRef = m_EdgeFrameData[bestFrameIndex].Edge;
            return true;
        }

        private bool TryPickXRayEdge(Vector2 mousePosition, out StructuralEdgeRef edgeRef)
        {
            edgeRef = default;

            bool mouseChanged = !m_HasCachedXRayEdgePick || (mousePosition - m_LastXRayEdgePickMousePosition).sqrMagnitude > kMouseMovementEpsilonSquared;

            bool projectionChanged = !m_HasCachedXRayEdgePick || m_LastXRayEdgePickProjectionVersion != m_ProjectionVersion;

            if (mouseChanged || projectionChanged)
            {
                PickXRayEdgeJob pickJob = new PickXRayEdgeJob
                {
                    Projections = m_XRayEdgeProjectionResults,
                    EdgeCount = m_XRayEdgeProjectionCount,
                    MousePosition = new float2(mousePosition.x, mousePosition.y),
                    MaximumDistanceSquared = kEdgePickRadiusPixels * kEdgePickRadiusPixels,
                    Result = m_XRayEdgePickResult,
                };

                JobHandle pickHandle = pickJob.Schedule();
                pickHandle.Complete();

                m_LastXRayEdgePickIndex = m_XRayEdgePickResult[0].EdgeIndex;
                m_LastXRayEdgePickMousePosition = mousePosition;
                m_LastXRayEdgePickProjectionVersion = m_ProjectionVersion;
                m_HasCachedXRayEdgePick = true;
            }

            if (m_LastXRayEdgePickIndex < 0 || m_LastXRayEdgePickIndex >= m_EdgeFrameDataCount)
            {
                return false;
            }

            edgeRef = m_EdgeFrameData[m_LastXRayEdgePickIndex].Edge;
            return true;
        }

        public bool TryGetHoveredEdge(out StructuralEdgeRef edgeRef)
        {
            if (SelectionMode == MeshSelectionMode.Edge && m_HoveredEdge.HasValue)
            {
                edgeRef = m_HoveredEdge.Value;
                return true;
            }

            edgeRef = default;
            return false;
        }

        private static float DistanceSquaredToScreenSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd, out float segmentT)
        {
            Vector2 segment = segmentEnd - segmentStart;
            float lengthSquared = segment.sqrMagnitude;

            if (lengthSquared <= 0.000001f)
            {
                segmentT = 0f;
                return (point - segmentStart).sqrMagnitude;
            }

            segmentT = Mathf.Clamp01(Vector2.Dot(point - segmentStart, segment) / lengthSquared);
            Vector2 closest = segmentStart + segment * segmentT;
            return (point - closest).sqrMagnitude;
        }

        private void InvalidateXRayEdgePickCache()
        {
            m_LastXRayEdgePickIndex = -1;
            m_HasCachedXRayEdgePick = false;
        }

        private struct XRayEdgePickResult
        {
            public int EdgeIndex;
        }

        [BurstCompile]
        private struct PickXRayEdgeJob : IJob
        {
            [ReadOnly] public NativeArray<XRayEdgeProjectionResult> Projections;

            public int EdgeCount;
            public float2 MousePosition;
            public float MaximumDistanceSquared;

            [WriteOnly] public NativeArray<XRayEdgePickResult> Result;

            public void Execute()
            {
                float bestDistanceSquared = MaximumDistanceSquared;
                float bestDepth = float.MaxValue;
                int bestEdgeIndex = -1;

                for (int i = 0; i < EdgeCount; i++)
                {
                    XRayEdgeProjectionResult projection = Projections[i];

                    if (projection.CameraDepthStart <= 0f || projection.CameraDepthEnd <= 0f)
                    {
                        continue;
                    }

                    float segmentT;
                    float distanceSquared = DistanceSquaredToSegment(MousePosition, projection.ScreenStart, projection.ScreenEnd, out segmentT);

                    float depth = math.lerp(projection.CameraDepthStart, projection.CameraDepthEnd, segmentT);

                    bool closerInScreenSpace = distanceSquared < bestDistanceSquared;

                    bool sameScreenDistance = math.abs(distanceSquared - bestDistanceSquared) < 0.0001f;

                    if (!closerInScreenSpace && !(sameScreenDistance && depth < bestDepth))
                    {
                        continue;
                    }

                    bestDistanceSquared = distanceSquared;
                    bestDepth = depth;
                    bestEdgeIndex = i;
                }

                Result[0] = new XRayEdgePickResult
                {
                    EdgeIndex = bestEdgeIndex,
                };
            }

            private static float DistanceSquaredToSegment(float2 point, float2 segmentStart, float2 segmentEnd, out float segmentT)
            {
                float2 segment = segmentEnd - segmentStart;
                float lengthSquared = math.lengthsq(segment);

                if (lengthSquared <= 0.000001f)
                {
                    segmentT = 0f;
                    return math.lengthsq(point - segmentStart);
                }

                segmentT = math.clamp(math.dot(point - segmentStart, segment) / lengthSquared, 0f, 1f);

                float2 closest = segmentStart + segment * segmentT;

                return math.lengthsq(point - closest);
            }
        }

        private void UpdateHoveredVertex(Camera camera, Vector2 mousePosition)
        {
            VertexRef? previousHovered = m_HoveredVertex;

            m_HoveredVertex = null;

            float bestDistanceSquared = kVertexPickRadiusPixels * kVertexPickRadiusPixels;

            float bestDepth = float.MaxValue;

            float minX = -kVertexPickRadiusPixels;
            float minY = -kVertexPickRadiusPixels;
            float maxX = camera.pixelWidth + kVertexPickRadiusPixels;
            float maxY = camera.pixelHeight + kVertexPickRadiusPixels;

            for (int i = 0; i < m_FrameDataCount; i++)
            {
                VertexFrameData frame = m_FrameData[i];

                if (frame.CameraDepth <= 0f || (VisibilityMode == MeshEditVisibilityMode.Solid && !frame.SolidVisible) || frame.ScreenPosition.x < minX || frame.ScreenPosition.x > maxX || frame.ScreenPosition.y < minY || frame.ScreenPosition.y > maxY)
                {
                    continue;
                }

                float dx = frame.ScreenPosition.x - mousePosition.x;

                float dy = frame.ScreenPosition.y - mousePosition.y;

                float distanceSquared = dx * dx + dy * dy;

                bool closerInScreenSpace = distanceSquared < bestDistanceSquared;

                // At effectively the same screen distance, prefer the vertex in
                // front. Otherwise stacked vertices flicker based on array order.
                bool sameScreenDistance = Mathf.Abs(distanceSquared - bestDistanceSquared) < 0.0001f;

                if (!closerInScreenSpace && !(sameScreenDistance && frame.CameraDepth < bestDepth))
                {
                    continue;
                }

                bestDistanceSquared = distanceSquared;
                bestDepth = frame.CameraDepth;
                m_HoveredVertex = frame.Vertex;
            }

            if (!Nullable.Equals(previousHovered, m_HoveredVertex))
            {
                m_DirtyFlags |= MeshEditCacheDirtyFlags.Hover;
            }
        }
    }
}