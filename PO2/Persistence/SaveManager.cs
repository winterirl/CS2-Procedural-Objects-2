using System;
using System.Collections.Generic;
using System.Globalization;
using PO2.Localization;

namespace PO2.Persistence
{
    // Plans contain only files already shown in the manager. Confirmation never expands a plan.
    internal sealed class SaveDeletionPlan
    {
        internal string Token { get; } = Guid.NewGuid().ToString("N");
        internal string Kind { get; }
        internal Guid? Session { get; }
        internal DateTime? BeforeUtc { get; }
        internal IReadOnlyList<SaveRecord> Saves { get; }

        internal SaveDeletionPlan(string kind, Guid? session, DateTime? beforeUtc, List<SaveRecord> saves)
        {
            Kind = kind;
            Session = session;
            BeforeUtc = beforeUtc;
            Saves = saves.AsReadOnly();
        }

        internal bool StillMatches(SaveRecord save) =>
            (!Session.HasValue || save.SessionGuid == Session.Value) && (Kind != "before" || save.DisplayTimeUtc < BeforeUtc.Value) && (Kind != "orphaned" || save.GameSaveStatus == GameSaveStatus.Orphaned);
    }

    internal sealed class SaveDeletionResult
    {
        internal int Deleted;
        internal long DeletedBytes;
        internal readonly List<LocalizedText> Failures = new List<LocalizedText>();
    }

    internal sealed class SaveManager
    {
        private readonly SnapshotFileStore m_Store;
        internal SaveDiscoveryResult Discovery { get; private set; }
        internal bool CatalogComplete { get; private set; }
        internal SaveDeletionPlan Pending { get; private set; }
        internal int Revision { get; private set; }

        internal SaveManager(SnapshotFileStore store)
        {
            m_Store = store;
        }

        internal void Refresh(GameSaveCatalogSnapshot catalog)
        {
            Cancel();
            Discovery = m_Store.DiscoverSaves(catalog);
            CatalogComplete = catalog != null && catalog.IsComplete;
            Revision++;
        }

        internal void Cancel()
        {
            Pending = null;
        }

        internal static bool TryLocalCutoff(string date, out DateTime cutoffUtc)
        {
            cutoffUtc = default;
            if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local)) return false;
            if (TimeZoneInfo.Local.IsInvalidTime(local)) return false;
            cutoffUtc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
            return true;
        }

        // value is a comma-separated set of server-issued keys, or a local calendar date.
        internal LocalizedText Prepare(string kind, string session, string value, ISet<string> protectedKeys)
        {
            Cancel();
            if (Discovery == null || !Discovery.ScanSucceeded) return LocalizedText.For("PO2.SaveManager.Validation.RefreshRequired");
            Guid? lineage = null;
            if (!string.IsNullOrEmpty(session))
            {
                if (!Guid.TryParseExact(session, "D", out Guid parsed)) return LocalizedText.For("PO2.SaveManager.Validation.LineageInvalid");
                lineage = parsed;
            }

            DateTime? cutoff = null;
            HashSet<string> keys = null;
            switch (kind)
            {
                case "selected":
                    keys = new HashSet<string>((value ?? "").Split(','), StringComparer.OrdinalIgnoreCase);
                    break;
                case "lineage":
                    if (!lineage.HasValue || lineage.Value == Guid.Empty) return LocalizedText.For("PO2.SaveManager.Validation.LineageRequired");
                    break;
                case "before":
                    if (!TryLocalCutoff(value, out DateTime utc)) return LocalizedText.For("PO2.SaveManager.Validation.CutoffDate");
                    cutoff = utc;
                    break;
                case "orphaned":
                    if (!CatalogComplete) return LocalizedText.For("PO2.SaveManager.Validation.OrphanCatalogIncomplete");
                    break;
                default:
                    return LocalizedText.For("PO2.SaveManager.Validation.RequestInvalid");
            }

            var targets = new List<SaveRecord>();
            int protectedCount = 0;
            var criteria = new SaveDeletionPlan(kind, lineage, cutoff, targets);
            foreach (SaveRecord save in Discovery.Saves)
            {
                if (!criteria.StillMatches(save) || (keys != null && !keys.Contains(save.FileKey))) continue;
                if (protectedKeys.Contains(save.FileKey))
                {
                    protectedCount++;
                    continue;
                }

                targets.Add(save);
            }

            if (targets.Count == 0) return protectedCount > 0 ? LocalizedText.For("PO2.SaveManager.DeleteResult.OnlyProtected") : LocalizedText.For("PO2.SaveManager.DeleteResult.NoMatches");
            Pending = criteria;
            return LocalizedText.Empty;
        }

        internal SaveDeletionResult Confirm(string token, GameSaveCatalogSnapshot catalog, ISet<string> protectedKeys)
        {
            var result = new SaveDeletionResult();
            SaveDeletionPlan plan = Pending;
            // A stale token must not dismiss a newer confirmation.
            if (plan == null || !string.Equals(token, plan.Token, StringComparison.Ordinal))
            {
                result.Failures.Add(LocalizedText.For("PO2.SaveManager.DeleteError.ConfirmationExpired"));
                return result;
            }

            Cancel();
            SaveDiscoveryResult current = m_Store.DiscoverSaves(catalog);
            if (!current.ScanSucceeded)
            {
                result.Failures.Add(LocalizedText.For("PO2.SaveManager.DeleteError.ScanFailed"));
                return result;
            }

            var byKey = new Dictionary<string, SaveRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (SaveRecord save in current.Saves) byKey[save.FileKey] = save;
            foreach (SaveRecord target in plan.Saves)
            {
                LocalizedText reason;
                if (protectedKeys.Contains(target.FileKey)) reason = LocalizedText.For("PO2.SaveManager.DeleteError.Protected");
                else if (!byKey.TryGetValue(target.FileKey, out SaveRecord fresh)) reason = LocalizedText.For("PO2.SaveManager.DeleteError.FileUnavailable");
                else if (!plan.StillMatches(fresh)) reason = LocalizedText.For("PO2.SaveManager.DeleteError.CriteriaChanged");
                else if (m_Store.TryDeleteUnchanged(target, out reason))
                {
                    result.Deleted++;
                    result.DeletedBytes += target.FileBytes;
                    continue;
                }

                result.Failures.Add(LocalizedText.For("PO2.SaveManager.DeleteError.FileDetail", ("fileKey", target.FileKey), ("reason", reason)));
            }

            return result;
        }
    }
}