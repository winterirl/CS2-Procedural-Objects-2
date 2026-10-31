using System;
using System.Collections.Generic;
using PO2.Persistence.Format;
using PO2.Persistence.Model;

namespace PO2.Persistence
{
    internal static class AutosaveRetentionPolicy
    {
        internal static List<SaveRecord> Select(SaveDiscoveryResult discovery, bool catalogComplete, int keep,
            ISet<string> protectedKeys, Guid protectedSession, Action checkpoint = null)
        {
            var selected = new List<SaveRecord>();
            if (!catalogComplete || !discovery.ScanSucceeded) return selected;
            keep = Math.Max(0, keep);
            var groups = new Dictionary<Guid, List<SaveRecord>>();
            foreach (SaveRecord save in discovery.Saves)
            {
                checkpoint?.Invoke();
                if (save.GameSaveStatus != GameSaveStatus.Orphaned || save.SessionGuid == Guid.Empty ||
                    save.SessionGuid == protectedSession || save.Header.MetadataStatus != SnapshotMetadataStatus.Available ||
                    save.Header.Metadata?.SaveKind != SnapshotSaveKind.AutoSave) continue;
                if (!groups.TryGetValue(save.SessionGuid, out List<SaveRecord> group))
                    groups.Add(save.SessionGuid, group = new List<SaveRecord>());
                group.Add(save);
            }
            foreach (List<SaveRecord> group in groups.Values)
            {
                checkpoint?.Invoke();
                group.Sort((a, b) =>
                {
                    int time = b.DisplayTimeUtc.CompareTo(a.DisplayTimeUtc);
                    return time != 0 ? time : string.CompareOrdinal(a.FileKey, b.FileKey);
                });
                for (int i = keep; i < group.Count; i++)
                {
                    checkpoint?.Invoke();
                    if (!protectedKeys.Contains(group[i].FileKey)) selected.Add(group[i]);
                }
            }
            return selected;
        }
    }
}
