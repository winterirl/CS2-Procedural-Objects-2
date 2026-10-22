using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;

namespace PO2.Tools.MeshEdit
{
    public sealed partial class MeshEditController
    {
        private const float kEdgeSolidIntervalPixels = 24f;
        private const int kEdgeSolidMaxIntervals = 32;

        private EdgeFrameData[] m_EdgeFrameData = Array.Empty<EdgeFrameData>();

        // Query starts are scratch only. The final visibility lives on the
        // edge frame as a compact interval mask shared by renderer and picker.
        private int[] m_EdgeVisibilityQueryStarts = Array.Empty<int>();

        // X-Ray keeps its edge endpoints in native memory so camera movement
        // only has to run compact projection math. The managed edge frames are
        // still kept for stable edge refs, rendering fallback, and Solid mode.
        private NativeArray<XRayEdgeProjectionInput> m_XRayEdgeProjectionInputs;

        private NativeArray<XRayEdgeProjectionResult> m_XRayEdgeProjectionResults;

        private NativeArray<XRayEdgePickResult> m_XRayEdgePickResult;

        private NativeList<int> m_XRayEdgeBoxMatches;

        private int m_XRayEdgeProjectionCapacity;
        private int m_XRayEdgeProjectionCount;
        private bool m_HasXRayEdgeWorldCache;
        private bool m_HasXRayEdgeProjection;
        private int m_EdgeFrameDataCount;
        private uint m_EdgeWorldVersion;
        public int EdgeFrameDataCount => m_EdgeFrameDataCount;
        public EdgeFrameData[] EdgeFrameData => m_EdgeFrameData;
        public uint EdgeWorldVersion => m_EdgeWorldVersion;

        private void RebuildXRayEdgeFrameCache(ProceduralObject proceduralObject, Camera camera, bool edgeWorldDataChanged)
        {
            if (edgeWorldDataChanged || !m_HasXRayEdgeWorldCache || m_XRayEdgeProjectionCount != m_EdgeFrameDataCount)
            {
                RebuildXRayEdgeWorldCache(proceduralObject);
            }

            if (!m_HasXRayEdgeWorldCache || m_XRayEdgeProjectionCount <= 0)
            {
                m_HasXRayEdgeProjection = false;
                InvalidateXRayEdgePickCache();
                return;
            }

            int pixelWidth = Mathf.Max(1, camera.pixelWidth > 0 ? camera.pixelWidth : Screen.width);

            int pixelHeight = Mathf.Max(1, camera.pixelHeight > 0 ? camera.pixelHeight : Screen.height);

            Rect pixelRect = camera.pixelRect;
            Matrix4x4 viewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;

            ProjectXRayEdgesJob projectionJob = new ProjectXRayEdgesJob
            {
                Inputs = m_XRayEdgeProjectionInputs,
                Results = m_XRayEdgeProjectionResults,
                ViewProjection = ToFloat4x4(viewProjection),
                WorldToCamera = ToFloat4x4(camera.worldToCameraMatrix),
                PixelOrigin = new float2(pixelRect.xMin, pixelRect.yMin),
                PixelSize = new float2(pixelWidth, pixelHeight),
            };

            JobHandle projectionHandle = projectionJob.Schedule(m_XRayEdgeProjectionCount, 64);

            projectionHandle.Complete();
            m_HasXRayEdgeProjection = true;
            InvalidateXRayEdgePickCache();
        }

