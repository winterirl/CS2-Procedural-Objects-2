using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Colossal.IO.AssetDatabase;
using Game;
using Game.Assets;
using Game.SceneFlow;
using PO2.Localization;

namespace PO2.Persistence
{
    public partial class PersistenceCoordinatorSystem
    {
        private enum MaintenanceKind { Automatic, Refresh, Preview, Delete, Page, Recovery }
        private sealed class MaintenanceRequest
        {
            internal MaintenanceKind Kind;
            internal string Session = "";
            internal string Filter = "all";
            internal string Value = "";
            internal bool Oldest;
            internal int Page;
            internal SaveDeletionPlan Plan;
        }
        private sealed class MaintenanceResult
        {
            internal SaveManager Manager;
            internal SaveManagerPage Page;
            internal SaveDeletionPlan Plan;
            internal SaveDiscoveryResult Recovery;
            internal bool Retry;
            internal bool TempsCleaned;
            internal LocalizedText Status;
            internal int Failures;
        }

        private readonly SnapshotAccessGate m_SnapshotGate = new SnapshotAccessGate();
        private readonly SnapshotMaintenanceService m_Maintenance = new SnapshotMaintenanceService();
        private GameSaveCatalog m_CatalogCapture;
        private MaintenanceRequest m_MaintenanceRequest;
        private MaintenanceRequest m_ActiveMaintenance;
        private long m_ActiveMaintenanceRevision;
        private int m_CatalogChanged;
        private readonly object m_MissingStorageLock = new object();
        private readonly HashSet<string> m_MissingStorages = new HashSet<string>(StringComparer.Ordinal);
        private bool m_AutomaticCleanupRequested;
        private bool m_MaintenanceReady;
        private bool m_ManagerVisible;
        private bool m_WasPersistenceBusy;
        private bool m_WasCatalogReady;
        private bool m_LastCleanupEnabled = true;
        private int m_LastRetentionCount = 3;
        private long m_MaintenanceNotBefore;
        private long m_NextMaintenanceStatus;
        private readonly DateTime m_MaintenanceStartedUtc = DateTime.UtcNow;
        private bool m_TempsCleaned;
        private string m_RecoveryWaitingKey;
        private bool m_RecoveryDiscoveryPending;
        private int m_RecoveryPage;
        private SaveManagerPage m_ManagerPage = new SaveManagerPage();
        private LocalizedText m_ManagerOperationStatus = LocalizedText.Empty;
        internal LocalizedText MaintenanceStatus { get; private set; } = LocalizedText.Empty;
        internal int ManagerStateVersion { get; private set; }
        internal int ManagerCatalogVersion { get; private set; }
        internal SaveManagerPage ManagerPage => m_ManagerPage;
        internal LocalizedText ManagerOperationStatus => m_ManagerOperationStatus;
        internal bool ManagerOperationBusy => m_MaintenanceRequest != null && m_MaintenanceRequest.Kind != MaintenanceKind.Recovery ||
            m_ActiveMaintenance != null && m_ActiveMaintenance.Kind != MaintenanceKind.Automatic && m_ActiveMaintenance.Kind != MaintenanceKind.Recovery;
        internal bool RecoveryOperationBusy => m_RecoveryDiscoveryPending || m_RecoveryWaitingKey != null;
        internal int RecoveryPageIndex => m_RecoveryPage;
        internal int RecoveryPageCount => Math.Max(1, (m_RecoveryCandidates.Count + SaveManagerPage.Size - 1) / SaveManagerPage.Size);

        private void InitializeMaintenance()
        {
            AssetDatabase.global.onAssetDatabaseChanged.Subscribe(OnSaveCatalogChanged);
            m_WasCatalogReady = MaintenanceCatalogReady();
        }

        private void OnSaveCatalogChanged(AssetChangedEventArgs args)
        {
            if (args.asset != null && !(args.asset is SaveGameMetadata) && !(args.asset is SaveGameData) && !(args.asset is PackageAsset)) return;
            if ((args.change == ChangeType.DatabaseUnregistered || args.change == ChangeType.DatabaseRegistered) &&
                args.database is IDataSourceAccessor storage &&
                (storage.dataSource.isRemoteStorageSource || storage.dataSource.remoteStorageSourceName == "Local"))
            {
                lock (m_MissingStorageLock)
                {
                    if (args.change == ChangeType.DatabaseUnregistered) m_MissingStorages.Add(args.database.hostname);
                    else m_MissingStorages.Remove(args.database.hostname);
                }
            }
            m_SnapshotGate.Suspend();
            Interlocked.Exchange(ref m_CatalogChanged, 1);
        }

