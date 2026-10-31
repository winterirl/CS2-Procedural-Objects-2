using System;
using System.Threading;

namespace PO2.Persistence
{
    // The lock covers admission only. No disk work runs under it.
    internal sealed class SnapshotAccessGate
    {
        private readonly object m_Lock = new object();
        private long m_Revision;
        private bool m_Suspended = true;
        private int m_Active;

        internal bool IsActive { get { lock (m_Lock) return m_Active != 0; } }
        internal long Revision { get { lock (m_Lock) return m_Revision; } }

        internal void Suspend()
        {
            lock (m_Lock)
            {
                m_Suspended = true;
                m_Revision++;
            }
        }

        internal void SuspendAndDrain()
        {
            lock (m_Lock)
            {
                m_Suspended = true;
                m_Revision++;
                while (m_Active != 0) Monitor.Wait(m_Lock);
            }
        }

        internal bool TryResume(long revision)
        {
            lock (m_Lock)
            {
                if (revision != m_Revision || m_Active != 0) return false;
                m_Suspended = false;
                return true;
            }
        }

        internal IDisposable TryEnter(long revision)
        {
            lock (m_Lock)
            {
                if (m_Suspended || revision != m_Revision || m_Active != 0) return null;
                m_Active++;
                return new Lease(this);
            }
        }

        private sealed class Lease : IDisposable
        {
            private SnapshotAccessGate m_Gate;
            internal Lease(SnapshotAccessGate gate) { m_Gate = gate; }
            public void Dispose()
            {
                SnapshotAccessGate gate = Interlocked.Exchange(ref m_Gate, null);
                if (gate == null) return;
                lock (gate.m_Lock)
                {
                    gate.m_Active--;
                    Monitor.PulseAll(gate.m_Lock);
                }
            }
        }
    }
}