        private void RebuildXRayEdgeWorldCache(ProceduralObject proceduralObject)
        {
            int structuralEdgeCount = 0;

            for (int partIndex = 0; partIndex < proceduralObject.Parts.Count; partIndex++)
            {
                if (!IsPartInVertexScope(partIndex))
                {
                    continue;
                }

                structuralEdgeCount += proceduralObject.Parts[partIndex].StructuralTopology.StructuralEdgeCount;
            }

            EnsureEdgeFrameCapacity(structuralEdgeCount);
            EnsureXRayEdgeProjectionCapacity(structuralEdgeCount);

            int frameIndex = 0;

            for (int partIndex = 0; partIndex < proceduralObject.Parts.Count; partIndex++)
            {
                if (!IsPartInVertexScope(partIndex))
                {
                    continue;
                }

                MeshPart part = proceduralObject.Parts[partIndex];
                StructuralTopology topology = part.StructuralTopology;

                for (int edgeId = 0; edgeId < topology.StructuralEdgeCount; edgeId++)
                {
                    StructuralEdge edge = topology.GetEdge(edgeId);

                    Vertex logicalA = part.Topology.GetVertex(edge.LogicalVertexA);
                    Vertex logicalB = part.Topology.GetVertex(edge.LogicalVertexB);

                    Vector3 localStart = logicalA.GetPosition(part.Mesh);
                    Vector3 localEnd = logicalB.GetPosition(part.Mesh);
                    Vector3 worldStart = proceduralObject.Transform.TransformPoint(localStart);
                    Vector3 worldEnd = proceduralObject.Transform.TransformPoint(localEnd);

                    m_EdgeVisibilityQueryStarts[frameIndex] = 0;

                    m_EdgeFrameData[frameIndex] = new EdgeFrameData
                    {
                        Edge = new StructuralEdgeRef(partIndex, edgeId),
                        LocalStart = localStart,
                        LocalEnd = localEnd,
                        WorldStart = worldStart,
                        WorldEnd = worldEnd,
                        VisibleIntervalCount = 1,
                        VisibleIntervalMask = 1u,
                    };

                    m_XRayEdgeProjectionInputs[frameIndex] = new XRayEdgeProjectionInput
                    {
                        WorldStart = new float3(worldStart.x, worldStart.y, worldStart.z),
                        WorldEnd = new float3(worldEnd.x, worldEnd.y, worldEnd.z),
                    };

                    frameIndex++;
                }
            }

            m_EdgeFrameDataCount = frameIndex;
            m_XRayEdgeProjectionCount = frameIndex;
            m_HasXRayEdgeWorldCache = true;
            m_HasXRayEdgeProjection = false;
            InvalidateXRayEdgePickCache();
        }

