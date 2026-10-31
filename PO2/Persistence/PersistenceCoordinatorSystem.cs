using System;
using System.Collections.Generic;
using System.Diagnostics;
using Colossal;
using Colossal.Serialization.Entities;
using Game;
using Game.Assets;
using Game.PSI;
using Game.SceneFlow;
using Game.Serialization;
using UnityEngine.Scripting;
using PO2.Core;
using PO2.Localization;
using PO2.Persistence.Format;
using PO2.Persistence.Model;
using PO2.Persistence.PreparedTopology;
using PO2.Tools.ObjectTool;

namespace PO2.Persistence
{
    // Writes the PO sidecar after the vanilla save commits.
    // Loading waits for vanilla deserialization and session identity before restoring the matching sidecar.
    public partial class PersistenceCoordinatorSystem : GameSystemBase
    {
        private readonly TopologyEncodingSession m_TopologyEncoding = new TopologyEncodingSession();
        private sealed class PendingSuccessfulSave
        {
            internal readonly string SaveName;
            internal readonly Hash128 VanillaSaveGuid;
            internal readonly string ExactSaveFileKey;
            internal readonly Guid SnapshotSaveGuid;
            internal readonly Guid PersistedSessionGuid;
            internal readonly Guid RuntimeSessionGuid;
            internal readonly SnapshotMetadata Metadata;
            internal bool AutoSave => Metadata.SaveKind == SnapshotSaveKind.AutoSave;

            internal PendingSuccessfulSave(string saveName, Hash128 vanillaSaveGuid, Guid persistedSessionGuid, Guid runtimeSessionGuid, SnapshotMetadata metadata)
            {
                SaveName = saveName ?? string.Empty;
                VanillaSaveGuid = vanillaSaveGuid;
                ExactSaveFileKey = vanillaSaveGuid.ToString();
                SnapshotSaveGuid = SaveIdentity.ToSnapshotGuid(vanillaSaveGuid);
                PersistedSessionGuid = persistedSessionGuid;
                RuntimeSessionGuid = runtimeSessionGuid;
                Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            }
        }

        private ProceduralObjectSystem m_ObjectSystem;
        private ProceduralObjectToolSystem m_ToolSystem;
        private SnapshotFileStore m_FileStore;
        private LoadGameSystem m_LoadGameSystem;
        private Hash128 m_PendingLoadedSaveGuid;
        private bool m_HasPendingLoadedSaveGuid;
        private Hash128 m_CurrentLoadedSaveGuid;
        private string m_CurrentLoadedExactFileKey = string.Empty;
        private Guid m_CurrentLoadedSessionGuid;
        private bool m_HasRecoveryContext;
        private IReadOnlyList<SaveRecord> m_RecoveryCandidates = Array.Empty<SaveRecord>();
        internal uint RecoveryCandidatesVersion { get; private set; }

        private LocalizedText m_RecoveryStatus = LocalizedText.Empty;

        private readonly List<PendingSuccessfulSave> m_DeferredSuccessfulSaves = new List<PendingSuccessfulSave>();
        private int m_DeferredSuccessfulSaveIndex;

        internal IReadOnlyList<SaveRecord> RecoveryCandidates => m_RecoveryCandidates;

        internal LocalizedText RecoveryStatus => m_RecoveryStatus;

        internal bool RecoveryResultPending { get; private set; }

        internal bool DismissRecoveryResult()
        {
            if (!RecoveryResultPending) return false;
            ClearRecoveryState();
            return true;
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();

            m_ObjectSystem = World.GetOrCreateSystemManaged<ProceduralObjectSystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ProceduralObjectToolSystem>();
            m_FileStore = new SnapshotFileStore(Mod.SaveRootPath);
            InitializeMaintenance();
            m_LoadGameSystem = World.GetOrCreateSystemManaged<LoadGameSystem>();

            if (m_LoadGameSystem != null)
            {
                m_LoadGameSystem.onOnSaveGameLoaded += OnSaveGameLoaded;
            }

            if (GameManager.instance != null)
            {
                GameManager.instance.onGameSaveLoad += OnGameSaveLoad;
            }

            Mod.log.Info($"[PO][Persistence] Persistence coordinator created. " + $"Exact snapshots load/write normally; missing or rejected exact snapshots retain same-session recovery candidates from '{m_FileStore.RootPath}' for explicit recovery only.");
        }

