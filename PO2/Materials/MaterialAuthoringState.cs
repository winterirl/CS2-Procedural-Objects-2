using System;

namespace PO2.Materials
{
    // Mutable form state shared by import/edit UI. Setters normalize values here
    // so previews and the eventual manifest are always working from the same data.
    public sealed class MaterialAuthoringState
    {
        public const float kDefaultPhysicalSizeMeters = 2f;
        public const string kDefaultCategory = "Imported";

        public string DisplayName { get; private set; } = string.Empty;
        public string Category { get; private set; } = kDefaultCategory;
        public float PhysicalWidthMeters { get; private set; } = kDefaultPhysicalSizeMeters;
        public float PhysicalHeightMeters { get; private set; } = kDefaultPhysicalSizeMeters;
        public TextureMappingMode MappingMode { get; private set; } = TextureMappingMode.Repeat;
        public float Metallic { get; private set; }
        public float Smoothness { get; private set; }
        public float CoatStrength { get; private set; }
        public MaterialNormalMode NormalMode { get; private set; } = MaterialNormalMode.None;

        public void Reset()
        {
            DisplayName = string.Empty;
            Category = kDefaultCategory;
            PhysicalWidthMeters = kDefaultPhysicalSizeMeters;
            PhysicalHeightMeters = kDefaultPhysicalSizeMeters;
            MappingMode = TextureMappingMode.Repeat;
            Metallic = 0f;
            Smoothness = 0f;
            CoatStrength = 0f;
            NormalMode = MaterialNormalMode.None;
        }


        public void LoadFromDefinition(MaterialDefinition definition)
        {
            // Reset first because a definition may omit optional values that were
            // present on whatever the UI was editing previously.
            Reset();
            if (definition == null)
            {
                return;
            }

            DisplayName = definition.DisplayName ?? string.Empty;
            Category = definition.Category ?? kDefaultCategory;
            if (definition.HasPhysicalTileSize)
            {
                PhysicalWidthMeters = ClampPhysicalSize(definition.TileSizeMeters.x);
                PhysicalHeightMeters = ClampPhysicalSize(definition.TileSizeMeters.y);
            }

            MappingMode = definition.DefaultMappingMode == TextureMappingMode.Stretch ? TextureMappingMode.Stretch : TextureMappingMode.Repeat;
            Metallic = Clamp01(definition.Metallic);
            Smoothness = Clamp01(definition.Smoothness);
            CoatStrength = Clamp01(definition.CoatStrength);
            NormalMode = definition.NormalMode;
        }

        public void ResetForCandidate(MaterialImportCandidate candidate)
        {
            Reset();
            if (candidate == null)
            {
                return;
            }

            DisplayName = candidate.SuggestedDisplayName ?? string.Empty;
            // Normal mode is not independently editable yet. The selected file pair determines it.
            // TODO: Normal editing mode? Maybe? I'm sort of setup for it.
            NormalMode = string.IsNullOrWhiteSpace(candidate.NormalPath) ? MaterialNormalMode.None : MaterialNormalMode.TangentSpace;
        }

        public void SetDisplayName(string value)
        {
            DisplayName = value ?? string.Empty;
        }

        public void SetCategory(string value)
        {
            Category = value ?? string.Empty;
        }

        public void SetPhysicalWidthMeters(float value)
        {
            PhysicalWidthMeters = ClampPhysicalSize(value);
        }

        public void SetPhysicalHeightMeters(float value)
        {
            PhysicalHeightMeters = ClampPhysicalSize(value);
        }

        public void SetMappingMode(TextureMappingMode mappingMode)
        {
            if (mappingMode != TextureMappingMode.Repeat && mappingMode != TextureMappingMode.Stretch)
            {
                return;
            }

            MappingMode = mappingMode;
        }

        public void SetMetallic(float value)
        {
            Metallic = Clamp01(value);
        }

        public void SetSmoothness(float value)
        {
            Smoothness = Clamp01(value);
        }

        public void SetCoatStrength(float value)
        {
            CoatStrength = Clamp01(value);
        }

        private static float ClampPhysicalSize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return kDefaultPhysicalSizeMeters;
            }

            return Math.Max(0.1f, Math.Min(1000f, value));
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}