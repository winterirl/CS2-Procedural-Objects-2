using System;
using System.Linq;
using System.Threading;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Assets;
using PO2;
using PO2.Persistence;

internal static partial class Program
{
    private static class CoordinatorTests
    {
        private static void Reset()
        {
            AssetDatabase.global.Saves.Clear();
            AssetDatabase.global.OnRead = null;
            AssetDatabase.global.ReadCount = 0;
            AssetDatabase.global.areNotificationsEnabled = true;
            AssetDatabase.user.isCached = true;
            Mod.Settings.CleanupOrphanedAutosaves = true;
            Mod.Settings.RetainedOrphanedAutosaves = 3;
        }
        private static void NotifyChange() => AssetDatabase.global.onAssetDatabaseChanged.Notify(new AssetChangedEventArgs
            { asset = new SaveGameMetadata(), database = AssetDatabase.user, change = ChangeType.AssetAdded });
        private static void Drain(PersistenceCoordinatorSystem coordinator)
        {
            Check(SpinWait.SpinUntil(() => { coordinator.TestTick(); return !coordinator.TestHasWork && !coordinator.ManagerOperationBusy; }, TimeSpan.FromSeconds(10)), "Coordinator failed to finish its queued work.");
        }

        internal static void CatalogCaptureAvoidsLazyLoads()
        {
            Reset();
            for (uint i = 1; i <= 100; i++) AssetDatabase.global.Saves.Add(new SaveGameMetadata { id = new Hash128(i, 0, 0, 0) });
            using var capture = new GameSaveCatalog(7);
            capture.Advance();
            Check(!capture.Complete && capture.Entries.Count <= 32, "Catalog capture must stop after a bounded batch.");
            while (!capture.Complete) capture.Advance();
            Check(capture.Succeeded && capture.Entries.Count == 100 && capture.Entries.All(e => e.PresenceOnly), "Catalog capture should collect only immutable IDs without target loads.");
            AssetDatabase.user.isCached = false;
            Check(!GameSaveCatalog.StorageReady(), "An uncached save storage is not a complete catalog.");
            AssetDatabase.user.isCached = true;
            AssetDatabase.global.areNotificationsEnabled = false;
            Check(!GameSaveCatalog.StorageReady(), "Suppressed catalog events must suspend cleanup.");
            Reset();
        }

        internal static void CatalogChangeRejectsStaleWork()
        {
            Reset();
            using var folder = new Folder();
            Guid session = NextId();
            for (int i = 0; i < 8; i++) folder.Write(session, i);
            AssetDatabase.global.Saves.Add(new SaveGameMetadata { id = new Hash128(50000, 0, 0, 0) });
            var coordinator = new PersistenceCoordinatorSystem(folder.Path);
            try
            {
                bool notified = false;
                AssetDatabase.global.OnRead = () => { if (!notified) { notified = true; NotifyChange(); } };
                coordinator.TestRequestAutomatic();
                coordinator.TestTick();
                Check(notified && !coordinator.TestWorkerActive && coordinator.TestAutoRequested && folder.Store.DiscoverSaves().Saves.Count == 8,
                    "A catalog changed during capture must not start deletion from that capture.");
                AssetDatabase.global.OnRead = null;
                coordinator.TestTick();
                coordinator.TestRequestAutomatic();
                coordinator.TestTick();
                Drain(coordinator);
                Check(folder.Store.DiscoverSaves().Saves.Count == 3, "A fresh catalog may resume retention after invalidation.");
            }
            finally { coordinator.TestDispose(); Reset(); }
        }