        protected override void OnUpdate()
        {
            UpdateMaintenance();
            if (m_RecoveryWaitingKey != null && !m_SnapshotGate.IsActive && !PersistenceBusy)
            {
                string key = m_RecoveryWaitingKey;
                m_RecoveryWaitingKey = null;
                RecoverCandidateNow(key);
            }
            if (m_SnapshotGate.IsActive) return;
            if (m_DeferredSuccessfulSaveIndex >= m_DeferredSuccessfulSaves.Count || (m_ToolSystem != null && m_ToolSystem.MeshMutationBusy))
            {
                return;
            }

            PendingSuccessfulSave pending = m_DeferredSuccessfulSaves[m_DeferredSuccessfulSaveIndex++];

            if (m_DeferredSuccessfulSaveIndex >= m_DeferredSuccessfulSaves.Count)
            {
                m_DeferredSuccessfulSaves.Clear();
                m_DeferredSuccessfulSaveIndex = 0;
            }

            try
            {
                WriteSuccessfulSaveSnapshot(pending);
            }
            catch (Exception exception)
            {
                Mod.log.Error($"[PO][Persistence] Deferred snapshot for vanilla " + $"save '{pending.SaveName}' failed. The vanilla save " + "remains valid. " + exception);
            }
        }

        private void OnSaveGameLoaded(Context context)
        {
            m_TopologyEncoding.Clear();
            StopMaintenance();
            m_MaintenanceReady = false;
            m_MaintenanceRequest = null;
            m_RecoveryWaitingKey = null;
            m_ManagerVisible = false;
            ResetSaveManagerContext();
            int deferredSaveCount = m_DeferredSuccessfulSaves.Count - m_DeferredSuccessfulSaveIndex;
            if (deferredSaveCount > 0)
            {
                Mod.log.Warn($"[PO][Persistence] Discarded " + $"{deferredSaveCount} deferred " + "snapshot request(s) because the runtime context " + "changed before deformation finalization completed.");
                m_DeferredSuccessfulSaves.Clear();
                m_DeferredSuccessfulSaveIndex = 0;
            }

            ClearRecoveryState();

            if (context.purpose != Purpose.LoadGame)
            {
                ClearPendingLoadIdentity();
                return;
            }

            // This event has the exact save guid but fires before sessionGuid has for sure been restored from the save.
            // Hold it until load complete.
            m_PendingLoadedSaveGuid = context.instigatorGuid;
            m_LoadedSnapshotKey = context.instigatorGuid.ToString();
            m_HasPendingLoadedSaveGuid = true;

            Mod.log.Info($"[PO][Persistence] LOAD pending exact save identity: " + $"saveGuid={m_PendingLoadedSaveGuid}, version={context.version}. " + "Snapshot restore deferred until vanilla loading completes.");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            StopMaintenance();
            m_MaintenanceReady = mode.IsGame();
            RequestAutomaticCleanup();
            if (purpose != Purpose.LoadGame)
            {
                ClearPendingLoadIdentity();
                return;
            }
            // The gate lease covers worker disk access only.
            // Cancellation was requested above, wait here so restoration stays inside game loading.
            m_SnapshotGate.SuspendAndDrain();
            CompleteSnapshotRestore(mode);
        }

        private void CompleteSnapshotRestore(GameMode mode)
        {
            try
            {
                RestoreLoadedSaveSnapshot(mode);
            }
            catch (Exception exception)
            {
                // Vanilla loading has already completed.
                // PO2 failure must never escape into game flow or leave stale POs from the prior city.
                Mod.log.Error("[PO][Persistence] Exact snapshot restore failed unexpectedly. " + "The vanilla city remains loaded and PO2 will start empty. " + exception);
                ClearRuntimeObjectsAfterLoadFailure();
            }
            finally
            {
                ClearPendingLoadIdentity();
            }
        }

