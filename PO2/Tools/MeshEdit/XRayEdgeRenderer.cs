using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Game.Prefabs;
using Game.Rendering;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;
using PO2.Geometry.Topology;

namespace PO2.Tools.MeshEdit
{
    // X-Ray edge backend. CPU picking keeps its exact projected edge cache,
    // while compute turns compact endpoints/state into one Overlay instance
    // per structural edge and submits the lot in one indirect draw.
    internal sealed class XRayEdgeRenderer : IDisposable
    {
        private const string kComputeKernelName = "BuildEdgeInstances";
        private const int kComputeThreadGroupSize = 64;
        private const float kEdgeHalfWidthPixels = 0.75f;

        [StructLayout(LayoutKind.Sequential)]
        private struct EdgeInput
        {
            // Explicit float4 lanes keep the StructuredBuffer stride identical
            // across Unity graphics backends. W is unused by the kernel.
            public Vector4 Start;
            public Vector4 End;
        }

        private static readonly int s_CustomMeshBufferId = Shader.PropertyToID("colossal_OverlayCustomMeshBuffer");
        private static readonly int s_ComputeEdgeInputsId = Shader.PropertyToID("_PO2EdgeInputs");
        private static readonly int s_ComputeEdgeInstancesId = Shader.PropertyToID("_PO2EdgeInstances");
        private static readonly int s_ComputeEdgeSelectionBitsId = Shader.PropertyToID("_PO2EdgeSelectionBits");
        private static readonly int s_ComputeCameraPositionId = Shader.PropertyToID("_PO2EdgeCameraPosition");
        private static readonly int s_ComputeCameraForwardId = Shader.PropertyToID("_PO2EdgeCameraForward");
        private static readonly int s_ComputeCameraRightId = Shader.PropertyToID("_PO2EdgeCameraRight");
        private static readonly int s_ComputeCameraUpId = Shader.PropertyToID("_PO2EdgeCameraUp");
        private static readonly int s_ComputeHalfWidthScaleId = Shader.PropertyToID("_PO2EdgeHalfWidthScale");
        private static readonly int s_ComputeMinimumDepthId = Shader.PropertyToID("_PO2EdgeMinimumDepth");
        private static readonly int s_ComputeEdgeCountId = Shader.PropertyToID("_PO2EdgeCount");
        private static readonly int s_ComputeHoveredEdgeIndexId = Shader.PropertyToID("_PO2HoveredEdgeIndex");
        private static readonly int s_ComputeIsOrthographicId = Shader.PropertyToID("_PO2EdgeIsOrthographic");
        private static readonly int s_ComputeCustomMeshTypeId = Shader.PropertyToID("_PO2EdgeCustomMeshType");
        private static readonly int s_ComputeNormalColorId = Shader.PropertyToID("_PO2EdgeNormalColor");
        private static readonly int s_ComputeHoveredColorId = Shader.PropertyToID("_PO2EdgeHoveredColor");
        private static readonly int s_ComputeSelectedColorId = Shader.PropertyToID("_PO2EdgeSelectedColor");
        private static readonly int s_ComputeSelectedHoveredColorId = Shader.PropertyToID("_PO2EdgeSelectedHoveredColor");

        private readonly PrefabSystem m_PrefabSystem;
        private readonly EntityQuery m_SettingsQuery;
        private readonly ComputeShader m_ComputeShader;
        private readonly int m_InstanceStride;

        private Mesh m_Mesh;
        private Material m_Material;
        private ComputeBuffer m_InstanceBuffer;
        private ComputeBuffer m_ArgsBuffer;
        private ComputeBuffer m_EdgeInputBuffer;
        private ComputeBuffer m_EdgeSelectionBitsBuffer;
        private int m_ComputeKernel = -1;
        private EdgeInput[] m_EdgeInputs;
        private uint[] m_EdgeSelectionBits;
        private int[] m_PartEdgeOffsets = Array.Empty<int>();
        private int[] m_PartEdgeCounts = Array.Empty<int>();
        private bool[] m_PartHasEdgeOffset = Array.Empty<bool>();
        private Bounds m_Bounds;
        private int m_EdgeCount;
        private int m_EdgeCapacity;
        private int m_HoveredEdgeIndex = -1;
        private bool m_Active;
        private bool m_InstanceDataDirty;
        private bool m_HasGeneratedInstances;

        private Camera m_LastCamera;
        private Vector3 m_LastCameraPosition;
        private Quaternion m_LastCameraRotation;
        private bool m_LastCameraOrthographic;
        private float m_LastCameraProjectionSize;
        private float m_LastCameraNearClip;
        private int m_LastCameraPixelHeight;

