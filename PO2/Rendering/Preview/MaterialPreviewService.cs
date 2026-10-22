using System;
using System.IO;
using Colossal.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using PO2.Localization;
using PO2.Materials;

namespace PO2.Rendering.Preview
{
    // Preview textures stay outside the material library and object renderer caches.
    // This service releases them with the preview.
    internal sealed class MaterialPreviewService : IDisposable
    {
        private const int kPreviewSize = 384;
        private const int kPreviewLayer = 31;
        private const int kPreviewLayerMask = 1 << kPreviewLayer;
        private const int kPreviewVolumeLayer = 30;
        private const int kPreviewVolumeLayerMask = 1 << kPreviewVolumeLayer;
        private const float kDefaultPreviewExposure = 1.5f;
        private const float kDefaultIndirectDiffuse = 0f;
        private const float kDefaultReflectionLighting = 0f;
        private const float kDefaultKeyLightIntensity = 1600f;
        private const float kDefaultFillLightIntensity = 850f;

        // GameFace can keep an old screencapture URL cached after its texture is
        // gone. A new generation makes each recreated transport a distinct URL.
        private static int s_Generation;

        private GameObject m_Root;
        private GameObject m_CameraObject;
        private GameObject m_CubeObject;
        private GameObject m_KeyLightObject;
        private GameObject m_FillLightObject;
        private GameObject m_VolumeObject;
        private Camera m_Camera;
        private HDAdditionalCameraData m_CameraData;
        private Volume m_PreviewVolume;
        private VolumeProfile m_PreviewVolumeProfile;
        private RenderTexture m_RenderTexture;
        private Mesh m_CubeMesh;
        private Material m_PreviewMaterial;
        private Texture2D m_BaseColorTexture;
        private Texture2D m_NormalTexture;
        private Texture2D m_MaskMapTexture;
        private Texture2D m_ControlMaskTexture;
        private Exposure m_Exposure;
        private IndirectLightingController m_IndirectLighting;
        private Light m_KeyLight;
        private Light m_FillLight;
        private float m_PreviewExposure = kDefaultPreviewExposure;
        private float m_IndirectDiffuse = kDefaultIndirectDiffuse;
        private float m_ReflectionLighting = kDefaultReflectionLighting;
        private float m_KeyLightIntensity = kDefaultKeyLightIntensity;
        private float m_FillLightIntensity = kDefaultFillLightIntensity;
        private Light m_SunLight;
        private Light m_MoonLight;
        private Light m_NightLight;
        private bool m_SunLightWasEnabled;
        private bool m_MoonLightWasEnabled;
        private bool m_NightLightWasEnabled;
        private bool m_ExternalDirectionalLightsSuppressed;
        private bool m_RenderCallbacksRegistered;

        public string Url { get; private set; } = string.Empty;
        public LocalizedText ErrorMessage { get; private set; } = LocalizedText.Empty;
        public bool IsActive => !string.IsNullOrWhiteSpace(Url);
        public float PreviewExposure => m_PreviewExposure;
        public float IndirectDiffuse => m_IndirectDiffuse;
        public float ReflectionLighting => m_ReflectionLighting;
        public float KeyLightIntensity => m_KeyLightIntensity;
        public float FillLightIntensity => m_FillLightIntensity;

        public void AdjustPreviewExposure(float delta)
        {
            m_PreviewExposure = Mathf.Clamp(m_PreviewExposure + delta, -5f, 15f);
            ApplyLightingTuning();
        }

        public void AdjustIndirectDiffuse(float delta)
        {
            m_IndirectDiffuse = Mathf.Clamp(m_IndirectDiffuse + delta, 0f, 2f);
            ApplyLightingTuning();
        }

        public void AdjustReflectionLighting(float delta)
        {
            m_ReflectionLighting = Mathf.Clamp(m_ReflectionLighting + delta, 0f, 2f);
            ApplyLightingTuning();
        }

        public void AdjustKeyLightIntensity(float delta)
        {
            m_KeyLightIntensity = Mathf.Clamp(m_KeyLightIntensity + delta, 0f, 5000f);
            ApplyLightingTuning();
        }

        public void AdjustFillLightIntensity(float delta)
        {
            m_FillLightIntensity = Mathf.Clamp(m_FillLightIntensity + delta, 0f, 5000f);
            ApplyLightingTuning();
        }

