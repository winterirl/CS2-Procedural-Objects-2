#pragma warning disable CS0649 // Fields are populated by Colossal JSON deserialization.

using System;

namespace PO2.Materials
{
    // Mirrors material.json exactly for Colossal's field-based serializer.
    // Keep validation and runtime conveniences in MaterialLibrary, not this DTO.
    [Serializable]
    internal sealed class MaterialManifest
    {
        public int schemaVersion;
        public int catalogOrder;
        public string id;
        public string displayName;
        public string category;
        public MaterialManifestTextures textures;
        public MaterialManifestPhysicalSize physicalSize;
        public MaterialManifestDefaults defaults;
    }

    [Serializable]
    internal sealed class MaterialManifestTextures
    {
        public string baseColor;
        public string normal;
    }

    [Serializable]
    internal sealed class MaterialManifestPhysicalSize
    {
        public float widthMeters;
        public float heightMeters;
    }

    [Serializable]
    internal sealed class MaterialManifestDefaults
    {
        public string mappingMode;
        public float metallic;
        public float smoothness;
        public float coat;
        public string normalMode;
    }
}

#pragma warning restore CS0649