        private bool MaintenanceCatalogReady()
        {
            lock (m_MissingStorageLock) if (m_MissingStorages.Count != 0) return false;
            return GameSaveCatalog.StorageReady();
        }

        private void StopMaintenance()
        {
            m_SnapshotGate.Suspend();
            m_Maintenance.Cancel();
            m_CatalogCapture?.Dispose();
            m_CatalogCapture = null;
            m_MaintenanceNotBefore = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
        }

        private void RequestAutomaticCleanup()
        {
            m_AutomaticCleanupRequested = true;
            m_MaintenanceNotBefore = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
        }

        internal void SetSaveManagerVisible(bool visible)
        {
            m_ManagerVisible = visible;
            StopMaintenance();
            if (!visible)
            {
                if (m_MaintenanceRequest?.Kind != MaintenanceKind.Recovery) m_MaintenanceRequest = null;
                m_SaveManager?.Cancel();
                RequestAutomaticCleanup();
            }
        }

        internal void RequestSaveManagerRefresh()
        {
            QueueMaintenance(new MaintenanceRequest { Kind = MaintenanceKind.Refresh, Session = m_ManagerPage.Session,
                Filter = m_ManagerPage.Status, Oldest = m_ManagerPage.Oldest, Page = m_ManagerPage.Index });
        }

        internal void RequestSaveManagerPage(string session, string filter, bool oldest, int page)
        {
            if (m_SaveManager?.Discovery == null || ManagerOperationBusy || m_SaveManager.Pending != null) return;
            if (session.Length > 0 && !Guid.TryParseExact(session, "D", out _)) return;
            if (filter != "all" && filter != "Matched" && filter != "Orphaned" && filter != "Unknown") return;
            QueueMaintenance(new MaintenanceRequest { Kind = MaintenanceKind.Page, Session = session, Filter = filter, Oldest = oldest, Page = Math.Max(0, page) });
        }

        internal void RequestSaveDeletion(string kind, string session, string value)
        {
            if (ManagerOperationBusy || m_SaveManager?.Discovery == null) return;
            QueueMaintenance(new MaintenanceRequest { Kind = MaintenanceKind.Preview, Filter = kind, Session = session, Value = value });
        }

        internal void RequestConfirmSaveDeletion(string token)
        {
            if (ManagerOperationBusy || m_SaveManager?.Pending == null || m_SaveManager.Pending.Token != token) return;
            var request = new MaintenanceRequest { Kind = MaintenanceKind.Delete, Plan = m_SaveManager.Pending,
                Session = m_ManagerPage.Session, Filter = m_ManagerPage.Status, Oldest = m_ManagerPage.Oldest, Page = m_ManagerPage.Index };
            QueueMaintenance(request);
            m_SaveManager.Cancel();
            ManagerStateVersion++;
        }

        private void QueueMaintenance(MaintenanceRequest request)
        {
            StopMaintenance();
            m_MaintenanceRequest = request;
        }

        private void QueueRecoveryDiscovery()
        {
            m_RecoveryDiscoveryPending = true;
            m_RecoveryStatus = LocalizedText.For("PO2.Recovery.Status.Scanning");
            QueueMaintenance(new MaintenanceRequest { Kind = MaintenanceKind.Recovery });
        }

        internal IReadOnlyList<SaveRecord> RecoveryPage()
        {
            int first = m_RecoveryPage * SaveManagerPage.Size;
            int count = Math.Max(0, Math.Min(SaveManagerPage.Size, m_RecoveryCandidates.Count - first));
            var page = new SaveRecord[count];
            for (int i = 0; i < count; i++) page[i] = m_RecoveryCandidates[first + i];
            return page;
        }

        internal void SetRecoveryPage(int page)
        {
            m_RecoveryPage = Math.Max(0, Math.Min(page, RecoveryPageCount - 1));
            NotifyRecoveryCandidatesChanged();
        }