        private void RestoreLoadedSaveSnapshot(GameMode mode)
        {
            if (!m_HasPendingLoadedSaveGuid)
            {
                Mod.log.Warn("[PO][Persistence] Vanilla load completed without a pending exact save GUID. " + "No PO2 snapshot can be selected safely; starting with zero POs.");
                ClearRuntimeObjectsAfterLoadFailure();
                return;
            }

            Hash128 vanillaSaveGuid = m_PendingLoadedSaveGuid;
            string exactSaveFileKey = vanillaSaveGuid.ToString();
            Guid expectedSaveGuid = SaveIdentity.ToSnapshotGuid(vanillaSaveGuid);
            Guid runtimeSessionGuid = Telemetry.GetCurrentSession();

            // Exact filename plus both embedded identities must match before any
            // persistence DTO is handed to the runtime object system.
            SnapshotLoadResult load = m_FileStore.ReadExact(exactSaveFileKey, expectedSaveGuid, runtimeSessionGuid);

            if (!load.Exists)
            {
                Mod.log.Info($"[PO][Persistence] LOAD no exact snapshot: " + $"saveGuid={vanillaSaveGuid}, sessionGuid={runtimeSessionGuid:D}, " + $"mode={mode}, path='{load.Path}'. Starting with zero POs.");
                ClearRuntimeObjectsAfterLoadFailure();
                DiscoverRecoveryCandidates(vanillaSaveGuid, exactSaveFileKey, runtimeSessionGuid);
                return;
            }

            if (!load.IsValid || load.Result == null)
            {
                Mod.log.Warn($"[PO][Persistence] LOAD rejected exact snapshot: " + $"saveGuid={vanillaSaveGuid}, sessionGuid={runtimeSessionGuid:D}, " + $"mode={mode}, path='{load.Path}', error='{load.Error}'. " + "The vanilla city remains loaded and PO2 will start empty.");
                ClearRuntimeObjectsAfterLoadFailure();
                DiscoverRecoveryCandidates(vanillaSaveGuid, exactSaveFileKey, runtimeSessionGuid);
                return;
            }

            LogMetadataWarning(load.Result);

            // A good exact snapshot always wins and removes any stale recovery UI letf over from an earlier load.
            ClearRecoveryState();

            Stopwatch restoreTimer = Stopwatch.StartNew();
            int restoredCount;
            try
            {
                m_TopologyEncoding.Clear();
                restoredCount = m_ObjectSystem.RestoreSnapshot(load.Result.TakeObjectsForRestore(m_TopologyEncoding));
                m_TopologyEncoding.CommitLoaded(m_ObjectSystem.Objects);
            }
            catch
            {
                m_TopologyEncoding.Clear();
                ClearRuntimeObjectsAfterLoadFailure();
                throw;
            }

            restoreTimer.Stop();

            for (int i = 0; i < load.Result.SkippedObjects.Count; i++)
            {
                SnapshotObjectError skipped = load.Result.SkippedObjects[i];
                Mod.log.Warn($"[PO][Persistence] LOAD skipped object directoryIndex={skipped.DirectoryIndex}, " + $"persistentId={skipped.PersistentId}: {skipped.Message}");
            }

            Mod.log.Info($"[PO][Persistence] LOAD PASS saveGuid={vanillaSaveGuid}, " + $"sessionGuid={runtimeSessionGuid:D}, mode={mode}, " + $"objectsDecoded={load.Result.ObjectCount}, objectsRestored={restoredCount}, " + $"objectsSkipped={load.Result.SkippedObjects.Count}, bytes={load.Bytes}, " + $"readMs={load.ReadMs:0.###}, restoreMs={restoreTimer.Elapsed.TotalMilliseconds:0.###}, " + $"path='{load.Path}'.");
        }

        private void DiscoverRecoveryCandidates(Hash128 currentLoadedSaveGuid, string currentExactFileKey, Guid currentSessionGuid)
        {
            m_CurrentLoadedSaveGuid = currentLoadedSaveGuid;
            m_CurrentLoadedExactFileKey = currentExactFileKey ?? string.Empty;
            m_CurrentLoadedSessionGuid = currentSessionGuid;
            m_HasRecoveryContext = true;
            m_RecoveryCandidates = Array.Empty<SaveRecord>();
            m_RecoveryPage = 0;
            NotifyRecoveryCandidatesChanged();
            QueueRecoveryDiscovery();
        }

