using System;
using System.Collections.Generic;
using System.IO;
using Colossal.UI.Binding;
using UnityEngine;
using PO2.Interface.Bindings;
using PO2.Interface.Models;
using PO2.Localization;
using PO2.Materials;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private readonly List<MaterialImportCandidate> m_MaterialImportCandidates = new List<MaterialImportCandidate>();

        private readonly MaterialAuthoringState m_MaterialAuthoringState = new MaterialAuthoringState();

        private int m_SelectedMaterialImportCandidateIndex = -1;

        private string[] m_MaterialImportCandidateNames = Array.Empty<string>();

        private MaterialImportNotice m_MaterialImportNotice;

        private bool m_MaterialImportBusy;

        private void RegisterMaterialImportStateBindings()
        {
            AddUpdateBinding(new GetterValueBinding<string[]>(kGroup, "materialImportCandidates",
                // Candidate/library lists change as a unit; editor fields stay separate.
                () => m_MaterialImportCandidateNames, new ArrayWriter<string>(new Colossal.UI.Binding.StringWriter())));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "materialImportCandidateCount", () => m_MaterialImportCandidates.Count));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "selectedMaterialImportCandidateIndex", () => m_SelectedMaterialImportCandidateIndex));

            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "materialImportBaseColorName", GetSelectedImportBaseColorName));

            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "materialImportNormalName", GetSelectedImportNormalName, new Colossal.UI.Binding.StringWriter().Nullable()));

            AddUpdateBinding(new GetterValueBinding<LocalizedText>(kGroup, "materialImportStatus", () => m_MaterialImportNotice.Text, LocalizedTextWriter.s_Instance));
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "materialImportMessageKind", () => (int)m_MaterialImportNotice.Kind));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "materialImportBusy", () => m_MaterialImportBusy));
        }

        private void RegisterMaterialAuthoringStateBindings()
        {
            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "materialImportDisplayName", () => m_MaterialAuthoringState.DisplayName));

            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "materialImportCategory", () => m_MaterialAuthoringState.Category));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "materialImportPhysicalWidth", () => m_MaterialAuthoringState.PhysicalWidthMeters));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "materialImportPhysicalHeight", () => m_MaterialAuthoringState.PhysicalHeightMeters));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "materialImportMappingMode", () => (int)m_MaterialAuthoringState.MappingMode));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "materialImportMetallic", () => m_MaterialAuthoringState.Metallic));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "materialImportSmoothness", () => m_MaterialAuthoringState.Smoothness));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "materialImportCoatStrength", () => m_MaterialAuthoringState.CoatStrength));
        }

        private void RegisterMaterialAuthoringActionBindings()
        {
            AddBinding(new TriggerBinding(kGroup, "cancelMaterialImportView", GuardHistory(CloseMaterialImportView)));

            AddBinding(new TriggerBinding<string>(kGroup, "scanMaterialImportDirectory", GuardHistory<string>(ScanMaterialImportDirectory)));

            AddBinding(new TriggerBinding<int>(kGroup, "selectMaterialImportCandidate", GuardHistory<int>(SelectMaterialImportCandidate)));

            AddBinding(new TriggerBinding<string>(kGroup, "setMaterialImportDisplayName", GuardHistory<string>(value => m_MaterialAuthoringState.SetDisplayName(value))));

            AddBinding(new TriggerBinding<string>(kGroup, "setMaterialImportCategory", GuardHistory<string>(value => m_MaterialAuthoringState.SetCategory(value))));

            AddBinding(new TriggerBinding(kGroup, "decreaseMaterialImportWidth", GuardHistory(() => AdjustMaterialImportPhysicalSize(width: true, increase: false))));

            AddBinding(new TriggerBinding(kGroup, "increaseMaterialImportWidth", GuardHistory(() => AdjustMaterialImportPhysicalSize(width: true, increase: true))));

            AddBinding(new TriggerBinding<float>(kGroup, "setMaterialImportWidth", GuardHistory<float>(value => SetMaterialImportPhysicalSize(width: true, value))));

            AddBinding(new TriggerBinding(kGroup, "decreaseMaterialImportHeight", GuardHistory(() => AdjustMaterialImportPhysicalSize(width: false, increase: false))));

            AddBinding(new TriggerBinding(kGroup, "increaseMaterialImportHeight", GuardHistory(() => AdjustMaterialImportPhysicalSize(width: false, increase: true))));

            AddBinding(new TriggerBinding<float>(kGroup, "setMaterialImportHeight", GuardHistory<float>(value => SetMaterialImportPhysicalSize(width: false, value))));

            AddBinding(new TriggerBinding(kGroup, "setMaterialImportMappingRepeat", GuardHistory(() => SetMaterialImportMappingMode(TextureMappingMode.Repeat))));

            AddBinding(new TriggerBinding(kGroup, "setMaterialImportMappingStretch", GuardHistory(() => SetMaterialImportMappingMode(TextureMappingMode.Stretch))));

            AddBinding(new TriggerBinding(kGroup, "decreaseMaterialImportMetallic", GuardHistory(() => AdjustMaterialImportMetallic(-0.05f))));

            AddBinding(new TriggerBinding(kGroup, "increaseMaterialImportMetallic", GuardHistory(() => AdjustMaterialImportMetallic(0.05f))));

            AddBinding(new TriggerBinding<float>(kGroup, "setMaterialImportMetallic", GuardHistory<float>(SetMaterialImportMetallic)));

            AddBinding(new TriggerBinding(kGroup, "decreaseMaterialImportSmoothness", GuardHistory(() => AdjustMaterialImportSmoothness(-0.05f))));

            AddBinding(new TriggerBinding(kGroup, "increaseMaterialImportSmoothness", GuardHistory(() => AdjustMaterialImportSmoothness(0.05f))));

            AddBinding(new TriggerBinding<float>(kGroup, "setMaterialImportSmoothness", GuardHistory<float>(SetMaterialImportSmoothness)));

            AddBinding(new TriggerBinding(kGroup, "decreaseMaterialImportCoatStrength", GuardHistory(() => AdjustMaterialImportCoatStrength(-0.05f))));

            AddBinding(new TriggerBinding(kGroup, "increaseMaterialImportCoatStrength", GuardHistory(() => AdjustMaterialImportCoatStrength(0.05f))));

            AddBinding(new TriggerBinding<float>(kGroup, "setMaterialImportCoatStrength", GuardHistory<float>(SetMaterialImportCoatStrength)));

            AddBinding(new TriggerBinding(kGroup, "saveMaterialImport", GuardHistory(ImportSelectedMaterialCandidate)));

            AddBinding(new TriggerBinding(kGroup, "saveManagedMaterialEdit", GuardHistory(SaveManagedMaterialEdit)));
        }

        private void OpenMaterialImportView()
        {
            if (m_MaterialImportBusy)
            {
                return;
            }

            CancelPendingMaterialPreviewDispose();
            m_MaterialPreviewService.Hide();
            ClearMaterialImportState();
            m_MaterialManagementView = MaterialManagementView.ImportNew;
        }

        private void CloseMaterialImportView()
        {
            if (m_MaterialImportBusy)
            {
                return;
            }

            m_MaterialManagementView = MaterialManagementView.Library;
            ClearMaterialImportState();
            RebuildMaterialLibraryEntries();
            EnsureManagedMaterialSelection();
            RefreshManagedMaterialPreview();
        }

        private void ScanMaterialImportDirectory(string sourceDirectory)
        {
            if (m_MaterialImportBusy)
            {
                return;
            }

            try
            {
                IReadOnlyList<MaterialImportCandidate> candidates = MaterialImporter.ScanDirectory(sourceDirectory);

                m_MaterialImportCandidates.Clear();
                for (int i = 0; i < candidates.Count; i++)
                {
                    m_MaterialImportCandidates.Add(candidates[i]);
                }

                // Selecting the first result also resets the editor and preview to that candidate.
                // A previous candidate's values must not leak in.
                m_SelectedMaterialImportCandidateIndex = m_MaterialImportCandidates.Count > 0 ? 0 : -1;
                RebuildMaterialImportCandidateNames();
                m_MaterialAuthoringState.ResetForCandidate(GetSelectedImportCandidate());
                RefreshMaterialPreview();

                if (m_MaterialImportCandidates.Count == 0)
                {
                    m_MaterialImportNotice = new MaterialImportNotice(MaterialImportMessageKind.ScanResult, LocalizedText.For("PO2.MaterialEditor.Scan.Result.Empty"));
                }
                else
                {
                    m_MaterialImportNotice = new MaterialImportNotice(MaterialImportMessageKind.ScanResult, m_MaterialImportCandidates.Count == 1 ? LocalizedText.For("PO2.MaterialEditor.Scan.Result.Singular", ("count", m_MaterialImportCandidates.Count)) : LocalizedText.For("PO2.MaterialEditor.Scan.Result.Plural", ("count", m_MaterialImportCandidates.Count)));
                }
            }
            catch (Exception exception)
            {
                m_MaterialImportCandidates.Clear();
                m_SelectedMaterialImportCandidateIndex = -1;
                m_MaterialImportCandidateNames = Array.Empty<string>();
                m_MaterialPreviewService.Hide();
                ScheduleMaterialPreviewDispose();
                m_MaterialImportNotice = new MaterialImportNotice(MaterialImportMessageKind.Error, LocalizedText.For("PO2.MaterialEditor.Scan.Error.Failed", ("reason", LocalizedText.FromException(exception))));

                Mod.log.Warn($"[PO Material Import] Directory scan failed: {exception.Message}");
            }
        }

        private void SelectMaterialImportCandidate(int index)
        {
            if (m_MaterialImportBusy || index < 0 || index >= m_MaterialImportCandidates.Count)
            {
                return;
            }

            m_SelectedMaterialImportCandidateIndex = index;
            m_MaterialAuthoringState.ResetForCandidate(GetSelectedImportCandidate());
            RefreshMaterialPreview();
            m_MaterialImportNotice = default;
        }

        private void ImportSelectedMaterialCandidate()
        {
            if (m_MaterialImportBusy)
            {
                return;
            }

            MaterialImportCandidate candidate = GetSelectedImportCandidate();
            if (candidate == null)
            {
                m_MaterialImportNotice = new MaterialImportNotice(MaterialImportMessageKind.Error, LocalizedText.For("PO2.MaterialEditor.Validation.CandidateRequired"));
                return;
            }

            string displayName = m_MaterialAuthoringState.DisplayName?.Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                m_MaterialImportNotice = new MaterialImportNotice(MaterialImportMessageKind.Error, LocalizedText.For("PO2.MaterialEditor.Validation.NameRequired"));
                return;
            }

            string category = m_MaterialAuthoringState.Category?.Trim();
            if (string.IsNullOrWhiteSpace(category))
            {
                m_MaterialImportNotice = new MaterialImportNotice(MaterialImportMessageKind.Error, LocalizedText.For("PO2.MaterialEditor.Validation.CategoryRequired"));
                return;
            }

            // Busy disables every close/save/delete route until the synchronous
            // filesystem transaction has finished.
            m_MaterialImportBusy = true;

            try
            {
                MaterialImportResult result = MaterialImporter.Import(new MaterialImportRequest(displayName, category, candidate.BaseColorPath, candidate.NormalPath, m_MaterialAuthoringState.PhysicalWidthMeters, m_MaterialAuthoringState.PhysicalHeightMeters, m_MaterialAuthoringState.MappingMode, m_MaterialAuthoringState.Metallic, m_MaterialAuthoringState.Smoothness, m_MaterialAuthoringState.CoatStrength, m_MaterialAuthoringState.NormalMode));

                if (!result.Success)
                {
                    m_MaterialImportNotice = new MaterialImportNotice(MaterialImportMessageKind.Error, LocalizedText.For("PO2.MaterialEditor.Save.Error.Failed", ("reason", result.ErrorMessage)));
                    return;
                }

                string importedDisplayName = displayName;
                if (MaterialLibrary.TryGet(result.MaterialId, out MaterialDefinition importedDefinition))
                {
                    importedDisplayName = importedDefinition.DisplayName;
                }

                m_SelectedManagedMaterialId = result.MaterialId;
                m_MaterialManagementView = MaterialManagementView.Library;
                ClearMaterialImportState();
                RebuildMaterialLibraryEntries();
                EnsureManagedMaterialSelection();
                RefreshManagedMaterialPreview();
            }
            finally
            {
                m_MaterialImportBusy = false;
            }
        }

        private void SaveManagedMaterialEdit()
        {
            if (m_MaterialImportBusy || m_MaterialManagementView != MaterialManagementView.EditUser || !TryGetSelectedManagedMaterial(out MaterialDefinition definition) || definition.LibrarySource != MaterialLibrarySource.User)
            {
                return;
            }

            string displayName = m_MaterialAuthoringState.DisplayName.Trim();
            string category = m_MaterialAuthoringState.Category.Trim();
            if (displayName.Length == 0)
            {
                m_MaterialImportNotice = new MaterialImportNotice(MaterialImportMessageKind.Error, LocalizedText.For("PO2.MaterialEditor.Validation.NameRequired"));
                return;
            }

            if (category.Length == 0)
            {
                m_MaterialImportNotice = new MaterialImportNotice(MaterialImportMessageKind.Error, LocalizedText.For("PO2.MaterialEditor.Validation.CategoryRequired"));
                return;
            }

            m_MaterialImportBusy = true;
            try
            {
                UserMaterialUpdateResult result = UserMaterialService.UpdateDefinition(definition.Id, m_MaterialAuthoringState);

                if (!result.Success)
                {
                    m_MaterialImportNotice = new MaterialImportNotice(MaterialImportMessageKind.Error, LocalizedText.For("PO2.MaterialEditor.Save.Error.Failed", ("reason", result.ErrorMessage)));
                    return;
                }

                // Definitions are shared metadata.
                // Their physical size affects generated mapping on every loaded PO that references this ID.
                m_ProceduralObjectSystem.RefreshLibraryMaterialPhysicalSize(definition.Id);

                m_SelectedManagedMaterialId = definition.Id;
                m_MaterialManagementView = MaterialManagementView.Library;
                ClearMaterialImportState();
                RebuildMaterialLibraryEntries();
                EnsureManagedMaterialSelection();
                RefreshManagedMaterialPreview();
            }
            finally
            {
                m_MaterialImportBusy = false;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void SetMaterialImportMetallic(float value)
        {
            if (m_MaterialImportBusy || !IsFinite(value))
            {
                return;
            }

            m_MaterialAuthoringState.SetMetallic(value);
            RefreshMaterialPreviewScalars();
        }

        private void AdjustMaterialImportMetallic(float delta)
        {
            m_MaterialAuthoringState.SetMetallic(m_MaterialAuthoringState.Metallic + delta);
            RefreshMaterialPreviewScalars();
        }

        private void SetMaterialImportSmoothness(float value)
        {
            if (m_MaterialImportBusy || !IsFinite(value))
            {
                return;
            }

            m_MaterialAuthoringState.SetSmoothness(value);
            RefreshMaterialPreviewScalars();
        }

        private void AdjustMaterialImportSmoothness(float delta)
        {
            m_MaterialAuthoringState.SetSmoothness(m_MaterialAuthoringState.Smoothness + delta);
            RefreshMaterialPreviewScalars();
        }

        private void SetMaterialImportCoatStrength(float value)
        {
            if (m_MaterialImportBusy || !IsFinite(value))
            {
                return;
            }

            m_MaterialAuthoringState.SetCoatStrength(value);
            RefreshMaterialPreviewScalars();
        }

        private void AdjustMaterialImportCoatStrength(float delta)
        {
            m_MaterialAuthoringState.SetCoatStrength(m_MaterialAuthoringState.CoatStrength + delta);
            RefreshMaterialPreviewScalars();
        }

        private void ClearMaterialImportState()
        {
            m_MaterialImportCandidates.Clear();
            m_SelectedMaterialImportCandidateIndex = -1;
            m_MaterialImportCandidateNames = Array.Empty<string>();
            m_MaterialImportNotice = default;
            m_MaterialAuthoringState.Reset();
        }

        private void AdjustMaterialImportPhysicalSize(bool width, bool increase)
        {
            if (m_MaterialImportBusy)
            {
                return;
            }

            float current = width ? m_MaterialAuthoringState.PhysicalWidthMeters : m_MaterialAuthoringState.PhysicalHeightMeters;

            // Fine steps below one meter, whole-meter steps after that.
            // The split keeps small textures adjustable without making large sizes tedious.
            float step;
            if (increase)
            {
                step = current < 1f ? 0.1f : 1f;
            }
            else
            {
                step = current <= 1f ? 0.1f : 1f;
            }

            float next = increase ? current + step : current - step;
            if (width)
            {
                m_MaterialAuthoringState.SetPhysicalWidthMeters(next);
            }
            else
            {
                m_MaterialAuthoringState.SetPhysicalHeightMeters(next);
            }

            RefreshMaterialPreviewMapping();
        }

        private void SetMaterialImportPhysicalSize(bool width, float value)
        {
            if (m_MaterialImportBusy || !IsFinite(value))
            {
                return;
            }

            if (width)
            {
                m_MaterialAuthoringState.SetPhysicalWidthMeters(value);
            }
            else
            {
                m_MaterialAuthoringState.SetPhysicalHeightMeters(value);
            }

            RefreshMaterialPreviewMapping();
        }

        private void SetMaterialImportMappingMode(TextureMappingMode mappingMode)
        {
            if (m_MaterialImportBusy)
            {
                return;
            }

            m_MaterialAuthoringState.SetMappingMode(mappingMode);
            RefreshMaterialPreviewMapping();
        }

        private MaterialImportCandidate GetSelectedImportCandidate()
        {
            if (m_SelectedMaterialImportCandidateIndex < 0 || m_SelectedMaterialImportCandidateIndex >= m_MaterialImportCandidates.Count)
            {
                return null;
            }

            return m_MaterialImportCandidates[m_SelectedMaterialImportCandidateIndex];
        }

        private string GetSelectedImportBaseColorName()
        {
            if (m_MaterialManagementView == MaterialManagementView.EditUser && TryGetSelectedManagedMaterial(out MaterialDefinition definition))
            {
                return Path.GetFileName(definition.BaseColorPath);
            }

            MaterialImportCandidate candidate = GetSelectedImportCandidate();
            return candidate == null ? string.Empty : Path.GetFileName(candidate.BaseColorPath);
        }

        private string GetSelectedImportNormalName()
        {
            if (m_MaterialManagementView == MaterialManagementView.EditUser && TryGetSelectedManagedMaterial(out MaterialDefinition definition))
            {
                return string.IsNullOrWhiteSpace(definition.NormalPath) ? null : Path.GetFileName(definition.NormalPath);
            }

            MaterialImportCandidate candidate = GetSelectedImportCandidate();
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.NormalPath))
            {
                return null;
            }

            return Path.GetFileName(candidate.NormalPath);
        }

        private void RebuildMaterialImportCandidateNames()
        {
            var names = new string[m_MaterialImportCandidates.Count];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = m_MaterialImportCandidates[i].SuggestedDisplayName;
            }

            m_MaterialImportCandidateNames = names;
        }
    }
}