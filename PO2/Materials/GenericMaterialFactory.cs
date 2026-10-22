using UnityEngine;

namespace PO2.Materials
{
    // Creates generic materials using the game's building/prop shader.
    // The caller manages their cache and disposal; native special materials use a separate path.

    internal static class GenericMaterialFactory
    {
        public static Material Create(string materialName, Texture baseColor, Texture normal, Texture maskMap, Texture controlMask, float metallic, float smoothness, float coatStrength)
        {
            Shader shader = Shader.Find("BH/SG_DefaultShader");
            if (shader == null)
            {
                Mod.log.Error("[PO Material] Could not find BH/SG_DefaultShader for " + $"generic material '{materialName}'.");
                return null;
            }

            Material material = new Material(shader)
            {
                name = string.IsNullOrWhiteSpace(materialName) ? "PO2 Generic Material" : materialName,
            };

            // Imported textures are ordinary Unity textures.
            // Leaving VT enabled makes the vanilla shader expect a virtual-texture stack instead.
            material.DisableKeyword("ENABLE_VT");

            ApplyScalars(material, metallic, smoothness, coatStrength);
            SetFloat(material, "_AlphaCutoffEnable", 0f);

            BindTexture(material, "_BaseColorMap", baseColor);
            BindTexture(material, "_NormalMap", normal);
            BindTexture(material, "_MaskMap", maskMap);
            BindTexture(material, "_ControlMask", controlMask);

            return material;
        }

        public static void ApplyScalars(Material material, float metallic, float smoothness, float coatStrength)
        {
            if (material == null)
            {
                return;
            }

            SetFloat(material, "_Metallic", Mathf.Clamp01(metallic));
            SetFloat(material, "_Smoothness", Mathf.Clamp01(smoothness));
            SetFloat(material, "_CoatStrength", Mathf.Clamp01(coatStrength));
        }

        private static void SetFloat(Material material, string propertyName, float value)
        {
            // Game shader variants do not all expose the same property set.
            // Missing optional scalars are safe to ignore.
            if (material != null && material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static void BindTexture(Material material, string propertyName, Texture texture)
        {
            if (material == null || texture == null)
            {
                return;
            }

            if (!material.HasProperty(propertyName))
            {
                Mod.log.Warn($"[PO Material] Shader '{material.shader?.name}' does not expose " + $"expected texture property '{propertyName}'.");
                return;
            }

            material.SetTexture(propertyName, texture);
        }
    }
}