        private void RebuildSolidEdgeFrameCache(ProceduralObject proceduralObject, Camera camera)
        {
            m_HasXRayEdgeWorldCache = false;
            m_HasXRayEdgeProjection = false;
            InvalidateXRayEdgePickCache();

            int structuralEdgeCount = 0;

            for (int partIndex = 0; partIndex < proceduralObject.Parts.Count; partIndex++)
            {
                if (!IsPartInVertexScope(partIndex))
                {
                    continue;
                }

                structuralEdgeCount += proceduralObject.Parts[partIndex].StructuralTopology.StructuralEdgeCount;
            }

            EnsureEdgeFrameCapacity(structuralEdgeCount);

            bool solidMode = VisibilityMode == MeshEditVisibilityMode.Solid;
            int frameIndex = 0;
            int solidQueryCount = 0;

            for (int partIndex = 0; partIndex < proceduralObject.Parts.Count; partIndex++)
            {
                if (!IsPartInVertexScope(partIndex))
                {
                    continue;
                }

                MeshPart part = proceduralObject.Parts[partIndex];
                StructuralTopology topology = part.StructuralTopology;

                for (int edgeId = 0; edgeId < topology.StructuralEdgeCount; edgeId++)
                {
                    StructuralEdge edge = topology.GetEdge(edgeId);

                    Vertex logicalA = part.Topology.GetVertex(edge.LogicalVertexA);
                    Vertex logicalB = part.Topology.GetVertex(edge.LogicalVertexB);

                    Vector3 localStart = logicalA.GetPosition(part.Mesh);
                    Vector3 localEnd = logicalB.GetPosition(part.Mesh);
                    Vector3 worldStart = proceduralObject.Transform.TransformPoint(localStart);
                    Vector3 worldEnd = proceduralObject.Transform.TransformPoint(localEnd);

                    Vector3 screenStart3 = camera.WorldToScreenPoint(worldStart);
                    Vector3 screenEnd3 = camera.WorldToScreenPoint(worldEnd);
                    Vector2 screenStart = new Vector2(screenStart3.x, screenStart3.y);
                    Vector2 screenEnd = new Vector2(screenEnd3.x, screenEnd3.y);

                    int visibleIntervalCount = solidMode ? CalculateSolidEdgeIntervalCount(screenStart, screenEnd, screenStart3.z, screenEnd3.z) : 1;

                    m_EdgeVisibilityQueryStarts[frameIndex] = solidQueryCount;

                    m_EdgeFrameData[frameIndex] = new EdgeFrameData
                    {
                        Edge = new StructuralEdgeRef(partIndex, edgeId),
                        LocalStart = localStart,
                        LocalEnd = localEnd,
                        WorldStart = worldStart,
                        WorldEnd = worldEnd,
                        ScreenStart = screenStart,
                        ScreenEnd = screenEnd,
                        CameraDepthStart = screenStart3.z,
                        CameraDepthEnd = screenEnd3.z,
                        VisibleIntervalCount = visibleIntervalCount,
                        VisibleIntervalMask = solidMode ? 0u : 1u,
                    };

                    if (solidMode)
                    {
                        solidQueryCount += visibleIntervalCount;
                    }

                    frameIndex++;
                }
            }

            m_EdgeFrameDataCount = frameIndex;

            if (!solidMode || solidQueryCount <= 0)
            {
                return;
            }

            // SolidVisibilityBvh already owns/refits the PO triangle tree for
            // Vertex Solid mode. Edge mode submits interval midpoints through
            // the same query buffers instead of maintaining another mesh tree.
            m_SolidVisibilityBvh.EnsureBuilt(proceduralObject);
            m_SolidVisibilityBvh.EnsureQueryCapacity(solidQueryCount);

            NativeArray<float3> queryTargets = m_SolidVisibilityBvh.QueryTargets;
            NativeArray<byte> facingCandidates = m_SolidVisibilityBvh.QueryFacingCandidates;

            for (int i = 0; i < frameIndex; i++)
            {
                EdgeFrameData frame = m_EdgeFrameData[i];
                int intervalCount = frame.VisibleIntervalCount;
                if (intervalCount <= 0)
                {
                    continue;
                }

                MeshPart part = proceduralObject.Parts[frame.Edge.PartIndex];
                StructuralEdge edge = part.StructuralTopology.GetEdge(frame.Edge.EdgeId);

                Vector3 worldMidpoint = Vector3.Lerp(frame.WorldStart, frame.WorldEnd, 0.5f);

                bool facingCandidate = IsStructuralEdgeFacingCameraCandidate(proceduralObject, part, edge, worldMidpoint, camera);

                int queryStart = m_EdgeVisibilityQueryStarts[i];

                for (int intervalIndex = 0; intervalIndex < intervalCount; intervalIndex++)
                {
                    float sampleT = (intervalIndex + 0.5f) / intervalCount;
                    Vector3 localTarget = Vector3.Lerp(frame.LocalStart, frame.LocalEnd, sampleT);
                    int queryIndex = queryStart + intervalIndex;

                    queryTargets[queryIndex] = new float3(localTarget.x, localTarget.y, localTarget.z);

                    facingCandidates[queryIndex] = facingCandidate ? (byte)1 : (byte)0;
                }
            }

            Vector3 localCameraPosition = proceduralObject.Transform.InverseTransformPoint(camera.transform.position);

            Vector3 localCameraForward = proceduralObject.Transform.InverseTransformVector(camera.transform.forward);

            float forwardMagnitudeSquared = localCameraForward.sqrMagnitude;
            if (forwardMagnitudeSquared > 0.00000001f)
            {
                localCameraForward /= Mathf.Sqrt(forwardMagnitudeSquared);
            }
            else
            {
                localCameraForward = Vector3.forward;
            }

            JobHandle visibilityHandle = m_SolidVisibilityBvh.ScheduleVisibility(solidQueryCount, new float3(localCameraPosition.x, localCameraPosition.y, localCameraPosition.z), new float3(localCameraForward.x, localCameraForward.y, localCameraForward.z), camera.orthographic);

            visibilityHandle.Complete();

            NativeArray<byte> queryResults = m_SolidVisibilityBvh.QueryResults;

            for (int i = 0; i < frameIndex; i++)
            {
                EdgeFrameData frame = m_EdgeFrameData[i];
                int intervalCount = frame.VisibleIntervalCount;
                int queryStart = m_EdgeVisibilityQueryStarts[i];
                uint visibleMask = 0u;

                for (int intervalIndex = 0; intervalIndex < intervalCount; intervalIndex++)
                {
                    if (queryResults[queryStart + intervalIndex] != 0)
                    {
                        visibleMask |= 1u << intervalIndex;
                    }
                }

                frame.VisibleIntervalMask = visibleMask;
                m_EdgeFrameData[i] = frame;
            }
        }