        internal bool RecoverCandidate(string fileKey)
        {
            if (PersistenceBusy || RecoveryOperationBusy) return false;
            StopMaintenance();
            if (m_SnapshotGate.IsActive)
            {
                m_RecoveryWaitingKey = fileKey;
                return true;
            }
            return RecoverCandidateNow(fileKey);
        }

        private bool RecoverCandidateNow(string fileKey)
        {
            if (PersistenceBusy || (m_ObjectSystem != null && m_ObjectSystem.HistoryBusy) || (m_ToolSystem != null && m_ToolSystem.MeshMutationBusy)) return false;

            if (!m_HasRecoveryContext)
            {
                m_RecoveryStatus = RecoveryMessages.NoContext;
                Mod.log.Warn("[PO][Persistence Recovery] Recovery requested without an active recovery context.");
                return false;
            }

            // Only accept keys from the current discovery result.
            // The UI can't make this method read an arbitrary path from disk.
            SaveRecord candidate = null;
            for (int i = 0; i < m_RecoveryCandidates.Count; i++)
            {
                if (string.Equals(m_RecoveryCandidates[i].FileKey, fileKey, StringComparison.OrdinalIgnoreCase))
                {
                    candidate = m_RecoveryCandidates[i];
                    break;
                }
            }

            if (candidate == null)
            {
                m_RecoveryStatus = RecoveryMessages.Unavailable;
                Mod.log.Warn($"[PO][Persistence Recovery] Recovery rejected for unknown/stale candidate key='{fileKey}'.");
                return false;
            }

            Guid runtimeSessionGuid = Telemetry.GetCurrentSession();
            if (runtimeSessionGuid != m_CurrentLoadedSessionGuid)
            {
                m_RecoveryStatus = RecoveryMessages.Stale;
                Mod.log.Warn($"[PO][Persistence Recovery] Recovery rejected because runtime session changed: " + $"discovered={m_CurrentLoadedSessionGuid:D}, runtime={runtimeSessionGuid:D}.");
                ClearRecoveryCandidatesOnly();
                return false;
            }

            if (candidate.SessionGuid != m_CurrentLoadedSessionGuid)
            {
                m_RecoveryStatus = RecoveryMessages.WrongSession;
                Mod.log.Warn($"[PO][Persistence Recovery] Recovery rejected because candidate session mismatch key={candidate.FileKey}.");
                return false;
            }

            // Header-only discovery was just a preview.
            // Read the selected file in full and check its stored save/session identities again.
            SnapshotLoadResult load = m_FileStore.ReadExact(candidate.FileKey, candidate.SnapshotSaveGuid, m_CurrentLoadedSessionGuid);

            if (!load.Exists)
            {
                m_RecoveryStatus = RecoveryMessages.MissingFile;
                Mod.log.Warn($"[PO][Persistence Recovery] APPLY failed sourceKey={candidate.FileKey}: snapshot disappeared before recovery.");
                RefreshRecoveryCandidatesAfterApplyFailure();
                return false;
            }

            if (!load.IsValid || load.Result == null)
            {
                m_RecoveryStatus = RecoveryMessages.Invalid;
                Mod.log.Warn($"[PO][Persistence Recovery] APPLY rejected sourceKey={candidate.FileKey}, " + $"targetKey={m_CurrentLoadedExactFileKey}, error='{load.Error}'. Runtime PO state was not changed.");
                return false;
            }

            LogMetadataWarning(load.Result);
            Stopwatch restoreTimer = Stopwatch.StartNew();
            int restoredCount;
            try
            {
                m_TopologyEncoding.Clear();
                restoredCount = m_ObjectSystem.RestoreSnapshot(load.Result.TakeObjectsForRestore(m_TopologyEncoding));
                m_TopologyEncoding.CommitLoaded(m_ObjectSystem.Objects);
            }
            catch (Exception exception)
            {
                m_TopologyEncoding.Clear();
                restoreTimer.Stop();
                m_RecoveryStatus = RecoveryMessages.ReconstructionFailed;
                Mod.log.Error($"[PO][Persistence Recovery] APPLY reconstruction failed sourceKey={candidate.FileKey}, " + $"targetKey={m_CurrentLoadedExactFileKey}: {exception}");
                return false;
            }

            restoreTimer.Stop();

            for (int i = 0; i < load.Result.SkippedObjects.Count; i++)
            {
                SnapshotObjectError skipped = load.Result.SkippedObjects[i];
                Mod.log.Warn($"[PO][Persistence Recovery] APPLY skipped object directoryIndex={skipped.DirectoryIndex}, " + $"persistentId={skipped.PersistentId}: {skipped.Message}");
            }

            // Recovery changes runtime state only.
            // It does not copy the old file onto the current save's exact key ~ the next normal save does that.
            string sourceKey = candidate.FileKey;
            m_RecoveredSnapshotKey = sourceKey;
            SaveCatalogVersion++;
            string targetKey = m_CurrentLoadedExactFileKey;
            Hash128 targetSaveGuid = m_CurrentLoadedSaveGuid;
            m_RecoveryStatus = RecoveryMessages.Restored(sourceKey, load.Result.ObjectCount, load.Result.SkippedObjects.Count, restoredCount);

            Mod.log.Info($"[PO][Persistence Recovery] APPLY PASS sourceKey={sourceKey}, " + $"sourceSaveGuid={candidate.SnapshotSaveGuid:D}, targetKey={targetKey}, " + $"targetSaveGuid={targetSaveGuid}, sessionGuid={m_CurrentLoadedSessionGuid:D}, " + $"objectsDecoded={load.Result.ObjectCount}, objectsRestored={restoredCount}, " + $"objectsSkipped={load.Result.SkippedObjects.Count}, bytes={load.Bytes}, " + $"readMs={load.ReadMs:0.###}, restoreMs={restoreTimer.Elapsed.TotalMilliseconds:0.###}. " + "No sidecar was copied or renamed; the next successful vanilla save will write the current exact snapshot.");

            ClearRecoveryCandidatesOnly();
            RecoveryResultPending = true;
            return true;
        }

