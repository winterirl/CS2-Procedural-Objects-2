using System;
using System.Collections.Generic;
using PO2.Localization;
using PO2.Persistence.Format;
using PO2.Persistence.Model;

namespace PO2.Persistence
{
    internal enum GameSaveStatus
    {
        Unknown,
        Matched,
        Orphaned,
    }

    // Shared by recovery and the save manager. Discovery never decodes PO geometry.
    internal sealed class SaveRecord
    {
        internal SaveRecord(string fileKey, string path, SnapshotHeaderInfo header, long fileBytes, DateTime lastWriteTimeUtc, GameSaveCatalogSnapshot catalog)
        {
            FileKey = fileKey;
            Path = path;
            Header = header;
            FileBytes = fileBytes;
            LastWriteTimeUtc = lastWriteTimeUtc;
            GameSaveStatus = catalog.Resolve(fileKey, header.SaveGuid, header.SessionGuid, out SnapshotMetadata fallback);

            SnapshotMetadata embedded = header.Metadata;
            DisplayMetadata = new SnapshotMetadata(PreferLabel(embedded?.CityName, fallback?.CityName), PreferLabel(embedded?.SaveName, fallback?.SaveName), PreferLabel(embedded?.MapName, fallback?.MapName), embedded?.SavedAtUtc ?? fallback?.SavedAtUtc, embedded != null && embedded.SaveKind != SnapshotSaveKind.Unknown ? embedded.SaveKind : fallback?.SaveKind ?? SnapshotSaveKind.Unknown, embedded?.CityDate ?? fallback?.CityDate);
        }

        internal string FileKey { get; }
        internal string Path { get; }
        internal SnapshotHeaderInfo Header { get; }
        internal Guid SnapshotSaveGuid => Header.SaveGuid;
        internal Guid SessionGuid => Header.SessionGuid;
        internal int ObjectCount => Header.ObjectCount;
        internal long FileBytes { get; }
        internal DateTime LastWriteTimeUtc { get; }
        internal GameSaveStatus GameSaveStatus { get; }
        internal SnapshotMetadata DisplayMetadata { get; }
        internal DateTime DisplayTimeUtc => DisplayMetadata.SavedAtUtc ?? LastWriteTimeUtc;
        internal bool DateEstimated => !DisplayMetadata.SavedAtUtc.HasValue;

        private static string PreferLabel(string embedded, string fallback) =>
            !string.IsNullOrWhiteSpace(embedded) ? embedded : fallback ?? string.Empty;
    }

    internal sealed class SaveDiscoveryError
    {
        internal SaveDiscoveryError(string fileKey, string path, LocalizedText message)
        {
            FileKey = fileKey ?? string.Empty;
            Path = path ?? string.Empty;
            Message = message;
        }

        internal string FileKey { get; }
        internal string Path { get; }
        internal LocalizedText Message { get; }
    }

    internal sealed class SaveDiscoveryResult
    {
        internal SaveDiscoveryResult(IReadOnlyList<SaveRecord> saves, IReadOnlyList<SaveDiscoveryError> rejectedFiles, bool scanSucceeded = true)
        {
            Saves = saves;
            RejectedFiles = rejectedFiles;
            ScanSucceeded = scanSucceeded;
        }

        internal IReadOnlyList<SaveRecord> Saves { get; }
        internal IReadOnlyList<SaveDiscoveryError> RejectedFiles { get; }
        internal bool ScanSucceeded { get; }

        internal SaveDiscoveryResult ForRecovery(string currentFileKey, Guid sessionGuid)
        {
            var candidates = new List<SaveRecord>();
            for (int i = 0; i < Saves.Count; i++)
            {
                SaveRecord save = Saves[i];
                if (sessionGuid != Guid.Empty && save.SessionGuid == sessionGuid && !string.Equals(save.FileKey, currentFileKey, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(save);
                }
            }

            return new SaveDiscoveryResult(candidates, RejectedFiles, ScanSucceeded);
        }
    }

    internal sealed class GameSaveCatalogEntry
    {
        internal GameSaveCatalogEntry(string fileKey, Guid snapshotSaveGuid, Guid sessionGuid, SnapshotMetadata metadata)
        {
            FileKey = fileKey;
            SnapshotSaveGuid = snapshotSaveGuid;
            SessionGuid = sessionGuid;
            Metadata = metadata;
        }

        internal string FileKey { get; }
        internal Guid SnapshotSaveGuid { get; }
        internal Guid SessionGuid { get; }
        internal SnapshotMetadata Metadata { get; }
    }

    // Copies only values from the available game catalog. No asset references or disk index.
    internal sealed class GameSaveCatalogSnapshot
    {
        internal static readonly GameSaveCatalogSnapshot s_Unavailable = new GameSaveCatalogSnapshot(Array.Empty<GameSaveCatalogEntry>(), false);

        private readonly Dictionary<string, GameSaveCatalogEntry> m_Entries = new Dictionary<string, GameSaveCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> m_ConflictingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal bool IsComplete { get; }

        internal GameSaveCatalogSnapshot(IEnumerable<GameSaveCatalogEntry> entries, bool isComplete)
        {
            IsComplete = isComplete;
            foreach (GameSaveCatalogEntry entry in entries)
            {
                if (m_Entries.TryGetValue(entry.FileKey, out GameSaveCatalogEntry previous))
                {
                    if (previous.SnapshotSaveGuid != entry.SnapshotSaveGuid || previous.SessionGuid != entry.SessionGuid || !Equals(previous.Metadata, entry.Metadata))
                    {
                        m_ConflictingKeys.Add(entry.FileKey);
                    }
                }
                else
                {
                    m_Entries.Add(entry.FileKey, entry);
                }
            }
        }

        internal GameSaveStatus Resolve(string fileKey, Guid saveGuid, Guid sessionGuid, out SnapshotMetadata metadata)
        {
            metadata = null;
            if (m_ConflictingKeys.Contains(fileKey)) return GameSaveStatus.Unknown;
            if (m_Entries.TryGetValue(fileKey, out GameSaveCatalogEntry entry))
            {
                // Names and timestamps never establish a match.
                if (entry.SnapshotSaveGuid != saveGuid || entry.SessionGuid != sessionGuid || sessionGuid == Guid.Empty) return GameSaveStatus.Unknown;
                metadata = entry.Metadata;
                return GameSaveStatus.Matched;
            }

            return IsComplete ? GameSaveStatus.Orphaned : GameSaveStatus.Unknown;
        }
    }
}