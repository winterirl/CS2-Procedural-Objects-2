using System;
using System.Collections.Generic;
using System.IO;
using PO2.Localization;
using PO2.Persistence.Format;

namespace PO2.Persistence
{
    internal sealed partial class SnapshotFileStore
    {
        internal SaveDiscoveryResult DiscoverSaves(GameSaveCatalogSnapshot catalog = null, Action checkpoint = null, Action checkedFile = null)
        {
            catalog = catalog ?? GameSaveCatalogSnapshot.s_Unavailable;
            int rejectedCount = 0;
            List<SaveRecord> saves = new List<SaveRecord>();
            List<SaveDiscoveryError> rejected = new List<SaveDiscoveryError>();

            void AddIssue(SaveDiscoveryError issue)
            {
                rejectedCount++;
                if (rejected.Count < 32) rejected.Add(issue);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(m_RootPath, "*.po2", SearchOption.TopDirectoryOnly);
            }
            catch (DirectoryNotFoundException) when (!File.Exists(m_RootPath))
            {
                return new SaveDiscoveryResult(saves, rejected, true, rejectedCount);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                rejected.Add(new SaveDiscoveryError(string.Empty, m_RootPath, LocalizedText.FromException(exception)));
                return new SaveDiscoveryResult(saves, rejected, false);
            }

            // Discovery is cheap ~ validate container/header data and session identity,
            // but leave every object block untouched for now.
            try
            {
                foreach (string path in files)
                {
                    checkpoint?.Invoke();
                    checkedFile?.Invoke();
                    string fileKey = Path.GetFileNameWithoutExtension(path);

                    if (!IsValidExactFileKey(fileKey))
                    {
                        AddIssue(new SaveDiscoveryError(fileKey, path, LocalizedText.For("PO2.SaveManager.Scan.Error.InvalidFilename")));
                        continue;
                    }

                    try
                    {
                        FileInfo info = new FileInfo(path);
                        SnapshotHeaderInfo header;
                        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.SequentialScan))
                        {
                            if (!SnapshotReader.TryReadHeader(stream, out header, out string error))
                            {
                                AddIssue(new SaveDiscoveryError(fileKey, path, LocalizedText.For("PO2.SaveManager.Scan.Error.InvalidHeader", ("details", error))));
                                continue;
                            }
                        }

                        if (!string.Equals(fileKey, SaveIdentity.ToFileKey(header.SaveGuid), StringComparison.OrdinalIgnoreCase))
                        {
                            AddIssue(new SaveDiscoveryError(fileKey, path, LocalizedText.For("PO2.SaveManager.Scan.Error.IdentityMismatch")));
                            continue;
                        }

                        saves.Add(new SaveRecord(fileKey, path, header, info.Length, info.LastWriteTimeUtc, catalog));
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException || exception is EndOfStreamException || exception is OverflowException || exception is ArgumentException)
                    {
                        AddIssue(new SaveDiscoveryError(fileKey, path, LocalizedText.FromException(exception)));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                AddIssue(new SaveDiscoveryError("", m_RootPath, LocalizedText.FromException(exception)));
                return new SaveDiscoveryResult(saves, rejected, false, rejectedCount);
            }

            saves.Sort((left, right) =>
            {
                // Most recent is the useful default presentation.
                // File key breaks timestamp ties so UI order stays stable.
                int byTime = right.DisplayTimeUtc.CompareTo(left.DisplayTimeUtc);
                return byTime != 0 ? byTime : string.CompareOrdinal(left.FileKey, right.FileKey);
            });

            return new SaveDiscoveryResult(saves, rejected, true, rejectedCount);
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

    }
}
