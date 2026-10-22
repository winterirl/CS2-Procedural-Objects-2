using System;

namespace PO2.History
{
    // The caller owns its captured action until
    // TryCommit succeeds, and must restore a preview before cancelling an edit.
    internal sealed class HistoryTransaction : IDisposable
    {
        private ActionHistory m_History;
        internal bool IsValid => m_History != null;

        internal HistoryTransaction(ActionHistory history)
        {
            m_History = history;
        }

        internal bool TryCommit(IHistoryAction action)
        {
            if (m_History == null || !m_History.Commit(this, action)) return false;
            m_History = null;
            return true;
        }

        internal void Invalidate()
        {
            m_History = null;
        }

        internal void ClearAfterFailedRestore()
        {
            // The caller could not put the model back at the timeline cursor.
            // Retaining earlier actions would pretend that it did.
            m_History?.Clear();
        }

        public void Dispose()
        {
            if (m_History == null) return;
            m_History.Cancel(this);
            m_History = null;
        }
    }
}