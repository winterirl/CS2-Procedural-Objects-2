using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using PO2.Core;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;

namespace PO2.Tools.MeshEdit
{
    public sealed partial class MeshEditController
    {
        public bool ApplyBoxSelection(ProceduralObject proceduralObject, Rect screenRect, BoxSelectionMode mode)
        {
            if (!ValidateSelectionTarget(proceduralObject) || (SelectionMode != MeshSelectionMode.Vertex && SelectionMode != MeshSelectionMode.Edge))
            {
                return false;
            }

            if (UsesGpuMarkers) return BeginGpuBox(screenRect, mode);

            if (SelectionMode == MeshSelectionMode.Edge)
            {
                return ApplyEdgeBoxSelection(proceduralObject, screenRect, mode);
            }

            bool changed = false;

            if (mode == BoxSelectionMode.Replace)
            {
                changed = m_VertexSelection.Clear();
            }

            for (int i = 0; i < m_FrameDataCount; i++)
            {
                VertexFrameData frame = m_FrameData[i];

                if (frame.CameraDepth <= 0f || (VisibilityMode == MeshEditVisibilityMode.Solid && !frame.SolidVisible) || !screenRect.Contains(frame.ScreenPosition))
                {
                    continue;
                }

                if (mode == BoxSelectionMode.Remove)
                {
                    changed |= m_VertexSelection.Remove(frame.Vertex);
                }
                else
                {
                    changed |= m_VertexSelection.Add(frame.Vertex);
                }
            }

            return FinishSelectionChange(proceduralObject, changed);
        }

        private bool ApplyEdgeBoxSelection(ProceduralObject proceduralObject, Rect screenRect, BoxSelectionMode mode)
        {
            if (VisibilityMode == MeshEditVisibilityMode.XRay && HasValidXRayEdgeProjection())
            {
                return ApplyXRayEdgeBoxSelection(proceduralObject, screenRect, mode);
            }

            bool changed = false;

            if (mode == BoxSelectionMode.Replace)
            {
                changed = m_EdgeSelection.Clear();
            }

            for (int i = 0; i < m_EdgeFrameDataCount; i++)
            {
                EdgeFrameData frame = m_EdgeFrameData[i];

                if (frame.CameraDepthStart <= 0f || frame.CameraDepthEnd <= 0f || frame.VisibleIntervalCount <= 0 || frame.VisibleIntervalMask == 0u || !VisibleEdgeIntersectsScreenRect(frame, screenRect))
                {
                    continue;
                }

                if (mode == BoxSelectionMode.Remove)
                {
                    changed |= m_EdgeSelection.Remove(frame.Edge);
                }
                else
                {
                    changed |= m_EdgeSelection.Add(frame.Edge);
                }
            }

            if (changed)
            {
                RebuildEffectiveEdgeVertices(proceduralObject);
            }

            return FinishSelectionChange(proceduralObject, changed);
        }

        private bool ApplyXRayEdgeBoxSelection(ProceduralObject proceduralObject, Rect screenRect, BoxSelectionMode mode)
        {
            EnsureXRayEdgeBoxMatchCapacity(m_XRayEdgeProjectionCount);
            m_XRayEdgeBoxMatches.Clear();

            XRayEdgeBoxSelectionJob boxSelectionJob = new XRayEdgeBoxSelectionJob
            {
                Projections = m_XRayEdgeProjectionResults,
                EdgeCount = m_XRayEdgeProjectionCount,
                RectMin = new float2(screenRect.xMin, screenRect.yMin),
                RectMax = new float2(screenRect.xMax, screenRect.yMax),
                Matches = m_XRayEdgeBoxMatches,
            };

            JobHandle boxSelectionHandle = boxSelectionJob.Schedule();
            boxSelectionHandle.Complete();

            bool changed = false;

            if (mode == BoxSelectionMode.Replace)
            {
                changed = m_EdgeSelection.Clear();
            }

            for (int i = 0; i < m_XRayEdgeBoxMatches.Length; i++)
            {
                int edgeIndex = m_XRayEdgeBoxMatches[i];
                if (edgeIndex < 0 || edgeIndex >= m_EdgeFrameDataCount)
                {
                    continue;
                }

                StructuralEdgeRef edge = m_EdgeFrameData[edgeIndex].Edge;

                if (mode == BoxSelectionMode.Remove)
                {
                    changed |= m_EdgeSelection.Remove(edge);
                }
                else
                {
                    changed |= m_EdgeSelection.Add(edge);
                }
            }

            if (changed)
            {
                RebuildEffectiveEdgeVertices(proceduralObject);
            }

            return FinishSelectionChange(proceduralObject, changed);
        }

