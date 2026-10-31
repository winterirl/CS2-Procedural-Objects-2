using System;
using System.Collections.Generic;

namespace PO2.Persistence
{
    internal sealed class SaveLineage
    {
        internal Guid Id;
        internal string Name;
        internal int Count;
    }

    internal sealed class SaveManagerPage
    {
        internal const int Size = 50;
        internal SaveRecord[] Saves = Array.Empty<SaveRecord>();
        internal SaveLineage[] Lineages = Array.Empty<SaveLineage>();
        internal int Index;
        internal int MatchCount;
        internal int TotalCount;
        internal long PageBytes;
        internal long TotalBytes;
        internal string Session = string.Empty;
        internal string Status = "all";
        internal bool Oldest;

        internal static SaveManagerPage Create(SaveDiscoveryResult discovery, string session, string status, bool oldest, int page, Action checkpoint)
        {
            var result = new SaveManagerPage { Session = session ?? "", Status = status ?? "all", Oldest = oldest };
            Guid filter = Guid.Empty;
            bool filterSession = result.Session.Length > 0;
            if (filterSession && !Guid.TryParse(result.Session, out filter)) filter = Guid.Empty;
            var groups = new Dictionary<Guid, SaveLineage>();
            var matches = new List<SaveRecord>();
            foreach (SaveRecord save in discovery.Saves)
            {
                checkpoint?.Invoke();
                result.TotalCount++;
                result.TotalBytes += save.FileBytes;
                if (!groups.TryGetValue(save.SessionGuid, out SaveLineage group))
                {
                    group = new SaveLineage { Id = save.SessionGuid, Name = save.DisplayMetadata.CityName ?? "" };
                    groups.Add(save.SessionGuid, group);
                }
                group.Count++;
                if ((!filterSession || filter == save.SessionGuid) && (result.Status == "all" || save.GameSaveStatus.ToString() == result.Status)) matches.Add(save);
            }
            var lineages = new List<SaveLineage>(groups.Values);
            lineages.Sort((a, b) => a.Id.CompareTo(b.Id));
            result.Lineages = lineages.ToArray();
            result.MatchCount = matches.Count;
            result.Index = Math.Max(0, Math.Min(page, Math.Max(0, (matches.Count - 1) / Size)));
            int first = result.Index * Size;
            int count = Math.Min(Size, matches.Count - first);
            result.Saves = new SaveRecord[count];
            for (int i = 0; i < count; i++)
            {
                SaveRecord save = matches[oldest ? matches.Count - first - i - 1 : first + i];
                result.Saves[i] = save;
                result.PageBytes += save.FileBytes;
            }
            return result;
        }
    }
}
