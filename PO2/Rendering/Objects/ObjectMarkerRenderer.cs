using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Game.Prefabs;
using UnityEngine;
using UnityEngine.Rendering;

namespace PO2.Rendering.Objects
{
    // Borrow the game's textured icon material, not its notification entities.
    // Its vertex shader handles billboarding; one indirect draw carries the batch.
    internal sealed class ObjectMarkerRenderer : IDisposable
    {
        private static readonly int s_InstanceBufferId = Shader.PropertyToID("instanceBuffer");
        private readonly ObjectMarkerFrame m_Frame = new ObjectMarkerFrame();
        private readonly uint[] m_Args = new uint[5];
        private Mesh m_Mesh;
        private Material m_Material;
        private Texture2DArray m_Texture;
        private ComputeBuffer m_Instances, m_Arguments;
        private int m_Capacity;
        private IReadOnlyList<Vector3> m_Anchors;
        private uint m_Version;
        private bool m_Active, m_Failed;

        internal ObjectMarkerRenderer(IconConfigurationPrefab configuration)
        {
            try
            {
                CreateResources(configuration);
                RenderPipelineManager.beginContextRendering += Render;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void Update(ObjectMarkerCache cache)
        {
            m_Anchors = cache.Anchors;
            m_Version = cache.Version;
            m_Active = m_Anchors.Count > 0;
        }

        internal void Hide()
        {
            m_Active = false;
            m_Anchors = null;
            m_Frame.Invalidate();
        }

        private void CreateResources(IconConfigurationPrefab configuration)
        {
            if (configuration == null || configuration.m_Material == null)
                throw new InvalidOperationException("The game's icon material is unavailable.");
            m_Material = new Material(configuration.m_Material) { name = "PO2 object markers" };
            Texture2D image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                using (Stream stream = typeof(ObjectMarkerRenderer).Assembly.GetManifestResourceStream("PO2.ObjectMarker.png"))
                {
                    if (stream == null) throw new InvalidOperationException("The PO marker image is missing.");
                    using (var bytes = new MemoryStream())
                    {
                        stream.CopyTo(bytes);
                        if (!ImageConversion.LoadImage(image, bytes.ToArray(), false))
                            throw new InvalidOperationException("The PO marker image could not be read.");
                    }
                }

                m_Texture = new Texture2DArray(image.width, image.height, 1, TextureFormat.RGBA32, false)
                {
                    name = "PO2 object marker texture", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
                m_Texture.SetPixels32(image.GetPixels32(), 0);
                m_Texture.Apply(false, true);
                m_Material.mainTexture = m_Texture;
            }
            finally
            {
                UnityEngine.Object.Destroy(image);
            }

            // Match NotificationIconRenderSystem's quad and UV layout.
            m_Mesh = new Mesh { name = "PO2 object marker quad" };
            m_Mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0) };
            m_Mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            m_Mesh.triangles = new[] { 0, 1, 2, 2, 3, 0 };
            m_Mesh.UploadMeshData(true);
            m_Args[0] = m_Mesh.GetIndexCount(0);
            m_Args[2] = m_Mesh.GetIndexStart(0);
            m_Args[3] = m_Mesh.GetBaseVertex(0);
            m_Arguments = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
            m_Arguments.name = "PO2 object marker draw args";
        }

        private void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!m_Active || m_Failed) return;
            Camera main = Camera.main;
            if (main == null || !cameras.Contains(main)) return;
            try
            {
                Matrix4x4 projection = main.projectionMatrix;
                var transform = main.transform;
                var view = new ObjectMarkerFrame.View
                {
                    ViewProjection = projection * main.worldToCameraMatrix,
                    Position = transform.position, Forward = transform.forward, Up = transform.up,
                    ProjectionY = Mathf.Abs(projection.m11), PixelHeight = main.pixelHeight,
                    // The old badge was 22rem at the game's 1080p UI reference size.
                    IconPixels = 22f * Screen.height / 1080f,
                    Orthographic = main.orthographic
                };
                if (m_Frame.Update(m_Anchors, m_Version, view))
                {
                    if (m_Frame.Count > 0)
                    {
                        EnsureCapacity(m_Frame.Count);
                        m_Instances.SetData(m_Frame.Instances, 0, 0, m_Frame.Count);
                    }

                    m_Args[1] = (uint)m_Frame.Count;
                    m_Arguments.SetData(m_Args);
                }

                if (m_Frame.Count == 0) return;
                Graphics.DrawMeshInstancedIndirect(m_Mesh, 0, m_Material, m_Frame.Bounds, m_Arguments, 0, null, ShadowCastingMode.Off, false, 0, main);
            }
            catch (Exception e)
            {
                // Do not retry a failed graphics allocation every camera frame.
                m_Failed = true;
                m_Active = false;
                Mod.log.Error("[PO Markers] Rendering stopped: " + e.Message);
            }
        }

        private void EnsureCapacity(int count)
        {
            if (m_Instances != null && m_Capacity >= count) return;
            int capacity = Math.Max(16, m_Capacity);
            while (capacity < count) capacity = checked(capacity * 2);
            var next = new ComputeBuffer(capacity, Marshal.SizeOf<Game.Rendering.NotificationIconBufferSystem.InstanceData>());
            next.name = "PO2 object marker instances";
            m_Instances?.Release();
            m_Instances = next;
            m_Capacity = capacity;
            m_Material.SetBuffer(s_InstanceBufferId, m_Instances);
        }

        public void Dispose()
        {
            RenderPipelineManager.beginContextRendering -= Render;
            Hide();
            m_Instances?.Release();
            m_Instances = null;
            m_Arguments?.Release();
            m_Arguments = null;
            if (m_Mesh != null)
            {
                UnityEngine.Object.Destroy(m_Mesh);
                m_Mesh = null;
            }

            if (m_Material != null)
            {
                UnityEngine.Object.Destroy(m_Material);
                m_Material = null;
            }

            if (m_Texture != null)
            {
                UnityEngine.Object.Destroy(m_Texture);
                m_Texture = null;
            }
        }
    }
}