        public void ResetLightingTuning()
        {
            m_PreviewExposure = kDefaultPreviewExposure;
            m_IndirectDiffuse = kDefaultIndirectDiffuse;
            m_ReflectionLighting = kDefaultReflectionLighting;
            m_KeyLightIntensity = kDefaultKeyLightIntensity;
            m_FillLightIntensity = kDefaultFillLightIntensity;
            ApplyLightingTuning();
        }

        public void UpdateMaterialScalars(float metallic, float smoothness, float coatStrength)
        {
            if (m_PreviewMaterial == null)
            {
                return;
            }

            GenericMaterialFactory.ApplyScalars(m_PreviewMaterial, metallic, smoothness, coatStrength);
        }

        public void UpdateMaterialMapping(float physicalWidthMeters, float physicalHeightMeters, TextureMappingMode mappingMode)
        {
            if (m_CubeMesh == null)
            {
                return;
            }

            float uScale = 1f;
            float vScale = 1f;

            if (mappingMode == TextureMappingMode.Repeat)
            {
                // The preview cube is two meters wide on every face.
                // Scaling UVs by that span shows the same physical tile size used in-world.
                uScale = 2f / Mathf.Max(0.1f, physicalWidthMeters);
                vScale = 2f / Mathf.Max(0.1f, physicalHeightMeters);
            }

            Vector2[] uv = BuildCubeUVs(uScale, vScale);
            m_CubeMesh.uv = uv;
            m_CubeMesh.RecalculateTangents();
        }

        public bool ShowDefinition(MaterialDefinition definition)
        {
            if (definition == null)
            {
                Hide();
                DestroyCandidateMaterialResources();
                ErrorMessage = LocalizedText.Empty;
                return false;
            }

            if (definition.ShaderKind != MaterialShaderKind.DefaultObject)
            {
                Hide();
                DestroyCandidateMaterialResources();
                ErrorMessage = LocalizedText.For("PO2.MaterialPreview.Error.UnsupportedMaterial");
                return false;
            }

            try
            {
                EnsureTransport();
                ReplaceDefinitionMaterial(definition);
                m_Camera.enabled = true;
                ErrorMessage = LocalizedText.Empty;
                return true;
            }
            catch (Exception exception)
            {
                ErrorMessage = LocalizedText.FromException(exception);
                Mod.log.Error($"[PO Material Preview] Failed to preview library definition " + $"'{definition.DisplayName}': {exception}");
                DestroyCandidateMaterialResources();
                Hide();
                return false;
            }
        }

        public bool ShowCandidate(MaterialImportCandidate candidate, MaterialAuthoringState authoringState)
        {
            if (candidate == null)
            {
                Hide();
                DestroyCandidateMaterialResources();
                ErrorMessage = LocalizedText.Empty;
                return false;
            }

            try
            {
                EnsureTransport();
                ReplaceCandidateMaterial(candidate, authoringState);
                m_Camera.enabled = true;
                ErrorMessage = LocalizedText.Empty;
                return true;
            }
            catch (Exception exception)
            {
                ErrorMessage = LocalizedText.FromException(exception);
                Mod.log.Error($"[PO Material Preview] Failed to preview " + $"'{candidate.SuggestedDisplayName}': {exception}");
                DestroyCandidateMaterialResources();
                Hide();
                return false;
            }
        }

        public void Hide()
        {
            Url = string.Empty;
            if (m_Camera != null)
            {
                m_Camera.enabled = false;
            }
        }

