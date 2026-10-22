using System;
using Colossal.UI.Binding;
using PO2.Geometry;
using PO2.Materials;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private MeshPart m_SurfaceAreaIdsPart;

        private uint m_SurfaceAreaIdsVersion;

        private int[] m_SurfaceAreaIds = Array.Empty<int>();

        private void RegisterSurfaceAreaSelectionBindings()
        {
            AddUpdateBinding(new GetterValueBinding<int[]>(kGroup, "selectedSurfaceAreaIds", GetSelectedSurfaceAreaIds, new ArrayWriter<int>(new DelegateWriter<int>((writer, value) => writer.Write(value)))));
            AddBinding(new TriggerBinding<int>(kGroup, "setSelectedSurfaceArea", GuardHistory<int>(id => { m_ProceduralObjectSystem.SetSelectedSurfaceArea(id); })));
        }

        private void RegisterTexturingStateBindings()
        {
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "selectedSurfaceAreaId", () => m_ProceduralObjectSystem.SelectedSurfaceAreaId));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "selectedSurfaceAreaCount", () => m_ProceduralObjectSystem.SelectedSurfaceAreaCount));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "mappingMode", () => m_ProceduralObjectSystem.SelectedMappingMode));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "mappingTextureWidth", () => m_ProceduralObjectSystem.SelectedMappingTextureWidthMeters));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "mappingTextureHeight", () => m_ProceduralObjectSystem.SelectedMappingTextureHeightMeters));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "mappingOffsetU", () => m_ProceduralObjectSystem.SelectedMappingOffsetU));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "mappingOffsetV", () => m_ProceduralObjectSystem.SelectedMappingOffsetV));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "mappingRotation", () => m_ProceduralObjectSystem.SelectedMappingRotation));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "colorMaskMode", () => m_ProceduralObjectSystem.SelectedColorMaskMode));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "colorMaskHasSource", () => m_ProceduralObjectSystem.SelectedColorMaskHasSource));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "surfaceAreaHasOriginalAppearance", () => m_ProceduralObjectSystem.SelectedSurfaceAreaHasOriginalAppearance));

            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "colorMask1Hex", () => m_ProceduralObjectSystem.SelectedColorMask1Hex));

            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "colorMask2Hex", () => m_ProceduralObjectSystem.SelectedColorMask2Hex));

            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "colorMask3Hex", () => m_ProceduralObjectSystem.SelectedColorMask3Hex));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "libraryMaterialCount", () => MaterialLibrary.CatalogCount));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "selectedLibraryMaterialIndex", () => m_ProceduralObjectSystem.SelectedLibraryMaterialIndex));

            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "selectedMaterialName", () => m_ProceduralObjectSystem.SelectedMaterialName));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "selectedMaterialSourceKind", () => m_ProceduralObjectSystem.SelectedMaterialSourceKind));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "selectedVanillaSpecialMaterialKind", () => m_ProceduralObjectSystem.SelectedVanillaSpecialMaterialKind));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "materialMetallic", () => m_ProceduralObjectSystem.SelectedMaterialMetallic));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "materialSmoothness", () => m_ProceduralObjectSystem.SelectedMaterialSmoothness));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "materialCoatStrength", () => m_ProceduralObjectSystem.SelectedMaterialCoatStrength));
        }

        private void RegisterMappingActionBindings()
        {
            AddBinding(CommandBinding("cycleMappingMode", GuardHistory(m_ProceduralObjectSystem.CycleSelectedMappingMode)));

            AddBinding(new TriggerBinding<float>(kGroup, "setMappingTextureWidth", GuardHistory<float>(m_ProceduralObjectSystem.SetSelectedMappingTextureWidth)));

            AddBinding(new TriggerBinding<float>(kGroup, "setMappingTextureHeight", GuardHistory<float>(m_ProceduralObjectSystem.SetSelectedMappingTextureHeight)));

            AddBinding(new TriggerBinding<float>(kGroup, "setMappingOffsetU", GuardHistory<float>(m_ProceduralObjectSystem.SetSelectedMappingOffsetU)));

            AddBinding(new TriggerBinding<float>(kGroup, "setMappingOffsetV", GuardHistory<float>(m_ProceduralObjectSystem.SetSelectedMappingOffsetV)));

            AddBinding(new TriggerBinding<float>(kGroup, "setMappingRotation", GuardHistory<float>(m_ProceduralObjectSystem.SetSelectedMappingRotation)));

            AddBinding(CommandBinding("resetMappingTransform", GuardHistory(m_ProceduralObjectSystem.ResetSelectedMappingTransform)));
        }

        private void RegisterSurfaceAreaActionBindings()
        {
            AddBinding(CommandBinding("previousSelectedSurfaceArea", GuardHistory(m_ProceduralObjectSystem.PreviousSelectedSurfaceArea)));

            AddBinding(CommandBinding("nextSelectedSurfaceArea", GuardHistory(m_ProceduralObjectSystem.NextSelectedSurfaceArea)));

            AddBinding(CommandBinding("newSurfaceAreaFromSelection", GuardHistory(m_ProceduralObjectToolSystem.NewSurfaceAreaFromSelection)));

            AddBinding(CommandBinding("assignSelectionToSurfaceArea", GuardHistory(m_ProceduralObjectToolSystem.AssignSelectionToActiveSurfaceArea)));

            AddBinding(CommandBinding("deleteSurfaceArea", GuardHistory(m_ProceduralObjectToolSystem.DeleteActiveSurfaceArea)));

            AddBinding(CommandBinding("previousLibraryMaterial", GuardHistory(m_ProceduralObjectSystem.PreviousLibraryMaterial)));

            AddBinding(CommandBinding("nextLibraryMaterial", GuardHistory(m_ProceduralObjectSystem.NextLibraryMaterial)));

            AddBinding(new TriggerBinding<string>(kGroup, "setSelectedLibraryMaterial", GuardHistory<string>(materialId => { m_ProceduralObjectSystem.SetSelectedLibraryMaterial(materialId); })));
        }

        private void RegisterAppearanceActionBindings()
        {
            AddBinding(CommandBinding("setVanillaSpecialGlass", GuardHistory(m_ProceduralObjectSystem.SetSelectedVanillaSpecialGlass)));

            AddBinding(CommandBinding("setVanillaSpecialGrass", GuardHistory(m_ProceduralObjectSystem.SetSelectedVanillaSpecialGrass)));

            AddBinding(CommandBinding("setVanillaSpecialWater", GuardHistory(m_ProceduralObjectSystem.SetSelectedVanillaSpecialWater)));

            AddBinding(CommandBinding("setColorMaskSource", GuardHistory(m_ProceduralObjectSystem.SetSelectedColorMaskSource)));

            AddBinding(CommandBinding("setColorMaskOff", GuardHistory(m_ProceduralObjectSystem.SetSelectedColorMaskDisabled)));

            AddBinding(CommandBinding("setColorMaskOverride", GuardHistory(m_ProceduralObjectSystem.SetSelectedColorMaskOverride)));

            AddBinding(new TriggerBinding<string>(kGroup, "setColorMask1Hex", GuardHistory<string>(m_ProceduralObjectSystem.SetSelectedColorMask1Hex)));

            AddBinding(new TriggerBinding<string>(kGroup, "setColorMask2Hex", GuardHistory<string>(m_ProceduralObjectSystem.SetSelectedColorMask2Hex)));

            AddBinding(new TriggerBinding<string>(kGroup, "setColorMask3Hex", GuardHistory<string>(m_ProceduralObjectSystem.SetSelectedColorMask3Hex)));

            AddBinding(CommandBinding("restoreSurfaceAreaOriginalAppearance", GuardHistory(m_ProceduralObjectSystem.RestoreSelectedSurfaceAreaOriginalAppearance)));

            AddBinding(CommandBinding("restoreSelectedObjectOriginalAppearance", GuardHistory(m_ProceduralObjectSystem.RestoreSelectedObjectOriginalAppearance)));

            AddBinding(new TriggerBinding<float>(kGroup, "setMaterialMetallic", GuardHistory<float>(m_ProceduralObjectSystem.SetSelectedMaterialMetallic)));
            AddBinding(new TriggerBinding<float>(kGroup, "setMaterialSmoothness", GuardHistory<float>(m_ProceduralObjectSystem.SetSelectedMaterialSmoothness)));
            AddBinding(new TriggerBinding<float>(kGroup, "setMaterialCoatStrength", GuardHistory<float>(m_ProceduralObjectSystem.SetSelectedMaterialCoatStrength)));

            AddBinding(new TriggerBinding(kGroup, "decreaseMaterialMetallic", GuardHistory(m_ProceduralObjectSystem.DecreaseSelectedMaterialMetallic)));

            AddBinding(new TriggerBinding(kGroup, "increaseMaterialMetallic", GuardHistory(m_ProceduralObjectSystem.IncreaseSelectedMaterialMetallic)));

            AddBinding(new TriggerBinding(kGroup, "decreaseMaterialSmoothness", GuardHistory(m_ProceduralObjectSystem.DecreaseSelectedMaterialSmoothness)));

            AddBinding(new TriggerBinding(kGroup, "increaseMaterialSmoothness", GuardHistory(m_ProceduralObjectSystem.IncreaseSelectedMaterialSmoothness)));

            AddBinding(new TriggerBinding(kGroup, "decreaseMaterialCoatStrength", GuardHistory(m_ProceduralObjectSystem.DecreaseSelectedMaterialCoatStrength)));

            AddBinding(new TriggerBinding(kGroup, "increaseMaterialCoatStrength", GuardHistory(m_ProceduralObjectSystem.IncreaseSelectedMaterialCoatStrength)));

            AddBinding(CommandBinding("resetMaterialProperties", GuardHistory(m_ProceduralObjectSystem.ResetSelectedMaterialProperties)));
        }

        private int[] GetSelectedSurfaceAreaIds()
        {
            var obj = m_ProceduralObjectSystem.SelectedObject;
            int index = m_ProceduralObjectSystem.SelectedPartIndex;
            var part = obj != null && index >= 0 && index < obj.Parts.Count ? obj.Parts[index] : null;
            if (part == null)
            {
                m_SurfaceAreaIdsPart = null;
                m_SurfaceAreaIds = Array.Empty<int>();
                return m_SurfaceAreaIds;
            }

            if (ReferenceEquals(part, m_SurfaceAreaIdsPart) && part.SurfaceAreaMembershipVersion == m_SurfaceAreaIdsVersion) return m_SurfaceAreaIds;

            var ids = new int[part.SurfaceAreas.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = part.SurfaceAreas[i].Id;
            m_SurfaceAreaIdsPart = part;
            m_SurfaceAreaIdsVersion = part.SurfaceAreaMembershipVersion;
            m_SurfaceAreaIds = ids;
            return ids;
        }
    }
}