using System;
using Colossal.UI.Binding;
using PO2.Interface.Bindings;
using PO2.Interface.Models;
using PO2.Localization;
using PO2.Persistence;
using PO2.Tools.ObjectTool;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private SaveManagerPayload m_SaveManagerState = SaveManagerPayload.s_Empty;
        private LocalizedText m_SaveManagerStatus = LocalizedText.Empty;
        private int m_SaveManagerCatalogVersion;
        private bool m_SaveManagerOpen;

        private uint m_RecoveryCandidatesVersion = uint.MaxValue;

        private SaveTile[] m_RecoveryCandidates = Array.Empty<SaveTile>();

        private bool SaveManagementBlocked => NavigationInputBlocked || m_MaterialImportBusy || m_PersistenceCoordinatorSystem.PersistenceBusy;

        private void InitializeSaveManagerBindings()
        {
            AddUpdateBinding(new GetterValueBinding<SaveManagerPayload>(kGroup, "saveManager", () => m_SaveManagerState, new ValueWriter<SaveManagerPayload>()));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "saveManagerBusy", () => SaveManagementBlocked));
            AddBinding(new TriggerBinding(kGroup, "openSaveManager", GuardNavigation(OpenSaveManager)));
            AddBinding(new TriggerBinding(kGroup, "refreshSaveManager", () =>
            {
                if (m_SaveManagerOpen && !SaveManagementBlocked) RefreshSaveManager();
            }));
            AddBinding(new TriggerBinding<string, string, string>(kGroup, "previewSaveDeletion", PreviewSaveDeletion));
            AddBinding(new TriggerBinding<string>(kGroup, "confirmSaveDeletion", ConfirmSaveDeletion));
            AddBinding(new TriggerBinding(kGroup, "cancelSaveDeletion", CancelSaveDeletion));
        }

        private void OpenSaveManager()
        {
            if (SaveManagementBlocked) return;
            if (m_SaveManagerOpen)
            {
                CloseSaveManager();
                return;
            }

            NavigateWorkspace(() =>
            {
                m_PersistenceCoordinatorSystem.DismissRecoveryResult();
                m_ProceduralObjectToolSystem.EnterSaveManagerWorkspace();
                m_SaveManagerOpen = true;
                RefreshSaveManager();
            });
        }

        private void CloseSaveManager()
        {
            if (!m_SaveManagerOpen) return;
            m_SaveManagerOpen = false;
            m_PersistenceCoordinatorSystem.SaveManager.Cancel();
            m_SaveManagerState = SaveManagerPayload.s_Empty;
            m_ProceduralObjectToolSystem.ExitSaveManagerWorkspace();
        }

        public static bool TryBackOutSaveManager()
        {
            if (s_Instance == null || !s_Instance.m_SaveManagerOpen) return false;
            if (s_Instance.m_PersistenceCoordinatorSystem.SaveManager.Pending != null)
            {
                s_Instance.CancelSaveDeletion();
            }
            else s_Instance.CloseSaveManager();

            return true;
        }

        private void UpdateSaveManager()
        {
            if (!m_SaveManagerOpen) return;
            if (!m_ProceduralObjectSystem.IsToolActive || m_ProceduralObjectToolSystem.Workspace != Workspace.SaveManager)
            {
                CloseSaveManager();
                return;
            }

            if (!SaveManagementBlocked && m_SaveManagerCatalogVersion != m_PersistenceCoordinatorSystem.SaveCatalogVersion)
            {
                RefreshSaveManager(LocalizedText.For("PO2.SaveManager.Status.UpdatedAfterSave"));
            }
        }

        private void RefreshSaveManager(LocalizedText status = default)
        {
            m_PersistenceCoordinatorSystem.SaveManager.Refresh(GameSaveCatalog.Read());
            m_SaveManagerCatalogVersion = m_PersistenceCoordinatorSystem.SaveCatalogVersion;
            m_SaveManagerStatus = status;
            RebuildSaveManagerState();
        }

        private void PreviewSaveDeletion(string kind, string session, string value)
        {
            if (!m_SaveManagerOpen || SaveManagementBlocked) return;
            // Refreshing or starting a save invalidates any older UI selection first.
            if (m_SaveManagerCatalogVersion != m_PersistenceCoordinatorSystem.SaveCatalogVersion)
            {
                RefreshSaveManager(LocalizedText.For("PO2.SaveManager.Status.SelectionExpired"));
                return;
            }

            m_SaveManagerStatus = m_PersistenceCoordinatorSystem.SaveManager.Prepare(kind, session, value, m_PersistenceCoordinatorSystem.ProtectedSnapshotKeys());
            RebuildSaveManagerState();
        }

        private void CancelSaveDeletion()
        {
            m_PersistenceCoordinatorSystem.SaveManager.Cancel();
            if (m_SaveManagerOpen) RebuildSaveManagerState();
        }

        private void ConfirmSaveDeletion(string token)
        {
            if (!m_SaveManagerOpen || SaveManagementBlocked) return;
            if (m_SaveManagerCatalogVersion != m_PersistenceCoordinatorSystem.SaveCatalogVersion)
            {
                RefreshSaveManager(LocalizedText.For("PO2.SaveManager.Status.DeletionExpired"));
                return;
            }

            SaveDeletionPlan pending = m_PersistenceCoordinatorSystem.SaveManager.Pending;
            if (pending == null || !string.Equals(pending.Token, token, StringComparison.Ordinal)) return;
            SaveDeletionResult result = m_PersistenceCoordinatorSystem.SaveManager.Confirm(token, GameSaveCatalog.Read(), m_PersistenceCoordinatorSystem.ProtectedSnapshotKeys());
            LocalizedText summary = result.Failures.Count == 0 ? LocalizedText.For("PO2.SaveManager.DeleteResult.Succeeded", ("count", result.Deleted), ("size", LocalizedText.Number(result.DeletedBytes / 1048576d, 2))) : LocalizedText.For("PO2.SaveManager.DeleteResult.Partial", ("count", result.Deleted), ("size", LocalizedText.Number(result.DeletedBytes / 1048576d, 2)), ("failedCount", result.Failures.Count));
            Mod.log.Info("[PO][Save Manager] " + summary);
            foreach (LocalizedText failure in result.Failures) Mod.log.Warn("[PO][Save Manager] " + failure);
            if (result.Deleted > 0) m_PersistenceCoordinatorSystem.RefreshRecoveryAfterSaveDeletion();
            var messages = new System.Collections.Generic.List<LocalizedText> { summary };
            messages.AddRange(result.Failures);
            RefreshSaveManager(LocalizedText.Join(messages));
        }

        private void RebuildSaveManagerState()
        {
            SaveManager manager = m_PersistenceCoordinatorSystem.SaveManager;
            m_SaveManagerState = manager.Discovery == null ? SaveManagerPayload.s_Empty : new SaveManagerPayload(manager, m_PersistenceCoordinatorSystem.ProtectedSnapshotKeys(), m_SaveManagerStatus);
        }

        private void RegisterRecoveryStateBindings()
        {
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "persistenceBusy", () => m_PersistenceCoordinatorSystem != null && m_PersistenceCoordinatorSystem.PersistenceBusy));

            // The game's writer factory cannot reflectively create ArrayWriter<T>.
            // Supply the array and element writers directly for each list binding.
            AddUpdateBinding(new GetterValueBinding<SaveTile[]>(kGroup, "recoveryCandidates", GetRecoveryCandidates, new ArrayWriter<SaveTile>(new ValueWriter<SaveTile>())));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "recoveryResultPending", () => m_PersistenceCoordinatorSystem != null && m_PersistenceCoordinatorSystem.RecoveryResultPending));

            AddUpdateBinding(new GetterValueBinding<LocalizedText>(kGroup, "recoveryStatus", () => m_PersistenceCoordinatorSystem != null ? m_PersistenceCoordinatorSystem.RecoveryStatus : LocalizedText.Empty, LocalizedTextWriter.s_Instance));
        }

        private void RegisterRecoveryActionBindings()
        {
            AddBinding(new TriggerBinding(kGroup, "dismissRecoveryResult", () => { m_PersistenceCoordinatorSystem?.DismissRecoveryResult(); }));

            AddBinding(new TriggerBinding<string>(kGroup, "recoverSnapshotCandidate", GuardHistory<string>(RecoverSnapshotCandidate)));
        }

        public static bool TryBackOutRecoveryResult()
        {
            if (s_Instance?.m_PersistenceCoordinatorSystem == null) return false;
            bool visible = s_Instance.m_ProceduralObjectSystem.IsToolActive && s_Instance.m_ProceduralObjectToolSystem.Workspace == Workspace.Select && s_Instance.m_MaterialManagementView == MaterialManagementView.None;
            return s_Instance.m_PersistenceCoordinatorSystem.DismissRecoveryResult() && visible;
        }

        private SaveTile[] GetRecoveryCandidates()
        {
            if (m_PersistenceCoordinatorSystem == null) return Array.Empty<SaveTile>();
            if (m_RecoveryCandidatesVersion != m_PersistenceCoordinatorSystem.RecoveryCandidatesVersion)
            {
                m_RecoveryCandidates = SaveTile.Capture(m_PersistenceCoordinatorSystem.RecoveryCandidates);
                m_RecoveryCandidatesVersion = m_PersistenceCoordinatorSystem.RecoveryCandidatesVersion;
            }

            return m_RecoveryCandidates;
        }

        private void RecoverSnapshotCandidate(string fileKey)
        {
            if (m_PersistenceCoordinatorSystem == null)
            {
                Mod.log.Warn("[PO][Persistence Recovery] UI recovery requested but persistence coordinator is unavailable.");
                return;
            }

            m_PersistenceCoordinatorSystem.RecoverCandidate(fileKey);
        }
    }
}