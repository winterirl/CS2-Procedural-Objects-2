using System;
using System.Collections.Generic;
using System.Diagnostics;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Assets;
using Game.SceneFlow;

namespace PO2.Persistence
{
    // Avoid Metadata.target which would probs load from disk.
    internal sealed class GameSaveCatalog : IDisposable
    {
        private readonly IEnumerator<SaveGameMetadata> m_Assets;
        internal readonly List<GameSaveCatalogEntry> Entries = new List<GameSaveCatalogEntry>();
        internal bool Complete { get; private set; }
        internal bool Succeeded { get; private set; } = true;
        internal readonly long Revision;

        internal GameSaveCatalog(long revision)
        {
            Revision = revision;
            m_Assets = AssetDatabase.global.GetAssets(default(SearchFilter<SaveGameMetadata>)).GetEnumerator();
        }

        internal static bool StorageReady()
        {
            try
            {
                if (!AssetDatabase.exists || !AssetDatabase.global.areNotificationsEnabled || !AssetDatabase.user.isCached ||
                    !TaskManager.instance.Complete(AssetDatabase.kCacheAssetsTaskName).IsCompleted) return false;
                foreach (var storage in AssetDatabase.global.GetAvailableRemoteStorages())
                    if (!storage.db.isCached || !storage.db.areNotificationsEnabled) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal void Advance()
        {
            if (Complete) return;
            long until = Stopwatch.GetTimestamp() + Math.Max(1, Stopwatch.Frequency / 2000);
            try
            {
                for (int i = 0; i < 32; i++)
                {
                    if (!m_Assets.MoveNext())
                    {
                        Complete = true;
                        break;
                    }
                    SaveGameMetadata asset = m_Assets.Current;
                    Hash128 id = asset == null ? default : (Hash128)asset.id;
                    if (!id.isValid) Succeeded = false;
                    else Entries.Add(new GameSaveCatalogEntry(id.ToString(), SaveIdentity.ToSnapshotGuid(id), Guid.Empty, null, presenceOnly: true));
                    if (Stopwatch.GetTimestamp() >= until) break;
                }
            }
            catch
            {
                Succeeded = false;
                Complete = true;
            }
            if (Complete) Dispose();
        }

        public void Dispose() { m_Assets.Dispose(); }
    }
}
