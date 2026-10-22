using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Game.Prefabs;
using Game.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using PO2.Geometry.Editing;
using PO2.Tools.ObjectTool;

namespace PO2.Tools.MeshEdit
{
    // GPU marker backend. CPU uploads compact position/radius/state arrays;
    // compute expands those into Overlay custom-mesh instances, then one
    // indirect draw renders every logical vertex.
    internal sealed class VertexMarkerRenderer : IDisposable
    {
        private const string kComputeBundleRelativePath = "ComputeAssets/po2_vertex_marker_compute.bundle";
        private const string kComputeKernelName = "BuildInstances";
        private const int kComputeThreadGroupSize = 64;

        private struct MarkerInput
        {
            public Vector3 Position;
            public float Radius;
        }

        private static readonly int s_CustomMeshBufferId = Shader.PropertyToID("colossal_OverlayCustomMeshBuffer");
        private static readonly int s_ComputeMarkerInputsId = Shader.PropertyToID("_PO2MarkerInputs");
        private static readonly int s_ComputeMarkerInstancesId = Shader.PropertyToID("_PO2MarkerInstances");
        private static readonly int s_ComputeMarkerVisualStatesId = Shader.PropertyToID("_PO2MarkerVisualStates");
        private static readonly int s_ComputeCameraRightId = Shader.PropertyToID("_PO2CameraRight");
        private static readonly int s_ComputeCameraNormalId = Shader.PropertyToID("_PO2CameraNormal");
        private static readonly int s_ComputeCameraUpId = Shader.PropertyToID("_PO2CameraUp");
        private static readonly int s_ComputeMarkerCountId = Shader.PropertyToID("_PO2MarkerCount");
        private static readonly int s_ComputeCustomMeshTypeId = Shader.PropertyToID("_PO2CustomMeshType");
        private static readonly int s_ComputeNormalColorId = Shader.PropertyToID("_PO2NormalColor");
        private static readonly int s_ComputeHoveredColorId = Shader.PropertyToID("_PO2HoveredColor");
        private static readonly int s_ComputeSelectedColorId = Shader.PropertyToID("_PO2SelectedColor");
        private static readonly int s_ComputeSelectedHoveredColorId = Shader.PropertyToID("_PO2SelectedHoveredColor");
        private static readonly int s_ComputeAlignmentTargetColorId = Shader.PropertyToID("_PO2AlignmentTargetColor");
        private static readonly int s_ComputeSlideTargetColorId = Shader.PropertyToID("_PO2SlideTargetColor");

        private readonly PrefabSystem m_PrefabSystem;
        private readonly EntityQuery m_SettingsQuery;
        private readonly int m_InstanceStride;

        private Mesh m_Mesh;
        private Material m_Material;
        private ComputeBuffer m_InstanceBuffer;
        private ComputeBuffer m_ArgsBuffer;
        private AssetBundle m_ComputeBundle;
        private ComputeShader m_ComputeShader;
        private ComputeBuffer m_ComputeInputBuffer;
        private ComputeBuffer m_ComputeVisualStateBuffer;
        private int m_ComputeKernel = -1;
        private MarkerInput[] m_MarkerInputs;
        private uint[] m_MarkerVisualStates;
        private Bounds m_Bounds;
        private int m_MarkerCount;
        private int m_InstanceCapacity;
        private bool m_Active;
        private bool m_InstanceDataDirty;
        private bool m_HasGeneratedInstances;
        private Quaternion m_LastCameraRotation;
        private MeshEditVisibilityMode m_VisibilityMode = MeshEditVisibilityMode.XRay;

        public VertexMarkerRenderer(PrefabSystem prefabSystem, EntityQuery settingsQuery)
        {
            m_PrefabSystem = prefabSystem;
            m_SettingsQuery = settingsQuery;
            m_InstanceStride = Marshal.SizeOf<OverlayRenderSystem.CustomMeshdData>();

            CreateResources();
            // GameSystem updates prepare inputs, but the SRP callback gives us
            // the actual camera list and correct point to issue indirect draws.
            RenderPipelineManager.beginContextRendering += Render;
        }

        public bool IsAvailable =>
            m_Mesh != null && m_Material != null && m_ArgsBuffer != null && m_ComputeShader != null && m_ComputeKernel >= 0;

        // The compute AssetBundle must have one owner. The X-Ray edge renderer
        // borrows this shader and is disposed before this renderer unloads it.
        internal ComputeShader SharedComputeShader => m_ComputeShader;
        internal T LoadMeshEditAsset<T>(string path) where T : UnityEngine.Object => m_ComputeBundle?.LoadAsset<T>(path);