        public XRayEdgeRenderer(PrefabSystem prefabSystem, EntityQuery settingsQuery, ComputeShader computeShader)
        {
            m_PrefabSystem = prefabSystem;
            m_SettingsQuery = settingsQuery;
            m_ComputeShader = computeShader;
            m_InstanceStride = Marshal.SizeOf<OverlayRenderSystem.CustomMeshdData>();

            CreateResources();
            RenderPipelineManager.beginContextRendering += Render;
        }

        public bool IsAvailable =>
            m_Mesh != null && m_Material != null && m_ArgsBuffer != null && m_InstanceBuffer != null && m_EdgeInputBuffer != null && m_EdgeSelectionBitsBuffer != null && m_ComputeShader != null && m_ComputeKernel >= 0;

        public void SetActive(bool active)
        {
            if (active && !m_Active)
            {
                m_InstanceDataDirty = true;
            }

            m_Active = active;
            if (!active)
            {
                m_EdgeCount = 0;
                m_HoveredEdgeIndex = -1;
            }
        }

        public bool UpdateEdges(EdgeFrameData[] frameData, int frameDataCount)
        {
            if (!IsAvailable || frameData == null || frameDataCount <= 0 || frameDataCount > frameData.Length)
            {
                SetActive(false);
                return false;
            }

            EnsureCapacity(frameDataCount);
            if (!IsAvailable)
            {
                SetActive(false);
                return false;
            }

            Array.Clear(m_PartHasEdgeOffset, 0, m_PartHasEdgeOffset.Length);
            Array.Clear(m_PartEdgeCounts, 0, m_PartEdgeCounts.Length);

            Vector3 min = frameData[0].WorldStart;
            Vector3 max = min;

            for (int i = 0; i < frameDataCount; i++)
            {
                EdgeFrameData edge = frameData[i];
                m_EdgeInputs[i] = new EdgeInput
                {
                    Start = new Vector4(edge.WorldStart.x, edge.WorldStart.y, edge.WorldStart.z, 0f),
                    End = new Vector4(edge.WorldEnd.x, edge.WorldEnd.y, edge.WorldEnd.z, 0f),
                };

                min = Vector3.Min(min, edge.WorldStart);
                min = Vector3.Min(min, edge.WorldEnd);
                max = Vector3.Max(max, edge.WorldStart);
                max = Vector3.Max(max, edge.WorldEnd);

                int partIndex = edge.Edge.PartIndex;
                if (partIndex < 0)
                {
                    continue;
                }

                EnsurePartCapacity(partIndex + 1);
                if (edge.Edge.EdgeId == 0)
                {
                    m_PartEdgeOffsets[partIndex] = i;
                    m_PartHasEdgeOffset[partIndex] = true;
                }

                m_PartEdgeCounts[partIndex]++;
            }

            m_EdgeInputBuffer.SetData(m_EdgeInputs, 0, 0, frameDataCount);

            // Unity performs one coarse bounds test for the whole indirect
            // draw. Endpoints define the real extent; a small pad covers line
            // width and avoids a zero-sized bound on tiny or flat objects.
            Vector3 boundsPadding = Vector3.one * 1f;
            min -= boundsPadding;
            max += boundsPadding;
            m_Bounds = new Bounds((min + max) * 0.5f, max - min);

            if (frameDataCount != m_EdgeCount)
            {
                UpdateArgsBuffer(frameDataCount);
            }

            m_EdgeCount = frameDataCount;
            m_Active = true;
            m_InstanceDataDirty = true;

            return true;
        }

        public void UpdateSelectedEdges(StructuralEdgeRef[] selectedEdges)
        {
            if (!IsAvailable || m_EdgeCount <= 0)
            {
                return;
            }

            int wordCount = GetSelectionWordCount(m_EdgeCount);
            Array.Clear(m_EdgeSelectionBits, 0, wordCount);

            if (selectedEdges != null)
            {
                for (int i = 0; i < selectedEdges.Length; i++)
                {
                    if (!TryGetFrameIndex(selectedEdges[i], out int frameIndex))
                    {
                        continue;
                    }

                    int wordIndex = frameIndex >> 5;
                    int bitIndex = frameIndex & 31;
                    m_EdgeSelectionBits[wordIndex] |= 1u << bitIndex;
                }
            }

            m_EdgeSelectionBitsBuffer.SetData(m_EdgeSelectionBits, 0, 0, wordCount);
            m_InstanceDataDirty = true;
        }

