using System;
using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Assets;
using PO2.Persistence.Model;

namespace PO2.Persistence
{
    internal static class GameSaveCatalog
    {
        // Called on the game thread when discovery is requested, never from a UI getter.
        internal static GameSaveCatalogSnapshot Read()
        {
            var entries = new List<GameSaveCatalogEntry>();
            bool complete = true;
            try
            {
                foreach (SaveGameMetadata asset in AssetDatabase.global.GetAssets(default(SearchFilter<SaveGameMetadata>)))
                {
                    try
                    {
                        SaveInfo info = asset?.target;
                        if (asset == null || !asset.isValid || info == null || info.saveGameData == null || !info.saveGameData.isValid)
                        {
                            // An unreadable catalog entry is not evidence that its save was deleted.
                            complete = false;
                            continue;
                        }

                        Hash128 id = asset.id;
                        SnapshotMetadata metadata = SnapshotMetadata.Capture(info.cityName, info.displayName, info.mapName, info.lastModified, info.autoSave, new SnapshotCityDate(info.simulationDate.year, info.simulationDate.month, info.simulationDate.hour, info.simulationDate.minute), out _);
                        entries.Add(new GameSaveCatalogEntry(id.ToString(), SaveIdentity.ToSnapshotGuid(id), info.sessionGuid, metadata));
                    }
                    catch (Exception exception)
                    {
                        complete = false;
                        Mod.log.Warn($"[PO][Save Discovery] Game save metadata could not be read: {exception.Message}");
                    }
                }
            }
            catch (Exception exception)
            {
                complete = false;
                Mod.log.Warn($"[PO][Save Discovery] Game save catalog scan failed: {exception.Message}");
            }

            if (!complete)
            {
                Mod.log.Warn("[PO][Save Discovery] Game save catalog is incomplete. Unmatched PO2 files have Unknown status.");
            }

            return new GameSaveCatalogSnapshot(entries, complete);
        }
    }
}