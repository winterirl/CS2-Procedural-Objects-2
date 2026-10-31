using System;
using System.Collections.Generic;
using System.IO;

namespace PO2.Persistence
{
    internal sealed partial class SnapshotFileStore
    {
        internal void CleanupStaleTemps(DateTime startedUtc, ISet<string> protectedKeys, MaintenanceWork work, SnapshotAccessGate gate, long revision)
        {
            if (!Directory.Exists(m_RootPath)) return;
            foreach (string path in Directory.EnumerateFiles(m_RootPath, "*.po2.tmp", SearchOption.TopDirectoryOnly))
            {
                work.Checkpoint();
                string key = Path.GetFileName(path);
                key = key.Substring(0, key.Length - ".po2.tmp".Length);
                if (!IsValidExactFileKey(key) || protectedKeys.Contains(key)) continue;
                using (IDisposable lease = gate.TryEnter(revision))
                {
                    if (lease == null) return;
                    try
                    {
                        var info = new FileInfo(path);
                        if (!info.Exists || info.LastWriteTimeUtc >= startedUtc || (info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        File.Delete(path);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }
}
