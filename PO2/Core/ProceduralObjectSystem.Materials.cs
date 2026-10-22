using System;
using System.Collections.Generic;
using Game;
using Game.Tools;
using UnityEngine;
using PO2.Geometry;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Materials;

namespace PO2.Core
{
    public partial class ProceduralObjectSystem
    {
        private readonly int[] m_ColorMaskRgb = { -1, -1, -1 };
        private readonly string[] m_ColorMaskHex = new string[3];

        public bool SelectedObjectHasOriginalAppearance =>
            TryGetSelected(out ProceduralObject selected) && selected.HasOriginalAppearance;

        public int SelectedColorMaskMode
        {
            get { return TryGetSelectedSurfaceArea(out ProceduralObject _, out MeshPart _, out int _, out SurfaceArea area, out int _) ? (int)area.ColorMask.Mode : (int)ColorMaskMode.Disabled; }
        }

        public bool SelectedColorMaskHasSource
        {
            get { return TryGetSelectedSurfaceArea(out ProceduralObject _, out MeshPart _, out int _, out SurfaceArea area, out int _) && area.ColorMask.HasSourceMask; }
        }

        public bool SelectedSurfaceAreaHasOriginalAppearance
        {
            get { return TryGetSelectedSurfaceArea(out ProceduralObject _, out MeshPart _, out int _, out SurfaceArea area, out int _) && area.HasOriginalState; }
        }

        public string SelectedColorMask1Hex =>
            GetSelectedColorMaskHex(0);

        public string SelectedColorMask2Hex =>
            GetSelectedColorMaskHex(1);

        public string SelectedColorMask3Hex =>
            GetSelectedColorMaskHex(2);

        public int SelectedLibraryMaterialIndex
        {
            get
            {
                if (!TryGetSelectedSurfaceArea(out ProceduralObject _, out MeshPart _, out int _, out SurfaceArea area, out int _) || area.Material.SourceKind != MaterialSourceKind.LibraryMaterial)
                {
                    return -1;
                }

                return MaterialLibrary.IndexOfCatalogMaterial(area.Material.DefinitionId);
            }
        }

        public string SelectedLibraryMaterialId
        {
            get
            {
                if (!TryGetSelectedSurfaceArea(out ProceduralObject _, out MeshPart _, out int _, out SurfaceArea area, out int _) || area.Material.SourceKind != MaterialSourceKind.LibraryMaterial || MaterialLibrary.IndexOfCatalogMaterial(area.Material.DefinitionId) < 0)
                {
                    return string.Empty;
                }

                return area.Material.DefinitionId;
            }
        }

        public int SelectedMaterialSourceKind
        {
            get
            {
                if (!TryGetSelectedSurfaceArea(out ProceduralObject _, out MeshPart _, out int _, out SurfaceArea area, out int _))
                {
                    return -1;
                }

                return (int)area.Material.SourceKind;
            }
        }

        public int SelectedVanillaSpecialMaterialKind
        {
            get
            {
                if (!TryGetSelectedSurfaceArea(out ProceduralObject _, out MeshPart _, out int _, out SurfaceArea area, out int _) || area.Material.SourceKind != MaterialSourceKind.VanillaSpecial)
                {
                    return (int)VanillaSpecialMaterialKind.None;
                }

                return (int)area.Material.VanillaSpecialKind;
            }
        }

        public string SelectedMaterialName
        {
            get
            {
                if (!TryGetSelectedSurfaceArea(out ProceduralObject _, out MeshPart _, out int _, out SurfaceArea area, out int _))
                {
                    return "None";
                }

                MaterialData material = area.Material;
                if (material.SourceKind == MaterialSourceKind.LibraryMaterial && MaterialLibrary.TryGet(material.DefinitionId, out MaterialDefinition definition))
                {
                    return definition.DisplayName;
                }

                if (material.SourceKind == MaterialSourceKind.SurfaceAsset)
                {
                    return "Inherited Material";
                }

                if (material.SourceKind == MaterialSourceKind.VanillaSpecial && VanillaSpecialMaterialLibrary.TryGet(material.VanillaSpecialKind, out VanillaSpecialMaterialDefinition specialDefinition))
                {
                    return $"Vanilla {specialDefinition.DisplayName}";
                }

                return "Unknown";
            }
        }

        public float SelectedMaterialMetallic => GetSelectedMaterialScalar(material => material.Metallic);

