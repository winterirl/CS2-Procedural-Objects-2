using MaterialLibrary = PO2.Materials.MaterialLibrary;
using MaterialData = PO2.Materials.MaterialData;
using System;
using Colossal.IO.AssetDatabase;
using Game.Prefabs;
using Game.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using PO2.Materials;

namespace PO2.Conversion
{
    internal sealed partial class AssetMeshExtractor
    {
        private static MaterialData CreateSourceMaterial(RenderPrefab renderPrefab, int materialIndex, out NativeWindowKind nativeWindowKind, out bool requiresIsolationHint)
        {
            nativeWindowKind = NativeWindowKind.None;
            requiresIsolationHint = false;

            if (renderPrefab == null || materialIndex < 0 || materialIndex >= renderPrefab.materialCount)
            {
                Mod.log.Warn($"[PO Conversion] RenderPrefab '{renderPrefab?.name ?? "<null>"}' " + $"has no native material slot {materialIndex}. " + "Using fallback material for this Surface Area.");
                return CreateFallbackMaterial();
            }

            SurfaceAsset surfaceAsset;

            try
            {
                surfaceAsset = renderPrefab.GetSurfaceAsset(materialIndex);
            }
            catch (Exception exception)
            {
                Mod.log.Warn($"[PO Conversion] Could not resolve native material slot " + $"{materialIndex} on '{renderPrefab.name}': {exception.Message}. " + "Using fallback material for this Surface Area.");
                return CreateFallbackMaterial();
            }

            if (surfaceAsset == null || !surfaceAsset.id.guid.isValid)
            {
                Mod.log.Warn($"[PO Conversion] Native material slot {materialIndex} on " + $"'{renderPrefab.name}' did not resolve to a valid SurfaceAsset. " + "Using fallback material for this Surface Area.");
                return CreateFallbackMaterial();
            }

            // Keep the exact SurfaceAsset GUID for rendering.
            // The window kind is not a replacement material identity, just some extra safety.
            nativeWindowKind = ClassifyNativeWindowKind(surfaceAsset);
            requiresIsolationHint = nativeWindowKind != NativeWindowKind.None || IsGlassSurfaceName(surfaceAsset.name);

            MaterialData material = CreateFallbackMaterial();
            if (!material.SetSurfaceAsset(surfaceAsset.id.guid))
            {
                nativeWindowKind = NativeWindowKind.None;
                requiresIsolationHint = false;
                Mod.log.Warn($"[PO Conversion] Could not assign SurfaceAsset " + $"'{surfaceAsset.name}' ({surfaceAsset.id.guid}) to material " + $"slot {materialIndex} on '{renderPrefab.name}'. " + "Using fallback material for this Surface Area.");
                return CreateFallbackMaterial();
            }

            // Converted native shaders expect their authored UV payload
            material.Mapping.SetMode(TextureMappingMode.Original);

            return material;
        }


        private static bool IsGlassSurfaceName(string surfaceName)
        {
            return !string.IsNullOrEmpty(surfaceName) && surfaceName.IndexOf("_Gls_", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static NativeWindowKind ClassifyNativeWindowKind(SurfaceAsset surfaceAsset)
        {
            return NativeSurfaceClassifier.ClassifyWindowKind(surfaceAsset != null ? surfaceAsset.name : null);
        }

        private static MaterialData CreateFallbackMaterial()
        {
            return new MaterialData(MaterialLibrary.kPrototypeNoisyGray, new TextureMappingData(TextureMappingMode.Original));
        }
    }
}