        public void SetActive(bool active)
        {
            m_Active = active;
            if (!active)
            {
                m_MarkerCount = 0;
            }
        }

        public void SetVisibilityMode(MeshEditVisibilityMode mode)
        {
            m_VisibilityMode = mode;
        }

        public void UpdateMarkers(VertexFrameData[] frameData, int frameDataCount)
        {
            if (!IsAvailable || frameData == null || frameDataCount <= 0)
            {
                SetActive(false);
                return;
            }

            EnsureCapacity(frameDataCount);
            if (m_InstanceBuffer == null || m_ComputeInputBuffer == null)
            {
                SetActive(false);
                return;
            }

            // Solid-hidden markers stay in the buffer with radius zero. Keeping
            // stable indices means the visual-state buffer still lines up.
            Vector3 min = frameData[0].WorldPosition;
            Vector3 max = min;
            float maxRadius = 0f;

            for (int i = 0; i < frameDataCount; i++)
            {
                VertexFrameData marker = frameData[i];
                m_MarkerInputs[i] = new MarkerInput
                {
                    Position = marker.WorldPosition,
                    Radius = m_VisibilityMode == MeshEditVisibilityMode.Solid && !marker.SolidVisible ? 0f : marker.MarkerRadius,
                };
                min = Vector3.Min(min, marker.WorldPosition);
                max = Vector3.Max(max, marker.WorldPosition);
                maxRadius = Mathf.Max(maxRadius, marker.MarkerRadius);
            }

            if (m_ComputeInputBuffer != null)
            {
                m_ComputeInputBuffer.SetData(m_MarkerInputs, 0, 0, frameDataCount);
            }

            // DrawMeshInstancedIndirect still performs bounds culling even
            // though instance positions live in a GPU buffer.
            Vector3 boundsPadding = Vector3.one * Mathf.Max(maxRadius * 2f, 0.5f);
            min -= boundsPadding;
            max += boundsPadding;
            m_Bounds = new Bounds((min + max) * 0.5f, max - min);

            if (frameDataCount != m_MarkerCount)
            {
                UpdateArgsBuffer(frameDataCount);
            }

            m_MarkerCount = frameDataCount;
            m_Active = true;
            m_InstanceDataDirty = true;
        }

        public void UpdateVisualStates(VertexFrameData[] frameData, int frameDataCount, ProceduralObjectToolSystem toolSystem)
        {
            if (!IsAvailable || frameData == null || frameDataCount <= 0 || toolSystem == null)
            {
                return;
            }

            EnsureCapacity(frameDataCount);
            if (m_ComputeVisualStateBuffer == null)
            {
                return;
            }

            for (int i = 0; i < frameDataCount; i++)
            {
                VertexMarkerVisualState visualState = VertexMarkerVisualStateUtility.GetState(toolSystem, frameData[i].Vertex);
                m_MarkerVisualStates[i] = (uint)visualState;
            }

            m_ComputeVisualStateBuffer.SetData(m_MarkerVisualStates, 0, 0, frameDataCount);

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

            ReleaseInstanceBuffer();

            if (m_ComputeBundle != null)
            {
                m_ComputeBundle.Unload(true);
                m_ComputeBundle = null;
                m_ComputeShader = null;
                m_ComputeKernel = -1;
            }

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
            OverlayConfigurationPrefab overlayConfiguration = m_PrefabSystem.GetSingletonPrefab<OverlayConfigurationPrefab>(m_SettingsQuery);

            if (overlayConfiguration == null || overlayConfiguration.m_SolidObjectMaterial == null)
            {
                Mod.log.Error("[PO Vertex] GPU marker renderer could not resolve " + "OverlayConfigurationPrefab solid-object material. " + "Gizmo marker fallback will be used.");
                return;
            }

            // Reuse the game's overlay material because its CustomMeshdData
            // layout is what the compute shader writes into m_InstanceBuffer.
            m_Mesh = CreateRingMesh();
            m_Material = new Material(overlayConfiguration.m_SolidObjectMaterial)
            {
                name = "PO2 Vertex Marker Renderer",
            };

            m_ArgsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
            m_ArgsBuffer.name = "PO2 vertex marker indirect args";
            UpdateArgsBuffer(0);

            LoadComputeResources();
            EnsureCapacity(16);
        }