        private void RefreshRecoveryCandidatesAfterApplyFailure()
        {
            if (!m_HasRecoveryContext) return;
            QueueRecoveryDiscovery();
        }

        private void ClearRecoveryState()
        {
            m_RecoveryDiscoveryPending = false;
            m_RecoveryWaitingKey = null;
            m_RecoveryPage = 0;
            if (m_MaintenanceRequest?.Kind == MaintenanceKind.Recovery) m_MaintenanceRequest = null;
            RecoveryResultPending = false;
            m_CurrentLoadedSaveGuid = default;
            m_CurrentLoadedExactFileKey = string.Empty;
            m_CurrentLoadedSessionGuid = Guid.Empty;
            m_HasRecoveryContext = false;
            m_RecoveryCandidates = Array.Empty<SaveRecord>();
            NotifyRecoveryCandidatesChanged();
            m_RecoveryStatus = LocalizedText.Empty;
        }

        private void ClearRecoveryCandidatesOnly()
        {
            m_RecoveryPage = 0;
            m_RecoveryCandidates = Array.Empty<SaveRecord>();
            NotifyRecoveryCandidatesChanged();
        }

        private void NotifyRecoveryCandidatesChanged()
        {
            unchecked
            {
                RecoveryCandidatesVersion++;
            }
        }

        private void ClearRuntimeObjectsAfterLoadFailure()
        {
            if (m_ObjectSystem == null)
            {
                return;
            }

            try
            {
                m_ObjectSystem.RestoreSnapshot(Array.Empty<PersistenceObjectData>());
            }
            catch (Exception exception)
            {
                Mod.log.Error("[PO][Persistence] Failed to clear PO runtime state after snapshot load failure: " + exception);
            }
        }