        private static int CalculateSolidEdgeIntervalCount(Vector2 screenStart, Vector2 screenEnd, float cameraDepthStart, float cameraDepthEnd)
        {
            // Near-plane crossing is deliberately conservative for Solid mode.
            // X-Ray keeps the old whole-line behavior, while Solid avoids
            // generating enormous/unstable projected intervals behind camera.
            if (cameraDepthStart <= 0f || cameraDepthEnd <= 0f)
            {
                return 0;
            }

            float screenLength = (screenEnd - screenStart).magnitude;

            if (float.IsNaN(screenLength) || float.IsInfinity(screenLength))
            {
                return 0;
            }

            return Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(1f, screenLength) / kEdgeSolidIntervalPixels), 1, kEdgeSolidMaxIntervals);
        }

        private static bool IsStructuralEdgeFacingCameraCandidate(ProceduralObject proceduralObject, MeshPart part, StructuralEdge edge, Vector3 worldPosition, Camera camera)
        {
            if (edge.AdjacentFaceCount == 0)
            {
                return true;
            }

            Vector3 viewDirection;
            if (camera.orthographic)
            {
                viewDirection = -camera.transform.forward;
            }
            else
            {
                viewDirection = camera.transform.position - worldPosition;
                float magnitudeSquared = viewDirection.sqrMagnitude;
                if (magnitudeSquared <= 0.00000001f)
                {
                    return true;
                }

                viewDirection /= Mathf.Sqrt(magnitudeSquared);
            }

            for (int i = 0; i < edge.AdjacentFaceCount; i++)
            {
                int faceId = edge.AdjacentFaceIds[i];

                if (faceId < 0 || faceId >= part.StructuralTopology.FaceCount)
                {
                    // Do not hide geometry when structural metadata is odd.
                    return true;
                }

                StructuralFace face = part.StructuralTopology.GetFace(faceId);

                if (!TryGetStructuralFaceWorldNormal(proceduralObject.Transform, part, face, out Vector3 worldNormal))
                {
                    // Degenerate faces are not safe evidence for backface
                    // rejection. Let the BVH make the occlusion decision.
                    return true;
                }

                if (Vector3.Dot(worldNormal, viewDirection) >= -0.0001f)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetStructuralFaceWorldNormal(ObjectTransform transform, MeshPart part, StructuralFace face, out Vector3 worldNormal)
        {
            worldNormal = Vector3.zero;

            if (face.CornerCount < 3)
            {
                return false;
            }

            Vector3[] vertices = part.Mesh.Vertices;
            int index0 = face.GetRenderVertexIndex(0);
            int index1 = face.GetRenderVertexIndex(1);
            int index2 = face.GetRenderVertexIndex(2);

            if (index0 < 0 || index0 >= vertices.Length || index1 < 0 || index1 >= vertices.Length || index2 < 0 || index2 >= vertices.Length)
            {
                return false;
            }

            Vector3 a = vertices[index0];
            Vector3 b = vertices[index1];
            Vector3 c = vertices[index2];
            Vector3 localNormal = Vector3.Cross(b - a, c - a);

            if (face.IsQuad)
            {
                int index3 = face.GetRenderVertexIndex(3);
                if (index3 < 0 || index3 >= vertices.Length)
                {
                    return false;
                }

                Vector3 d = vertices[index3];
                localNormal += Vector3.Cross(c - a, d - a);
            }

            if (localNormal.sqrMagnitude <= 0.00000001f)
            {
                return false;
            }

            worldNormal = TransformNormal(transform, localNormal);

            return worldNormal.sqrMagnitude > 0.00000001f;
        }

        private void EnsureEdgeFrameCapacity(int requiredCount)
        {
            if (m_EdgeFrameData.Length >= requiredCount)
            {
                return;
            }

            int newCapacity = Mathf.Max(16, m_EdgeFrameData.Length == 0 ? requiredCount : m_EdgeFrameData.Length);

            while (newCapacity < requiredCount)
            {
                newCapacity *= 2;
            }

            m_EdgeFrameData = new EdgeFrameData[newCapacity];
            m_EdgeVisibilityQueryStarts = new int[newCapacity];
        }

        private bool HasValidXRayEdgeProjection()
        {
            return m_HasXRayEdgeProjection && m_XRayEdgeProjectionInputs.IsCreated && m_XRayEdgeProjectionResults.IsCreated && m_XRayEdgePickResult.IsCreated && m_XRayEdgeProjectionCount > 0 && m_XRayEdgeProjectionCount == m_EdgeFrameDataCount;
        }

        private void EnsureXRayEdgeProjectionCapacity(int requiredCount)
        {
            if (requiredCount <= 0)
            {
                return;
            }

            if (m_XRayEdgeProjectionCapacity >= requiredCount && m_XRayEdgeProjectionInputs.IsCreated && m_XRayEdgeProjectionResults.IsCreated && m_XRayEdgePickResult.IsCreated)
            {
                return;
            }

            int newCapacity = Mathf.Max(16, m_XRayEdgeProjectionCapacity == 0 ? requiredCount : m_XRayEdgeProjectionCapacity);

            while (newCapacity < requiredCount)
            {
                newCapacity *= 2;
            }

            DisposeXRayEdgeProjectionBuffers();

            m_XRayEdgeProjectionInputs = new NativeArray<XRayEdgeProjectionInput>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            m_XRayEdgeProjectionResults = new NativeArray<XRayEdgeProjectionResult>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            m_XRayEdgePickResult = new NativeArray<XRayEdgePickResult>(1, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            m_XRayEdgeProjectionCapacity = newCapacity;
        }

        private void EnsureXRayEdgeBoxMatchCapacity(int requiredCount)
        {
            int requiredCapacity = Mathf.Max(16, requiredCount);

            if (!m_XRayEdgeBoxMatches.IsCreated)
            {
                m_XRayEdgeBoxMatches = new NativeList<int>(requiredCapacity, Allocator.Persistent);
                return;
            }

            if (m_XRayEdgeBoxMatches.Capacity < requiredCapacity)
            {
                m_XRayEdgeBoxMatches.Capacity = requiredCapacity;
            }
        }

        private void InvalidateXRayEdgeCache()
        {
            m_XRayEdgeProjectionCount = 0;
            m_HasXRayEdgeWorldCache = false;
            m_HasXRayEdgeProjection = false;
            InvalidateXRayEdgePickCache();

            if (m_XRayEdgeBoxMatches.IsCreated)
            {
                m_XRayEdgeBoxMatches.Clear();
            }
        }

        private void DisposeXRayEdgeProjectionBuffers()
        {
            if (m_XRayEdgeProjectionInputs.IsCreated)
            {
                m_XRayEdgeProjectionInputs.Dispose();
            }

            if (m_XRayEdgeProjectionResults.IsCreated)
            {
                m_XRayEdgeProjectionResults.Dispose();
            }

            if (m_XRayEdgePickResult.IsCreated)
            {
                m_XRayEdgePickResult.Dispose();
            }

            if (m_XRayEdgeBoxMatches.IsCreated)
            {
                m_XRayEdgeBoxMatches.Dispose();
            }

            m_XRayEdgeProjectionCapacity = 0;
            m_XRayEdgeProjectionCount = 0;
            m_HasXRayEdgeWorldCache = false;
            m_HasXRayEdgeProjection = false;
            InvalidateXRayEdgePickCache();
        }

        private struct XRayEdgeProjectionInput
        {
            public float3 WorldStart;
            public float3 WorldEnd;
        }

        private struct XRayEdgeProjectionResult
        {
            public float2 ScreenStart;
            public float2 ScreenEnd;
            public float CameraDepthStart;
            public float CameraDepthEnd;
        }

        [BurstCompile]
        private struct ProjectXRayEdgesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<XRayEdgeProjectionInput> Inputs;

            [WriteOnly] public NativeArray<XRayEdgeProjectionResult> Results;

            public float4x4 ViewProjection;
            public float4x4 WorldToCamera;
            public float2 PixelOrigin;
            public float2 PixelSize;

            public void Execute(int index)
            {
                XRayEdgeProjectionInput input = Inputs[index];

                ProjectPoint(input.WorldStart, out float2 screenStart, out float cameraDepthStart);

                ProjectPoint(input.WorldEnd, out float2 screenEnd, out float cameraDepthEnd);

                Results[index] = new XRayEdgeProjectionResult
                {
                    ScreenStart = screenStart,
                    ScreenEnd = screenEnd,
                    CameraDepthStart = cameraDepthStart,
                    CameraDepthEnd = cameraDepthEnd,
                };
            }

            private void ProjectPoint(float3 worldPosition, out float2 screenPosition, out float cameraDepth)
            {
                float4 world = new float4(worldPosition, 1f);
                float4 clip = math.mul(ViewProjection, world);

                float inverseW = math.abs(clip.w) > 0.000001f ? 1f / clip.w : 0f;

                float2 ndc = clip.xy * inverseW;
                screenPosition = PixelOrigin + new float2((ndc.x * 0.5f + 0.5f) * PixelSize.x, (ndc.y * 0.5f + 0.5f) * PixelSize.y);

                float4 view = math.mul(WorldToCamera, world);
                cameraDepth = -view.z;
            }
        }
    }
}