        public void Dispose()
        {
            // Gameface owns a registration for this RenderTexture in addition to Unity's objects.
            // Drop that registration before releasing the texture.
            Hide();
            RestoreExternalDirectionalLights();
            UnregisterRenderCallbacks();
            RemoveUserImageRegistration();

            if (m_Camera != null)
            {
                m_Camera.targetTexture = null;
            }

            DestroyCandidateMaterialResources();

            if (m_RenderTexture != null)
            {
                if (m_RenderTexture.IsCreated())
                {
                    m_RenderTexture.Release();
                }

                UnityEngine.Object.Destroy(m_RenderTexture);
            }

            if (m_CubeMesh != null)
            {
                UnityEngine.Object.Destroy(m_CubeMesh);
            }

            DestroyPreviewEnvironment();

            if (m_Root != null)
            {
                UnityEngine.Object.Destroy(m_Root);
            }
            else
            {
                DestroyObject(m_CubeObject);
                DestroyObject(m_CameraObject);
                DestroyObject(m_KeyLightObject);
                DestroyObject(m_FillLightObject);
            }

            m_Camera = null;
            m_CameraData = null;
            m_RenderTexture = null;
            m_CubeMesh = null;
            m_CubeObject = null;
            m_CameraObject = null;
            m_KeyLightObject = null;
            m_FillLightObject = null;
            m_KeyLight = null;
            m_FillLight = null;
            m_VolumeObject = null;
            m_PreviewVolume = null;
            m_PreviewVolumeProfile = null;
            m_SunLight = null;
            m_MoonLight = null;
            m_NightLight = null;
            m_ExternalDirectionalLightsSuppressed = false;
            m_Root = null;
            ErrorMessage = LocalizedText.Empty;
        }

        private void EnsureTransport()
        {
            // Material changes can reuse the camera and RenderTexture.
            // Rebuilding the transport on every field edit causes stale GameFace image entries.
            if (m_RenderTexture != null && m_Camera != null && m_CubeObject != null)
            {
                if (string.IsNullOrWhiteSpace(Url))
                {
                    Url = BuildUrl(m_RenderTexture.name, s_Generation);
                }

                return;
            }

            Dispose();

            int generation = ++s_Generation;
            string targetName = $"PO2MaterialPreview_{generation}";

            m_Root = new GameObject("PO2 Material Preview")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = kPreviewLayer,
            };
            // Keep preview geometry away from the map as a second line of defense.
            // Its private layer and camera mask are still what isolate rendering.
            m_Root.transform.position = new Vector3(0f, -100000f, 0f);

            m_CubeObject = new GameObject("Preview Cube")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = kPreviewLayer,
            };
            m_CubeObject.transform.SetParent(m_Root.transform, false);
            m_CubeObject.transform.localPosition = Vector3.zero;
            m_CubeObject.transform.localRotation = Quaternion.Euler(22f, 35f, 0f);

            MeshFilter filter = m_CubeObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = m_CubeObject.AddComponent<MeshRenderer>();
            m_CubeMesh = CreateCubeMesh();
            filter.sharedMesh = m_CubeMesh;
            renderer.sharedMaterial = null;

