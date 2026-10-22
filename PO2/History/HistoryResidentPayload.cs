using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PO2.History
{
    // Keeps an encoded history buffer in memory, with disk storage for larger payloads.
    internal sealed class HistoryResidentPayload : IDisposable
    {
        private byte[] m_Bytes;
        private readonly long m_LogicalBytes;
        private readonly int m_Length;
        private readonly byte[] m_Digest;
        private HistoryFileStore m_Store;
        private string m_Path;
        private bool m_Disposed;

        internal HistoryResidentPayload(byte[] ownedBytes, long logicalBytes)
        {
            if (ownedBytes == null) throw new ArgumentNullException(nameof(ownedBytes));
            if (logicalBytes < 0) throw new ArgumentOutOfRangeException(nameof(logicalBytes));
            m_Bytes = ownedBytes;
            m_Length = ownedBytes.Length;
            m_LogicalBytes = logicalBytes;
            using (var sha = SHA256.Create()) m_Digest = sha.ComputeHash(ownedBytes);
        }

        internal long LongLength => m_Length;
        internal int Length => m_Length;
        internal bool IsCold => !m_Disposed && m_Bytes == null;
        internal HistoryFootprint Footprint => m_Disposed ? default : new HistoryFootprint(0, m_Bytes == null ? 0 : m_Length, m_Bytes == null ? m_Length : 0, m_LogicalBytes);

        internal byte[] Bytes
        {
            get
            {
                if (m_Disposed) throw new ObjectDisposedException(nameof(HistoryResidentPayload));
                if (m_Bytes == null) throw new InvalidOperationException("History payload must be prepared before replay.");
                return m_Bytes;
            }
        }

        internal void Spill(HistoryFileStore store)
        {
            if (m_Disposed || m_Bytes == null) return;
            if (m_Path == null)
            {
                string path = store.Write(m_Bytes, m_Digest);
                m_Store = store;
                m_Path = path;
            }

            m_Bytes = null;
        }

        internal Task Prepare(CancellationToken token)
        {
            if (!IsCold) return Task.CompletedTask;
            // The history manager owns the payload until this task settles,
            // including when load/clear cancels a pending replay.
            return Task.Run(() =>
            {
                byte[] bytes = m_Store.Read(m_Path, m_Length, m_Digest, token);
                token.ThrowIfCancellationRequested();
                m_Bytes = bytes;
            }, token);
        }

        internal Stream OpenRead() => new MemoryStream(Bytes, false);

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_Bytes = null;
            string path = m_Path;
            m_Path = null;
            if (path != null) m_Store.Delete(path);
            m_Store = null;
        }
    }
}