        public void UpdateHoveredEdge(StructuralEdgeRef? hoveredEdge)
        {
            int nextHoveredIndex = -1;
            if (hoveredEdge.HasValue)
            {
                TryGetFrameIndex(hoveredEdge.Value, out nextHoveredIndex);
            }

            if (nextHoveredIndex == m_HoveredEdgeIndex)
            {
                return;
            }

            m_HoveredEdgeIndex = nextHoveredIndex;
            m_InstanceDataDirty = true;
        }

        public void Dispose()
        {
            RenderPipelineManager.beginContextRendering -= Render;

            if (m_ArgsBuffer != null)
            {
                m_ArgsBuffer.Release();
                m_ArgsBuffer = null;
            }

            ReleaseInstanceBuffers();

            if (m_Material != null)
            {
                UnityEngine.Object.Destroy(m_Material);
                m_Material = null;
            }

            if (m_Mesh != null)
            {
                UnityEngine.Object.Destroy(m_Mesh);
                m_Mesh = null;
            }
        }

        private void CreateResources()
        {
            if (m_ComputeShader == null)
            {
                Mod.log.Error("[PO Edge] GPU X-Ray renderer has no shared compute " + "shader. Gizmo edge fallback will be used.");
                return;
            }

            try
            {
                m_ComputeKernel = m_ComputeShader.FindKernel(kComputeKernelName);
                if (m_ComputeKernel < 0 || !m_ComputeShader.IsSupported(m_ComputeKernel))
                {
                    throw new InvalidOperationException("Kernel is not supported or did not compile for this graphics backend");
                }
            }
            catch (Exception exception)
            {
                Mod.log.Error($"[PO Edge] GPU X-Ray compute kernel " + $"'{kComputeKernelName}' could not be resolved: " + $"{exception.Message}. Rebuild the PO2 compute bundle; " + "Gizmo edge fallback will be used meanwhile.");
                m_ComputeKernel = -1;
                return;
            }

            if (m_ComputeKernel < 0)
            {
                Mod.log.Error($"[PO Edge] GPU X-Ray compute kernel " + $"'{kComputeKernelName}' resolved to an invalid index. " + "Rebuild the PO2 compute bundle; Gizmo edge fallback " + "will be used meanwhile.");
                return;
            }

            OverlayConfigurationPrefab overlayConfiguration = m_PrefabSystem.GetSingletonPrefab<OverlayConfigurationPrefab>(m_SettingsQuery);

            if (overlayConfiguration == null || overlayConfiguration.m_SolidObjectMaterial == null)
            {
                Mod.log.Error("[PO Edge] GPU X-Ray renderer could not resolve " + "OverlayConfigurationPrefab solid-object material. " + "Gizmo edge fallback will be used.");
                return;
            }

            // This is the same CS2 overlay material already proven by the GPU
            // vertex markers, so the new path stays inside the game's HDRP
            // overlay contract instead of introducing a custom render pass.
            m_Mesh = CreateEdgeMesh();
            m_Material = new Material(overlayConfiguration.m_SolidObjectMaterial)
            {
                name = "PO2 X-Ray Edge Renderer",
            };

            m_ArgsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
            m_ArgsBuffer.name = "PO2 X-Ray edge indirect args";
            UpdateArgsBuffer(0);

            EnsureCapacity(16);
        }

        private void EnsureCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= m_EdgeCapacity && m_InstanceBuffer != null && m_EdgeInputBuffer != null && m_EdgeSelectionBitsBuffer != null && m_EdgeInputs != null && m_EdgeSelectionBits != null)
            {
                return;
            }

            int newCapacity = Math.Max(16, m_EdgeCapacity);
            while (newCapacity < requiredCapacity)
            {
                int doubled = newCapacity <= int.MaxValue / 2 ? newCapacity * 2 : requiredCapacity;
                newCapacity = Math.Max(doubled, requiredCapacity);
            }

            ReleaseInstanceBuffers();

            m_InstanceBuffer = new ComputeBuffer(newCapacity, m_InstanceStride, ComputeBufferType.Structured);
            m_InstanceBuffer.name = "PO2 X-Ray edge instances";
            m_Material?.SetBuffer(s_CustomMeshBufferId, m_InstanceBuffer);

            m_EdgeInputBuffer = new ComputeBuffer(newCapacity, Marshal.SizeOf<EdgeInput>(), ComputeBufferType.Structured);
            m_EdgeInputBuffer.name = "PO2 X-Ray edge inputs";

