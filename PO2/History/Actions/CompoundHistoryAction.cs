using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PO2.History;
using PO2.Localization;

namespace PO2.History.Actions
{
    internal sealed class CompoundHistoryAction : IHistoryAction, IHistoryPayloadOwner, IHistoryBackgroundWork, IHistoryOwnerCleanup
    {
        private readonly IHistoryAction[] m_Children;
        private readonly object m_PreparationGate = new object();
        private bool m_Disposed;

        public System.Collections.Generic.IEnumerable<HistoryResidentPayload> Payloads
        {
            get
            {
                foreach (var child in m_Children)
                foreach (var payload in HistoryPayloads.Get(child))
                    yield return payload;
            }
        }

        public void CoolManagedPayload()
        {
            foreach (var child in m_Children) (child as IHistoryPayloadOwner)?.CoolManagedPayload();
        }

        public void PollBackgroundWork(Action<string> log)
        {
            foreach (var child in m_Children)
            {
                (child as IHistoryBackgroundWork)?.PollBackgroundWork(log);
            }
        }

        public void CancelPreparation()
        {
            lock (m_PreparationGate)
            {
                foreach (var child in m_Children) (child as IHistoryOwnerCleanup)?.CancelPreparation();
            }
        }

        public LocalizedText Description { get; }
        public bool HasChanges => m_Children.Length > 0;

        public HistoryFootprint Footprint
        {
            get
            {
                var bytes = new HistoryFootprint();
                foreach (var child in m_Children) bytes = bytes.Add(child.Footprint);
                return bytes;
            }
        }

        internal CompoundHistoryAction(LocalizedText description, IReadOnlyList<IHistoryAction> children)
        {
            if (description.IsEmpty || children == null) throw new ArgumentException();
            Description = description;
            m_Children = new IHistoryAction[children.Count];
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] == null || !children[i].HasChanges) throw new ArgumentException("Empty compound child.");
                for (int j = 0; j < i; j++)
                {
                    if (children[j] == children[i]) throw new ArgumentException("Repeated compound child.");
                }

                m_Children[i] = children[i];
            }
        }

        public async Task<HistoryResult> Prepare(HistoryDirection direction, CancellationToken cancellation)
        {
            foreach (var child in m_Children)
            {
                Task<HistoryResult> preparation;
                // Cancellation cannot miss a child that is just starting.
                lock (m_PreparationGate)
                {
                    cancellation.ThrowIfCancellationRequested();
                    preparation = child.Prepare(direction, cancellation);
                }

                var result = await preparation.ConfigureAwait(false);
                if (!result.Success) return result;
            }

            return HistoryResult.Ok;
        }

        public HistoryResult Replay(HistoryDirection direction, HistoryContext context)
        {
            int start = direction == HistoryDirection.Undo ? m_Children.Length - 1 : 0;
            int step = direction == HistoryDirection.Undo ? -1 : 1;
            int completed = 0;
            HistoryResult result = HistoryResult.Ok;
            for (; completed < m_Children.Length; completed++)
            {
                try
                {
                    result = m_Children[start + completed * step].Replay(direction, context);
                }
                catch (Exception exception)
                {
                    result = new HistoryResult(false, exception.Message, true);
                }

                if (!result.Success) break;
            }

            if (result.Success) return result;
            var reverse = direction == HistoryDirection.Undo ? HistoryDirection.Redo : HistoryDirection.Undo;
            for (int i = completed - 1; i >= 0; i--)
            {
                try
                {
                    var rollback = m_Children[start + i * step].Replay(reverse, context);
                    if (!rollback.Success) result = new HistoryResult(false, result.Reason + "; rollback: " + rollback.Reason, true);
                }
                catch (Exception exception)
                {
                    result = new HistoryResult(false, result.Reason + "; rollback: " + exception.Message, true);
                }
            }

            return result;
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            Exception failure = null;
            foreach (var child in m_Children)
            {
                try
                {
                    child.Dispose();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }

            if (failure != null) throw failure;
        }
    }
}