        private void LoadComputeResources()
        {
            if (string.IsNullOrWhiteSpace(Mod.ModRootPath))
            {
                Mod.log.Error("[PO Vertex] GPU marker renderer could not " + "resolve the mod root path. Gizmo marker fallback " + "will be used.");
                return;
            }

            string bundlePath = Path.Combine(Mod.ModRootPath, kComputeBundleRelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(bundlePath))
            {
                Mod.log.Error($"[PO Vertex] GPU marker compute bundle was not found at " + $"'{bundlePath}'. Gizmo marker fallback will be " + "used.");
                return;
            }

            m_ComputeBundle = AssetBundle.LoadFromFile(bundlePath);
            if (m_ComputeBundle == null)
            {
                Mod.log.Error($"[PO Vertex] Failed to load GPU marker compute bundle " + $"'{bundlePath}'. Gizmo marker fallback will be " + "used.");
                return;
            }

            // The bundle contains separate compute assets for markers and visibility.
            m_ComputeShader = m_ComputeBundle.LoadAsset<ComputeShader>("assets/po2vertexmarkerinstances.compute");
            if (m_ComputeShader == null)
            {
                Mod.log.Error("[PO Vertex] Marker compute asset is missing. Gizmo marker fallback will be used.");
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
                Mod.log.Error($"[PO Vertex] GPU marker compute kernel " + $"'{kComputeKernelName}' could not be resolved: " + $"{exception.Message}. Gizmo marker fallback will be " + "used.");
                m_ComputeShader = null;
                m_ComputeKernel = -1;
                return;
            }
        }

        private void EnsureCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= m_InstanceCapacity && m_InstanceBuffer != null && m_MarkerInputs != null && (m_ComputeShader == null || (m_ComputeInputBuffer != null && m_ComputeVisualStateBuffer != null)))
            {
                return;
            }

            // All CPU/GPU arrays share one capacity and index space. Grow them
            // together or position and visual state will silently disagree.
            int newCapacity = Mathf.Max(16, m_InstanceCapacity);
            while (newCapacity < requiredCapacity)
            {
                newCapacity *= 2;
            }

            ReleaseInstanceBuffer();

            m_InstanceBuffer = new ComputeBuffer(newCapacity, m_InstanceStride, ComputeBufferType.Structured);
            m_InstanceBuffer.name = "PO2 vertex marker instances";
            m_Material?.SetBuffer(s_CustomMeshBufferId, m_InstanceBuffer);

            if (m_ComputeShader != null)
            {
                m_ComputeInputBuffer = new ComputeBuffer(newCapacity, Marshal.SizeOf<MarkerInput>(), ComputeBufferType.Structured);
                m_ComputeInputBuffer.name = "PO2 vertex marker compute inputs";

                m_ComputeVisualStateBuffer = new ComputeBuffer(newCapacity, sizeof(uint), ComputeBufferType.Structured);
                m_ComputeVisualStateBuffer.name = "PO2 vertex marker visual states";
            }