        private static void LogMetadataWarning(SnapshotReadResult result)
        {
            if (result.MetadataStatus == SnapshotMetadataStatus.Invalid || result.MetadataStatus == SnapshotMetadataStatus.Unsupported)
            {
                Mod.log.Warn($"[PO][Persistence] Save metadata unavailable: {result.MetadataWarning} PO data validation is unchanged.");
            }
        }

        private void ClearPendingLoadIdentity()
        {
            m_PendingLoadedSaveGuid = default;
            m_HasPendingLoadedSaveGuid = false;
        }

        private void OnGameSaveLoad(string saveName, string previewUri, bool start, bool success)
        {
            // The start event is too early ~ writing now could leave a PO2 sidecar for a vanilla save that ultimately failed.
            if (start)
            {
                StopMaintenance();
                m_SaveManager?.Cancel();
                SaveCatalogVersion++;
                return;
            }

            if (!success)
            {
                Mod.log.Warn($"[PO][Persistence] Vanilla save failed for '{saveName}'. " + "No PO2 snapshot was written.");
                return;
            }

            try
            {
                WriteSuccessfulSaveSnapshot(saveName);
            }
            catch (Exception exception)
            {
                // The vanilla save has already succeeded.
                // PO2 persistence must never allow its own failure to escape back into game flow.
                Mod.log.Error($"[PO][Persistence] Vanilla save '{saveName}' succeeded, " + "but the external PO2 snapshot write failed. The vanilla save remains valid. " + exception);
            }
        }

        private void WriteSuccessfulSaveSnapshot(string saveName)
        {
            StopMaintenance();
            GameManager gameManager = GameManager.instance;
            SaveGameMetadata metadata = gameManager?.settings?.userState?.lastSaveGameMetadata;
            if (metadata == null)
            {
                Mod.log.Warn($"[PO][Persistence] Vanilla save '{saveName}' succeeded, " + "but lastSaveGameMetadata was null. No PO2 snapshot was written.");
                return;
            }

            SaveInfo saveInfo = metadata.target;
            if (saveInfo == null)
            {
                Mod.log.Warn($"[PO][Persistence] Vanilla save '{saveName}' succeeded, " + $"saveGuid={metadata.id}, but SaveGameMetadata.target was null. " + "No PO2 snapshot was written.");
                return;
            }

            Hash128 vanillaSaveGuid = metadata.id;
            // Use the session stored in SaveInfo for the file.
            // The telemetry value is logged as a check, not substituted into save identity.
            Guid persistedSessionGuid = saveInfo.sessionGuid;
            Guid runtimeSessionGuid = Telemetry.GetCurrentSession();

            string capturedSaveName = string.IsNullOrEmpty(saveName) ? saveInfo.displayName : saveName;
            SnapshotMetadata snapshotMetadata = SnapshotMetadata.Capture(saveInfo.cityName, capturedSaveName, saveInfo.mapName, saveInfo.lastModified, saveInfo.autoSave, new SnapshotCityDate(saveInfo.simulationDate.year, saveInfo.simulationDate.month, saveInfo.simulationDate.hour, saveInfo.simulationDate.minute), out string metadataWarning);
            if (metadataWarning.Length > 0)
            {
                Mod.log.Warn($"[PO][Persistence] {metadataWarning} PO data will still be saved.");
            }

            m_LatestSnapshotKey = vanillaSaveGuid.ToString();
            SaveCatalogVersion++;

            var pending = new PendingSuccessfulSave(capturedSaveName, vanillaSaveGuid, persistedSessionGuid, runtimeSessionGuid, snapshotMetadata);

            if (m_SnapshotGate.IsActive || (m_ToolSystem != null && m_ToolSystem.MeshMutationBusy))
            {
                m_DeferredSuccessfulSaves.Add(pending);
                Mod.log.Info($"[PO][Persistence] Deferred snapshot for vanilla " + $"save '{saveName}' until active PO work " + "finishes.");
                return;
            }

            WriteSuccessfulSaveSnapshot(pending);
        }

