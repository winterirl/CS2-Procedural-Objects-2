using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PO2.Localization;
using PO2.Persistence.Format;
using PO2.Persistence.Model;

namespace PO2.Persistence
{
    // External snapshot store used by exact-save persistence and recovery discovery.
    // The vanilla save is already committed before this service is invoked.
    // A PO2 failure NEVER deletes or replaces the previous known-good snapshot.
    internal sealed partial class SnapshotFileStore
    {
        private readonly string m_RootPath;

        internal SnapshotFileStore(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException("PO2 snapshot root path cannot be blank.", nameof(rootPath));
            }

            m_RootPath = rootPath;
        }

        internal string RootPath => m_RootPath;

        internal TempCleanupResult CleanupStaleTemps()
        {
            List<string> deleted = new List<string>();
            List<TempCleanupError> failed = new List<TempCleanupError>();

            if (!Directory.Exists(m_RootPath))
            {
                return new TempCleanupResult(deleted, failed);
            }

            // Only top-level transaction temps belong to this cleanup.
            // Never walk arbitrary directories under ModsData looking for files to delete.
            string[] tempFiles;
            try
            {
                tempFiles = Directory.GetFiles(m_RootPath, "*.po2.tmp", SearchOption.TopDirectoryOnly);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                failed.Add(new TempCleanupError(m_RootPath, exception.Message));
                return new TempCleanupResult(deleted, failed);
            }

            for (int i = 0; i < tempFiles.Length; i++)
            {
                string tempPath = tempFiles[i];
                try
                {
                    File.Delete(tempPath);
                    deleted.Add(tempPath);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
                {
                    failed.Add(new TempCleanupError(tempPath, exception.Message));
                }
            }

            return new TempCleanupResult(deleted, failed);
        }

        internal SnapshotLoadResult ReadExact(string exactSaveFileKey, Guid expectedSaveGuid, Guid expectedSessionGuid)
        {
            if (string.IsNullOrWhiteSpace(exactSaveFileKey))
            {
                throw new ArgumentException("Exact save file key cannot be blank.", nameof(exactSaveFileKey));
            }

            // The filename chooses a candidate, but embedded identities still have to agree.
            // A renamed/copied sidecar is not silently accepted.
            string path = Path.Combine(m_RootPath, exactSaveFileKey + ".po2");
            if (!File.Exists(path))
            {
                return SnapshotLoadResult.Missing(path);
            }

            Stopwatch timer = Stopwatch.StartNew();
            try
            {
                long bytes;
                SnapshotReadResult result;
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 64, FileOptions.SequentialScan))
                {
                    bytes = stream.Length;
                    if (!SnapshotReader.TryRead(stream, out result, out string error))
                    {
                        timer.Stop();
                        return SnapshotLoadResult.Invalid(path, bytes, timer.Elapsed.TotalMilliseconds, error);
                    }
                }

                if (result.SaveGuid != expectedSaveGuid)
                {
                    timer.Stop();
                    return SnapshotLoadResult.Invalid(path, bytes, timer.Elapsed.TotalMilliseconds, "Embedded exact save GUID does not match the loaded vanilla save.");
                }

                if (result.SessionGuid != expectedSessionGuid)
                {
                    timer.Stop();
                    return SnapshotLoadResult.Invalid(path, bytes, timer.Elapsed.TotalMilliseconds, $"Embedded session GUID {result.SessionGuid:D} does not match restored vanilla session {expectedSessionGuid:D}.");
                }

                timer.Stop();
                return SnapshotLoadResult.Valid(path, bytes, timer.Elapsed.TotalMilliseconds, result);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException || exception is EndOfStreamException || exception is OverflowException || exception is ArgumentException)
            {
                timer.Stop();
                return SnapshotLoadResult.Invalid(path, 0, timer.Elapsed.TotalMilliseconds, exception.Message);
            }
        }

        internal SaveDiscoveryResult DiscoverSaves(GameSaveCatalogSnapshot catalog = null)
        {
            catalog = catalog ?? GameSaveCatalogSnapshot.s_Unavailable;
            List<SaveRecord> saves = new List<SaveRecord>();
            List<SaveDiscoveryError> rejected = new List<SaveDiscoveryError>();

            string[] files;
            try
            {
                files = Directory.GetFiles(m_RootPath, "*.po2", SearchOption.TopDirectoryOnly);
            }
            catch (DirectoryNotFoundException) when (!File.Exists(m_RootPath))
            {
                return new SaveDiscoveryResult(saves, rejected);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                rejected.Add(new SaveDiscoveryError(string.Empty, m_RootPath, LocalizedText.FromException(exception)));
                return new SaveDiscoveryResult(saves, rejected, false);
            }

            // Discovery is cheap ~ validate container/header data and session identity,
            // but leave every object block untouched for now.
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                string fileKey = Path.GetFileNameWithoutExtension(path);

                if (!IsValidExactFileKey(fileKey))
                {
                    rejected.Add(new SaveDiscoveryError(fileKey, path, LocalizedText.For("PO2.SaveManager.Scan.Error.InvalidFilename")));
                    continue;
                }

                try
                {
                    FileInfo info = new FileInfo(path);
                    SnapshotHeaderInfo header;
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
                    {
                        if (!SnapshotReader.TryReadHeader(stream, out header, out string error))
                        {
                            rejected.Add(new SaveDiscoveryError(fileKey, path, LocalizedText.For("PO2.SaveManager.Scan.Error.InvalidHeader", ("details", error))));
                            continue;
                        }
                    }

                    if (!string.Equals(fileKey, SaveIdentity.ToFileKey(header.SaveGuid), StringComparison.OrdinalIgnoreCase))
                    {
                        rejected.Add(new SaveDiscoveryError(fileKey, path, LocalizedText.For("PO2.SaveManager.Scan.Error.IdentityMismatch")));
                        continue;
                    }

                    saves.Add(new SaveRecord(fileKey, path, header, info.Length, info.LastWriteTimeUtc, catalog));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException || exception is EndOfStreamException || exception is OverflowException || exception is ArgumentException)
                {
                    rejected.Add(new SaveDiscoveryError(fileKey, path, LocalizedText.FromException(exception)));
                }
            }

            saves.Sort((left, right) =>
            {
                // Most recent is the useful default presentation.
                // File key breaks timestamp ties so UI order stays stable.
                int byTime = right.DisplayTimeUtc.CompareTo(left.DisplayTimeUtc);
                return byTime != 0 ? byTime : string.CompareOrdinal(left.FileKey, right.FileKey);
            });

            return new SaveDiscoveryResult(saves, rejected);
        }

        private static bool IsValidExactFileKey(string fileKey)
        {
            if (string.IsNullOrEmpty(fileKey) || fileKey.Length != 32)
            {
                return false;
            }

            for (int i = 0; i < fileKey.Length; i++)
            {
                char c = fileKey[i];
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }

        internal SnapshotWriteResult WriteTransactional(string exactSaveFileKey, Guid exactSaveGuid, Guid sessionGuid, SnapshotMetadata metadata, IReadOnlyList<PersistenceObjectData> objects, SnapshotEncodingOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(exactSaveFileKey))
            {
                throw new ArgumentException("Exact save file key cannot be blank.", nameof(exactSaveFileKey));
            }

            if (objects == null)
            {
                throw new ArgumentNullException(nameof(objects));
            }

            Directory.CreateDirectory(m_RootPath);

            // Temp and final live together so the final rename/replace stays on
            // one volume and can use filesystem transaction guarantees.
            string finalPath = Path.Combine(m_RootPath, exactSaveFileKey + ".po2");
            string tempPath = finalPath + ".tmp";

            Stopwatch totalTimer = Stopwatch.StartNew();
            Stopwatch phaseTimer = Stopwatch.StartNew();
            var encodingMetrics = new EncodingMetrics();
            double encodeMs, writeMs;
            long storedBytes;
            // Keep the writer's buffer through the disk write instead of copying
            // the complete snapshot into another byte array.
            using (var encoded = new MemoryStream())
            {
                SnapshotWriter.Write(encoded, exactSaveGuid, sessionGuid, metadata, objects, options, encodingMetrics);
                encodeMs = phaseTimer.Elapsed.TotalMilliseconds;
                storedBytes = encoded.Length;
                TryDeleteTemp(tempPath);
                phaseTimer.Restart();
                using (FileStream stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 64, FileOptions.WriteThrough))
                {
                    encoded.WriteTo(stream);
                    stream.Flush(true);
                }

                writeMs = phaseTimer.Elapsed.TotalMilliseconds;
            }

            // Reopen the actual bytes that reached disk and validate the complete
            // header/directory/object checksums before finalization.
            phaseTimer.Restart();
            using (FileStream stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 64, FileOptions.SequentialScan))
            {
                ValidateSnapshotStream(stream, exactSaveGuid, sessionGuid, objects.Count, metadata, "temporary snapshot");
            }

            double diskVerifyMs = phaseTimer.Elapsed.TotalMilliseconds;

            phaseTimer.Restart();
            FinalizeTransactional(tempPath, finalPath);
            double finalizeMs = phaseTimer.Elapsed.TotalMilliseconds;

            totalTimer.Stop();
            return new SnapshotWriteResult(finalPath, storedBytes, objects.Count, encodeMs, writeMs, diskVerifyMs, finalizeMs, totalTimer.Elapsed.TotalMilliseconds, options?.Compression ?? SaveCompression.Compact, encodingMetrics);
        }

        private static void ValidateSnapshotStream(Stream stream, Guid expectedSaveGuid, Guid expectedSessionGuid, int expectedObjectCount, SnapshotMetadata expectedMetadata, string label)
        {
            if (!SnapshotReader.TryRead(stream, out SnapshotReadResult result, out string error))
            {
                throw new InvalidDataException($"PO2 {label} failed self-validation: {error}");
            }

            ValidateReadResult(result, expectedSaveGuid, expectedSessionGuid, expectedObjectCount, expectedMetadata, label);
        }

        private static void ValidateReadResult(SnapshotReadResult result, Guid expectedSaveGuid, Guid expectedSessionGuid, int expectedObjectCount, SnapshotMetadata expectedMetadata, string label)
        {
            if (result == null)
            {
                throw new InvalidDataException($"PO2 {label} produced no read result.");
            }

            if (result.SaveGuid != expectedSaveGuid)
            {
                throw new InvalidDataException($"PO2 {label} exact save identity mismatch.");
            }

            if (result.SessionGuid != expectedSessionGuid)
            {
                throw new InvalidDataException($"PO2 {label} session identity mismatch.");
            }

            if (result.ContainerVersion != SnapshotFormat.kContainerVersion || result.MetadataStatus != SnapshotMetadataStatus.Available || expectedMetadata == null || !expectedMetadata.Equals(result.Metadata))
            {
                throw new InvalidDataException($"PO2 {label} metadata did not round-trip: {result.MetadataStatus}. {result.MetadataWarning}");
            }

            // Skipping a damaged historical object is useful during load, but a
            // file we just wrote must round-trip every captured object.
            if (result.SkippedObjects.Count != 0)
            {
                throw new InvalidDataException($"PO2 {label} unexpectedly skipped {result.SkippedObjects.Count} object(s).");
            }

            if (result.Objects.Count != expectedObjectCount)
            {
                throw new InvalidDataException($"PO2 {label} object count mismatch: expected {expectedObjectCount}, got {result.Objects.Count}.");
            }
        }

        private static void FinalizeTransactional(string tempPath, string finalPath)
        {
            if (File.Exists(finalPath))
            {
                // File.Replace keeps the old final file intact if replacement fails.
                // Do not fall back to delete+move here.
                File.Replace(tempPath, finalPath, null);
            }
            else
            {
                // Same-volume rename from the adjacent temp path is the first
                // publication for this exact save GUID.
                File.Move(tempPath, finalPath);
            }
        }

        private static void TryDeleteTemp(string tempPath)
        {
            if (!File.Exists(tempPath))
            {
                return;
            }

            try
            {
                File.Delete(tempPath);
            }
            catch (Exception exception)
            {
                throw new IOException($"Could not clear stale PO2 temporary snapshot '{tempPath}'.", exception);
            }
        }
    }

    internal sealed class TempCleanupResult
    {
        internal TempCleanupResult(IReadOnlyList<string> deletedPaths, IReadOnlyList<TempCleanupError> failures)
        {
            DeletedPaths = deletedPaths ?? Array.Empty<string>();
            Failures = failures ?? Array.Empty<TempCleanupError>();
        }

        internal IReadOnlyList<string> DeletedPaths { get; }
        internal IReadOnlyList<TempCleanupError> Failures { get; }
    }

    internal sealed class TempCleanupError
    {
        internal TempCleanupError(string path, string message)
        {
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        internal string Path { get; }
        internal string Message { get; }
    }

    internal sealed class SnapshotWriteResult
    {
        internal SnapshotWriteResult(string finalPath, long bytes, int objectCount, double encodeMs, double writeMs, double diskVerifyMs, double finalizeMs, double totalMs, SaveCompression compression, EncodingMetrics encodingMetrics)
        {
            Compression = compression;
            EncodingMetrics = encodingMetrics;
            FinalPath = finalPath;
            Bytes = bytes;
            ObjectCount = objectCount;
            EncodeMs = encodeMs;
            WriteMs = writeMs;
            DiskVerifyMs = diskVerifyMs;
            FinalizeMs = finalizeMs;
            TotalMs = totalMs;
        }

        internal SaveCompression Compression { get; }
        internal EncodingMetrics EncodingMetrics { get; }
        internal string FinalPath { get; }
        internal long Bytes { get; }
        internal int ObjectCount { get; }
        internal double EncodeMs { get; }
        internal double WriteMs { get; }
        internal double DiskVerifyMs { get; }
        internal double FinalizeMs { get; }
        internal double TotalMs { get; }
    }

    internal sealed class SnapshotLoadResult
    {
        // Missing and invalid are separate outcomes.
        // The coordinator reports them differently and may offer recovery for either one.
        private SnapshotLoadResult(string path, bool exists, bool isValid, long bytes, double readMs, SnapshotReadResult result, string error)
        {
            Path = path ?? string.Empty;
            Exists = exists;
            IsValid = isValid;
            Bytes = bytes;
            ReadMs = readMs;
            Result = result;
            Error = error ?? string.Empty;
        }

        internal string Path { get; }
        internal bool Exists { get; }
        internal bool IsValid { get; }
        internal long Bytes { get; }
        internal double ReadMs { get; }
        internal SnapshotReadResult Result { get; }
        internal string Error { get; }

        internal static SnapshotLoadResult Missing(string path)
        {
            return new SnapshotLoadResult(path, false, false, 0, 0, null, "Snapshot does not exist.");
        }

        internal static SnapshotLoadResult Invalid(string path, long bytes, double readMs, string error)
        {
            return new SnapshotLoadResult(path, true, false, bytes, readMs, null, error);
        }

        internal static SnapshotLoadResult Valid(string path, long bytes, double readMs, SnapshotReadResult result)
        {
            return new SnapshotLoadResult(path, true, true, bytes, readMs, result, null);
        }
    }
}