        public float SelectedMaterialSmoothness => GetSelectedMaterialScalar(material => material.Smoothness);

        public float SelectedMaterialCoatStrength => GetSelectedMaterialScalar(material => material.CoatStrength);

        public void DecreaseSelectedMaterialMetallic()
        {
            AdjustSelectedMaterialScalar(MaterialScalar.Metallic, -0.05f);
        }

        public void IncreaseSelectedMaterialMetallic()
        {
            AdjustSelectedMaterialScalar(MaterialScalar.Metallic, 0.05f);
        }

        public void DecreaseSelectedMaterialSmoothness()
        {
            AdjustSelectedMaterialScalar(MaterialScalar.Smoothness, -0.05f);
        }

        public void IncreaseSelectedMaterialSmoothness()
        {
            AdjustSelectedMaterialScalar(MaterialScalar.Smoothness, 0.05f);
        }

        public void DecreaseSelectedMaterialCoatStrength()
        {
            AdjustSelectedMaterialScalar(MaterialScalar.CoatStrength, -0.05f);
        }

        public void IncreaseSelectedMaterialCoatStrength()
        {
            AdjustSelectedMaterialScalar(MaterialScalar.CoatStrength, 0.05f);
        }

        public void ResetSelectedMaterialProperties()
        {
            if (!TryGetSelectedSurfaceArea(out ProceduralObject selected, out MeshPart _, out int partIndex, out SurfaceArea area, out int _))
            {
                return;
            }

            if (selected.IsLocked)
            {
                return;
            }

            using (var history = BeginAppearanceHistory(selected, partIndex, area.Id, LocalizedText.For("PO2.History.Action.Material.ResetProperties")))
            {
                if (history == null) return;
                if (selected.ResetSurfaceAreaMaterialProperties(partIndex, area.Id))
                {
                    if (!history.Complete()) return;
                }
            }
        }

        public void PreviousLibraryMaterial()
        {
            StepLibraryMaterial(-1);
        }

        public void NextLibraryMaterial()
        {
            StepLibraryMaterial(1);
        }

        public int ReplaceLibraryMaterialReferencesWithDefault(string definitionId)
        {
            if (string.IsNullOrWhiteSpace(definitionId) || string.Equals(definitionId, MaterialLibrary.DefaultMaterialId, StringComparison.Ordinal))
            {
                return 0;
            }

            int replacementCount = 0;
            foreach (ProceduralObject proceduralObject in m_Objects.Values)
            {
                replacementCount += proceduralObject.ReplaceLibraryMaterialReferencesWithDefault(definitionId);
            }

            // An older action can reference this definition even when no live
            // Surface Area uses it anymore.
            ActionHistory.Clear();

            return replacementCount;
        }

        public int RefreshLibraryMaterialPhysicalSize(string definitionId)
        {
            if (string.IsNullOrWhiteSpace(definitionId))
            {
                return 0;
            }

            int refreshedCount = 0;
            foreach (ProceduralObject proceduralObject in m_Objects.Values)
            {
                bool objectChanged = false;

                for (int partIndex = 0; partIndex < proceduralObject.Parts.Count; partIndex++)
                {
                    IReadOnlyList<SurfaceArea> areas = proceduralObject.Parts[partIndex].SurfaceAreas;

                    for (int areaIndex = 0; areaIndex < areas.Count; areaIndex++)
                    {
                        if (!areas[areaIndex].RefreshLibraryPhysicalSize(definitionId))
                        {
                            continue;
                        }

                        refreshedCount++;
                        objectChanged = true;
                    }
                }

                if (objectChanged)
                {
                    proceduralObject.MarkDirty(ProceduralObjectDirtyFlags.Material);
                }
            }

            return refreshedCount;
        }

        public bool SetSelectedLibraryMaterial(string materialId)
        {
            if (!TryGetSelectedSurfaceArea(out ProceduralObject selected, out MeshPart _, out int partIndex, out SurfaceArea area, out int _))
            {
                return false;
            }

            if (selected.IsLocked)
            {
                return false;
            }

            if (!MaterialLibrary.TryGet(materialId, out MaterialDefinition definition) || definition.Id == MaterialLibrary.kPrototypeNoisyGray)
            {
                Mod.log.Warn($"[PO Material] Library material '{materialId}' is not available.");
                return false;
            }

            return ApplyLibraryMaterial(selected, partIndex, area.Id, definition);
        }

