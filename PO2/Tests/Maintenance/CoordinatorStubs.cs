using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PO2.Localization;

// Model the catalog and game-loop signals. The coordinator, catalog capture and worker are linked production files.
namespace Colossal
{
    public sealed class TaskManager
    {
        public static readonly TaskManager instance = new TaskManager();
        internal Task Pending = Task.CompletedTask;
        public Task Complete(string name) => Pending;
    }
}
namespace Colossal.IO.AssetDatabase
{
    public enum ChangeType { DatabaseRegistered, DatabaseUnregistered, AssetAdded, AssetDeleted, BulkAssetsChange }
    public interface IAssetDatabase { string hostname { get; } bool isCached { get; } bool areNotificationsEnabled { get; } }
    public interface IDataSourceAccessor { DataSource dataSource { get; } }
    public sealed class DataSource { public bool isRemoteStorageSource; public string remoteStorageSourceName = "Local"; }
    public sealed class Storage : IAssetDatabase, IDataSourceAccessor
    {
        public string hostname { get; set; } = "user";
        public bool isCached { get; set; } = true;
        public bool areNotificationsEnabled { get; set; } = true;
        public DataSource dataSource { get; } = new DataSource();
    }
    public sealed class AssetChangedEventArgs
    {
        public object asset;
        public ChangeType change;
        public IAssetDatabase database;
    }
    public sealed class EventBroadcaster<T>
    {
        private event Action<T> m_Changed;
        public void Subscribe(Action<T> handler) { m_Changed += handler; }
        public void Unsubscribe(Action<T> handler) { m_Changed -= handler; }
        internal void Notify(T args) { m_Changed?.Invoke(args); }
    }
    public struct SearchFilter<T> { }
    public class PackageAsset { }
    public sealed class AssetDatabase
    {
        public static bool exists = true;
        public static readonly AssetDatabase global = new AssetDatabase();
        public static readonly Storage user = new Storage();
        public const string kCacheAssetsTaskName = "CacheAssets";
        public bool areNotificationsEnabled = true;
        public readonly EventBroadcaster<AssetChangedEventArgs> onAssetDatabaseChanged = new EventBroadcaster<AssetChangedEventArgs>();
        internal readonly List<Game.Assets.SaveGameMetadata> Saves = new List<Game.Assets.SaveGameMetadata>();
        internal Action OnRead;
        internal int ReadCount;
        public IEnumerable<(string name, Storage db)> GetAvailableRemoteStorages() { yield return ("Local", user); }
        public IEnumerable<T> GetAssets<T>(SearchFilter<T> filter)
        {
            foreach (var save in Saves)
            {
                OnRead?.Invoke();
                ReadCount++;
                yield return (T)(object)save;
            }
        }
    }
}
namespace Game.Assets
{
    public class SaveGameData { }
    public sealed class SaveGameMetadata
    {
        public Colossal.Hash128 id;
        public object target => throw new InvalidOperationException("Catalog capture loaded metadata.");
    }
}
namespace Game
{
    public enum GameMode { Game }
}
namespace Game.SceneFlow
{
    public static class SaveHelpers { public const string kSaveLoadTaskName = "SaveLoad"; }
    public sealed class GameManager
    {
        public static readonly GameManager instance = new GameManager();
        public bool isGameLoading;
    }
}
namespace PO2
{
    internal static class Mod
    {
        internal static readonly TestSettings Settings = new TestSettings();
        internal static readonly TestLog log = new TestLog();
    }
    internal sealed class TestSettings { internal bool CleanupOrphanedAutosaves = true; internal int RetainedOrphanedAutosaves = 3; }
    internal sealed class TestLog { internal void Info(string value) { } internal void Warn(string value) { } }
}
namespace PO2.Persistence
{
    public partial class PersistenceCoordinatorSystem
    {
        private readonly SnapshotFileStore m_FileStore;
        private bool m_HasPendingLoadedSaveGuid;
        private int m_DeferredSuccessfulSaveIndex;
        private readonly List<TestPendingSave> m_DeferredSuccessfulSaves = new List<TestPendingSave>();
        private IReadOnlyList<SaveRecord> m_RecoveryCandidates = Array.Empty<SaveRecord>();
        private bool m_HasRecoveryContext;
        private Guid m_CurrentLoadedSessionGuid;
        private string m_CurrentLoadedExactFileKey = "";
        private LocalizedText m_RecoveryStatus;
        private bool RecoveryResultPending;
        private void NotifyRecoveryCandidatesChanged() { }
        internal PersistenceCoordinatorSystem(string root)
        {
            m_FileStore = new SnapshotFileStore(root);
            m_MaintenanceReady = true;
            InitializeMaintenance();
        }
        internal void TestTick() { UpdateMaintenance(); }
        internal void TestRequestAutomatic() { RequestAutomaticCleanup(); m_MaintenanceNotBefore = 0; }
        internal bool TestHasWork => m_Maintenance.HasWork || m_CatalogCapture != null;
        internal bool TestAutoRequested => m_AutomaticCleanupRequested;
        internal int TestRecoveryCount => m_RecoveryCandidates.Count;
        internal void TestBeginRecovery() { QueueRecoveryDiscovery(); }
        internal bool TestWorkerActive => m_Maintenance.HasWork;
        internal void TestRecoveryContext(Guid session) { m_HasRecoveryContext = true; m_CurrentLoadedSessionGuid = session; }
        internal void TestProtect(string key) { m_LoadedSnapshotKey = key; }
        internal void TestDispose() { DisposeMaintenance(); }
        private sealed class TestPendingSave { internal string ExactSaveFileKey = ""; }
    }
}
