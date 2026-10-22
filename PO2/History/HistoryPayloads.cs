using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PO2.History
{
    internal interface IHistoryPayloadOwner
    {
        IEnumerable<HistoryResidentPayload> Payloads { get; }
        void CoolManagedPayload();
    }

    internal interface IHistoryBackgroundWork
    {
        void PollBackgroundWork(Action<string> log);
    }

    internal interface IHistoryOwnerCleanup
    {
        void CancelPreparation();
    }

    internal static class HistoryPayloads
    {
        internal static IEnumerable<HistoryResidentPayload> Get(IHistoryAction action) =>
            (action as IHistoryPayloadOwner)?.Payloads ?? Array.Empty<HistoryResidentPayload>();

        internal static async Task<HistoryResult> Prepare(IHistoryAction action, HistoryDirection direction, CancellationToken token)
        {
            var result = await action.Prepare(direction, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!result.Success) return result;
            foreach (var payload in Get(action))
            {
                token.ThrowIfCancellationRequested();
                await payload.Prepare(token).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
            return HistoryResult.Ok;
        }
    }
}