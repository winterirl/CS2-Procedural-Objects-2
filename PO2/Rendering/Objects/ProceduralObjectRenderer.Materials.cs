using MaterialLibrary = PO2.Materials.MaterialLibrary;
using MaterialData = PO2.Materials.MaterialData;
using System;
using System.Collections.Generic;
using System.IO;
using Colossal.IO.AssetDatabase;
using Colossal.IO.AssetDatabase.VirtualTexturing;
using Game.Rendering;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using PO2.Geometry;
using PO2.Materials;
using PO2.Rendering;
using AssetHash128 = Colossal.Hash128;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        private const string kGlassShaderName = "BH/GlsShader";
        private const string kGrassMaterialTemplateName = "Grass";
        private const string kWaterShaderName = "BH/WatShader";
        private const string kWindowShaderName = "BH/SG_WinShader";
        private const string kSupportDecalsProperty = "_SupportDecals";
        private const string kDisableDecalsKeyword = "_DISABLE_DECALS";
        private const string kSurfaceTypeProperty = "_SurfaceType";

        private readonly Dictionary<AssetHash128, SurfaceMaterialEntry> m_SurfaceMaterials = new Dictionary<AssetHash128, SurfaceMaterialEntry>();

        private readonly Dictionary<VanillaSpecialMaterialKind, SpecialMaterialEntry> m_SpecialMaterials = new Dictionary<VanillaSpecialMaterialKind, SpecialMaterialEntry>();

        private readonly Dictionary<string, LibraryMaterialEntry> m_LibraryMaterials = new Dictionary<string, LibraryMaterialEntry>(StringComparer.Ordinal);

        private readonly Dictionary<string, TextureResourceEntry> m_LibraryTextures = new Dictionary<string, TextureResourceEntry>(StringComparer.Ordinal);

        private readonly HashSet<string> m_FailedLibraryMaterials = new HashSet<string>(StringComparer.Ordinal);

        private readonly Dictionary<AssetHash128, bool> m_GlassSurfaceClassification = new Dictionary<AssetHash128, bool>();

        private readonly Dictionary<AssetHash128, bool> m_WindowSurfaceClassification = new Dictionary<AssetHash128, bool>();

        private readonly TextureStreamingSystem m_TextureStreamingSystem;
        private readonly string m_ModRootPath;

        private static bool MaterialKeywordEnabled(Material material, string keyword)
        {
            if (material == null || material.shaderKeywords == null)
            {
                return false;
            }

            string[] keywords = material.shaderKeywords;
            for (int i = 0; i < keywords.Length; i++)
            {
                if (string.Equals(keywords[i], keyword, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private bool RefreshDecalReceptionMaterials(RenderPart renderPart, MeshPart sourcePart, bool hovered)
        {
            bool mainMaterialArrayChanged = false;
            bool anyMaterialChanged = false;

            for (int i = 0; i < renderPart.Areas.Count; i++)
            {
                RenderArea renderArea = renderPart.Areas[i];

                if (renderArea.ReceivesDecals == renderPart.ReceivesDecals)
                {
                    continue;
                }

                // HDRP/Unlit has no decal receiver pass. There is no material
                // variant to swap, but remember the PO state so later source
                // changes acquire the right variant.
                if (IsUnlitMaterial(renderArea.Material))
                {
                    renderArea.ReceivesDecals = renderPart.ReceivesDecals;
                    continue;
                }

                if (!sourcePart.TryGetSurfaceArea(renderArea.SurfaceAreaId, out SurfaceArea sourceArea))
                {
                    return false;
                }

                Material replacement = AcquireMaterial(sourceArea.Material, renderPart.ReceivesDecals);

                ReleaseMaterial(renderArea);
                renderArea.Material = replacement;
                renderArea.ReceivesDecals = renderPart.ReceivesDecals;
                anyMaterialChanged = true;

                if (renderArea.IsolatedRenderer)
                {
                    if (renderArea.IsolatedRendererComponent != null)
                    {
                        renderArea.IsolatedRendererComponent.sharedMaterial = replacement;
                    }
                }
                else
                {
                    mainMaterialArrayChanged = true;
                }
            }

            if (mainMaterialArrayChanged)
            {
                ApplyMainRendererMaterials(renderPart);
            }

            if (anyMaterialChanged)
            {
                ApplyPartPropertyBlocks(renderPart, hovered);
            }
            else
            {
                ApplyRenderPartDecalLayers(renderPart);
            }

            return true;
        }

        private static void ApplyMainRendererMaterials(RenderPart renderPart)
        {
            int mainMaterialCount = 0;
            for (int i = 0; i < renderPart.Areas.Count; i++)
            {
                if (!renderPart.Areas[i].IsolatedRenderer)
                {
                    mainMaterialCount++;
                }
            }

            Material[] materials = new Material[mainMaterialCount];
            for (int i = 0; i < renderPart.Areas.Count; i++)
            {
                RenderArea area = renderPart.Areas[i];
                if (!area.IsolatedRenderer && area.MaterialIndex >= 0)
                {
                    materials[area.MaterialIndex] = area.Material;
                }
            }

            renderPart.Renderer.sharedMaterials = materials;
        }

        private void ReleaseMaterial(RenderArea renderArea)
        {
            if (renderArea == null || renderArea.Material == null)
            {
                return;
            }

            if (renderArea.MaterialSourceKind == MaterialSourceKind.VanillaSpecial && m_SpecialMaterials.TryGetValue(renderArea.VanillaSpecialKind, out SpecialMaterialEntry specialEntry) && specialEntry.ReleaseReference(renderArea.Material, renderArea.ReceivesDecals))
            {
                if (specialEntry.TotalRefCount == 0)
                {
                    DestroyMaterialVariants(specialEntry);
                    m_SpecialMaterials.Remove(renderArea.VanillaSpecialKind);
                }

                renderArea.Material = null;
                return;
            }

            // Native SurfaceAssets own load/unload state as well as the cloned Material,
            // so the last reference has to release both together.
            if ((renderArea.MaterialSourceKind == MaterialSourceKind.SurfaceAsset || renderArea.MaterialSourceKind == MaterialSourceKind.VanillaSpecial) && renderArea.ResolvedSurfaceGuid.isValid && m_SurfaceMaterials.TryGetValue(renderArea.ResolvedSurfaceGuid, out SurfaceMaterialEntry surfaceEntry) && surfaceEntry.ReleaseReference(renderArea.Material, renderArea.ReceivesDecals))
            {
                if (surfaceEntry.TotalRefCount == 0)
                {
                    DestroyMaterialVariants(surfaceEntry);
                    surfaceEntry.Surface?.Unload();
                    m_SurfaceMaterials.Remove(renderArea.ResolvedSurfaceGuid);
                }

                renderArea.Material = null;
                return;
            }

            if (renderArea.MaterialSourceKind == MaterialSourceKind.LibraryMaterial && !string.IsNullOrWhiteSpace(renderArea.DefinitionId) && m_LibraryMaterials.TryGetValue(renderArea.DefinitionId, out LibraryMaterialEntry libraryEntry) && libraryEntry.ReleaseReference(renderArea.Material, renderArea.ReceivesDecals))
            {
                // Keep one warm material like the existing library cache did,
                // but drop the extra variant once the family has no live POs.
                if (libraryEntry.TotalRefCount == 0)
                {
                    TrimUnusedLibraryMaterialVariant(libraryEntry);
                }

                renderArea.Material = null;
                return;
            }

            UnityEngine.Object.Destroy(renderArea.Material);
            renderArea.Material = null;
        }

        private static void DestroyMaterialVariants(MaterialVariantEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            Material disabled = entry.DecalsDisabledMaterial;
            Material enabled = entry.DecalsEnabledMaterial;

            if (disabled != null)
            {
                UnityEngine.Object.Destroy(disabled);
            }

            if (enabled != null && enabled != disabled)
            {
                UnityEngine.Object.Destroy(enabled);
            }

            entry.DecalsDisabledMaterial = null;
            entry.DecalsEnabledMaterial = null;
            entry.DecalsDisabledRefCount = 0;
            entry.DecalsEnabledRefCount = 0;
        }

        private static void TrimUnusedLibraryMaterialVariant(LibraryMaterialEntry entry)
        {
            if (entry == null || entry.TotalRefCount != 0 || entry.DecalsDisabledMaterial == null || entry.DecalsEnabledMaterial == null)
            {
                return;
            }

            // Disabled is the default PO state, so it is the useful warm copy.
            UnityEngine.Object.Destroy(entry.DecalsEnabledMaterial);
            entry.DecalsEnabledMaterial = null;
        }

        private bool IsWindowSurface(MaterialData materialData)
        {
            if (materialData == null || materialData.SourceKind != MaterialSourceKind.SurfaceAsset || !materialData.SurfaceGuid.isValid)
            {
                return false;
            }

            AssetHash128 surfaceGuid = materialData.SurfaceGuid;
            // Classification can require loading a SurfaceAsset, so remember
            // both positive and negative answers by GUID.
            if (m_WindowSurfaceClassification.TryGetValue(surfaceGuid, out bool cachedResult))
            {
                return cachedResult;
            }

            if (m_SurfaceMaterials.TryGetValue(surfaceGuid, out SurfaceMaterialEntry cachedSurface))
            {
                Material cachedMaterial = cachedSurface.GetAnyMaterial();
                bool cachedIsWindow = string.Equals(cachedMaterial?.shader?.name, kWindowShaderName, StringComparison.Ordinal);
                m_WindowSurfaceClassification[surfaceGuid] = cachedIsWindow;
                return cachedIsWindow;
            }

            if (!AssetDatabase.global.TryGetAsset<SurfaceAsset>(surfaceGuid, out SurfaceAsset surfaceAsset) || surfaceAsset == null)
            {
                m_WindowSurfaceClassification[surfaceGuid] = false;
                return false;
            }

            Material material = null;
            bool surfaceLoaded = false;

            try
            {
                material = surfaceAsset.Load(useVT: true);
                surfaceLoaded = true;
                bool isWindow = string.Equals(material?.shader?.name, kWindowShaderName, StringComparison.Ordinal);
                m_WindowSurfaceClassification[surfaceGuid] = isWindow;
                return isWindow;
            }
            catch (Exception exception)
            {
                Mod.log.Warn($"[PO Renderer] Could not classify SurfaceAsset " + $"'{surfaceAsset.name}' ({surfaceGuid}) for window " + $"isolation: {exception.Message}");
                m_WindowSurfaceClassification[surfaceGuid] = false;
                return false;
            }
            finally
            {
                if (surfaceLoaded)
                {
                    surfaceAsset.Unload();
                }
            }
        }

        private bool IsGlassSurface(MaterialData materialData)
        {
            if (materialData == null)
            {
                return false;
            }

            if (materialData.SourceKind != MaterialSourceKind.SurfaceAsset || !materialData.SurfaceGuid.isValid)
            {
                return false;
            }

            AssetHash128 surfaceGuid = materialData.SurfaceGuid;
            if (m_GlassSurfaceClassification.TryGetValue(surfaceGuid, out bool cachedResult))
            {
                return cachedResult;
            }

            if (m_SurfaceMaterials.TryGetValue(surfaceGuid, out SurfaceMaterialEntry cachedSurface))
            {
                Material cachedMaterial = cachedSurface.GetAnyMaterial();
                bool cachedIsGlass = string.Equals(cachedMaterial?.shader?.name, kGlassShaderName, StringComparison.Ordinal);
                m_GlassSurfaceClassification[surfaceGuid] = cachedIsGlass;
                return cachedIsGlass;
            }

            if (!AssetDatabase.global.TryGetAsset<SurfaceAsset>(surfaceGuid, out SurfaceAsset surfaceAsset) || surfaceAsset == null)
            {
                m_GlassSurfaceClassification[surfaceGuid] = false;
                return false;
            }

            Material material = null;
            bool surfaceLoaded = false;

            try
            {
                material = surfaceAsset.Load(useVT: true);
                surfaceLoaded = true;
                bool isGlass = string.Equals(material?.shader?.name, kGlassShaderName, StringComparison.Ordinal);
                m_GlassSurfaceClassification[surfaceGuid] = isGlass;
                return isGlass;
            }
            catch (Exception exception)
            {
                Mod.log.Warn($"[PO Renderer] Could not classify SurfaceAsset " + $"'{surfaceAsset.name}' ({surfaceGuid}) for glass " + $"isolation: {exception.Message}");
                m_GlassSurfaceClassification[surfaceGuid] = false;
                return false;
            }
            finally
            {
                if (surfaceLoaded)
                {
                    surfaceAsset.Unload();
                }
            }
        }

        private static void InitializeMaterialVariant(MaterialVariantEntry entry, Material material, bool receivesDecals)
        {
            entry.BaseName = material.name;
            material.name = GetMaterialVariantName(entry.BaseName, receivesDecals);
            ConfigureMaterialDecalReception(material, receivesDecals);
            NativeMaterialAnimation.DisableWind(material);
            entry.SetMaterial(receivesDecals, material);
            entry.AddReference(receivesDecals);
        }

        private Material AcquireMaterialVariant(MaterialVariantEntry entry, bool receivesDecals, SurfaceAsset virtualTextureSurface = null)
        {
            Material material = entry.GetMaterial(receivesDecals);
            if (material != null)
            {
                entry.AddReference(receivesDecals);
                return material;
            }

            Material source = entry.GetAnyMaterial();
            if (source == null)
            {
                return null;
            }

            try
            {
                material = new Material(source)
                {
                    name = GetMaterialVariantName(entry.BaseName, receivesDecals),
                };

                ConfigureMaterialDecalReception(material, receivesDecals);
                NativeMaterialAnimation.DisableWind(material);

                // Clones share the VT textures, but the streaming system still
                // needs to bind its small per-material parameter block.
                if (virtualTextureSurface != null)
                {
                    BindVirtualTextureSurface(material, virtualTextureSurface);
                }

                entry.SetMaterial(receivesDecals, material);
                entry.AddReference(receivesDecals);
                return material;
            }
            catch (Exception exception)
            {
                if (material != null)
                {
                    UnityEngine.Object.Destroy(material);
                }

                Mod.log.Error($"[PO Renderer] Failed to clone material variant " + $"'{entry.BaseName}' for decal reception " + $"'{receivesDecals}': {exception}");
                return null;
            }
        }

        private static void ConfigureMaterialDecalReception(Material material, bool receivesDecals)
        {
            if (material == null || IsUnlitMaterial(material))
            {
                return;
            }

            Shader shader = material.shader;
            string shaderName = shader != null ? shader.name : "<missing>";
            DecalLayers decalLayers = GetReceiverDecalLayers(receivesDecals);

            // CS2 uses Other when a normal mesh has no assigned decal layer.
            // Give BH shaders that real layer instead of zero, which they do
            // not treat as a supported "receive nothing" value.
            if (material.HasProperty(s_DecalLayerMaskProperty))
            {
                material.SetFloat(s_DecalLayerMaskProperty, math.asfloat((int)decalLayers));
            }

            bool hasSupportProperty = material.HasProperty(kSupportDecalsProperty);
            float desiredSupportValue = receivesDecals ? 1f : 0f;
            if (hasSupportProperty)
            {
                material.SetFloat(kSupportDecalsProperty, desiredSupportValue);
            }

            bool isOpaque = material.HasProperty(kSurfaceTypeProperty) && material.GetFloat(kSurfaceTypeProperty) < 0.5f;
            try
            {
                HDMaterial.ValidateMaterial(material);
            }
            catch (Exception exception)
            {
                Mod.log.Warn($"[PO Renderer] HDRP material validation failed for " + $"'{material.name}' using shader '{shaderName}': " + exception.Message);
            }

            if (hasSupportProperty && isOpaque)
            {
                try
                {
                    // Validation recalculates the queue from _SupportDecals,
                    // so force the depth-prepass queue after validation. Both
                    // states need to hide the projected area volume.
                    material.SetFloat(kSupportDecalsProperty, 1f);
                    HDMaterial.SetRenderingPass(material, HDMaterial.RenderingPass.Default);
                }
                catch (Exception exception)
                {
                    Mod.log.Warn($"[PO Renderer] Could not update HDRP render queue " + $"for material '{material.name}' using shader " + $"'{shaderName}': {exception.Message}");
                }
                finally
                {
                    // The queue stays decal-aware. The material setting and
                    // keyword still follow the PO-specific toggle.
                    material.SetFloat(kSupportDecalsProperty, desiredSupportValue);
                }
            }

            // Colossal's BH shader variants do not all expose the same HDRP
            // metadata, so make the receiver keyword explicit too.
            if (shader != null)
            {
                LocalKeyword disableKeyword = new LocalKeyword(shader, kDisableDecalsKeyword);

                if (disableKeyword.isValid)
                {
                    material.SetKeyword(in disableKeyword, !receivesDecals);
                }
            }
        }

        private static string GetMaterialVariantName(string baseName, bool receivesDecals)
        {
            return $"{baseName} " + (receivesDecals ? "(Receives Decals)" : "(No Decals)");
        }

        private static bool IsUnlitMaterial(Material material)
        {
            return string.Equals(material?.shader?.name, "HDRP/Unlit", StringComparison.Ordinal);
        }

        private Material AcquireMaterial(MaterialData materialData, bool receivesDecals)
        {
            // Resolve missing assets only in the renderer; keep their saved references intact.
            if (materialData.SourceKind == MaterialSourceKind.SurfaceAsset)
            {
                return AcquireSurfaceMaterial(materialData.SurfaceGuid, receivesDecals);
            }

            if (materialData.SourceKind == MaterialSourceKind.VanillaSpecial)
            {
                if (!VanillaSpecialMaterialLibrary.TryGet(materialData.VanillaSpecialKind, out VanillaSpecialMaterialDefinition definition))
                {
                    Mod.log.Warn($"[PO Renderer] Could not resolve vanilla special material " + $"'{materialData.VanillaSpecialKind}'. Falling back to " + "prototype material.");
                    return CreatePrototypeMaterial();
                }

                if (materialData.VanillaSpecialKind == VanillaSpecialMaterialKind.Grass)
                {
                    return AcquireTemplateSpecialMaterial(definition, receivesDecals);
                }

                if (VanillaSpecialMaterialLibrary.TryResolveSurfaceGuid(materialData.VanillaSpecialKind, out AssetHash128 surfaceGuid))
                {
                    return AcquireSurfaceMaterial(surfaceGuid, receivesDecals);
                }

                Mod.log.Warn($"[PO Renderer] Could not resolve vanilla special material " + $"'{materialData.VanillaSpecialKind}'. Falling back to " + "prototype material.");
                return CreatePrototypeMaterial();
            }

            return AcquireLibraryMaterial(materialData.DefinitionId, materialData.Mapping.Mode, receivesDecals);
        }

        private Material AcquireTemplateSpecialMaterial(VanillaSpecialMaterialDefinition definition, bool receivesDecals)
        {
            if (m_SpecialMaterials.TryGetValue(definition.Kind, out SpecialMaterialEntry cached))
            {
                Material cachedMaterial = AcquireMaterialVariant(cached, receivesDecals);
                return cachedMaterial ?? CreatePrototypeMaterial();
            }

            Colossal.IO.AssetDatabase.MaterialLibrary materialLibrary = AssetDatabase.global?.resources?.materialLibrary;
            Material template = materialLibrary?.GetMaterial(kGrassMaterialTemplateName);

            if (template == null || !string.Equals(template.shader?.name, definition.ShaderName, StringComparison.Ordinal))
            {
                Mod.log.Warn($"[PO Renderer] Vanilla special material " + $"'{definition.DisplayName}' could not resolve template " + $"'{kGrassMaterialTemplateName}' with shader " + $"'{definition.ShaderName}'. Falling back to prototype " + "material.");
                return CreatePrototypeMaterial();
            }

            Material material;
            try
            {
                material = new Material(template)
                {
                    name = $"PO2 Vanilla {definition.DisplayName}",
                };
            }
            catch (Exception exception)
            {
                Mod.log.Error($"[PO Renderer] Failed to clone vanilla special material " + $"'{definition.DisplayName}': {exception}");
                return CreatePrototypeMaterial();
            }

            // Game GeometryAssets use packed tangent-space streams.
            // PO meshes upload ordinary Unity normals and tangents instead.
            material.DisableKeyword("_TANGENTSPACE_OCTO");

            SpecialMaterialEntry entry = new SpecialMaterialEntry();
            InitializeMaterialVariant(entry, material, receivesDecals);
            m_SpecialMaterials.Add(definition.Kind, entry);

            return material;
        }

        private static AssetHash128 ResolveNativeSurfaceGuid(MaterialData materialData)
        {
            if (materialData == null)
            {
                return default;
            }

            if (materialData.SourceKind == MaterialSourceKind.SurfaceAsset)
            {
                return materialData.SurfaceGuid;
            }

            if (materialData.SourceKind == MaterialSourceKind.VanillaSpecial && VanillaSpecialMaterialLibrary.TryResolveSurfaceGuid(materialData.VanillaSpecialKind, out AssetHash128 surfaceGuid))
            {
                return surfaceGuid;
            }

            return default;
        }

        private Material AcquireLibraryMaterial(string definitionId, TextureMappingMode mappingMode, bool receivesDecals)
        {
            if (!MaterialLibrary.TryGet(definitionId, out MaterialDefinition definition))
            {
                Mod.log.Warn($"[PO Renderer] Unexpected unresolved library material '{definitionId}'. " + "Falling back to prototype material as a defensive safety net.");
                return CreatePrototypeMaterial();
            }

            if (definition.ShaderKind == MaterialShaderKind.PrototypeUnlit)
            {
                return CreatePrototypeMaterial();
            }

            if (m_LibraryMaterials.TryGetValue(definition.Id, out LibraryMaterialEntry cached))
            {
                Material cachedMaterial = AcquireMaterialVariant(cached, receivesDecals);
                return cachedMaterial ?? CreatePrototypeMaterial();
            }

            // Do not retry a broken PNG/material every sync frame.
            // Validation can repair the reference separately ~ rendering just stays usable.
            if (m_FailedLibraryMaterials.Contains(definition.Id))
            {
                return CreatePrototypeMaterial();
            }

            Material material = CreateLibraryObjectMaterial(definition);

            if (material == null)
            {
                m_FailedLibraryMaterials.Add(definition.Id);
                return CreatePrototypeMaterial();
            }

            LibraryMaterialEntry entry = new LibraryMaterialEntry();
            InitializeMaterialVariant(entry, material, receivesDecals);
            m_LibraryMaterials.Add(definition.Id, entry);

            return material;
        }

        private Material AcquireSurfaceMaterial(AssetHash128 surfaceGuid, bool receivesDecals)
        {
            if (m_SurfaceMaterials.TryGetValue(surfaceGuid, out SurfaceMaterialEntry cached))
            {
                Material cachedMaterial = AcquireMaterialVariant(cached, receivesDecals, cached.Surface);
                ConfigureEnvironmentWaterSafeTransparency(cachedMaterial, surfaceGuid);
                return cachedMaterial ?? CreatePrototypeMaterial();
            }

            if (!AssetDatabase.global.TryGetAsset<SurfaceAsset>(surfaceGuid, out SurfaceAsset surfaceAsset) || surfaceAsset == null)
            {
                Mod.log.Warn($"[PO Renderer] Could not resolve SurfaceAsset {surfaceGuid}. " + "Falling back to prototype material.");
                return CreatePrototypeMaterial();
            }

            // SurfaceAsset.Load participates in the game's asset/VT lifetime.
            // Cache one clone and balance the source Unload at the last release.
            Material sourceMaterial;

            try
            {
                sourceMaterial = surfaceAsset.Load(useVT: true);
            }
            catch (Exception exception)
            {
                Mod.log.Error($"[PO Renderer] Failed to load SurfaceAsset " + $"'{surfaceAsset.name}' ({surfaceGuid}): {exception}");
                return CreatePrototypeMaterial();
            }

            if (sourceMaterial == null)
            {
                Mod.log.Warn($"[PO Renderer] SurfaceAsset '{surfaceAsset.name}' loaded no " + "material. Falling back to prototype material.");
                surfaceAsset.Unload();
                return CreatePrototypeMaterial();
            }

            bool sourceUsesOctahedralTangentSpace = MaterialKeywordEnabled(sourceMaterial, "_TANGENTSPACE_OCTO");

            Material material;
            try
            {
                material = new Material(sourceMaterial)
                {
                    name = $"PO2 Native {surfaceAsset.name}",
                };
            }
            catch (Exception exception)
            {
                surfaceAsset.Unload();
                Mod.log.Error($"[PO Renderer] Failed to clone native SurfaceAsset material " + $"'{surfaceAsset.name}' ({surfaceGuid}): {exception}");
                return CreatePrototypeMaterial();
            }

            // Conversion already decodes packed normals and tangents. Native material clones
            // must disable octahedral decoding or the shader decodes them a second time.
            if (sourceUsesOctahedralTangentSpace)
            {
                material.DisableKeyword("_TANGENTSPACE_OCTO");
            }

            BindVirtualTextureSurface(material, surfaceAsset);

            SurfaceMaterialEntry entry = new SurfaceMaterialEntry
            {
                Surface = surfaceAsset,
            };
            InitializeMaterialVariant(entry, material, receivesDecals);
            ConfigureEnvironmentWaterSafeTransparency(material, surfaceGuid);
            m_SurfaceMaterials.Add(surfaceGuid, entry);

            return material;
        }

        private static void ConfigureEnvironmentWaterSafeTransparency(Material material, AssetHash128 surfaceGuid)
        {
            if (material == null)
            {
                return;
            }

            string shaderName = material.shader?.name;
            bool isGlass = string.Equals(shaderName, kGlassShaderName, StringComparison.Ordinal);
            bool isWater = string.Equals(shaderName, kWaterShaderName, StringComparison.Ordinal) || IsCanonicalWaterSurface(surfaceGuid);
            if (!isGlass && !isWater)
            {
                return;
            }

            // ConfigureMaterialDecalReception runs HDRP validation. That
            // validation recalculates this pass, so the PO-only safety override
            // must be the final material step.
            material.SetShaderPassEnabled("TransparentDepthPrepass", false);

            // Keep these PO transparencies in the normal transparent phase,
            // one queue ahead of the game gizmo and WaterGeneric.
            material.renderQueue = 2999;
        }

        private static bool IsCanonicalWaterSurface(AssetHash128 surfaceGuid)
        {
            return VanillaSpecialMaterialLibrary.TryResolveSurfaceGuid(VanillaSpecialMaterialKind.Water, out AssetHash128 waterSurfaceGuid) && surfaceGuid.Equals(waterSurfaceGuid);
        }

        private Material CreateLibraryObjectMaterial(MaterialDefinition definition)
        {
            Texture2D baseColor = null;
            Texture2D normal = null;
            Texture2D maskMap = null;
            Texture2D controlMask = null;
            List<string> acquiredTextureKeys = new List<string>(4);

            try
            {
                baseColor = AcquireLibraryTexture(definition.BaseColorPath, linear: false, "BaseColor", acquiredTextureKeys);
                normal = AcquireLibraryTexture(definition.NormalPath, linear: true, "Normal", acquiredTextureKeys);
                maskMap = AcquireLibraryTexture(definition.MaskMapPath, linear: true, "MaskMap", acquiredTextureKeys);
                controlMask = AcquireLibraryTexture(definition.ControlMaskPath, linear: true, "ControlMask", acquiredTextureKeys);
            }
            catch (Exception exception)
            {
                RollBackLibraryTextureAcquisitions(acquiredTextureKeys);
                Mod.log.Error($"[PO Renderer] Failed to load PO2 library material " + $"'{definition.Id}': {exception}");
                return null;
            }

            Material material = GenericMaterialFactory.Create($"PO2 {definition.DisplayName}", baseColor, normal, maskMap, controlMask, definition.Metallic, definition.Smoothness, definition.CoatStrength);

            if (material == null)
            {
                RollBackLibraryTextureAcquisitions(acquiredTextureKeys);
            }

            return material;
        }

        private Material CreatePrototypeMaterial()
        {
            Shader shader = Shader.Find("HDRP/Unlit");

            if (shader == null)
            {
                throw new InvalidOperationException("Could not find HDRP/Unlit shader.");
            }

            Material material = new Material(shader)
            {
                name = "PO2 Prototype Alignment Grid",
            };

            // Use the grid with both authored UVs and generated mapping.
            Texture2D texture = GetMappingDiagnosticTexture();

            if (material.HasProperty("_UnlitColorMap"))
            {
                material.SetTexture("_UnlitColorMap", texture);
            }
            else if (material.HasProperty("_BaseColorMap"))
            {
                material.SetTexture("_BaseColorMap", texture);
            }
            else
            {
                material.mainTexture = texture;
            }

            SetMaterialTint(material, s_PrototypeTint);
            return material;
        }

        private static void DestroyLibraryMaterialEntry(LibraryMaterialEntry entry)
        {
            DestroyMaterialVariants(entry);
        }
    }
}