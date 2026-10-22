using System;
using System.IO;
using PO2.Localization;
using PO2.Persistence.Format;

namespace PO2.Persistence
{
    internal sealed partial class SnapshotFileStore
    {
        internal bool TryDeleteUnchanged(SaveRecord expected, out LocalizedText reason)
        {
            reason = LocalizedText.Empty;
            if (expected == null || !IsValidExactFileKey(expected.FileKey))
            {
                reason = LocalizedText.For("PO2.SaveManager.DeleteError.IdentityInvalid");
                return false;
            }

            try
            {
                // Never accept a path from a trigger or walk below the snapshot folder.
                string path = Path.Combine(m_RootPath, expected.FileKey + ".po2");
                if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(expected.Path), StringComparison.Ordinal))
                {
                    reason = LocalizedText.For("PO2.SaveManager.DeleteError.PathChanged");
                    return false;
                }

                FileInfo info = new FileInfo(path);
                if (!info.Exists)
                {
                    reason = LocalizedText.For("PO2.SaveManager.DeleteError.FileMissing");
                    return false;
                }

                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    reason = LocalizedText.For("PO2.SaveManager.DeleteError.LinkedFile");
                    return false;
                }

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length != expected.FileBytes || info.LastWriteTimeUtc != expected.LastWriteTimeUtc || !SnapshotReader.TryReadHeader(stream, out SnapshotHeaderInfo header, out _) || header.SaveGuid != expected.SnapshotSaveGuid || header.SessionGuid != expected.SessionGuid || header.ObjectCount != expected.ObjectCount || header.DirectoryChecksum != expected.Header.DirectoryChecksum || header.ContainerVersion != expected.Header.ContainerVersion || !Equals(header.Metadata, expected.Header.Metadata))
                    {
                        reason = LocalizedText.For("PO2.SaveManager.DeleteError.FileChanged");
                        return false;
                    }
                }

                File.Delete(path);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is NotSupportedException)
            {
                reason = LocalizedText.FromException(exception);
                return false;
            }
        }
    }
}