using System;
using Game.Rendering;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using PO2.Core;
using PO2.Materials;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        // HDRP stores decal layers in bits 8 through 15. Keep the light-layer
        // byte and any game-owned upper bits intact when this setting changes.
        private const uint kDecalRenderingLayersMask = 0x0000FF00u;
        private const int kDecalRenderingLayersShift = 8;
        private const DecalLayers kDecalsDisabledLayers = DecalLayers.Other;
        private const DecalLayers kDecalsEnabledLayers = DecalLayers.Terrain | DecalLayers.Roads | DecalLayers.Buildings;

        private static readonly int s_DecalLayerMaskProperty = Shader.PropertyToID("colossal_DecalLayerMask");

        private static readonly Color s_PrototypeTint = new Color(0.76f, 0.76f, 0.76f, 1f);

        private const float kHoveredBrightnessMultiplier = 1.12f;

        private static void CopyColorMaskState(RenderArea renderArea, ColorMaskData source)
        {
            if (renderArea == null)
            {
                throw new ArgumentNullException(nameof(renderArea));
            }

            if (source == null)
            {
                renderArea.ColorMaskMode = ColorMaskMode.Disabled;
                renderArea.HasSourceColorMask = false;
                renderArea.SourceColorMask1 = Color.white;
                renderArea.SourceColorMask2 = Color.white;
                renderArea.SourceColorMask3 = Color.white;
                renderArea.OverrideColorMask1 = Color.white;
                renderArea.OverrideColorMask2 = Color.white;
                renderArea.OverrideColorMask3 = Color.white;
                return;
            }

            renderArea.ColorMaskMode = source.Mode;
            renderArea.HasSourceColorMask = source.HasSourceMask;
            renderArea.SourceColorMask1 = source.SourceMask1;
            renderArea.SourceColorMask2 = source.SourceMask2;
            renderArea.SourceColorMask3 = source.SourceMask3;
            renderArea.OverrideColorMask1 = source.OverrideMask1;
            renderArea.OverrideColorMask2 = source.OverrideMask2;
            renderArea.OverrideColorMask3 = source.OverrideMask3;
        }

        private static void ApplyColorMaskProperties(RenderArea area, MaterialPropertyBlock properties)
        {
            if (area == null || properties == null)
            {
                return;
            }

            if (area.ColorMaskMode == ColorMaskMode.Source)
            {
                if (!area.HasSourceColorMask)
                {
                    return;
                }

                // MeshColor already contains the resolved ECS render color.
                // Do NOYT apply Color.linear here ~ doing so double-converts it.
                properties.SetColor("colossal_ColorMask0", area.SourceColorMask1);
                properties.SetColor("colossal_ColorMask1", area.SourceColorMask2);
                properties.SetColor("colossal_ColorMask2", area.SourceColorMask3);
                return;
            }

            if (area.ColorMaskMode == ColorMaskMode.Override)
            {
                properties.SetColor("colossal_ColorMask0", area.OverrideColorMask1);
                properties.SetColor("colossal_ColorMask1", area.OverrideColorMask2);
                properties.SetColor("colossal_ColorMask2", area.OverrideColorMask3);
            }
        }

        private static void ApplyPartPropertyBlocks(RenderPart part, bool hovered)
        {
            if (part.Renderer == null) return;
            ApplyRenderPartDecalLayers(part);
            foreach (var area in part.Areas) ApplyAreaPropertyBlock(part, area, hovered);
        }

        private static void ApplyAreaPropertyBlock(RenderPart part, RenderArea area, bool hovered)
        {
            if (area.PropertyBlock == null)
            {
                area.PropertyBlock = new MaterialPropertyBlock();
            }

            // Per-area properties belong in blocks.
            // Mutating the shared cached Material would leak hover/masks into unrelated POs.
            MaterialPropertyBlock properties = area.PropertyBlock;
            properties.Clear();

            if (area.MaterialSourceKind == MaterialSourceKind.LibraryMaterial && area.Material != null)
            {
                if (area.Material.HasProperty("_Metallic"))
                {
                    properties.SetFloat("_Metallic", area.Metallic);
                }

                if (area.Material.HasProperty("_Smoothness"))
                {
                    properties.SetFloat("_Smoothness", area.Smoothness);
                }

                if (area.Material.HasProperty("_CoatStrength"))
                {
                    properties.SetFloat("_CoatStrength", area.CoatStrength);
                }
            }

            ApplyColorMaskProperties(area, properties);

            if (hovered && area.Material != null)
            {
                if (area.Material.HasProperty("_UnlitColor"))
                {
                    properties.SetColor("_UnlitColor", HoverColor(area.Material.GetColor("_UnlitColor"), part.DeleteHovered));
                }
                else if (area.Material.HasProperty("_BaseColor"))
                {
                    properties.SetColor("_BaseColor", HoverColor(area.Material.GetColor("_BaseColor"), part.DeleteHovered));
                }
            }

            if (area.IsolatedRenderer)
            {
                if (area.IsolatedRendererComponent != null)
                {
                    area.IsolatedRendererComponent.SetPropertyBlock(properties);
                }
            }
            else
            {
                part.Renderer.SetPropertyBlock(properties, area.MaterialIndex);
            }
        }

        private static void ApplyRenderPartDecalLayers(RenderPart part)
        {
            if (part == null)
            {
                return;
            }

            ApplyRendererDecalLayers(part.Renderer, part.ReceivesDecals);

            for (int i = 0; i < part.Areas.Count; i++)
            {
                RenderArea area = part.Areas[i];
                if (!area.IsolatedRenderer)
                {
                    continue;
                }

                ApplyRendererDecalLayers(area.IsolatedRendererComponent, part.ReceivesDecals);
            }
        }

        private static void ApplyRendererDecalLayers(Renderer renderer, bool receivesDecals)
        {
            if (renderer == null)
            {
                return;
            }

            uint renderingLayerMask = renderer.renderingLayerMask & ~kDecalRenderingLayersMask;
            DecalLayers decalLayers = GetReceiverDecalLayers(receivesDecals);

            renderingLayerMask |= (uint)decalLayers << kDecalRenderingLayersShift;

            renderer.renderingLayerMask = renderingLayerMask;
        }

        private static DecalLayers GetReceiverDecalLayers(bool receivesDecals)
        {
            return receivesDecals ? kDecalsEnabledLayers : kDecalsDisabledLayers;
        }

        private static Color BrightenColor(Color color)
        {
            return new Color(color.r * kHoveredBrightnessMultiplier, color.g * kHoveredBrightnessMultiplier, color.b * kHoveredBrightnessMultiplier, color.a);
        }

        private static Color HoverColor(Color color, bool delete)
        {
            Color brightened = BrightenColor(color);
            return delete ? Color.Lerp(brightened, new Color(1f, 0.08f, 0.08f, color.a), 0.8f) : brightened;
        }

        public void SetHovered(int objectId, bool hovered, bool delete = false)
        {
            delete &= hovered;
            if (!m_Instances.TryGetValue(objectId, out RenderInstance instance) || (instance.Hovered == hovered && instance.DeleteHovered == delete))
            {
                return;
            }

            instance.Hovered = hovered;
            instance.DeleteHovered = delete;
            ApplyHoverVisual(instance);
        }

        private static void ApplyTransform(RenderInstance instance, ObjectTransform transform)
        {
            instance.Root.transform.SetPositionAndRotation(transform.Position, transform.Rotation);

            instance.Root.transform.localScale = transform.Scale;
        }

        private static void SetMaterialTint(Material material, Color tint)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_UnlitColor"))
            {
                material.SetColor("_UnlitColor", tint);
            }
            else if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", tint);
            }
            else
            {
                material.color = tint;
            }
        }

        private void ApplyHoverVisual(RenderInstance instance)
        {
            foreach (RenderPart part in instance.Parts)
            {
                part.DeleteHovered = instance.DeleteHovered;
                ApplyPartPropertyBlocks(part, instance.Hovered);
            }

            ApplyPublishedLodPropertyBlocks(instance);
        }
    }
}