using System;
using System.Collections.Generic;
using PO2.History.Actions;
using PO2.Localization;

namespace PO2.History
{
    // A short-lived capture at a user command boundary. Completion transfers
    // changed actions to the timeline. An exception/cancel restores the capture.
    internal sealed class HistoryEdit : IDisposable
    {
        private readonly HistoryContext m_Context;
        private readonly LocalizedText m_Description;
        private readonly List<HistoryCaptureAction> m_Actions;
        private readonly Action<string> m_Log;
        private HistoryTransaction m_Transaction;
        private readonly bool m_RestoreFocus;
        private readonly HistoryFocusState m_BeforeFocus;
        internal bool RestoreSucceeded { get; private set; } = true;

        internal HistoryEdit(HistoryContext context, HistoryTransaction transaction, LocalizedText description, List<HistoryCaptureAction> actions, Action<string> log, bool restoreFocus = false)
        {
            m_Context = context;
            m_Transaction = transaction;
            m_Description = description;
            m_Actions = actions;
            m_Log = log;
            m_RestoreFocus = restoreFocus;
            if (restoreFocus) m_BeforeFocus = context.CaptureFocus();
        }

        internal bool Complete()
        {
            if (m_Transaction == null) return false;
            if (!m_Transaction.IsValid)
            {
                Release();
                return false;
            }

            try
            {
                foreach (var action in m_Actions) action.Seal(m_Context);
                var changed = new List<IHistoryAction>();
                foreach (var action in m_Actions)
                    if (action.HasChanges)
                        changed.Add(action);
                if (changed.Count == 0)
                {
                    Release();
                    return true;
                }

                IHistoryAction entry = changed.Count == 1 ? changed[0] : new CompoundHistoryAction(m_Description, changed);
                if (m_RestoreFocus) entry = new FocusedHistoryAction(entry, m_BeforeFocus, m_Context.CaptureFocus());
                if (!m_Transaction.TryCommit(entry))
                {
                    throw new InvalidOperationException("The action could not be added to history.");
                }

                // Changed children now belong to the timeline, including those
                // wrapped by a compound. Only no-op captures remain ours.
                foreach (var action in m_Actions)
                    if (!action.HasChanges)
                        action.Dispose();
                m_Actions.Clear();
                m_Transaction.Dispose();
                m_Transaction = null;
                return true;
            }
            catch (Exception exception)
            {
                Log("[PO History] " + m_Description + " failed: " + exception.Message);
                Dispose();
                return false;
            }
        }

        public void Dispose()
        {
            if (m_Transaction == null) return;
            // A load/clear invalidates the transaction. Never restore an old
            // capture into whatever model was loaded in its place.
            if (m_Transaction.IsValid)
            {
                bool failed = false;
                for (int i = m_Actions.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        m_Actions[i].RestoreCapture(m_Context);
                    }
                    catch (Exception exception)
                    {
                        failed = true;
                        RestoreSucceeded = false;
                        Log("[PO History] Could not restore cancelled edit: " + exception.Message);
                    }
                }

                if (failed) m_Transaction.ClearAfterFailedRestore();
                else if (m_RestoreFocus) m_Context.RestoreFocus(m_BeforeFocus);
            }

            Release();
        }

        private void Release()
        {
            foreach (var action in m_Actions) action.Dispose();
            m_Actions.Clear();
            m_Transaction?.Dispose();
            m_Transaction = null;
        }

        private void Log(string message)
        {
            try
            {
                m_Log?.Invoke(message);
            }
            catch
            {
            }
        }
    }
}