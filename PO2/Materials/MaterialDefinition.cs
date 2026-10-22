using UnityEngine;

namespace PO2.Materials
{
    // Shared, immutable catalog entry. Per-PO scalar and mapping overrides live in MaterialData.
    public sealed class MaterialDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Category { get; }
        public MaterialShaderKind ShaderKind { get; }
        public MaterialLibrarySource LibrarySource { get; }
        public string BaseColorPath { get; }
        public string NormalPath { get; }
        public string MaskMapPath { get; }
        public string ControlMaskPath { get; }
        public bool HasPhysicalTileSize { get; }
        public Vector2 TileSizeMeters { get; }
        public TextureMappingMode DefaultMappingMode { get; }
        public float Metallic { get; }
        public float Smoothness { get; }
        public float CoatStrength { get; }
        public MaterialNormalMode NormalMode { get; }

        public MaterialDefinition(string id, string displayName, string category, MaterialShaderKind shaderKind, MaterialLibrarySource librarySource, string baseColorPath, string normalPath, string maskMapPath, string controlMaskPath, bool hasPhysicalTileSize, Vector2 tileSizeMeters, TextureMappingMode defaultMappingMode, float metallic, float smoothness, float coatStrength, MaterialNormalMode normalMode)
        {
            Id = id;
            DisplayName = displayName;
            Category = category;
            ShaderKind = shaderKind;
            LibrarySource = librarySource;
            BaseColorPath = baseColorPath;
            NormalPath = normalPath;
            MaskMapPath = maskMapPath;
            ControlMaskPath = controlMaskPath;
            HasPhysicalTileSize = hasPhysicalTileSize;
            // Keep a usable value even for programmatically created definitions.
            // Manifest validation applies stricter rules before reaching this.
            TileSizeMeters = new Vector2(Mathf.Max(0.001f, tileSizeMeters.x), Mathf.Max(0.001f, tileSizeMeters.y));
            DefaultMappingMode = defaultMappingMode;
            Metallic = Mathf.Clamp01(metallic);
            Smoothness = Mathf.Clamp01(smoothness);
            CoatStrength = Mathf.Clamp01(coatStrength);
            NormalMode = normalMode;
        }
    }
}