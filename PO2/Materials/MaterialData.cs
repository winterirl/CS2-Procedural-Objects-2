using UnityEngine;
using AssetHash128 = Colossal.Hash128;

namespace PO2.Materials
{
    // Material choice and PO-owned overrides for one Surface Area.
    // Only the field matching SourceKind is meaningful, but keeping one shape simplifies saves.
    public sealed class MaterialData
    {
        public MaterialSourceKind SourceKind { get; private set; }
        public string DefinitionId { get; private set; }
        public AssetHash128 SurfaceGuid { get; private set; }
        public VanillaSpecialMaterialKind VanillaSpecialKind { get; private set; }
        public TextureMappingData Mapping { get; }

        // Library defaults are copied on assignment. These values then belong to the PO.
        private float m_Metallic;
        private float m_Smoothness;
        private float m_CoatStrength;

        public float Metallic => m_Metallic;
        public float Smoothness => m_Smoothness;
        public float CoatStrength => m_CoatStrength;

        public static MaterialData CreateDefault()
        {
            return new MaterialData(MaterialSourceKind.LibraryMaterial, MaterialLibrary.DefaultMaterialId, default, VanillaSpecialMaterialKind.None, new TextureMappingData(TextureMappingMode.Repeat, Vector2.one, 0f, Vector2.zero), GetDefaultMetallic(MaterialLibrary.DefaultMaterialId), GetDefaultSmoothness(MaterialLibrary.DefaultMaterialId), GetDefaultCoatStrength(MaterialLibrary.DefaultMaterialId));
        }

        internal static MaterialData Restore(MaterialSourceKind sourceKind, string definitionId, AssetHash128 surfaceGuid, VanillaSpecialMaterialKind vanillaSpecialKind, TextureMappingData mapping, float metallic, float smoothness, float coatStrength)
        {
            // Clone mapping at the persistence boundary so restored areas never
            // accidentally share mutable mapping state.
            return new MaterialData(sourceKind, definitionId, surfaceGuid, vanillaSpecialKind, mapping?.Clone(), metallic, smoothness, coatStrength);
        }

        public MaterialData(string definitionId) : this(MaterialSourceKind.LibraryMaterial, definitionId, default, VanillaSpecialMaterialKind.None, new TextureMappingData(), GetDefaultMetallic(definitionId), GetDefaultSmoothness(definitionId), GetDefaultCoatStrength(definitionId))
        {
        }

        public MaterialData(string definitionId, TextureMappingData mapping) : this(MaterialSourceKind.LibraryMaterial, definitionId, default, VanillaSpecialMaterialKind.None, mapping, GetDefaultMetallic(definitionId), GetDefaultSmoothness(definitionId), GetDefaultCoatStrength(definitionId))
        {
        }

        private MaterialData(MaterialSourceKind sourceKind, string definitionId, AssetHash128 surfaceGuid, VanillaSpecialMaterialKind vanillaSpecialKind, TextureMappingData mapping, float metallic, float smoothness, float coatStrength)
        {
            SourceKind = sourceKind;
            DefinitionId = definitionId;
            SurfaceGuid = surfaceGuid;
            VanillaSpecialKind = vanillaSpecialKind;
            Mapping = mapping ?? new TextureMappingData();
            m_Metallic = Mathf.Clamp01(metallic);
            m_Smoothness = Mathf.Clamp01(smoothness);
            m_CoatStrength = Mathf.Clamp01(coatStrength);
        }

        internal TextureMappingMode GetLibraryAssignmentMappingMode(TextureMappingMode defaultMode)
        {
            // Keep an authored UV choice when swapping library textures. Native
            // conversions still adopt the selected material's default tiling.
            return SourceKind == MaterialSourceKind.LibraryMaterial && Mapping.Mode == TextureMappingMode.Original ? TextureMappingMode.Original : defaultMode;
        }