        private void UpdateMaintenance()
        {
            long now = Stopwatch.GetTimestamp();
            bool changed = Interlocked.Exchange(ref m_CatalogChanged, 0) != 0;
            bool enabled = Mod.Settings?.CleanupOrphanedAutosaves ?? true;
            int keep = Math.Max(0, Math.Min(20, Mod.Settings?.RetainedOrphanedAutosaves ?? 3));
            bool busy = PersistenceBusy;
            bool ready = MaintenanceCatalogReady();
            if (changed || ready != m_WasCatalogReady || enabled != m_LastCleanupEnabled || keep != m_LastRetentionCount || busy && !m_WasPersistenceBusy)
            {
                StopMaintenance();
                m_SaveManager?.Cancel();
                // A reviewed manual deletion is never replayed after the catalog changes.
                if (m_MaintenanceRequest?.Kind == MaintenanceKind.Delete || m_MaintenanceRequest?.Kind == MaintenanceKind.Preview)
                    m_MaintenanceRequest = null;
                RequestAutomaticCleanup();
                SaveCatalogVersion++;
                ManagerStateVersion++;
            }
            m_LastCleanupEnabled = enabled;
            m_LastRetentionCount = keep;
            m_WasPersistenceBusy = busy;
            m_WasCatalogReady = ready;

            if (m_Maintenance.TryTake(out object completed, out Exception error))
            {
                MaintenanceRequest request = m_ActiveMaintenance;
                bool current = m_ActiveMaintenanceRevision == m_SnapshotGate.Revision;
                int deleted = m_Maintenance.Progress.Deleted;
                if (deleted > 0)
                {
                    SaveCatalogVersion++;
                    MaintenanceStatus = LocalizedText.For("PO2.SaveManager.Maintenance.Completed", ("count", deleted),
                        ("size", LocalizedText.Number(m_Maintenance.Progress.DeletedBytes / 1048576d, 2)));
                    Mod.log.Info($"[PO][Save Cleanup] Removed {deleted} snapshot(s), reclaimed {m_Maintenance.Progress.DeletedBytes} bytes.");
                }
                if (error != null)
                {
                    Mod.log.Warn($"[PO][Save Cleanup] Background operation failed: {error.Message}");
                    m_ManagerOperationStatus = LocalizedText.For("PO2.SaveManager.Maintenance.Failed");
                    MaintenanceStatus = m_ManagerOperationStatus;
                    ManagerStateVersion++;
                }
                else if (current && completed is MaintenanceResult result)
                {
                    if (result.TempsCleaned) m_TempsCleaned = true;
                    if (result.Retry)
                    {
                        m_AutomaticCleanupRequested = true;
                        m_MaintenanceNotBefore = now + Stopwatch.Frequency * 30;
                    }
                    if (result.Manager != null)
                    {
                        m_SaveManager = result.Manager;
                        ManagerCatalogVersion = SaveCatalogVersion;
                    }
                    if (result.Page != null) m_ManagerPage = result.Page;
                    if (request.Kind == MaintenanceKind.Preview) SaveManager.SetPending(result.Plan);
                    if (request.Kind != MaintenanceKind.Automatic && request.Kind != MaintenanceKind.Recovery) m_ManagerOperationStatus = result.Status;
                    if (request.Kind == MaintenanceKind.Automatic && deleted == 0) MaintenanceStatus = result.Status;
                    if (result.Failures > 0) Mod.log.Warn($"[PO][Save Cleanup] Skipped {result.Failures} file operation(s). No immediate retry.");
                    if (result.Recovery != null && m_HasRecoveryContext)
                    {
                        m_RecoveryCandidates = result.Recovery.Saves;
                        m_RecoveryPage = 0;
                        m_RecoveryDiscoveryPending = false;
                        m_RecoveryStatus = RecoveryMessages.Discovered(m_RecoveryCandidates.Count);
                        NotifyRecoveryCandidatesChanged();
                    }
                    ManagerStateVersion++;
                }
                else if (request.Kind == MaintenanceKind.Preview || request.Kind == MaintenanceKind.Delete)
                {
                    m_ManagerOperationStatus = LocalizedText.For("PO2.SaveManager.Maintenance.Interrupted");
                    ManagerStateVersion++;
                }
                if (request.Kind == MaintenanceKind.Recovery && m_HasRecoveryContext && m_RecoveryDiscoveryPending)
                {
                    if (error != null)
                    {
                        m_RecoveryDiscoveryPending = false;
                        m_RecoveryStatus = LocalizedText.For("PO2.Recovery.Status.ScanFailed");
                    }
                }
                if (request.Kind == MaintenanceKind.Automatic && error != null)
                {
                    m_AutomaticCleanupRequested = true;
                    m_MaintenanceNotBefore = now + Stopwatch.Frequency * 300;
                }
                if (request.Kind == MaintenanceKind.Automatic && completed == null && error == null && deleted == 0)
                    MaintenanceStatus = LocalizedText.Empty;
                m_ActiveMaintenance = null;
            }

            if (now >= m_NextMaintenanceStatus)
            {
                m_NextMaintenanceStatus = now + Stopwatch.Frequency / 4;
                if (m_ActiveMaintenance?.Kind == MaintenanceKind.Automatic)
                    MaintenanceStatus = LocalizedText.For("PO2.SaveManager.Maintenance.Progress", ("checked", m_Maintenance.Progress.Checked),
                        ("count", m_Maintenance.Progress.Deleted), ("size", LocalizedText.Number(m_Maintenance.Progress.DeletedBytes / 1048576d, 2)));
            }

            if (m_Maintenance.HasWork || busy || m_SnapshotGate.IsActive) return;
            MaintenanceRequest next = m_MaintenanceRequest;
            if (next == null && m_RecoveryDiscoveryPending && m_HasRecoveryContext)
                next = new MaintenanceRequest { Kind = MaintenanceKind.Recovery };
            if (next == null)
            {
                if (!m_MaintenanceReady || !enabled || !m_AutomaticCleanupRequested || m_ManagerVisible || m_HasRecoveryContext || now < m_MaintenanceNotBefore) return;
                next = new MaintenanceRequest { Kind = MaintenanceKind.Automatic };
            }
            bool needsCatalog = next.Kind != MaintenanceKind.Page && next.Kind != MaintenanceKind.Preview;
            long revision = m_CatalogCapture?.Revision ?? m_SnapshotGate.Revision;
            if (needsCatalog && next.Kind == MaintenanceKind.Automatic && !ready)
            {
                m_CatalogCapture?.Dispose();
                m_CatalogCapture = null;
                MaintenanceStatus = LocalizedText.For("PO2.SaveManager.Maintenance.WaitingForCatalog");
                m_MaintenanceNotBefore = now + Stopwatch.Frequency * 30;
                return;
            }
            if (needsCatalog && ready)
            {
                if (m_CatalogCapture == null) m_CatalogCapture = new GameSaveCatalog(revision);
                m_CatalogCapture.Advance();
                if (!m_CatalogCapture.Complete) return;
                if (m_CatalogCapture.Revision != m_SnapshotGate.Revision) { m_CatalogCapture.Dispose(); m_CatalogCapture = null; return; }
            }
            var entries = m_CatalogCapture?.Entries ?? new List<GameSaveCatalogEntry>();
            bool complete = needsCatalog && ready && m_CatalogCapture != null && m_CatalogCapture.Succeeded;
            m_CatalogCapture?.Dispose();
            m_CatalogCapture = null;
            if (needsCatalog && complete && !MaintenanceCatalogReady()) return;
            if (!m_SnapshotGate.TryResume(revision)) return;
            m_MaintenanceRequest = null;
            m_ActiveMaintenance = next;
            m_ActiveMaintenanceRevision = revision;
            if (next.Kind == MaintenanceKind.Automatic) m_AutomaticCleanupRequested = false;
            SnapshotFileStore store = m_FileStore;
            SnapshotAccessGate gate = m_SnapshotGate;
            HashSet<string> protectedKeys = ProtectedSnapshotKeys();
            Guid protectedSession = m_HasRecoveryContext ? m_CurrentLoadedSessionGuid : Guid.Empty;
            SaveDiscoveryResult previousDiscovery = m_SaveManager?.Discovery;
            bool previousComplete = m_SaveManager?.CatalogComplete ?? false;
            Guid recoverySession = m_CurrentLoadedSessionGuid;
            bool refreshRecoveryAfterDelete = m_HasRecoveryContext && !RecoveryResultPending;
            string recoveryKey = m_CurrentLoadedExactFileKey;
            bool cleanTemps = next.Kind == MaintenanceKind.Automatic && !m_TempsCleaned;
            DateTime startup = m_MaintenanceStartedUtc;
            m_Maintenance.Start(work =>
            {
                var catalog = new GameSaveCatalogSnapshot(entries, complete);
                var result = new MaintenanceResult();
                if (next.Kind == MaintenanceKind.Automatic)
                {
                    if (cleanTemps)
                    {
                        store.CleanupStaleTemps(startup, protectedKeys, work, gate, revision);
                        work.Checkpoint();
                        result.TempsCleaned = true;
                    }
                    SaveDiscoveryResult discovery = store.DiscoverSaves(catalog, work.Checkpoint, work.Progress.CheckedFile);
                    List<SaveRecord> targets = AutosaveRetentionPolicy.Select(discovery, complete, keep, protectedKeys, protectedSession, work.Checkpoint);
                    foreach (SaveRecord target in targets)
                    {
                        work.Checkpoint();
                        using (IDisposable lease = gate.TryEnter(revision))
                        {
                            if (lease == null) break;
                            if (store.TryDeleteUnchanged(target, out _)) work.Progress.DeletedFile(target.FileBytes);
                            else result.Failures++;
                        }
                    }
                    result.Retry = !complete || !discovery.ScanSucceeded;
                    result.Status = result.Retry ? LocalizedText.For("PO2.SaveManager.Maintenance.WaitingForCatalog") : LocalizedText.For("PO2.SaveManager.Maintenance.UpToDate");
                }
                else if (next.Kind == MaintenanceKind.Recovery)
                {
                    result.Recovery = store.DiscoverSaves(catalog, work.Checkpoint, work.Progress.CheckedFile).ForRecovery(recoveryKey, recoverySession);
                    if (!result.Recovery.ScanSucceeded) throw new System.IO.IOException("Recovery discovery could not read the snapshot folder.");
                }
                else
                {
                    var manager = new SaveManager(store);
                    if (next.Kind == MaintenanceKind.Refresh)
                        manager.Refresh(catalog, work.Checkpoint, work.Progress.CheckedFile);
                    else manager.SetDiscovery(previousDiscovery, previousComplete);
                    if (next.Kind == MaintenanceKind.Preview)
                    {
                        result.Status = manager.Prepare(next.Filter, next.Session, next.Value, protectedKeys, work.Checkpoint);
                        result.Plan = manager.Pending;
                    }
                    else
                    {
                        if (next.Kind == MaintenanceKind.Delete)
                        {
                            manager.SetPending(next.Plan);
                            SaveDeletionResult deletion = manager.Confirm(next.Plan.Token, catalog, protectedKeys, work, gate, revision);
                            result.Failures = deletion.FailureCount;
                            result.Status = deletion.FailureCount == 0
                                ? LocalizedText.For("PO2.SaveManager.DeleteResult.Succeeded", ("count", deletion.Deleted),
                                    ("size", LocalizedText.Number(deletion.DeletedBytes / 1048576d, 2)))
                                : LocalizedText.For("PO2.SaveManager.DeleteResult.Partial", ("count", deletion.Deleted),
                                    ("size", LocalizedText.Number(deletion.DeletedBytes / 1048576d, 2)), ("failedCount", deletion.FailureCount));
                            manager.Refresh(catalog, work.Checkpoint, work.Progress.CheckedFile);
                            if (refreshRecoveryAfterDelete && manager.Discovery.ScanSucceeded)
                                result.Recovery = manager.Discovery.ForRecovery(recoveryKey, recoverySession);
                        }
                        result.Manager = manager;
                        result.Page = SaveManagerPage.Create(manager.Discovery, next.Session, next.Filter, next.Oldest, next.Page, work.Checkpoint);
                    }
                }
                return result;
            });
        }

        private void DisposeMaintenance()
        {
            AssetDatabase.global.onAssetDatabaseChanged.Unsubscribe(OnSaveCatalogChanged);
            StopMaintenance();
            m_Maintenance.Dispose();
        }
    }
}