        private static bool VisibleEdgeIntersectsScreenRect(EdgeFrameData frame, Rect screenRect)
        {
            int intervalCount = frame.VisibleIntervalCount;
            uint visibleMask = frame.VisibleIntervalMask;

            for (int intervalIndex = 0; intervalIndex < intervalCount; intervalIndex++)
            {
                if ((visibleMask & (1u << intervalIndex)) == 0u)
                {
                    continue;
                }

                float startT = intervalIndex / (float)intervalCount;
                float endT = (intervalIndex + 1) / (float)intervalCount;

                Vector2 screenStart = Vector2.Lerp(frame.ScreenStart, frame.ScreenEnd, startT);
                Vector2 screenEnd = Vector2.Lerp(frame.ScreenStart, frame.ScreenEnd, endT);

                if (ScreenSegmentIntersectsRect(screenStart, screenEnd, screenRect))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ScreenSegmentIntersectsRect(Vector2 segmentStart, Vector2 segmentEnd, Rect screenRect)
        {
            Vector2 delta = segmentEnd - segmentStart;
            float tMin = 0f;
            float tMax = 1f;

            return ClipScreenSegmentAxis(segmentStart.x, delta.x, screenRect.xMin, screenRect.xMax, ref tMin, ref tMax) && ClipScreenSegmentAxis(segmentStart.y, delta.y, screenRect.yMin, screenRect.yMax, ref tMin, ref tMax);
        }

        private static bool ClipScreenSegmentAxis(float segmentStart, float segmentDelta, float rectMin, float rectMax, ref float tMin, ref float tMax)
        {
            if (Mathf.Abs(segmentDelta) <= 0.000001f)
            {
                return segmentStart >= rectMin && segmentStart <= rectMax;
            }

            float t1 = (rectMin - segmentStart) / segmentDelta;
            float t2 = (rectMax - segmentStart) / segmentDelta;

            if (t1 > t2)
            {
                float swap = t1;
                t1 = t2;
                t2 = swap;
            }

            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            return tMin <= tMax;
        }

        [BurstCompile]
        private struct XRayEdgeBoxSelectionJob : IJob
        {
            [ReadOnly] public NativeArray<XRayEdgeProjectionResult> Projections;

            public int EdgeCount;
            public float2 RectMin;
            public float2 RectMax;
            public NativeList<int> Matches;

            public void Execute()
            {
                for (int i = 0; i < EdgeCount; i++)
                {
                    XRayEdgeProjectionResult projection = Projections[i];

                    if (projection.CameraDepthStart <= 0f || projection.CameraDepthEnd <= 0f || !ScreenSegmentIntersectsRect(projection.ScreenStart, projection.ScreenEnd))
                    {
                        continue;
                    }

                    Matches.Add(i);
                }
            }

            private bool ScreenSegmentIntersectsRect(float2 segmentStart, float2 segmentEnd)
            {
                float2 delta = segmentEnd - segmentStart;
                float tMin = 0f;
                float tMax = 1f;

                return ClipAxis(segmentStart.x, delta.x, RectMin.x, RectMax.x, ref tMin, ref tMax) && ClipAxis(segmentStart.y, delta.y, RectMin.y, RectMax.y, ref tMin, ref tMax);
            }

            private static bool ClipAxis(float segmentStart, float segmentDelta, float rectMin, float rectMax, ref float tMin, ref float tMax)
            {
                if (math.abs(segmentDelta) <= 0.000001f)
                {
                    return segmentStart >= rectMin && segmentStart <= rectMax;
                }

                float t1 = (rectMin - segmentStart) / segmentDelta;
                float t2 = (rectMax - segmentStart) / segmentDelta;

                if (t1 > t2)
                {
                    float swap = t1;
                    t1 = t2;
                    t2 = swap;
                }

                tMin = math.max(tMin, t1);
                tMax = math.min(tMax, t2);
                return tMin <= tMax;
            }
        }
    }
}