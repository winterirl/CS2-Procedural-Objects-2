using System;

namespace PO2.Materials
{
    // Complete, immutable handoff from the import UI to the disk importer.
    public sealed class MaterialImportRequest
    {
        public string DisplayName { get; }
        public string Category { get; }
        public string BaseColorPath { get; }
        public string NormalPath { get; }
        public float PhysicalWidthMeters { get; }
        public float PhysicalHeightMeters { get; }
        public TextureMappingMode MappingMode { get; }
        public float Metallic { get; }
        public float Smoothness { get; }
        public float CoatStrength { get; }
        public MaterialNormalMode NormalMode { get; }

        public MaterialImportRequest(string displayName, string category, string baseColorPath, string normalPath, float physicalWidthMeters, float physicalHeightMeters, TextureMappingMode mappingMode, float metallic, float smoothness, float coatStrength, MaterialNormalMode normalMode)
        {
            DisplayName = displayName ?? string.Empty;
            Category = category ?? string.Empty;
            BaseColorPath = baseColorPath ?? throw new ArgumentNullException(nameof(baseColorPath));
            NormalPath = normalPath;
            PhysicalWidthMeters = physicalWidthMeters;
            PhysicalHeightMeters = physicalHeightMeters;
            MappingMode = mappingMode;
            Metallic = metallic;
            Smoothness = smoothness;
            CoatStrength = coatStrength;
            NormalMode = normalMode;
        }
    }
}