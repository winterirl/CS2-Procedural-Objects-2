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
        private const float kMarkerDiameterPixels = 10f;

        private VertexFrameData[] m_FrameData = Array.Empty<VertexFrameData>();

        private NativeArray<float3> m_ProjectionWorldPositions;
        private NativeArray<ProjectionJobResult> m_ProjectionResults;
        private int m_ProjectionJobCapacity;

        private readonly SolidVisibilityBvh m_SolidVisibilityBvh = new SolidVisibilityBvh();

        private int m_FrameDataCount;

        private uint m_ProjectionVersion;

        private uint m_LastGeometryVersion;
        private ObjectTransform m_LastTransform;
        private bool m_HasLastTransform;
        private CameraState m_LastCameraState;
        private bool m_HasLastCameraState;
        public int FrameDataCount => m_FrameDataCount;
        public VertexFrameData[] FrameData => m_FrameData;
        public uint ProjectionVersion => m_ProjectionVersion;

        public bool SetVisibilityMode(ProceduralObject proceduralObject, MeshEditVisibilityMode mode)
        {
            if (!ValidateSelectionTarget(proceduralObject) || (mode != MeshEditVisibilityMode.XRay && mode != MeshEditVisibilityMode.Solid) || mode == VisibilityMode)
            {
                return false;
            }

            VisibilityMode = mode;
            MeshGpu?.InvalidateGeometry();
            CancelGpuBox();
            m_HoveredVertex = null;
            m_HoveredEdge = null;
            m_HasLastMousePosition = false;

            // Mode changes invalidate projection:
            // Solid visibility also depends on camera position, facing, and the triangle BVH.
            m_DirtyFlags |= MeshEditCacheDirtyFlags.Projection | MeshEditCacheDirtyFlags.Hover;

            FlushVisualDirtyFlags();
            return true;
        }

        private void DetectProjectionChanges(ProceduralObject proceduralObject, Camera camera)
        {
            if (proceduralObject.GeometryVersion != m_LastGeometryVersion)
            {
                m_DirtyFlags |= MeshEditCacheDirtyFlags.Geometry;
            }

            if (!m_HasLastTransform || TransformChanged(proceduralObject.Transform, m_LastTransform))
            {
                m_DirtyFlags |= MeshEditCacheDirtyFlags.Transform;
            }

            // Capture only the camera values that affect projection/visibility.
            // Comparing Camera references would miss a moved existing camera.
            CameraState currentCamera = CameraState.Capture(camera);

            if (!m_HasLastCameraState || currentCamera.ViewChanged(m_LastCameraState))
            {
                m_DirtyFlags |= MeshEditCacheDirtyFlags.Camera;
            }

            if (!m_HasLastCameraState || currentCamera.ViewportChanged(m_LastCameraState))
            {
                m_DirtyFlags |= MeshEditCacheDirtyFlags.Viewport;
            }
        }

        private void RebuildProjectionCache(ProceduralObject proceduralObject, Camera camera, bool edgeWorldDataChanged)
        {
            // Edge mode has its own projection cache.
            // Projecting hidden vertex markers here would repeat expensive work on dense meshes.
            if (SelectionMode == MeshSelectionMode.Edge)
            {
                m_FrameDataCount = 0;

                if (VisibilityMode == MeshEditVisibilityMode.XRay)
                {
                    RebuildXRayEdgeFrameCache(proceduralObject, camera, edgeWorldDataChanged);
                }
                else
                {
                    RebuildSolidEdgeFrameCache(proceduralObject, camera);
                }

                CaptureProjectionState(proceduralObject, camera);
                return;
            }

            int logicalVertexCount = 0;

            for (int partIndex = 0; partIndex < proceduralObject.Parts.Count; partIndex++)
            {
                if (!IsPartInVertexScope(partIndex))
                {
                    continue;
                }

                logicalVertexCount += proceduralObject.Parts[partIndex].Topology.LogicalVertexCount;
            }

            // One frame entry per logical vertex.
            // Render copies at seams should still produce one marker and one pick target.
            EnsureFrameCapacity(logicalVertexCount);
            EnsureProjectionJobCapacity(logicalVertexCount);

            bool solidMode = VisibilityMode == MeshEditVisibilityMode.Solid;

            NativeArray<float3> solidQueryTargets = default;
            NativeArray<byte> solidFacingCandidates = default;

            if (solidMode)
            {
                m_SolidVisibilityBvh.EnsureBuilt(proceduralObject);
                m_SolidVisibilityBvh.EnsureQueryCapacity(logicalVertexCount);

                solidQueryTargets = m_SolidVisibilityBvh.QueryTargets;
                solidFacingCandidates = m_SolidVisibilityBvh.QueryFacingCandidates;
            }

            int frameIndex = 0;

            for (int partIndex = 0; partIndex < proceduralObject.Parts.Count; partIndex++)
            {
                if (!IsPartInVertexScope(partIndex))
                {
                    continue;
                }

                MeshPart part = proceduralObject.Parts[partIndex];

                for (int vertexIndex = 0; vertexIndex < part.Topology.LogicalVertexCount; vertexIndex++)
                {
                    Vertex vertex = part.Topology.GetVertex(vertexIndex);

                    Vector3 localPosition = vertex.GetPosition(part.Mesh);

                    Vector3 worldPosition = proceduralObject.Transform.TransformPoint(localPosition);

                    // Facing is a cheap early-out for Solid mode.
                    // The BVH query below answers actual occlusion for the survivors.
                    bool facingCandidate = !solidMode || IsVertexFacingCameraCandidate(proceduralObject, part, vertex, worldPosition, camera);

                    if (solidMode)
                    {
                        solidQueryTargets[frameIndex] = new float3(localPosition.x, localPosition.y, localPosition.z);

                        solidFacingCandidates[frameIndex] = facingCandidate ? (byte)1 : (byte)0;
                    }

                    m_FrameData[frameIndex] = new VertexFrameData
                    {
                        Vertex = new VertexRef(partIndex, vertex.Id),
                        WorldPosition = worldPosition,
                        SolidVisible = !solidMode,
                    };

                    m_ProjectionWorldPositions[frameIndex] = new float3(worldPosition.x, worldPosition.y, worldPosition.z);

                    frameIndex++;
                }
            }

            m_FrameDataCount = frameIndex;

            if (frameIndex > 0)
            {
                int pixelWidth = Mathf.Max(1, camera.pixelWidth > 0 ? camera.pixelWidth : Screen.width);

                int pixelHeight = Mathf.Max(1, camera.pixelHeight > 0 ? camera.pixelHeight : Screen.height);

                Matrix4x4 viewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;

                float perspectiveRadiusScale = 0f;
                float orthographicRadius = 0f;
                float desiredRadiusPixels = kMarkerDiameterPixels * 0.5f;

                if (camera.orthographic)
                {
                    orthographicRadius = 2f * camera.orthographicSize / pixelHeight * desiredRadiusPixels;
                }
                else
                {
                    float halfFovRadians = camera.fieldOfView * Mathf.Deg2Rad * 0.5f;

                    perspectiveRadiusScale = 2f * Mathf.Tan(halfFovRadians) / pixelHeight * desiredRadiusPixels;
                }

                Vector3 cameraPosition = camera.transform.position;

                ProjectVerticesJob projectionJob = new ProjectVerticesJob
                {
                    WorldPositions = m_ProjectionWorldPositions,
                    Results = m_ProjectionResults,
                    ViewProjection = ToFloat4x4(viewProjection),
                    WorldToCamera = ToFloat4x4(camera.worldToCameraMatrix),
                    CameraPosition = new float3(cameraPosition.x, cameraPosition.y, cameraPosition.z),
                    PixelWidth = pixelWidth,
                    PixelHeight = pixelHeight,
                    PerspectiveRadiusScale = perspectiveRadiusScale,
                    OrthographicRadius = orthographicRadius,
                    IsOrthographic = camera.orthographic,
                };

                // Schedule both before waiting so projection and visibility run together.
                JobHandle projectionHandle = projectionJob.Schedule(frameIndex, 64);

                JobHandle visibilityHandle = default;

                try
                {
                    if (solidMode)
                    {
                        Vector3 localCameraPosition = proceduralObject.Transform.InverseTransformPoint(cameraPosition);

                        Vector3 localCameraForward = proceduralObject.Transform.InverseTransformVector(camera.transform.forward);

                        float forwardMagnitudeSquared = localCameraForward.sqrMagnitude;
                        if (forwardMagnitudeSquared > 0.00000001f)
                        {
                            localCameraForward /= Mathf.Sqrt(forwardMagnitudeSquared);
                        } else
                        {
                            localCameraForward = Vector3.forward;
                        }

                        visibilityHandle = m_SolidVisibilityBvh.ScheduleVisibility(frameIndex, new float3(localCameraPosition.x, localCameraPosition.y, localCameraPosition.z), new float3(localCameraForward.x, localCameraForward.y, localCameraForward.z), camera.orthographic);
                    }
                } finally
                {
                    // A failed visibility schedule must not leave projection running.
                    try
                    {
                        projectionHandle.Complete();
                    } finally
                    {
                        visibilityHandle.Complete();
                    }
                }


                for (int i = 0; i < frameIndex; i++)
                {
                    ProjectionJobResult result = m_ProjectionResults[i];

                    VertexFrameData frameData = m_FrameData[i];

                    frameData.ScreenPosition = new Vector2(result.ScreenPosition.x, result.ScreenPosition.y);

                    frameData.CameraDepth = result.CameraDepth;
                    frameData.MarkerRadius = Mathf.Max(0.001f, result.MarkerRadius);

                    if (solidMode)
                    {
                        frameData.SolidVisible = m_SolidVisibilityBvh.QueryResults[i] != 0;
                    }

                    m_FrameData[i] = frameData;
                }
            }

            m_EdgeFrameDataCount = 0;
            InvalidateXRayEdgeCache();
            CaptureProjectionState(proceduralObject, camera);
        }

        private void CaptureProjectionState(ProceduralObject proceduralObject, Camera camera)
        {
            m_LastGeometryVersion = proceduralObject.GeometryVersion;

            m_LastTransform = proceduralObject.Transform;
            m_HasLastTransform = true;

            m_LastCameraState = CameraState.Capture(camera);
            m_HasLastCameraState = true;
        }

        private void EnsureProjectionJobCapacity(int requiredCount)
        {
            if (m_ProjectionJobCapacity >= requiredCount)
            {
                return;
            }

            int newCapacity = Mathf.Max(16, m_ProjectionJobCapacity == 0 ? requiredCount : m_ProjectionJobCapacity);

            while (newCapacity < requiredCount)
            {
                newCapacity *= 2;
            }

            // Persistent buffers avoid native allocations every frame.
            // Growing geometrically also keeps large imported meshes from thrashing.
            DisposeProjectionJobBuffers();

            m_ProjectionWorldPositions = new NativeArray<float3>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            m_ProjectionResults = new NativeArray<ProjectionJobResult>(newCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            m_ProjectionJobCapacity = newCapacity;
        }

        private void DisposeProjectionJobBuffers()
        {
            if (m_ProjectionWorldPositions.IsCreated)
            {
                m_ProjectionWorldPositions.Dispose();
            }

            if (m_ProjectionResults.IsCreated)
            {
                m_ProjectionResults.Dispose();
            }

            m_ProjectionJobCapacity = 0;
        }

        private static float4x4 ToFloat4x4(Matrix4x4 matrix)
        {
            return new float4x4(new float4(matrix.m00, matrix.m10, matrix.m20, matrix.m30), new float4(matrix.m01, matrix.m11, matrix.m21, matrix.m31), new float4(matrix.m02, matrix.m12, matrix.m22, matrix.m32), new float4(matrix.m03, matrix.m13, matrix.m23, matrix.m33));
        }

        private struct ProjectionJobResult
        {
            public float2 ScreenPosition;
            public float CameraDepth;
            public float MarkerRadius;
        }

        [BurstCompile]
        private struct ProjectVerticesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> WorldPositions;

            [WriteOnly] public NativeArray<ProjectionJobResult> Results;

            public float4x4 ViewProjection;
            public float4x4 WorldToCamera;
            public float3 CameraPosition;
            public float PixelWidth;
            public float PixelHeight;
            public float PerspectiveRadiusScale;
            public float OrthographicRadius;
            public bool IsOrthographic;

            public void Execute(int index)
            {
                float3 worldPosition = WorldPositions[index];
                float4 world = new float4(worldPosition, 1f);
                float4 clip = math.mul(ViewProjection, world);

                float inverseW = math.abs(clip.w) > 0.000001f ? 1f / clip.w : 0f;

                float2 ndc = clip.xy * inverseW;
                float2 screenPosition = new float2((ndc.x * 0.5f + 0.5f) * PixelWidth, (ndc.y * 0.5f + 0.5f) * PixelHeight);

                float4 view = math.mul(WorldToCamera, world);
                float cameraDepth = -view.z;

                float markerRadius;

                if (IsOrthographic)
                {
                    markerRadius = OrthographicRadius;
                }
                else
                {
                    float distance = math.distance(CameraPosition, worldPosition);

                    markerRadius = math.max(0.01f, distance) * PerspectiveRadiusScale;
                }

                Results[index] = new ProjectionJobResult
                {
                    ScreenPosition = screenPosition,
                    CameraDepth = cameraDepth,
                    MarkerRadius = markerRadius,
                };
            }
        }

        private void EnsureFrameCapacity(int requiredCount)
        {
            if (m_FrameData.Length >= requiredCount)
            {
                return;
            }

            int newCapacity = Mathf.Max(16, m_FrameData.Length == 0 ? requiredCount : m_FrameData.Length);

            while (newCapacity < requiredCount)
            {
                newCapacity *= 2;
            }

            m_FrameData = new VertexFrameData[newCapacity];
        }

        private static bool IsVertexFacingCameraCandidate(ProceduralObject proceduralObject, MeshPart part, Vertex vertex, Vector3 worldPosition, Camera camera)
        {
            Vector3[] normals = part.Mesh.Normals;
            if (normals == null || normals.Length == 0)
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

            IndexSpan renderVertexIndices = vertex.RenderVertexIndices;

            for (int i = 0; i < renderVertexIndices.Count; i++)
            {
                int renderVertexIndex = renderVertexIndices[i];
                if (renderVertexIndex < 0 || renderVertexIndex >= normals.Length)
                {
                    continue;
                }

                Vector3 localNormal = normals[renderVertexIndex];
                Vector3 worldNormal = TransformNormal(proceduralObject.Transform, localNormal);

                if (worldNormal.sqrMagnitude <= 0.00000001f)
                {
                    continue;
                }

                if (Vector3.Dot(worldNormal, viewDirection) >= -0.0001f)
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector3 TransformNormal(ObjectTransform transform, Vector3 localNormal)
        {
            const float minimumScaleMagnitude = 0.000001f;

            float scaleX = Mathf.Abs(transform.Scale.x) < minimumScaleMagnitude ? (transform.Scale.x < 0f ? -minimumScaleMagnitude : minimumScaleMagnitude) : transform.Scale.x;
            float scaleY = Mathf.Abs(transform.Scale.y) < minimumScaleMagnitude ? (transform.Scale.y < 0f ? -minimumScaleMagnitude : minimumScaleMagnitude) : transform.Scale.y;
            float scaleZ = Mathf.Abs(transform.Scale.z) < minimumScaleMagnitude ? (transform.Scale.z < 0f ? -minimumScaleMagnitude : minimumScaleMagnitude) : transform.Scale.z;

            // Normals use inverse scale, then rotation.
            // Applying TransformVector directly is wrong as soon as scale is non-uniform.
            Vector3 inverseScaledNormal = new Vector3(localNormal.x / scaleX, localNormal.y / scaleY, localNormal.z / scaleZ);

            return (transform.Rotation * inverseScaledNormal).normalized;
        }

        private static bool TransformChanged(ObjectTransform current, ObjectTransform previous)
        {
            const float vectorEpsilonSquared = 0.00000001f;
            const float rotationDotEpsilon = 0.0000001f;

            if ((current.Position - previous.Position).sqrMagnitude > vectorEpsilonSquared || (current.Scale - previous.Scale).sqrMagnitude > vectorEpsilonSquared)
            {
                return true;
            }

            float rotationDot = Mathf.Abs(Quaternion.Dot(current.Rotation, previous.Rotation));

            return 1f - rotationDot > rotationDotEpsilon;
        }

        private readonly struct CameraState
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly float FieldOfView;
            public readonly bool Orthographic;
            public readonly float OrthographicSize;
            public readonly float PixelX;
            public readonly float PixelY;
            public readonly int PixelWidth;
            public readonly int PixelHeight;

            private CameraState(Vector3 position, Quaternion rotation, float fieldOfView, bool orthographic, float orthographicSize, float pixelX, float pixelY, int pixelWidth, int pixelHeight)
            {
                Position = position;
                Rotation = rotation;
                FieldOfView = fieldOfView;
                Orthographic = orthographic;
                OrthographicSize = orthographicSize;
                PixelX = pixelX;
                PixelY = pixelY;
                PixelWidth = pixelWidth;
                PixelHeight = pixelHeight;
            }

            public static CameraState Capture(Camera camera)
            {
                Rect pixelRect = camera.pixelRect;

                return new CameraState(camera.transform.position, camera.transform.rotation, camera.fieldOfView, camera.orthographic, camera.orthographicSize, pixelRect.xMin, pixelRect.yMin, camera.pixelWidth, camera.pixelHeight);
            }

            public bool ViewChanged(CameraState previous)
            {
                const float positionEpsilonSquared = 0.00000001f;
                const float rotationDotEpsilon = 0.0000001f;
                const float scalarEpsilon = 0.0001f;

                if ((Position - previous.Position).sqrMagnitude > positionEpsilonSquared || Orthographic != previous.Orthographic || Mathf.Abs(FieldOfView - previous.FieldOfView) > scalarEpsilon || Mathf.Abs(OrthographicSize - previous.OrthographicSize) > scalarEpsilon)
                {
                    return true;
                }

                float rotationDot = Mathf.Abs(Quaternion.Dot(Rotation, previous.Rotation));

                return 1f - rotationDot > rotationDotEpsilon;
            }

            public bool ViewportChanged(CameraState previous)
            {
                const float scalarEpsilon = 0.0001f;

                return Mathf.Abs(PixelX - previous.PixelX) > scalarEpsilon || Mathf.Abs(PixelY - previous.PixelY) > scalarEpsilon || PixelWidth != previous.PixelWidth || PixelHeight != previous.PixelHeight;
            }
        }
    }
}