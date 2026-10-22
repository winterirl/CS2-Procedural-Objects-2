using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using PO2;
using PO2.Interface.Models;
using PO2.Persistence;
using PO2.Persistence.Model;

internal static partial class PersistenceTests {
    private static byte[] WithIdentity(byte[] bytes, Guid save, Guid session) {
        save.ToByteArray().CopyTo(bytes, 16);
        session.ToByteArray().CopyTo(bytes, 32);
        HeaderCrc(bytes);
        return bytes;
    }

    private static void SaveDiscovery() {
        string folder = Path.Combine(Path.GetTempPath(), "PO2-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try {
            var store = new SnapshotFileStore(folder);
            Guid legacyId = new Guid("10000000-0000-0000-0000-000000000001");
            Guid otherCityId = new Guid("20000000-0000-0000-0000-000000000002");
            Guid malformedMetadataId = new Guid("30000000-0000-0000-0000-000000000003");
            string key = SaveIdentity.ToFileKey(Save);
            string legacyKey = SaveIdentity.ToFileKey(legacyId);
            string otherKey = SaveIdentity.ToFileKey(otherCityId);
            string softKey = SaveIdentity.ToFileKey(malformedMetadataId);
            DateTime estimatedTime = new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.WriteAllBytes(Path.Combine(folder, key + ".po2"), Write());
            File.WriteAllBytes(Path.Combine(folder, legacyKey + ".po2"), WithIdentity(Legacy(2), legacyId, Session));
            File.SetLastWriteTimeUtc(Path.Combine(folder, legacyKey + ".po2"), estimatedTime);
            File.WriteAllBytes(Path.Combine(folder, otherKey + ".po2"), WithIdentity(Write(), otherCityId, Guid.NewGuid()));
            byte[] brokenMetadata = WithIdentity(Write(), malformedMetadataId, Session);
            brokenMetadata[96] ^= 1;
            File.WriteAllBytes(Path.Combine(folder, softKey + ".po2"), brokenMetadata);

            var unknown = store.DiscoverSaves();
            Check(unknown.ScanSucceeded && unknown.Saves.Count == 4, "Shared discovery spans all sessions and container versions");
            Check(unknown.Saves.All(s => s.GameSaveStatus == GameSaveStatus.Unknown), "Unavailable catalog never means orphaned");
            var legacy = unknown.Saves.Single(s => s.FileKey == legacyKey);
            Check(legacy.DateEstimated && legacy.DisplayTimeUtc == estimatedTime, "Legacy date explicitly estimated from file time");
            Check(legacy.DisplayMetadata.CityName == "" && legacy.DisplayMetadata.CityDate == null, "Legacy unknowns remain unknown");
            Check(unknown.Saves.Single(s => s.FileKey == softKey).Header.MetadataStatus == SnapshotMetadataStatus.Invalid,
                "Invalid metadata remains recoverable");
            Check(unknown.ForRecovery(key, Session).Saves.Count == 2, "Recovery excludes current save and other sessions");
            Check(unknown.ForRecovery(key, Guid.Empty).Saves.Count == 0, "No recovery lineage from empty session");

            var fallback = new SnapshotMetadata("Catalog city", "Old manual", "Catalog map",
                new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), SnapshotSaveKind.Manual,
                new SnapshotCityDate(2030, 3, 12, 0));
            var catalog = new GameSaveCatalogSnapshot(new[] {
                new GameSaveCatalogEntry(legacyKey.ToUpperInvariant(), legacyId, Session, fallback),
                new GameSaveCatalogEntry(key, Save, Session, fallback),
                new GameSaveCatalogEntry(softKey, malformedMetadataId, Session, fallback),
            }, true);
            var matched = store.DiscoverSaves(catalog);
            legacy = matched.Saves.Single(s => s.FileKey == legacyKey);
            Check(legacy.GameSaveStatus == GameSaveStatus.Matched && !legacy.DateEstimated &&
                legacy.DisplayMetadata.Equals(fallback), "Exact catalog identity enriches legacy metadata");
            Check(matched.Saves.Single(s => s.FileKey == key).DisplayMetadata.Equals(Metadata), "Embedded metadata wins over catalog");
            Check(matched.Saves.Single(s => s.FileKey == softKey).DisplayMetadata.Equals(fallback), "Catalog fills invalid optional metadata");
            Check(matched.Saves.Single(s => s.FileKey == otherKey).GameSaveStatus == GameSaveStatus.Orphaned,
                "Completed catalog scan can identify an orphan");
            Check(matched.Saves.First().DisplayTimeUtc >= matched.Saves.Last().DisplayTimeUtc, "Sort uses the displayed date");
            var ties = matched.Saves.Where(s => s.DisplayTimeUtc == fallback.SavedAtUtc).ToArray();
            Check(string.CompareOrdinal(ties[0].FileKey, ties[1].FileKey) < 0, "Date ties use stable file-key order");

            var incomplete = new GameSaveCatalogSnapshot(new[] {
                new GameSaveCatalogEntry(legacyKey, legacyId, Session, fallback),
            }, false);
            Check(incomplete.Resolve(key, Save, Session, out _) == GameSaveStatus.Unknown, "Failed scan leaves missing entries Unknown");
            Check(incomplete.Resolve(legacyKey, legacyId, Session, out _) == GameSaveStatus.Matched, "Known exact match survives a partial scan");
            Check(catalog.Resolve(legacyKey, legacyId, Guid.NewGuid(), out var noMetadata) == GameSaveStatus.Unknown && noMetadata == null,
                "Different session cannot supply names or orphan status");
            Check(catalog.Resolve(legacyKey, Save, Session, out _) == GameSaveStatus.Unknown, "Different exact GUID cannot supply metadata");
            var conflicting = new GameSaveCatalogSnapshot(new[] {
                new GameSaveCatalogEntry(key, Save, Session, fallback),
                new GameSaveCatalogEntry(key, Save, Guid.NewGuid(), Metadata),
            }, true);
            Check(conflicting.Resolve(key, Save, Session, out _) == GameSaveStatus.Unknown, "Conflicting catalog identities remain Unknown");

            var partial = new SnapshotMetadata("Embedded city", "", "", null, SnapshotSaveKind.Unknown, null);
            store.WriteTransactional(key, Save, Session, partial, Objects());
            var filled = store.DiscoverSaves(catalog).Saves.Single(s => s.FileKey == key);
            Check(filled.DisplayMetadata.CityName == "Embedded city" && filled.DisplayMetadata.SaveName == fallback.SaveName &&
                filled.DisplayMetadata.CityDate.Equals(fallback.CityDate) && !filled.DateEstimated, "Only unavailable fields use catalog fallback");

            // A copied/renamed file must not become a new exact save or recovery candidate.
            File.WriteAllBytes(Path.Combine(folder, new string('f', 32) + ".po2"), Write());
            File.WriteAllBytes(Path.Combine(folder, "bad-name.po2"), Write());
            File.WriteAllBytes(Path.Combine(folder, new string('e', 32) + ".po2"), new byte[] { 1, 2 });
            File.WriteAllBytes(Path.Combine(folder, key + ".po2.tmp"), Write());
            Directory.CreateDirectory(Path.Combine(folder, "nested"));
            File.WriteAllBytes(Path.Combine(folder, "nested", key + ".po2"), Write());
            var rejected = store.DiscoverSaves(catalog);
            Check(rejected.Saves.Count == 4 && rejected.RejectedFiles.Count == 3, "Invalid names, identity mismatch, and malformed files rejected; temps/nested files ignored");
            Equal(File.ReadAllBytes(Path.Combine(folder, legacyKey + ".po2")), WithIdentity(Legacy(2), legacyId, Session), "Discovery never rewrites legacy files");

            // A damaged PO is still listed; only recovery reads the object blocks.
            byte[] damagedObject = Write();
            damagedObject[FirstBlock(damagedObject) + 10] ^= 1;
            File.WriteAllBytes(Path.Combine(folder, key + ".po2"), damagedObject);
            Check(store.DiscoverSaves().Saves.Any(s => s.FileKey == key), "Discovery leaves object blocks unread");
            var load = store.ReadExact(key, Save, Session);
            Check(load.IsValid && load.Result.SkippedObjects.Count == 1, "Full read reports omitted PO");
            File.Delete(Path.Combine(folder, key + ".po2"));
            Check(!store.ReadExact(key, Save, Session).Exists, "Disappeared candidate remains a missing-file outcome");

            // Real game log identity, whose byte order differs from Guid.ToString.
            Check(SaveIdentity.ToFileKey(new Guid("0de6823b-9604-5022-07e9-1bb182e81c71")) == "b3286ed040692205709eb11b288ec117",
                "File-key conversion matches actual CS2 save identity");

            string blockingFile = Path.Combine(folder, "not-a-folder");
            File.WriteAllText(blockingFile, "file");
            Check(!new SnapshotFileStore(blockingFile).DiscoverSaves().ScanSucceeded, "Scan failure is distinct from an empty folder");
            Check(new SnapshotFileStore(Path.Combine(folder, "missing")).DiscoverSaves().ScanSucceeded, "Missing save folder is an empty scan");


            SaveTilePayload(matched.Saves.ToArray());
        }
        finally { Directory.Delete(folder, true); }
    }

    private static void SaveTilePayload(SaveRecord[] saves) {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            using var writer = new TestUIWriter();
            var tiles = SaveTile.Capture(saves);
            writer.ArrayBegin((uint)tiles.Length);
            foreach (var tile in tiles) tile.Write(writer);
            writer.ArrayEnd();
            using var json = writer.Read();
            Check(json.RootElement.GetArrayLength() == saves.Length, "Tile payload parses under non-English culture");
            for (int i = 0; i < saves.Length; i++) {
                var row = json.RootElement[i];
                Check(row.GetProperty("cityName").GetString() == saves[i].DisplayMetadata.CityName &&
                    row.GetProperty("saveName").GetString() == saves[i].DisplayMetadata.SaveName &&
                    row.GetProperty("mapName").GetString() == saves[i].DisplayMetadata.MapName,
                    "Unicode, quotes, backslash and line break survive JSON");
                Check(row.GetProperty("objectCount").GetInt32() == saves[i].ObjectCount &&
                    row.GetProperty("bytes").GetInt64() == saves[i].FileBytes, "Tile numbers are locale independent");
                Check(row.GetProperty("gameSaveStatus").GetString() == saves[i].GameSaveStatus.ToString(), "Game save relationship reaches tile payload");
            }
            using var stringWriter = new TestUIWriter();
            string controls = new string(Enumerable.Range(0, 32).Select(i => (char)i).ToArray()) + "\"\\\u96ea";
            stringWriter.Write(controls);
            using var escaped = stringWriter.Read();
            Check(escaped.RootElement.GetString() == controls, "Every JSON control character round-trips");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