            m_CameraObject = new GameObject("PO2 Preview Camera")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = kPreviewLayer,
            };
            m_CameraObject.transform.SetParent(m_Root.transform, false);
            m_CameraObject.transform.localPosition = new Vector3(0f, 0.15f, -6.6f);
            m_CameraObject.transform.localRotation = Quaternion.identity;
            m_CameraObject.tag = "Player";

            m_Camera = m_CameraObject.AddComponent<Camera>();
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = new Color(0.055f, 0.06f, 0.07f, 1f);
            m_Camera.fieldOfView = 32f;
            m_Camera.nearClipPlane = 0.1f;
            m_Camera.farClipPlane = 20f;
            m_Camera.allowHDR = true;
            m_Camera.allowMSAA = true;
            m_Camera.cullingMask = kPreviewLayerMask;

            m_CameraData = m_CameraObject.GetComponent<HDAdditionalCameraData>();
            if (m_CameraData == null)
            {
                m_CameraData = m_CameraObject.AddComponent<HDAdditionalCameraData>();
            }

            m_CameraData.volumeLayerMask = kPreviewVolumeLayerMask;

            CreatePreviewEnvironment();
            CreatePreviewLights();
            RegisterRenderCallbacks();

            m_RenderTexture = new RenderTexture(kPreviewSize, kPreviewSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = targetName,
                hideFlags = HideFlags.HideAndDontSave,
            };
            m_RenderTexture.Create();
            m_Camera.targetTexture = m_RenderTexture;
            m_Camera.enabled = false;

            Url = BuildUrl(targetName, generation);
        }

        private void ReplaceDefinitionMaterial(MaterialDefinition definition)
        {
            DestroyCandidateMaterialResources();

            Texture2D baseColor = null;
            Texture2D normal = null;
            Texture2D maskMap = null;
            Texture2D controlMask = null;
            Material material = null;

            try
            {
                // Load a private copy of every texture.
                // Library/renderer caches have different owners and must not be destroyed with the preview.
                baseColor = LoadTexture(definition.BaseColorPath, linear: false, LocalizedText.For("PO2.MaterialPreview.TextureRole.BaseColor"));
                normal = string.IsNullOrWhiteSpace(definition.NormalPath) ? null : LoadTexture(definition.NormalPath, linear: true, LocalizedText.For("PO2.MaterialPreview.TextureRole.Normal"));
                maskMap = string.IsNullOrWhiteSpace(definition.MaskMapPath) ? null : LoadTexture(definition.MaskMapPath, linear: true, LocalizedText.For("PO2.MaterialPreview.TextureRole.MaskMap"));
                controlMask = string.IsNullOrWhiteSpace(definition.ControlMaskPath) ? null : LoadTexture(definition.ControlMaskPath, linear: true, LocalizedText.For("PO2.MaterialPreview.TextureRole.ControlMask"));

                material = GenericMaterialFactory.Create($"PO2 Preview {definition.DisplayName}", baseColor, normal, maskMap, controlMask, definition.Metallic, definition.Smoothness, definition.CoatStrength);

                if (material == null)
                {
                    throw (LocalizedText.For("PO2.MaterialPreview.Error.MaterialCreation")).AsException(localizedMessage => new InvalidOperationException(localizedMessage));
                }

                material.hideFlags = HideFlags.HideAndDontSave;

                MeshRenderer renderer = m_CubeObject.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;

                m_BaseColorTexture = baseColor;
                m_NormalTexture = normal;
                m_MaskMapTexture = maskMap;
                m_ControlMaskTexture = controlMask;
                m_PreviewMaterial = material;

                Vector2 tileSize = definition.HasPhysicalTileSize ? definition.TileSizeMeters : new Vector2(MaterialAuthoringState.kDefaultPhysicalSizeMeters, MaterialAuthoringState.kDefaultPhysicalSizeMeters);

                UpdateMaterialMapping(tileSize.x, tileSize.y, definition.DefaultMappingMode);
            }
            catch
            {
                // Ownership is only transferred to the fields after the whole material is ready.
                // Until then, clean up these locals directly.
                if (material != null)
                {
                    UnityEngine.Object.Destroy(material);
                }

                if (baseColor != null)
                {
                    UnityEngine.Object.Destroy(baseColor);
                }

                if (normal != null)
                {
                    UnityEngine.Object.Destroy(normal);
                }

                if (maskMap != null)
                {
                    UnityEngine.Object.Destroy(maskMap);
                }

                if (controlMask != null)
                {
                    UnityEngine.Object.Destroy(controlMask);
                }

                throw;
            }
        }

        private void ReplaceCandidateMaterial(MaterialImportCandidate candidate, MaterialAuthoringState authoringState)
        {
            DestroyCandidateMaterialResources();

            Texture2D baseColor = null;
            Texture2D normal = null;
            Material material = null;

            try
            {
                // Candidates point at loose files and do not exist in the library
                // yet, so the preview builds the same generic material by hand.
                baseColor = LoadTexture(candidate.BaseColorPath, linear: false, LocalizedText.For("PO2.MaterialPreview.TextureRole.BaseColor"));
                normal = string.IsNullOrWhiteSpace(candidate.NormalPath) ? null : LoadTexture(candidate.NormalPath, linear: true, LocalizedText.For("PO2.MaterialPreview.TextureRole.Normal"));

                material = GenericMaterialFactory.Create($"PO2 Preview {candidate.SuggestedDisplayName}", baseColor, normal, null, null, authoringState?.Metallic ?? 0f, authoringState?.Smoothness ?? 0f, authoringState?.CoatStrength ?? 0f);

                if (material == null)
                {
                    throw (LocalizedText.For("PO2.MaterialPreview.Error.MaterialCreation")).AsException(localizedMessage => new InvalidOperationException(localizedMessage));
                }

                material.hideFlags = HideFlags.HideAndDontSave;

                MeshRenderer renderer = m_CubeObject.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;

                m_BaseColorTexture = baseColor;
                m_NormalTexture = normal;
                m_PreviewMaterial = material;

                UpdateMaterialMapping(authoringState?.PhysicalWidthMeters ?? MaterialAuthoringState.kDefaultPhysicalSizeMeters, authoringState?.PhysicalHeightMeters ?? MaterialAuthoringState.kDefaultPhysicalSizeMeters, authoringState?.MappingMode ?? TextureMappingMode.Repeat);
            }
            catch
            {
                // Construction failed before these resources were stored in the service fields.
                if (material != null)
                {
                    UnityEngine.Object.Destroy(material);
                }

                if (baseColor != null)
                {
                    UnityEngine.Object.Destroy(baseColor);
                }

                if (normal != null)
                {
                    UnityEngine.Object.Destroy(normal);
                }

                throw;
            }
        }

        private void CreatePreviewEnvironment()
        {
            // A local HDRP volume keeps exposure and indirect lighting stable as
            // the map changes time of day or weather behind the material editor.
            m_VolumeObject = new GameObject("PO2 Preview Volume")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = kPreviewVolumeLayer,
            };
            m_VolumeObject.transform.SetParent(m_Root.transform, false);
            m_VolumeObject.transform.localPosition = Vector3.zero;

            BoxCollider volumeBounds = m_VolumeObject.AddComponent<BoxCollider>();
            volumeBounds.isTrigger = true;
            volumeBounds.size = new Vector3(40f, 40f, 40f);

            m_PreviewVolume = m_VolumeObject.AddComponent<Volume>();
            m_PreviewVolume.isGlobal = false;
            m_PreviewVolume.priority = 10000f;
            m_PreviewVolume.weight = 1f;
            m_PreviewVolume.blendDistance = 0f;

            m_PreviewVolumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            m_PreviewVolumeProfile.name = "PO2 Preview Volume Profile";
            m_PreviewVolumeProfile.hideFlags = HideFlags.HideAndDontSave;
            m_PreviewVolume.sharedProfile = m_PreviewVolumeProfile;

            m_Exposure = m_PreviewVolumeProfile.Add<Exposure>();
            m_Exposure.active = true;
            m_Exposure.mode.Override(ExposureMode.Fixed);
            m_Exposure.fixedExposure.Override(m_PreviewExposure);

            ColorAdjustments colorAdjustments = m_PreviewVolumeProfile.Add<ColorAdjustments>();
            colorAdjustments.active = true;
            colorAdjustments.postExposure.Override(0f);
            colorAdjustments.contrast.Override(0f);
            colorAdjustments.colorFilter.Override(Color.white);

            m_IndirectLighting = m_PreviewVolumeProfile.Add<IndirectLightingController>();
            m_IndirectLighting.active = true;
            m_IndirectLighting.indirectDiffuseLightingMultiplier.Override(m_IndirectDiffuse);
            m_IndirectLighting.reflectionLightingMultiplier.Override(m_ReflectionLighting);
        }

        private void DestroyPreviewEnvironment()
        {
            if (m_PreviewVolume != null)
            {
                m_PreviewVolume.sharedProfile = null;
            }

            if (m_PreviewVolumeProfile != null)
            {
                UnityEngine.Object.Destroy(m_PreviewVolumeProfile);
            }

            if (m_VolumeObject != null)
            {
                UnityEngine.Object.Destroy(m_VolumeObject);
            }

            m_PreviewVolume = null;
            m_PreviewVolumeProfile = null;
            m_Exposure = null;
            m_IndirectLighting = null;
            m_VolumeObject = null;
        }

        private void CreatePreviewLights()
        {
            m_KeyLightObject = CreateLightObject("Preview Key Light", new Vector3(-4.25f, 4.0f, -4.75f), m_KeyLightIntensity, 12f, out m_KeyLight);

            m_FillLightObject = CreateLightObject("Preview Fill Light", new Vector3(4.5f, 2.25f, -2.5f), m_FillLightIntensity, 12f, out m_FillLight);
        }

        private GameObject CreateLightObject(string name, Vector3 localPosition, float intensity, float range, out Light createdLight)
        {
            GameObject lightObject = new GameObject(name)
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = kPreviewLayer,
            };
            lightObject.transform.SetParent(m_Root.transform, false);
            lightObject.transform.localPosition = localPosition;

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Color.white;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.Soft;
            light.cullingMask = kPreviewLayerMask;
            createdLight = light;
            return lightObject;
        }

        private void ApplyLightingTuning()
        {
            if (m_Exposure != null)
            {
                m_Exposure.fixedExposure.Override(m_PreviewExposure);
            }

            if (m_IndirectLighting != null)
            {
                m_IndirectLighting.indirectDiffuseLightingMultiplier.Override(m_IndirectDiffuse);
                m_IndirectLighting.reflectionLightingMultiplier.Override(m_ReflectionLighting);
            }

            if (m_KeyLight != null)
            {
                m_KeyLight.intensity = m_KeyLightIntensity;
            }

            if (m_FillLight != null)
            {
                m_FillLight.intensity = m_FillLightIntensity;
            }
        }

        private void RegisterRenderCallbacks()
        {
            if (m_RenderCallbacksRegistered)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            m_RenderCallbacksRegistered = true;
        }

        private void UnregisterRenderCallbacks()
        {
            if (!m_RenderCallbacksRegistered)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            m_RenderCallbacksRegistered = false;
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != m_Camera)
            {
                return;
            }

            // CS2's sun/moon lights do not respect this camera's culling mask for lighting.
            // Disable them only around the preview camera render.
            SuppressExternalDirectionalLights();
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != m_Camera)
            {
                return;
            }

            RestoreExternalDirectionalLights();
        }

        private void SuppressExternalDirectionalLights()
        {
            if (m_ExternalDirectionalLightsSuppressed)
            {
                return;
            }

            m_SunLight = FindTaggedLight("SunLight", m_SunLight);
            m_MoonLight = FindTaggedLight("MoonLight", m_MoonLight);
            m_NightLight = FindTaggedLight("NightLight", m_NightLight);

            m_SunLightWasEnabled = DisableLightTemporarily(m_SunLight);
            m_MoonLightWasEnabled = DisableLightTemporarily(m_MoonLight);
            m_NightLightWasEnabled = DisableLightTemporarily(m_NightLight);
            m_ExternalDirectionalLightsSuppressed = true;
        }

        private void RestoreExternalDirectionalLights()
        {
            if (!m_ExternalDirectionalLightsSuppressed)
            {
                return;
            }

            RestoreLight(m_SunLight, m_SunLightWasEnabled);
            RestoreLight(m_MoonLight, m_MoonLightWasEnabled);
            RestoreLight(m_NightLight, m_NightLightWasEnabled);
            m_ExternalDirectionalLightsSuppressed = false;
        }

        private static Light FindTaggedLight(string tag, Light cached)
        {
            if (cached != null)
            {
                return cached;
            }

            GameObject gameObject = GameObject.FindGameObjectWithTag(tag);
            return gameObject != null ? gameObject.GetComponent<Light>() : null;
        }

        private static bool DisableLightTemporarily(Light light)
        {
            if (light == null)
            {
                return false;
            }

            bool wasEnabled = light.enabled;
            if (wasEnabled)
            {
                light.enabled = false;
            }

            return wasEnabled;
        }

        private static void RestoreLight(Light light, bool wasEnabled)
        {
            if (light != null)
            {
                light.enabled = wasEnabled;
            }
        }

        private void DestroyCandidateMaterialResources()
        {
            // Clear the renderer first so it cannot submit a material while that
            // material or one of its textures is being destroyed.
            if (m_CubeObject != null)
            {
                MeshRenderer renderer = m_CubeObject.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = null;
                }
            }

            if (m_PreviewMaterial != null)
            {
                UnityEngine.Object.Destroy(m_PreviewMaterial);
            }

            if (m_BaseColorTexture != null)
            {
                UnityEngine.Object.Destroy(m_BaseColorTexture);
            }

            if (m_NormalTexture != null)
            {
                UnityEngine.Object.Destroy(m_NormalTexture);
            }

            if (m_MaskMapTexture != null)
            {
                UnityEngine.Object.Destroy(m_MaskMapTexture);
            }

            if (m_ControlMaskTexture != null)
            {
                UnityEngine.Object.Destroy(m_ControlMaskTexture);
            }

            m_PreviewMaterial = null;
            m_BaseColorTexture = null;
            m_NormalTexture = null;
            m_MaskMapTexture = null;
            m_ControlMaskTexture = null;
        }

        private void RemoveUserImageRegistration()
        {
            if (m_RenderTexture == null)
            {
                return;
            }

            try
            {
                UserImagesManager userImagesManager = UIManager.defaultUISystem?.userImagesManager;
                userImagesManager?.RemoveUserImageData(m_RenderTexture);
            }
            catch (Exception exception)
            {
                Mod.log.Error($"[PO Material Preview] Failed removing preview user image " + $"registration: {exception}");
            }
        }

        private static string BuildUrl(string targetName, int generation)
        {
            // liveView keeps GameFace reading the RenderTexture instead of taking a one-frame copy.
            // The fragment is only a cache-busting generation.
            return $"screencapture://player/{targetName}" + $"?width={kPreviewSize}&height={kPreviewSize}" + $"&liveView=true&alloc=Unmanaged#{generation}";
        }

        private static Texture2D LoadTexture(string texturePath, bool linear, LocalizedText role)
        {
            if (string.IsNullOrWhiteSpace(texturePath))
            {
                return null;
            }

            string fullPath = Path.GetFullPath(texturePath);
            if (!File.Exists(fullPath))
            {
                throw (LocalizedText.For("PO2.MaterialPreview.Error.TextureMissing", ("role", role))).AsException(localizedMessage => new FileNotFoundException(localizedMessage, fullPath));
            }

            byte[] bytes = File.ReadAllBytes(fullPath);
            // Base color is sRGB; data textures are linear.
            // That choice has to be made when Texture2D is created before LoadImage replaces its pixels.
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear)
            {
                name = $"PO2 Preview {role}: {Path.GetFileNameWithoutExtension(fullPath)}",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 16,
                hideFlags = HideFlags.HideAndDontSave,
            };

            if (!ImageConversion.LoadImage(texture, bytes, false))
            {
                UnityEngine.Object.Destroy(texture);
                throw (LocalizedText.For("PO2.MaterialPreview.Error.TextureDecode", ("role", role), ("path", fullPath))).AsException(localizedMessage => new InvalidOperationException(localizedMessage));
            }

            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 16;
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return texture;
        }

        private static Mesh CreateCubeMesh()
        {
            const float h = 1f;

            // Each face gets separate vertices so its UV square and hard normal do not bleed into the neighboring face.
            Vector3[] vertices =
            {
                new Vector3(-h, -h, -h), new Vector3(h, -h, -h),
                new Vector3(h, h, -h), new Vector3(-h, h, -h),
                new Vector3(h, -h, h), new Vector3(-h, -h, h),
                new Vector3(-h, h, h), new Vector3(h, h, h),
                new Vector3(-h, -h, h), new Vector3(-h, -h, -h),
                new Vector3(-h, h, -h), new Vector3(-h, h, h),
                new Vector3(h, -h, -h), new Vector3(h, -h, h),
                new Vector3(h, h, h), new Vector3(h, h, -h),
                new Vector3(-h, h, -h), new Vector3(h, h, -h),
                new Vector3(h, h, h), new Vector3(-h, h, h),
                new Vector3(-h, -h, h), new Vector3(h, -h, h),
                new Vector3(h, -h, -h), new Vector3(-h, -h, -h),
            };

            int[] triangles =
            {
                0, 2, 1, 0, 3, 2,
                4, 6, 5, 4, 7, 6,
                8, 10, 9, 8, 11, 10,
                12, 14, 13, 12, 15, 14,
                16, 18, 17, 16, 19, 18,
                20, 22, 21, 20, 23, 22,
            };

            Vector3[] normals =
            {
                Vector3.back, Vector3.back, Vector3.back, Vector3.back,
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.left, Vector3.left, Vector3.left, Vector3.left,
                Vector3.right, Vector3.right, Vector3.right, Vector3.right,
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                Vector3.down, Vector3.down, Vector3.down, Vector3.down,
            };

            Vector2[] uv = BuildCubeUVs(1f, 1f);

            Mesh mesh = new Mesh
            {
                name = "PO2 Material Preview Cube",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = vertices,
                normals = normals,
                uv = uv,
                triangles = triangles,
            };
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector2[] BuildCubeUVs(float uScale, float vScale)
        {
            Vector2[] uv = new Vector2[24];
            for (int face = 0; face < 6; face++)
            {
                int index = face * 4;
                uv[index] = new Vector2(0f, 0f);
                uv[index + 1] = new Vector2(uScale, 0f);
                uv[index + 2] = new Vector2(uScale, vScale);
                uv[index + 3] = new Vector2(0f, vScale);
            }

            return uv;
        }

        private static void DestroyObject(GameObject gameObject)
        {
            if (gameObject != null)
            {
                UnityEngine.Object.Destroy(gameObject);
            }
        }
    }
}