using System.Threading;
using System.Threading.Tasks;
using PO2.History;
using PO2.Localization;

namespace PO2.History.Actions
{
    internal sealed class FocusedHistoryAction : IHistoryAction, IHistoryPayloadOwner, IHistoryBackgroundWork, IHistoryOwnerCleanup
    {
        private readonly IHistoryAction m_Action;
        private readonly HistoryFocusState m_Before, m_After;

        internal FocusedHistoryAction(IHistoryAction action, HistoryFocusState before, HistoryFocusState after)
        {
            m_Action = action;
            m_Before = before;
            m_After = after;
        }

        public System.Collections.Generic.IEnumerable<HistoryResidentPayload> Payloads
        {
            get { return HistoryPayloads.Get(m_Action); }
        }

        public void CoolManagedPayload()
        {
            (m_Action as IHistoryPayloadOwner)?.CoolManagedPayload();
        }

        public void PollBackgroundWork(System.Action<string> log)
        {
            (m_Action as IHistoryBackgroundWork)?.PollBackgroundWork(log);
        }

        public void CancelPreparation() => (m_Action as IHistoryOwnerCleanup)?.CancelPreparation();
        public LocalizedText Description => m_Action.Description;
        public bool HasChanges => m_Action.HasChanges;
        public HistoryFootprint Footprint => m_Action.Footprint.Add(new HistoryFootprint(192, 0, 0, 192));
        public Task<HistoryResult> Prepare(HistoryDirection direction, CancellationToken token) => m_Action.Prepare(direction, token);

        public HistoryResult Replay(HistoryDirection direction, HistoryContext context)
        {
            var startingFocus = context.CaptureFocus();
            HistoryResult result;
            try
            {
                result = m_Action.Replay(direction, context);
            }
            catch
            {
                context.RestoreFocus(startingFocus);
                throw;
            }

            context.RestoreFocus(result.Success ? (direction == HistoryDirection.Undo ? m_Before : m_After) : startingFocus);
            return result;
        }

        public void Dispose() => m_Action.Dispose();
    }
}