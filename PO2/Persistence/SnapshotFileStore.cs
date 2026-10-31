using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PO2.Persistence.Format;
using PO2.Persistence.Model;
using PO2.Persistence.PreparedTopology;

namespace PO2.Persistence
{
    // External snapshot store used by exact-save persistence and recovery discovery.
    // The vanilla save is already committed before this service is invoked.
    // A PO2 failure NEVER deletes or replaces the previous known-good snapshot.
    internal sealed partial class SnapshotFileStore
    {
        internal SnapshotLoadResult ReadExact(string exactSaveFileKey, Guid expectedSaveGuid, Guid expectedSessionGuid)
        {
            if (string.IsNullOrWhiteSpace(exactSaveFileKey))
            {
                throw new ArgumentException("Exact save file key cannot be blank.", nameof(exactSaveFileKey));
            }

            // The filename chooses a candidate but embedded identities still have to agree.
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
                    if (!SnapshotReader.TryRead(stream, out result, out string error, parallel: true, expectedSaveGuid: expectedSaveGuid, expectedSessionGuid: expectedSessionGuid))
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

        internal SnapshotWriteResult WriteTransactional(string exactSaveFileKey, Guid exactSaveGuid, Guid sessionGuid, SnapshotMetadata metadata, IReadOnlyList<PersistenceObjectData> objects, SnapshotEncodingOptions options = null, CitySaveCapture prepared = null)
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
            long storedBytes;
            var expectedIds = new Guid[objects.Count];
            for (int i = 0; i < objects.Count; i++)
                expectedIds[i] = objects[i]?.PersistentId ?? throw new InvalidDataException("Snapshot object is null.");
            TryDeleteTemp(tempPath);
            using (Stream file = m_IO.CreateTemporary(tempPath))
            using (var output = new BufferedStream(file, 256 * 1024))
            {
                SnapshotWriter.Write(output, exactSaveGuid, sessionGuid, metadata, objects, options, encodingMetrics, prepared);
                storedBytes = output.Length;
                output.Flush();
                m_IO.FlushToDisk(file);
            }
            double encodeWriteMs = phaseTimer.Elapsed.TotalMilliseconds;

            // Verify the bytes actually stored on disk before publishing the sidecar.
            phaseTimer.Restart();
            using (Stream stream = m_IO.OpenVerification(tempPath))
                ValidateSnapshotStream(stream, exactSaveGuid, sessionGuid, expectedIds, metadata, "temporary snapshot", prepared);

            double diskVerifyMs = phaseTimer.Elapsed.TotalMilliseconds;

            phaseTimer.Restart();
            FinalizeTransactional(tempPath, finalPath);
            double finalizeMs = phaseTimer.Elapsed.TotalMilliseconds;

            totalTimer.Stop();
            return new SnapshotWriteResult(finalPath, storedBytes, objects.Count, encodeWriteMs, diskVerifyMs, finalizeMs, totalTimer.Elapsed.TotalMilliseconds, options?.Compression ?? SaveCompression.Compact, encodingMetrics, prepared == null ? SnapshotFormat.kContainerVersion : SnapshotFormat.kPreparedContainerVersion);
        }

        private static void ValidateSnapshotStream(Stream stream, Guid expectedSaveGuid, Guid expectedSessionGuid,
            IReadOnlyList<Guid> expectedIds, SnapshotMetadata expectedMetadata, string label, CitySaveCapture prepared)
        {
            try { SnapshotReader.Verify(stream, expectedSaveGuid, expectedSessionGuid, expectedMetadata, expectedIds, prepared: prepared); }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException($"PO2 {label} failed self-validation: {exception.Message}", exception);
            }
        }

        private void FinalizeTransactional(string tempPath, string finalPath)
        {
            if (File.Exists(finalPath))
            {
                // File.Replace keeps the old final file intact if replacement fails.
                // Do not fall back to delete+move here.
                m_IO.Replace(tempPath, finalPath);
            }
            else
            {
                // Same-volume rename from the adjacent temp path is the first publication for this exact save GUID.
                m_IO.Move(tempPath, finalPath);
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

    internal sealed class SnapshotWriteResult
    {
        internal SnapshotWriteResult(string finalPath, long bytes, int objectCount, double encodeWriteMs, double diskVerifyMs, double finalizeMs, double totalMs, SaveCompression compression, EncodingMetrics encodingMetrics, ushort containerVersion = SnapshotFormat.kContainerVersion)
        {
            ContainerVersion = containerVersion;
            Compression = compression;
            EncodingMetrics = encodingMetrics;
            FinalPath = finalPath;
            Bytes = bytes;
            ObjectCount = objectCount;
            EncodeWriteMs = encodeWriteMs;
            DiskVerifyMs = diskVerifyMs;
            FinalizeMs = finalizeMs;
            TotalMs = totalMs;
        }

        internal ushort ContainerVersion { get; }
        internal SaveCompression Compression { get; }
        internal EncodingMetrics EncodingMetrics { get; }
        internal string FinalPath { get; }
        internal long Bytes { get; }
        internal int ObjectCount { get; }
        internal double EncodeWriteMs { get; }
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