        private void StepLibraryMaterial(int direction)
        {
            if (!TryGetSelectedSurfaceArea(out ProceduralObject selected, out MeshPart _, out int partIndex, out SurfaceArea area, out int _))
            {
                return;
            }

            if (selected.IsLocked)
            {
                return;
            }

            int current = MaterialLibrary.IndexOfCatalogMaterial(area.Material.DefinitionId);
            int target = current < 0 ? (direction >= 0 ? 0 : MaterialLibrary.CatalogCount - 1) : current + direction;
            MaterialDefinition definition = MaterialLibrary.GetCatalogMaterial(target);
            if (definition == null)
            {
                Mod.log.Warn("[PO Material] Built-in material catalog is empty.");
                return;
            }

            ApplyLibraryMaterial(selected, partIndex, area.Id, definition);
        }

        private bool ApplyLibraryMaterial(ProceduralObject selected, int partIndex, int surfaceAreaId, MaterialDefinition definition)
        {
            if (partIndex < 0 || partIndex >= selected.Parts.Count || !selected.Parts[partIndex].TryGetSurfaceArea(surfaceAreaId, out var area)) return false;
            var mappingMode = area.Material.GetLibraryAssignmentMappingMode(definition.DefaultMappingMode);
            using (var history = BeginAppearanceHistory(selected, partIndex, surfaceAreaId, LocalizedText.For("PO2.History.Action.Material.Assign")))
            {
                if (history == null) return false;
                bool sourceChanged = selected.SetSurfaceAreaLibraryMaterial(partIndex, surfaceAreaId, definition.Id);
                bool mappingChanged = selected.SetSurfaceAreaMappingMode(partIndex, surfaceAreaId, mappingMode);
                bool scaleChanged = selected.SetSurfaceAreaTextureScale(partIndex, surfaceAreaId, Vector2.one);
                if (!sourceChanged && !mappingChanged && !scaleChanged)
                {
                    return history.Complete();
                }

                if (!history.Complete()) return false;
                return true;
            }
        }

        public void RestoreSelectedObjectOriginalAppearance()
        {
            ProceduralObject selected = SelectedObject;
            if (selected == null)
            {
                return;
            }

            if (selected.IsLocked)
            {
                return;
            }

            using (var history = BeginOriginalAppearanceHistory(selected))
            {
                if (history == null) return;
                if (selected.RestoreOriginalAppearance())
                {
                    if (!history.Complete()) return;
                    return;
                }
            }
        }

        public void RestoreSelectedSurfaceAreaOriginalAppearance()
        {
            if (!TryGetSelectedSurfaceArea(out ProceduralObject selected, out MeshPart _, out int partIndex, out SurfaceArea area, out int _))
            {
                return;
            }

            if (selected.IsLocked)
            {
                return;
            }

            if (!area.HasOriginalState)
            {
                return;
            }

            using (var history = BeginAppearanceHistory(selected, partIndex, area.Id, LocalizedText.For("PO2.History.Action.Material.RestoreOriginal")))
            {
                if (history == null) return;
                if (selected.RestoreSurfaceAreaOriginalAppearance(partIndex, area.Id))
                {
                    if (!history.Complete()) return;
                }
            }
        }

        public void SetSelectedVanillaSpecialGlass()
        {
            SetSelectedVanillaSpecialMaterial(VanillaSpecialMaterialKind.Glass);
        }

        public void SetSelectedVanillaSpecialGrass()
        {
            SetSelectedVanillaSpecialMaterial(VanillaSpecialMaterialKind.Grass);
        }

        public void SetSelectedVanillaSpecialWater()
        {
            SetSelectedVanillaSpecialMaterial(VanillaSpecialMaterialKind.Water);
        }

        public void SetSelectedVanillaSpecialMaterial(VanillaSpecialMaterialKind kind)
        {
            if (!TryGetSelectedSurfaceArea(out ProceduralObject selected, out MeshPart _, out int partIndex, out SurfaceArea area, out int _))
            {
                return;
            }

            if (selected.IsLocked)
            {
                return;
            }

            if (!VanillaSpecialMaterialLibrary.TryGet(kind, out VanillaSpecialMaterialDefinition definition))
            {
                Mod.log.Warn($"[PO Material] Unsupported vanilla special material kind {kind}.");
                return;
            }

            using (var history = BeginAppearanceHistory(selected, partIndex, area.Id, LocalizedText.For("PO2.History.Action.Material.AssignSpecial")))
            {
                if (history == null) return;
                bool sourceChanged = selected.SetSurfaceAreaVanillaSpecialMaterial(partIndex, area.Id, kind);
                bool mappingChanged = selected.SetSurfaceAreaMappingMode(partIndex, area.Id, definition.DefaultMappingMode);
                if (!history.Complete()) return;
                if (!sourceChanged && !mappingChanged)
                {
                    return;
                }
            }
        }