        internal static void SettingsAndRecoverySuspendCleanup()
        {
            Reset();
            using var folder = new Folder();
            Guid session = NextId();
            for (int i = 0; i < 8; i++) folder.Write(session, i);
            var coordinator = new PersistenceCoordinatorSystem(folder.Path);
            try
            {
                Mod.Settings.CleanupOrphanedAutosaves = false;
                coordinator.TestTick();
                coordinator.TestRequestAutomatic(); coordinator.TestTick();
                Check(!coordinator.TestHasWork && folder.Store.DiscoverSaves().Saves.Count == 8, "Disabled cleanup must not start a worker.");
                Mod.Settings.CleanupOrphanedAutosaves = true;
                coordinator.TestTick();
                coordinator.TestRecoveryContext(session);
                coordinator.TestRequestAutomatic(); coordinator.TestTick();
                Check(!coordinator.TestHasWork && folder.Store.DiscoverSaves().Saves.Count == 8, "Recovery context must defer automatic cleanup.");
                coordinator.SetSaveManagerVisible(true);
                coordinator.RequestSaveManagerRefresh(); coordinator.TestTick(); Drain(coordinator);
                Check(coordinator.ManagerPage.TotalCount == 8, "Save Manager refresh still works while automatic retention is suspended.");
            }
            finally { coordinator.TestDispose(); Reset(); }
        }

        internal static void RecoveryQueueSurvivesManagerRefresh()
        {
            Reset();
            using var folder = new Folder();
            Guid session = NextId();
            for (int i = 0; i < 65; i++) folder.Write(session, i);
            var coordinator = new PersistenceCoordinatorSystem(folder.Path);
            try
            {
                coordinator.TestRecoveryContext(session);
                coordinator.TestBeginRecovery();
                coordinator.SetSaveManagerVisible(true);
                coordinator.RequestSaveManagerRefresh();
                coordinator.TestTick(); Drain(coordinator);
                Check(!coordinator.RecoveryOperationBusy && coordinator.TestRecoveryCount == 65 && coordinator.RecoveryPage().Count == 50,
                    "A manager refresh must not strand a pending recovery scan.");
                string key = coordinator.ManagerPage.Saves[0].FileKey;
                coordinator.RequestSaveDeletion("selected", "", key); coordinator.TestTick(); Drain(coordinator);
                Check(coordinator.SaveManager.Pending != null, "Preview should publish a server-side deletion plan.");
                coordinator.RequestConfirmSaveDeletion(coordinator.SaveManager.Pending.Token); coordinator.TestTick(); Drain(coordinator);
                Check(coordinator.ManagerPage.TotalCount == 64 && coordinator.TestRecoveryCount == 64 &&
                    coordinator.ManagerCatalogVersion == coordinator.SaveCatalogVersion,
                    "Completed manual deletion must update both lists without requiring another manager scan.");
            }
            finally { coordinator.TestDispose(); Reset(); }
        }

        internal static void StartupRetainsThreeAndRefreshesPages()
        {
            Reset();
            using var folder = new Folder();
            Guid session = NextId();
            for (int i = 0; i < 70; i++) folder.Write(session, i);
            var coordinator = new PersistenceCoordinatorSystem(folder.Path);
            try
            {
                var old = folder.Store.DiscoverSaves(s_Complete).Saves.Last();
                coordinator.TestProtect(old.FileKey);
                coordinator.SetSaveManagerVisible(true);
                coordinator.RequestSaveManagerRefresh(); coordinator.TestTick(); Drain(coordinator);
                Check(coordinator.ManagerPage.Saves.Length == 50 && coordinator.ManagerPage.TotalCount == 70, "Refresh should publish a bounded page.");
                coordinator.RequestSaveManagerPage("", "all", false, 1); coordinator.TestTick(); Drain(coordinator);
                Check(coordinator.ManagerPage.Saves.Length == 20 && coordinator.ManagerPage.Index == 1, "Page requests should use the worker's cached discovery.");
                coordinator.SetSaveManagerVisible(false);
                coordinator.TestRequestAutomatic(); coordinator.TestTick(); Drain(coordinator);
                var remaining = folder.Store.DiscoverSaves(s_Complete);
                Check(remaining.Saves.Count == 4 && remaining.Saves.Any(s => s.FileKey == old.FileKey), "Automatic cleanup must retain newest three plus the loaded snapshot.");
            }
            finally { coordinator.TestDispose(); Reset(); }
        }
    }
}
