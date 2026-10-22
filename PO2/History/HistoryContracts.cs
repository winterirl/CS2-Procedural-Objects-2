using System;
using System.Threading;
using System.Threading.Tasks;
using PO2.Localization;

namespace PO2.History
{
    internal static class HistoryLimits
    {
        internal const int kMinimumCapacity = 1;
        internal const int kDefaultCapacity = 20;
        internal const int kMaximumCapacity = 40;
        internal static int ClampCapacity(int value) => Math.Max(kMinimumCapacity, Math.Min(kMaximumCapacity, value));
    }

    internal enum HistoryDirection
    {
        Undo,
        Redo
    }

    internal readonly struct HistoryFootprint
    {
        internal readonly long ManagedBytes;
        internal readonly long EncodedBytes;
        internal readonly long ColdBytes;
        internal readonly long LogicalBytes;

        internal HistoryFootprint(long managed, long encoded, long cold, long logical)
        {
            if (managed < 0 || encoded < 0 || cold < 0 || logical < 0) throw new ArgumentOutOfRangeException();
            ManagedBytes = managed;
            EncodedBytes = encoded;
            ColdBytes = cold;
            LogicalBytes = logical;
        }

        internal HistoryFootprint Add(HistoryFootprint other) => new HistoryFootprint(checked(ManagedBytes + other.ManagedBytes), checked(EncodedBytes + other.EncodedBytes), checked(ColdBytes + other.ColdBytes), checked(LogicalBytes + other.LogicalBytes));
    }

    internal readonly struct HistoryResult
    {
        internal readonly bool Success;
        internal readonly bool InvalidateHistory;
        internal readonly string Reason;

        internal HistoryResult(bool success, string reason = "", bool invalidateHistory = false)
        {
            Success = success;
            Reason = reason ?? string.Empty;
            InvalidateHistory = invalidateHistory;
        }

        internal HistoryResult(bool success, LocalizedText reason, bool invalidateHistory = false) : this(success, reason.ToString(), invalidateHistory)
        {
        }

        internal static HistoryResult Ok => new HistoryResult(true);
    }

    internal interface IHistoryAction : IDisposable
    {
        LocalizedText Description { get; }
        bool HasChanges { get; }

        HistoryFootprint Footprint { get; }

        // Preparation may decode owned payloads, but must never access the live model.
        Task<HistoryResult> Prepare(HistoryDirection direction, CancellationToken cancellation);

        // A failed replay must leave the model and action payload unchanged.
        // Unrecoverable rollback failure must return InvalidateHistory=true.
        // A successful child must be immediately reversible without more IO,
        // so a compound can roll it back if a later child fails.
        HistoryResult Replay(HistoryDirection direction, HistoryContext context);
    }
}