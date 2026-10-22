using System;
using System.Collections.Generic;
using Colossal.UI.Binding;
using PO2.Interface.Bindings;
using PO2.Interface.Models;
using PO2.Localization;
using PO2.Materials;
using PO2.Tools.ObjectTool;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private MaterialManagementView m_MaterialManagementView = MaterialManagementView.None;

        private MaterialLibraryEntry[] m_MaterialLibraryEntries = Array.Empty<MaterialLibraryEntry>();

        private LocalizedText m_MaterialLibraryStatus = LocalizedText.Empty;

        private string m_SelectedManagedMaterialId = string.Empty;

        private void RegisterMaterialLibraryStateBindings()
        {
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "materialManagementView", () => (int)m_MaterialManagementView));

            AddUpdateBinding(new GetterValueBinding<MaterialLibraryEntry[]>(kGroup, "materialLibraryEntries", () => m_MaterialLibraryEntries, new ArrayWriter<MaterialLibraryEntry>(new ValueWriter<MaterialLibraryEntry>())));

            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "selectedManagedMaterialId", () => m_SelectedManagedMaterialId));

            AddUpdateBinding(new GetterValueBinding<LocalizedText>(kGroup, "materialLibraryStatus", () => m_MaterialLibraryStatus, LocalizedTextWriter.s_Instance));

            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "managedMaterialDisplayName", () => TryGetSelectedManagedMaterial(out MaterialDefinition definition) ? definition.DisplayName : string.Empty));

            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "managedMaterialCategory", () => TryGetSelectedManagedMaterial(out MaterialDefinition definition) ? definition.Category : string.Empty));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "managedMaterialSource", () => TryGetSelectedManagedMaterial(out MaterialDefinition definition) ? (int)definition.LibrarySource : -1));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canEditManagedMaterial", () => TryGetSelectedManagedMaterial(out MaterialDefinition definition) && definition.LibrarySource == MaterialLibrarySource.User));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canDeleteManagedMaterial", () => TryGetSelectedManagedMaterial(out MaterialDefinition definition) && definition.LibrarySource == MaterialLibrarySource.User));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "managedMaterialMappingMode", () => TryGetSelectedManagedMaterial(out MaterialDefinition definition) ? (int)definition.DefaultMappingMode : -1));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "managedMaterialHasPhysicalSize", () => TryGetSelectedManagedMaterial(out MaterialDefinition definition) && definition.HasPhysicalTileSize));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "managedMaterialPhysicalWidth", () => TryGetSelectedManagedMaterial(out MaterialDefinition definition) ? definition.TileSizeMeters.x : 0f));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "managedMaterialPhysicalHeight", () => TryGetSelectedManagedMaterial(out MaterialDefinition definition) ? definition.TileSizeMeters.y : 0f));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "managedMaterialMetallic", () => TryGetSelectedManagedMaterial(out MaterialDefinition definition) ? definition.Metallic : 0f));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "managedMaterialSmoothness", () => TryGetSelectedManagedMaterial(out MaterialDefinition definition) ? definition.Smoothness : 0f));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "managedMaterialCoatStrength", () => TryGetSelectedManagedMaterial(out MaterialDefinition definition) ? definition.CoatStrength : 0f));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "managedMaterialHasNormal", () => TryGetSelectedManagedMaterial(out MaterialDefinition definition) && definition.NormalMode != MaterialNormalMode.None && !string.IsNullOrWhiteSpace(definition.NormalPath)));
        }

        private void RegisterMaterialLibraryActionBindings()
        {
            AddBinding(CommandBinding("openMaterialLibraryView", () => NavigateMainMode(Workspace.MaterialLibrary)));

            AddBinding(new TriggerBinding<string>(kGroup, "selectManagedMaterial", GuardHistory<string>(SelectManagedMaterial)));

            AddBinding(new TriggerBinding(kGroup, "openMaterialImportView", GuardHistory(OpenMaterialImportView)));

            AddBinding(new TriggerBinding(kGroup, "openManagedMaterialEditView", GuardHistory(OpenManagedMaterialEditView)));

            AddBinding(new TriggerBinding(kGroup, "deleteManagedMaterial", GuardHistory(DeleteManagedMaterial)));
        }

        private void OpenMaterialLibraryView()
        {
            if (m_MaterialImportBusy)
            {
                return;
            }

            CloseSaveManager();
            CancelPendingMaterialPreviewDispose();
            m_MaterialLibraryStatus = LocalizedText.Empty;
            // Rebuild on entry because imports/edits may have reloaded the catalog
            // since this view was last open.
            RebuildMaterialLibraryEntries();

            string selectedMaterialId = m_ProceduralObjectSystem.SelectedLibraryMaterialId;
            if (!string.IsNullOrWhiteSpace(selectedMaterialId))
            {
                m_SelectedManagedMaterialId = selectedMaterialId;
            }

            EnsureManagedMaterialSelection();
            m_MaterialManagementView = MaterialManagementView.Library;
            m_ProceduralObjectToolSystem.EnterMaterialLibraryWorkspace();
            RefreshManagedMaterialPreview();
        }

        private void CloseMaterialLibraryView()
        {
            if (m_MaterialImportBusy)
            {
                return;
            }

            m_MaterialManagementView = MaterialManagementView.None;
            m_ProceduralObjectToolSystem.ExitMaterialLibraryWorkspace();
            m_MaterialPreviewService.Hide();
            ScheduleMaterialPreviewDispose();
        }

        private void SelectManagedMaterial(string materialId)
        {
            if (string.IsNullOrWhiteSpace(materialId))
            {
                return;
            }

            int index = MaterialLibrary.IndexOfCatalogMaterial(materialId);
            if (index < 0)
            {
                return;
            }

            MaterialDefinition definition = MaterialLibrary.GetCatalogMaterial(index);
            if (definition == null)
            {
                return;
            }

            m_SelectedManagedMaterialId = definition.Id;
            m_MaterialLibraryStatus = LocalizedText.Empty;
            RefreshManagedMaterialPreview();
        }

        private void OpenManagedMaterialEditView()
        {
            if (m_MaterialImportBusy || !TryGetSelectedManagedMaterial(out MaterialDefinition definition) || definition.LibrarySource != MaterialLibrarySource.User)
            {
                return;
            }

            CancelPendingMaterialPreviewDispose();
            ClearMaterialImportState();
            m_MaterialAuthoringState.LoadFromDefinition(definition);
            m_MaterialManagementView = MaterialManagementView.EditUser;
            m_MaterialPreviewService.ShowDefinition(definition);
        }

        private void DeleteManagedMaterial()
        {
            if (m_MaterialImportBusy || m_MaterialManagementView != MaterialManagementView.Library || !TryGetSelectedManagedMaterial(out MaterialDefinition definition) || definition.LibrarySource != MaterialLibrarySource.User)
            {
                return;
            }

            string materialId = definition.Id;
            string displayName = definition.DisplayName;
            m_MaterialImportBusy = true;

            try
            {
                // Move loaded Surface Areas off the ID before deleting its files.
                // They remain renderable even if disk deletion later fails.
                int fallbackCount = m_ProceduralObjectSystem.ReplaceLibraryMaterialReferencesWithDefault(materialId);

                UserMaterialDeleteResult result = UserMaterialService.DeleteDefinition(materialId);

                RebuildMaterialLibraryEntries();

                if (!result.Success)
                {
                    EnsureManagedMaterialSelection();
                    RefreshManagedMaterialPreview();
                    m_MaterialLibraryStatus = fallbackCount > 0 ? LocalizedText.For("PO2.Materials.Delete.Result.FailedWithFallback", ("reason", result.ErrorMessage), ("count", fallbackCount)) : LocalizedText.For("PO2.Materials.Delete.Result.Failed", ("reason", result.ErrorMessage));
                    return;
                }

                m_SelectedManagedMaterialId = string.Empty;
                EnsureManagedMaterialSelection();
                RefreshManagedMaterialPreview();

                string deletedName = string.IsNullOrWhiteSpace(result.DisplayName) ? displayName : result.DisplayName;
                m_MaterialLibraryStatus = fallbackCount > 0 ? LocalizedText.For("PO2.Materials.Delete.Result.SucceededWithFallback", ("materialName", deletedName), ("count", fallbackCount)) : LocalizedText.For("PO2.Materials.Delete.Result.Succeeded", ("materialName", deletedName));
            }
            finally
            {
                m_MaterialImportBusy = false;
            }
        }

        private bool TryGetSelectedManagedMaterial(out MaterialDefinition definition)
        {
            definition = null;

            if (string.IsNullOrWhiteSpace(m_SelectedManagedMaterialId))
            {
                return false;
            }

            return MaterialLibrary.TryGet(m_SelectedManagedMaterialId, out definition) && definition != null && MaterialLibrary.IndexOfCatalogMaterial(definition.Id) >= 0;
        }

        private void EnsureManagedMaterialSelection()
        {
            if (!string.IsNullOrWhiteSpace(m_SelectedManagedMaterialId) && MaterialLibrary.IndexOfCatalogMaterial(m_SelectedManagedMaterialId) >= 0)
            {
                return;
            }

            // Catalog reload may remove the selected user material.
            // Fall back to the first real catalog entry rather than retaining a stale ID.
            MaterialDefinition first = MaterialLibrary.GetCatalogMaterial(0);
            m_SelectedManagedMaterialId = first?.Id ?? string.Empty;
        }

        private void RebuildMaterialLibraryEntries()
        {
            var entries = new List<MaterialLibraryEntry>(MaterialLibrary.CatalogCount);
            for (int i = 0; i < MaterialLibrary.CatalogCount; i++)
            {
                MaterialDefinition definition = MaterialLibrary.GetCatalogMaterial(i);
                if (definition == null) continue;
                entries.Add(new MaterialLibraryEntry(definition.Id, definition.DisplayName, definition.Category, (int)definition.LibrarySource));
            }

            m_MaterialLibraryEntries = entries.ToArray();
        }
    }
}