        private float GetSelectedMaterialScalar(Func<MaterialData, float> selector)
        {
            if (!TryGetSelectedSurfaceArea(out ProceduralObject _, out MeshPart _, out int _, out SurfaceArea area, out int _))
            {
                return 0f;
            }

            return selector(area.Material);
        }

        public void SetSelectedMaterialMetallic(float value) => SetSelectedMaterialScalar(MaterialScalar.Metallic, value);
        public void SetSelectedMaterialSmoothness(float value) => SetSelectedMaterialScalar(MaterialScalar.Smoothness, value);
        public void SetSelectedMaterialCoatStrength(float value) => SetSelectedMaterialScalar(MaterialScalar.CoatStrength, value);

        private void AdjustSelectedMaterialScalar(MaterialScalar scalar, float delta)
        {
            SetSelectedMaterialScalar(scalar, GetSelectedMaterialScalar(material => GetMaterialScalar(material, scalar)) + delta);
        }

        private static LocalizedText MaterialScalarName(MaterialScalar scalar) => scalar == MaterialScalar.Metallic ? LocalizedText.For("PO2.Texturing.MaterialSettings.Metallic.Label") : scalar == MaterialScalar.Smoothness ? LocalizedText.For("PO2.Texturing.MaterialSettings.Smoothness.Label") : LocalizedText.For("PO2.Texturing.MaterialSettings.Coat.Label");

        private void SetSelectedMaterialScalar(MaterialScalar scalar, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            if (!TryGetSelectedSurfaceArea(out ProceduralObject selected, out MeshPart _, out int partIndex, out SurfaceArea area, out int _))
            {
                return;
            }

            if (selected.IsLocked)
            {
                return;
            }

            using (var history = BeginAppearanceHistory(selected, partIndex, area.Id, LocalizedText.For("PO2.History.Action.Material.ChangeProperty", ("property", MaterialScalarName(scalar)))))
            {
                if (history == null) return;
                bool changed;
                switch (scalar)
                {
                    case MaterialScalar.Metallic:
                        changed = selected.SetSurfaceAreaMetallic(partIndex, area.Id, value);
                        break;
                    case MaterialScalar.Smoothness:
                        changed = selected.SetSurfaceAreaSmoothness(partIndex, area.Id, value);
                        break;
                    case MaterialScalar.CoatStrength:
                        changed = selected.SetSurfaceAreaCoatStrength(partIndex, area.Id, value);
                        break;
                    default:
                        return;
                }

                if (!history.Complete()) return;
            }
        }

        private static float GetMaterialScalar(MaterialData material, MaterialScalar scalar)
        {
            switch (scalar)
            {
                case MaterialScalar.Metallic:
                    return material.Metallic;
                case MaterialScalar.Smoothness:
                    return material.Smoothness;
                case MaterialScalar.CoatStrength:
                    return material.CoatStrength;
                default:
                    return 0f;
            }
        }

        private enum MaterialScalar
        {
            Metallic,
            Smoothness,
            CoatStrength,
        }

        public void ToggleSelectedReceivesDecals()
        {
            if (!TryGetSelected(out ProceduralObject selected))
            {
                return;
            }

            if (selected.IsLocked)
            {
                return;
            }

            bool receivesDecals = !selected.ReceivesDecals;
            using (var history = BeginObjectHistory(selected, ObjectValueKind.ReceivesDecals, LocalizedText.For("PO2.History.Action.Object.SetDecalReception")))
            {
                if (history == null) return;
                if (!selected.SetReceivesDecals(receivesDecals)) return;
                if (!history.Complete()) return;
            }
        }

        private bool TryGetEditableSelectedSurfaceArea(string operation, out ProceduralObject selected, out int partIndex, out SurfaceArea area)
        {
            if (!TryGetSelectedSurfaceArea(out selected, out MeshPart _, out partIndex, out area, out int _))
            {
                return false;
            }

            if (selected.IsLocked)
            {
                return false;
            }

            return true;
        }
    }
}