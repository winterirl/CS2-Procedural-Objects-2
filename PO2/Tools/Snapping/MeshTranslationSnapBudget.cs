using System;
using System.Threading;

namespace PO2.Tools.Snapping
{
    // The same budget covers cached, active and cancelled worker generations.
    internal sealed class MeshTranslationSnapBudget
    {
        private long m_Owned, m_Peak, m_Released;

        internal long Bytes
        {
            get
            {
                lock (this) return m_Owned;
            }
        }

        internal long Peak
        {
            get
            {
                lock (this) return m_Peak;
            }
        }

        internal long Released
        {
            get
            {
                lock (this) return m_Released;
            }
        }

        internal Reservation TryReserve(long bytes)
        {
            lock (this)
            {
                if (bytes < 0 || bytes > MeshTranslationSnapCapture.kMemoryLimit - m_Owned) return null;
                m_Owned += bytes;
                m_Peak = Math.Max(m_Peak, m_Owned);
                return new Reservation(this, bytes);
            }
        }

        internal sealed class Reservation : IDisposable
        {
            private MeshTranslationSnapBudget m_Owner;
            private long m_Bytes;
            private int m_References = 1;

            internal Reservation(MeshTranslationSnapBudget owner, long bytes)
            {
                m_Owner = owner;
                m_Bytes = bytes;
            }

            internal bool TryGrow(long bytes)
            {
                var owner = m_Owner;
                if (owner == null) throw new ObjectDisposedException(nameof(Reservation));
                lock (owner)
                {
                    long extra = bytes - m_Bytes;
                    if (extra <= 0) return true;
                    if (extra > MeshTranslationSnapCapture.kMemoryLimit - owner.m_Owned) return false;
                    owner.m_Owned += extra;
                    m_Bytes = bytes;
                    owner.m_Peak = Math.Max(owner.m_Peak, owner.m_Owned);
                    return true;
                }
            }

            internal void Retain()
            {
                int value;
                do
                {
                    value = Volatile.Read(ref m_References);
                    if (value <= 0) throw new ObjectDisposedException(nameof(Reservation));
                } while (Interlocked.CompareExchange(ref m_References, value + 1, value) != value);
            }

            public void Dispose()
            {
                if (Interlocked.Decrement(ref m_References) != 0) return;
                var owner = m_Owner;
                m_Owner = null;
                lock (owner)
                {
                    owner.m_Owned -= m_Bytes;
                    owner.m_Released += m_Bytes;
                }
            }
        }
    }
}