            int selectionWordCapacity = GetSelectionWordCount(newCapacity);
            m_EdgeSelectionBitsBuffer = new ComputeBuffer(selectionWordCapacity, sizeof(uint), ComputeBufferType.Structured);
            m_EdgeSelectionBitsBuffer.name = "PO2 X-Ray edge selection bits";

            Array.Resize(ref m_EdgeInputs, newCapacity);
            Array.Resize(ref m_EdgeSelectionBits, selectionWordCapacity);

            m_EdgeCapacity = newCapacity;
            m_HasGeneratedInstances = false;
        }

        private void ReleaseInstanceBuffers()
        {
            if (m_InstanceBuffer != null)
            {
                m_InstanceBuffer.Release();
                m_InstanceBuffer = null;
            }

            if (m_EdgeInputBuffer != null)
            {
                m_EdgeInputBuffer.Release();
                m_EdgeInputBuffer = null;
            }

            if (m_EdgeSelectionBitsBuffer != null)
            {
                m_EdgeSelectionBitsBuffer.Release();
                m_EdgeSelectionBitsBuffer = null;
            }

            m_EdgeCapacity = 0;
            m_HasGeneratedInstances = false;
        }

        private void EnsurePartCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= m_PartEdgeOffsets.Length)
            {
                return;
            }

            int newCapacity = Math.Max(4, m_PartEdgeOffsets.Length);
            while (newCapacity < requiredCapacity)
            {
                newCapacity *= 2;
            }

            Array.Resize(ref m_PartEdgeOffsets, newCapacity);
            Array.Resize(ref m_PartEdgeCounts, newCapacity);
            Array.Resize(ref m_PartHasEdgeOffset, newCapacity);
        }

        private bool TryGetFrameIndex(StructuralEdgeRef edge, out int frameIndex)
        {
            frameIndex = -1;

            if (edge.PartIndex < 0 || edge.PartIndex >= m_PartHasEdgeOffset.Length || !m_PartHasEdgeOffset[edge.PartIndex] || edge.EdgeId < 0 || edge.EdgeId >= m_PartEdgeCounts[edge.PartIndex])
            {
                return false;
            }

            int candidate = m_PartEdgeOffsets[edge.PartIndex] + edge.EdgeId;
            if (candidate < 0 || candidate >= m_EdgeCount)
            {
                return false;
            }

            frameIndex = candidate;
            return true;
        }

        private void UpdateArgsBuffer(int instanceCount)
        {
            if (m_ArgsBuffer == null || m_Mesh == null)
            {
                return;
            }

            uint[] args = new uint[5]
            {
                m_Mesh.GetIndexCount(0),
                (uint)Math.Max(instanceCount, 0),
                m_Mesh.GetIndexStart(0),
                m_Mesh.GetBaseVertex(0),
                0u,
            };
            m_ArgsBuffer.SetData(args);
        }

        private void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!m_Active || m_EdgeCount <= 0 || !IsAvailable)
            {
                return;
            }

            for (int i = 0; i < cameras.Count; i++)
            {
                Camera camera = cameras[i];
                if (camera == null || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView))
                {
                    continue;
                }

                PrepareInstancesForCamera(camera);
                if (!m_HasGeneratedInstances)
                {
                    continue;
                }

                Graphics.DrawMeshInstancedIndirect(m_Mesh, 0, m_Material, m_Bounds, m_ArgsBuffer, 0, null, ShadowCastingMode.Off, false, 0, camera);
            }
        }

        private void PrepareInstancesForCamera(Camera camera)
        {
            if (!IsAvailable || m_EdgeCount <= 0)
            {
                return;
            }

            int pixelHeight = Math.Max(1, camera.pixelHeight > 0 ? camera.pixelHeight : Screen.height);
            float projectionSize = camera.orthographic ? camera.orthographicSize : camera.fieldOfView;

            bool cameraChanged = !m_HasGeneratedInstances || !ReferenceEquals(camera, m_LastCamera) || (camera.transform.position - m_LastCameraPosition).sqrMagnitude > 0.00000001f || 1f - Mathf.Abs(Quaternion.Dot(camera.transform.rotation, m_LastCameraRotation)) > 0.0000001f || camera.orthographic != m_LastCameraOrthographic || Mathf.Abs(projectionSize - m_LastCameraProjectionSize) > 0.000001f || Mathf.Abs(camera.nearClipPlane - m_LastCameraNearClip) > 0.000001f || pixelHeight != m_LastCameraPixelHeight;

            if (!m_InstanceDataDirty && !cameraChanged)
            {
                return;
            }

            Vector3 cameraForward = camera.transform.forward.normalized;
            Vector3 cameraRight = camera.transform.right.normalized;
            Vector3 cameraUp = camera.transform.up.normalized;

            float halfWidthScale;
            if (camera.orthographic)
            {
                halfWidthScale = 2f * camera.orthographicSize / pixelHeight * kEdgeHalfWidthPixels;
            }
            else
            {
                float halfFovRadians = camera.fieldOfView * Mathf.Deg2Rad * 0.5f;
                halfWidthScale = 2f * Mathf.Tan(halfFovRadians) / pixelHeight * kEdgeHalfWidthPixels;
            }

            SetComputeVector(s_ComputeCameraPositionId, camera.transform.position);
            SetComputeVector(s_ComputeCameraForwardId, cameraForward);
            SetComputeVector(s_ComputeCameraRightId, cameraRight);
            SetComputeVector(s_ComputeCameraUpId, cameraUp);
            m_ComputeShader.SetFloat(s_ComputeHalfWidthScaleId, halfWidthScale);
            m_ComputeShader.SetFloat(s_ComputeMinimumDepthId, Mathf.Max(camera.nearClipPlane, 0.001f));
            m_ComputeShader.SetInt(s_ComputeEdgeCountId, m_EdgeCount);
            m_ComputeShader.SetInt(s_ComputeHoveredEdgeIndexId, m_HoveredEdgeIndex);
            m_ComputeShader.SetInt(s_ComputeIsOrthographicId, camera.orthographic ? 1 : 0);
            m_ComputeShader.SetInt(s_ComputeCustomMeshTypeId, (int)OverlayRenderSystem.CustomMeshType.Plane);
            SetComputeColor(s_ComputeNormalColorId, VertexRenderSystem.s_EdgeNormalColor);
            SetComputeColor(s_ComputeHoveredColorId, VertexRenderSystem.s_EdgeHoveredColor);
            SetComputeColor(s_ComputeSelectedColorId, VertexRenderSystem.s_EdgeSelectedColor);
            SetComputeColor(s_ComputeSelectedHoveredColorId, VertexRenderSystem.s_EdgeSelectedHoveredColor);

            m_ComputeShader.SetBuffer(m_ComputeKernel, s_ComputeEdgeInputsId, m_EdgeInputBuffer);
            m_ComputeShader.SetBuffer(m_ComputeKernel, s_ComputeEdgeSelectionBitsId, m_EdgeSelectionBitsBuffer);
            m_ComputeShader.SetBuffer(m_ComputeKernel, s_ComputeEdgeInstancesId, m_InstanceBuffer);

            int threadGroupCount = (m_EdgeCount + kComputeThreadGroupSize - 1) / kComputeThreadGroupSize;
            m_ComputeShader.Dispatch(m_ComputeKernel, threadGroupCount, 1, 1);

            m_LastCamera = camera;
            m_LastCameraPosition = camera.transform.position;
            m_LastCameraRotation = camera.transform.rotation;
            m_LastCameraOrthographic = camera.orthographic;
            m_LastCameraProjectionSize = projectionSize;
            m_LastCameraNearClip = camera.nearClipPlane;
            m_LastCameraPixelHeight = pixelHeight;
            m_HasGeneratedInstances = true;
            m_InstanceDataDirty = false;
        }

        private void SetComputeVector(int propertyId, Vector3 value)
        {
            m_ComputeShader.SetVector(propertyId, new Vector4(value.x, value.y, value.z, 0f));
        }

        private void SetComputeColor(int propertyId, Color color)
        {
            Color linearColor = color.linear;
            m_ComputeShader.SetVector(propertyId, new Vector4(linearColor.r, linearColor.g, linearColor.b, linearColor.a));
        }

        private static int GetSelectionWordCount(int edgeCapacity)
        {
            return Math.Max(1, (edgeCapacity + 31) >> 5);
        }

        private static Mesh CreateEdgeMesh()
        {
            Mesh mesh = new Mesh
            {
                name = "PO2 X-Ray Edge Renderer Quad",
            };

            // Compute maps local X onto the edge and local Z across it. Both
            // windings are present because an edge can cross the camera plane
            // and Overlay material culling is owned by the game.
            mesh.vertices = new[]
            {
                new Vector3(-1f, 0f, -1f),
                new Vector3(-1f, 0f, 1f),
                new Vector3(1f, 0f, 1f),
                new Vector3(1f, 0f, -1f),
            };

            mesh.triangles = new[]
            {
                0, 1, 2,
                2, 3, 0,
                0, 3, 2,
                2, 1, 0,
            };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}