            Array.Resize(ref m_MarkerInputs, newCapacity);
            Array.Resize(ref m_MarkerVisualStates, newCapacity);
            m_InstanceCapacity = newCapacity;
        }

        private void ReleaseInstanceBuffer()
        {
            if (m_InstanceBuffer != null)
            {
                m_InstanceBuffer.Release();
                m_InstanceBuffer = null;
            }

            if (m_ComputeInputBuffer != null)
            {
                m_ComputeInputBuffer.Release();
                m_ComputeInputBuffer = null;
            }

            if (m_ComputeVisualStateBuffer != null)
            {
                m_ComputeVisualStateBuffer.Release();
                m_ComputeVisualStateBuffer = null;
            }

            m_InstanceCapacity = 0;
            m_HasGeneratedInstances = false;
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
                (uint)Mathf.Max(instanceCount, 0),
                m_Mesh.GetIndexStart(0),
                m_Mesh.GetBaseVertex(0),
                0u,
            };
            m_ArgsBuffer.SetData(args);
        }

        private void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!m_Active || m_MarkerCount <= 0 || m_Mesh == null || m_Material == null || m_InstanceBuffer == null || m_ArgsBuffer == null)
            {
                return;
            }

            // Instance bases are camera-facing, so the compute pass is prepared
            // per eligible camera immediately before its indirect draw.
            for (int i = 0; i < cameras.Count; i++)
            {
                Camera camera = cameras[i];
                if (camera == null || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView))
                {
                    continue;
                }

                PrepareInstancesForCamera(camera);

                Graphics.DrawMeshInstancedIndirect(m_Mesh, 0, m_Material, m_Bounds, m_ArgsBuffer, 0, null, ShadowCastingMode.Off, false, 0, camera);
            }
        }

        private void PrepareInstancesForCamera(Camera camera)
        {
            if (m_ComputeShader == null || m_ComputeKernel < 0 || m_ComputeInputBuffer == null || m_ComputeVisualStateBuffer == null || m_InstanceBuffer == null || m_MarkerCount <= 0)
            {
                return;
            }

            Quaternion cameraRotation = camera.transform.rotation;
            bool cameraRotationChanged = !m_HasGeneratedInstances || 1f - Mathf.Abs(Quaternion.Dot(cameraRotation, m_LastCameraRotation)) > 0.0000001f;

            // Camera position/projection changes arrive through UpdateMarkers.
            // Rotation alone changes the billboard basis built in this method.
            if (!m_InstanceDataDirty && !cameraRotationChanged)
            {
                return;
            }

            Vector3 right = camera.transform.right.normalized;
            Vector3 normal = -camera.transform.forward.normalized;
            Vector3 up = camera.transform.up.normalized;

            m_ComputeShader.SetVector(s_ComputeCameraRightId, new Vector4(right.x, right.y, right.z, 0f));
            m_ComputeShader.SetVector(s_ComputeCameraNormalId, new Vector4(normal.x, normal.y, normal.z, 0f));
            m_ComputeShader.SetVector(s_ComputeCameraUpId, new Vector4(up.x, up.y, up.z, 0f));
            m_ComputeShader.SetInt(s_ComputeMarkerCountId, m_MarkerCount);
            m_ComputeShader.SetInt(s_ComputeCustomMeshTypeId, (int)OverlayRenderSystem.CustomMeshType.Plane);
            SetComputeColor(s_ComputeNormalColorId, VertexMarkerVisualStateUtility.s_NormalColor);
            SetComputeColor(s_ComputeHoveredColorId, VertexMarkerVisualStateUtility.s_HoveredColor);
            SetComputeColor(s_ComputeSelectedColorId, VertexMarkerVisualStateUtility.s_SelectedColor);
            SetComputeColor(s_ComputeSelectedHoveredColorId, VertexMarkerVisualStateUtility.s_SelectedHoveredColor);
            SetComputeColor(s_ComputeAlignmentTargetColorId, VertexMarkerVisualStateUtility.s_AlignmentTargetColor);
            SetComputeColor(s_ComputeSlideTargetColorId, VertexMarkerVisualStateUtility.s_SlideTargetColor);
            m_ComputeShader.SetBuffer(m_ComputeKernel, s_ComputeMarkerInputsId, m_ComputeInputBuffer);
            m_ComputeShader.SetBuffer(m_ComputeKernel, s_ComputeMarkerVisualStatesId, m_ComputeVisualStateBuffer);
            m_ComputeShader.SetBuffer(m_ComputeKernel, s_ComputeMarkerInstancesId, m_InstanceBuffer);

            // Round up because the shader bounds-checks threads past MarkerCount.
            int threadGroupCount = (m_MarkerCount + kComputeThreadGroupSize - 1) / kComputeThreadGroupSize;
            m_ComputeShader.Dispatch(m_ComputeKernel, threadGroupCount, 1, 1);

            m_LastCameraRotation = cameraRotation;
            m_HasGeneratedInstances = true;
            m_InstanceDataDirty = false;
        }

        private void SetComputeColor(int propertyId, Color color)
        {
            Color linearColor = color.linear;
            m_ComputeShader.SetVector(propertyId, new Vector4(linearColor.r, linearColor.g, linearColor.b, linearColor.a));
        }

        private static Mesh CreateRingMesh()
        {
            const int segments = 16;
            const float outerRadius = 1f;
            const float innerRadius = 0.68f;

            Mesh mesh = new Mesh
            {
                name = "PO2 Vertex Marker Renderer Ring",
            };

            // The ring lies in local XZ. Map X to camera right, Z to camera up,
            // and +Y toward the camera so markers stay screen-facing.
            Vector3[] vertices = new Vector3[segments * 2];
            for (int i = 0; i < segments; i++)
            {
                float angle = math.PI * 2f * i / segments;
                float x = math.cos(angle);
                float z = math.sin(angle);

                vertices[i * 2] = new Vector3(x * outerRadius, 0f, z * outerRadius);
                vertices[i * 2 + 1] = new Vector3(x * innerRadius, 0f, z * innerRadius);
            }

            int[] triangles = new int[segments * 12];
            int triangleIndex = 0;

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int outer0 = i * 2;
                int inner0 = outer0 + 1;
                int outer1 = next * 2;
                int inner1 = outer1 + 1;

                triangles[triangleIndex++] = outer0;
                triangles[triangleIndex++] = outer1;
                triangles[triangleIndex++] = inner1;
                triangles[triangleIndex++] = inner1;
                triangles[triangleIndex++] = inner0;
                triangles[triangleIndex++] = outer0;

                triangles[triangleIndex++] = outer0;
                triangles[triangleIndex++] = inner0;
                triangles[triangleIndex++] = inner1;
                triangles[triangleIndex++] = inner1;
                triangles[triangleIndex++] = outer1;
                triangles[triangleIndex++] = outer0;
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}