        private void WriteSuccessfulSaveSnapshot(PendingSuccessfulSave pending)
        {
            Stopwatch captureTimer = Stopwatch.StartNew();
            CitySaveCapture capture = CaptureObjects();
            captureTimer.Stop();

            SnapshotWriteResult write;
            try
            {
                write = m_FileStore.WriteTransactional(pending.ExactSaveFileKey, pending.SnapshotSaveGuid, pending.PersistedSessionGuid, pending.Metadata, capture.Objects, new SnapshotEncodingOptions(Mod.Settings?.SaveCompression ?? SaveCompression.Compact), capture);
                m_TopologyEncoding.Promote(capture);
            }
            catch
            {
                // A failed proof must not keep handing out the same encoded record.
                m_TopologyEncoding.Clear();
                throw;
            }

            m_RecoveredSnapshotKey = string.Empty;
            RequestAutomaticCleanup();
            SaveCatalogVersion++;
            if (m_HasRecoveryContext)
            {
                ClearRecoveryState();
            }

            SnapshotCityDate? cityDate = pending.Metadata.CityDate;
            string cityDateRaw = cityDate.HasValue ? $"{cityDate.Value.Year},{cityDate.Value.Month},{cityDate.Value.Hour},{cityDate.Value.Minute}" : "unknown";
            Mod.log.Info($"[PO][Persistence] WRITE PASS name='{pending.SaveName}', " + $"container={write.ContainerVersion}, " + $"city='{pending.Metadata.CityName}', map='{pending.Metadata.MapName}', " + $"savedAtUtc={pending.Metadata.SavedAtUtc?.ToString("O") ?? "unknown"}, cityDateRaw=({cityDateRaw}), " + $"saveGuid={pending.VanillaSaveGuid}, " + $"sessionGuid={pending.PersistedSessionGuid:D}, " + $"runtimeSessionGuid={pending.RuntimeSessionGuid:D}, " + $"sessionMatch={pending.PersistedSessionGuid == pending.RuntimeSessionGuid}, " + $"autoSave={pending.AutoSave}, objects={write.ObjectCount}, bytes={write.Bytes}, " + $"captureMs={captureTimer.Elapsed.TotalMilliseconds:0.###}, " + $"encodeWriteMs={write.EncodeWriteMs:0.###}, " + $"compression={write.Compression}, parts={write.EncodingMetrics.PartCount}, " + $"encodingWorkers={write.EncodingMetrics.PeakWorkers}, " + $"compressionCalls={write.EncodingMetrics.CompressionCalls}, compressionInputBytes={write.EncodingMetrics.CompressionInputBytes}, " + $"compressionWorkMs={write.EncodingMetrics.CompressionMs:0.###}, partWorkMs={write.EncodingMetrics.PartWorkMs:0.###}, " + $"diskVerifyMs={write.DiskVerifyMs:0.###}, " + $"finalizeMs={write.FinalizeMs:0.###}, totalWriteMs={write.TotalMs:0.###}, " + $"path='{write.FinalPath}'.");
        }

        private CitySaveCapture CaptureObjects()
        {
            if (m_ObjectSystem == null)
            {
                throw new InvalidOperationException("Procedural object runtime system is unavailable.");
            }

            // Runtime IDs are not persisted
            // Sorting by them makes output ordering deterministic for a given live object set though.
            List<ProceduralObject> liveObjects = new List<ProceduralObject>(m_ObjectSystem.Objects);
            liveObjects.Sort((left, right) => left.Id.CompareTo(right.Id));

            return new CitySaveCapture(liveObjects, m_TopologyEncoding);
        }

        [Preserve]
        protected override void OnDestroy()
        {
            m_TopologyEncoding.Clear();
            DisposeMaintenance();
            if (m_LoadGameSystem != null)
            {
                m_LoadGameSystem.onOnSaveGameLoaded -= OnSaveGameLoaded;
            }

            if (GameManager.instance != null)
            {
                GameManager.instance.onGameSaveLoad -= OnGameSaveLoad;
            }

            ClearPendingLoadIdentity();
            ClearRecoveryState();
            m_DeferredSuccessfulSaves.Clear();
            m_DeferredSuccessfulSaveIndex = 0;
            base.OnDestroy();
        }
    }
}
