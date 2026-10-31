using System;
using System.Collections.Generic;
using Colossal;
using Game.SceneFlow;

namespace PO2.Persistence
{
    public partial class PersistenceCoordinatorSystem
    {
        private SaveManager m_SaveManager;
        private string m_LoadedSnapshotKey = string.Empty;
        private string m_LatestSnapshotKey = string.Empty;
        private string m_RecoveredSnapshotKey = string.Empty;

        internal int SaveCatalogVersion { get; private set; }

        // A failed game save can skip its end event. The task queue still completes.
        internal bool PersistenceBusy => !TaskManager.instance.Complete(SaveHelpers.kSaveLoadTaskName).IsCompleted || (GameManager.instance != null && GameManager.instance.isGameLoading) || m_HasPendingLoadedSaveGuid || m_DeferredSuccessfulSaveIndex < m_DeferredSuccessfulSaves.Count;
        internal SaveManager SaveManager => m_SaveManager ?? (m_SaveManager = new SaveManager(m_FileStore));

        internal HashSet<string> ProtectedSnapshotKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (m_LoadedSnapshotKey.Length > 0) keys.Add(m_LoadedSnapshotKey);
            if (m_LatestSnapshotKey.Length > 0) keys.Add(m_LatestSnapshotKey);
            if (m_RecoveredSnapshotKey.Length > 0) keys.Add(m_RecoveredSnapshotKey);
            for (int i = m_DeferredSuccessfulSaveIndex; i < m_DeferredSuccessfulSaves.Count; i++)
                keys.Add(m_DeferredSuccessfulSaves[i].ExactSaveFileKey);
            return keys;
        }

        private void ResetSaveManagerContext()
        {
            m_SaveManager?.Cancel();
            m_LoadedSnapshotKey = string.Empty;
            m_LatestSnapshotKey = string.Empty;
            m_RecoveredSnapshotKey = string.Empty;
            SaveCatalogVersion++;
        }

    }
}
