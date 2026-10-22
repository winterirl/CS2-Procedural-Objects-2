using PO2.Localization;

namespace PO2.Persistence
{
    internal static class RecoveryMessages
    {
        internal static LocalizedText NoContext => LocalizedText.For("PO2.Recovery.Error.ContextMissing");
        internal static LocalizedText Unavailable => LocalizedText.For("PO2.Recovery.Error.CandidateUnavailable");
        internal static LocalizedText Stale => LocalizedText.For("PO2.Recovery.Error.ContextChanged");
        internal static LocalizedText WrongSession => LocalizedText.For("PO2.Recovery.Error.SessionMismatch");
        internal static LocalizedText MissingFile => LocalizedText.For("PO2.Recovery.Error.SnapshotMissing");
        internal static LocalizedText Invalid => LocalizedText.For("PO2.Recovery.Error.ValidationFailed");
        internal static LocalizedText ReconstructionFailed => LocalizedText.For("PO2.Recovery.Error.ReconstructionFailed");

        internal static LocalizedText Discovered(int count) => count > 0 ? LocalizedText.For("PO2.Recovery.Candidates.FoundNotice", ("count", count)) : LocalizedText.For("PO2.Recovery.Candidates.EmptyNotice");

        internal static LocalizedText Restored(string fileKey, int decodedCount, int skippedCount, int restoredCount)
        {
            // Material fallback alone does not mean a PO was omitted.
            if (skippedCount > 0 || restoredCount < decodedCount)
            {
                return LocalizedText.For("PO2.Recovery.Result.Partial");
            }

            string shortKey = fileKey.Length > 8 ? fileKey.Substring(0, 8) : fileKey;
            return LocalizedText.For("PO2.Recovery.Result.Restored", ("count", restoredCount), ("snapshotId", shortKey));
        }
    }
}