        public bool SetLibraryMaterial(string definitionId)
        {
            if (SourceKind == MaterialSourceKind.LibraryMaterial && DefinitionId == definitionId)
            {
                return false;
            }

            // Changing library definitions resets user-authorde scalars to that
            // definition's defaults. Mapping remains a Surface Area choice.
            SourceKind = MaterialSourceKind.LibraryMaterial;
            DefinitionId = definitionId;
            SurfaceGuid = default;
            VanillaSpecialKind = VanillaSpecialMaterialKind.None;
            ResetLibraryPropertiesToDefaults();
            return true;
        }

        public bool SetSurfaceAsset(AssetHash128 surfaceGuid)
        {
            if (!surfaceGuid.isValid)
            {
                return false;
            }

            if (SourceKind == MaterialSourceKind.SurfaceAsset && SurfaceGuid.Equals(surfaceGuid))
            {
                return false;
            }

            // Clear the other identities whenever source kind changes.
            // This keeps serialization unambiguous and avoids stale fallback behavior.
            SourceKind = MaterialSourceKind.SurfaceAsset;
            DefinitionId = null;
            SurfaceGuid = surfaceGuid;
            VanillaSpecialKind = VanillaSpecialMaterialKind.None;
            return true;
        }

        public bool SetVanillaSpecialMaterial(VanillaSpecialMaterialKind kind)
        {
            if (kind == VanillaSpecialMaterialKind.None)
            {
                return false;
            }

            if (SourceKind == MaterialSourceKind.VanillaSpecial && VanillaSpecialKind == kind)
            {
                return false;
            }

            SourceKind = MaterialSourceKind.VanillaSpecial;
            DefinitionId = null;
            SurfaceGuid = default;
            VanillaSpecialKind = kind;
            return true;
        }

        public bool SetMetallic(float value)
        {
            return SetScalar(ref m_Metallic, value);
        }

        public bool SetSmoothness(float value)
        {
            return SetScalar(ref m_Smoothness, value);
        }

        public bool SetCoatStrength(float value)
        {
            return SetScalar(ref m_CoatStrength, value);
        }

        public bool ResetLibraryPropertiesToDefaults()
        {
            if (SourceKind != MaterialSourceKind.LibraryMaterial || !MaterialLibrary.TryGet(DefinitionId, out MaterialDefinition definition))
            {
                return false;
            }

            // Do not report success unless at least one stored override changed.
            // Surface Area version counters depend on this return value.
            bool changed = false;
            changed |= SetMetallic(definition.Metallic);
            changed |= SetSmoothness(definition.Smoothness);
            changed |= SetCoatStrength(definition.CoatStrength);
            return changed;
        }

        public Vector2 GetBaseTileSizeMeters()
        {
            if (SourceKind == MaterialSourceKind.LibraryMaterial && MaterialLibrary.TryGet(DefinitionId, out MaterialDefinition definition))
            {
                return definition.TileSizeMeters;
            }

            // Native and special materials do not have PO2 manifest metadata.
            // A one-meter base keeps existing generated mapping behavior.
            return Vector2.one;
        }

        public Vector2 GetEffectiveTileSizeMeters()
        {
            Vector2 baseTileSize = GetBaseTileSizeMeters();
            return new Vector2(Mathf.Max(0.001f, baseTileSize.x * Mapping.TextureScale.x), Mathf.Max(0.001f, baseTileSize.y * Mapping.TextureScale.y));
        }

        public MaterialData Clone()
        {
            return new MaterialData(SourceKind, DefinitionId, SurfaceGuid, VanillaSpecialKind, Mapping.Clone(), Metallic, Smoothness, CoatStrength);
        }

        private static bool SetScalar(ref float field, float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(field, value))
            {
                return false;
            }

            field = value;
            return true;
        }

        private static float GetDefaultMetallic(string definitionId)
        {
            return MaterialLibrary.TryGet(definitionId, out MaterialDefinition definition) ? definition.Metallic : 0f;
        }

        private static float GetDefaultSmoothness(string definitionId)
        {
            return MaterialLibrary.TryGet(definitionId, out MaterialDefinition definition) ? definition.Smoothness : 0f;
        }

        private static float GetDefaultCoatStrength(string definitionId)
        {
            return MaterialLibrary.TryGet(definitionId, out MaterialDefinition definition) ? definition.CoatStrength : 0f;
        }
    }
}