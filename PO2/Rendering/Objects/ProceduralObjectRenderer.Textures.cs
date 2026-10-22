using System;
using System.Collections.Generic;
using System.IO;
using Colossal.IO.AssetDatabase;
using Colossal.IO.AssetDatabase.VirtualTexturing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        private void BindVirtualTextureSurface(Material material, SurfaceAsset surfaceAsset)
        {
            if (m_TextureStreamingSystem == null || material == null || surfaceAsset == null || !surfaceAsset.isVTMaterial)
            {
                return;
            }

            VTAtlassingInfo[] atlassingInfos = surfaceAsset.VTAtlassingInfos ?? surfaceAsset.PreReservedAtlassingInfos;

            if (atlassingInfos == null)
            {
                Mod.log.Warn($"[PO Renderer] VT SurfaceAsset '{surfaceAsset.name}' has no " + "atlassing information available yet.");
                return;
            }

            // Cloning the Material does not bind its runtime VT stack.
            // The streaming system still needs the atlas parameter blocks.
            material.EnableKeyword("ENABLE_VT");

            int stackCount = Mathf.Min(atlassingInfos.Length, 2);

            for (int stackIndex = 0; stackIndex < stackCount; stackIndex++)
            {
                VTAtlassingInfo info = atlassingInfos[stackIndex];

                if (info.indexInStack < 0)
                {
                    continue;
                }

                m_TextureStreamingSystem.BindMaterial(material, info.stackGlobalIndex, stackIndex, m_TextureStreamingSystem.GetTextureParamBlock(info));
            }
        }

        private Texture2D AcquireLibraryTexture(string texturePath, bool linear, string role, List<string> acquiredTextureKeys)
        {
            if (string.IsNullOrWhiteSpace(texturePath))
            {
                return null;
            }

            // The same PNG loaded as sRGB and linear is two different Unity
            // textures, hence color space being part of the cache key.
            string cacheKey = GetLibraryTextureCacheKey(texturePath, linear);

            if (m_LibraryTextures.TryGetValue(cacheKey, out TextureResourceEntry cached))
            {
                cached.RefCount++;
                acquiredTextureKeys?.Add(cacheKey);
                return cached.Texture;
            }

            Texture2D texture = LoadLibraryTexture(texturePath, linear, role);

            m_LibraryTextures.Add(cacheKey, new TextureResourceEntry
            {
                Texture = texture,
                RefCount = 1,
            });
            acquiredTextureKeys?.Add(cacheKey);

            return texture;
        }

        private static string GetLibraryTextureCacheKey(string texturePath, bool linear)
        {
            return (linear ? "linear|" : "srgb|") + texturePath;
        }

        private void RollBackLibraryTextureAcquisitions(List<string> acquiredTextureKeys)
        {
            if (acquiredTextureKeys == null)
            {
                return;
            }

            foreach (string cacheKey in acquiredTextureKeys)
            {
                if (!m_LibraryTextures.TryGetValue(cacheKey, out TextureResourceEntry entry))
                {
                    continue;
                }

                entry.RefCount = Mathf.Max(0, entry.RefCount - 1);
            }
        }

        private static bool HasTexturePath(string texturePath)
        {
            return !string.IsNullOrWhiteSpace(texturePath);
        }

        private Texture2D LoadLibraryTexture(string texturePath, bool linear, string role)
        {
            if (string.IsNullOrWhiteSpace(texturePath))
            {
                return null;
            }

            string fullPath = Path.GetFullPath(texturePath);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"PO2 {role} texture was not found.", fullPath);
            }

            byte[] bytes = File.ReadAllBytes(fullPath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear)
            {
                name = $"PO2 {role}: {Path.GetFileNameWithoutExtension(fullPath)}",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 16,
            };

            if (!ImageConversion.LoadImage(texture, bytes, false))
            {
                UnityEngine.Object.Destroy(texture);
                throw new InvalidOperationException($"Unity could not decode PO2 {role} texture '{fullPath}'.");
            }

            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 16;
            // Once uploaded, CPU pixels are dead weight.
            // PO never edits an imported texture in place.
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return texture;
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
        }
    }
}