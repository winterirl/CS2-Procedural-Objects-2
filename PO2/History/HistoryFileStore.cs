using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace PO2.History
{
    internal sealed class HistoryFileStore : IDisposable
    {
        private readonly string m_Root;
        private string m_Directory;
        private FileStream m_Lease;
        private int m_Files;
        private bool m_Disposed;
        private readonly object m_Gate = new object();

        internal HistoryFileStore(string root = null)
        {
            m_Root = root ?? Path.Combine(Path.GetTempPath(), "PO2-ActionHistory");
        }

        private void EnsureDirectory()
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(HistoryFileStore));
            if (m_Directory != null) return;
            Directory.CreateDirectory(m_Root);
            foreach (string path in Directory.GetDirectories(m_Root))
            {
                if (!Guid.TryParseExact(Path.GetFileName(path), "N", out _) || Directory.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddDays(-1)) continue;
                try
                {
                    // A live process keeps this lease open. Only abandoned
                    // session folders can be opened exclusively and removed.
                    using (File.Open(Path.Combine(path, "owner"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                    }

                    Directory.Delete(path, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            string directory = Path.Combine(m_Root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            m_Lease = File.Open(Path.Combine(directory, "owner"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            m_Directory = directory;
        }

        internal string Write(byte[] bytes, byte[] digest)
        {
            lock (m_Gate)
            {
                EnsureDirectory();
                string path = Path.Combine(m_Directory, Guid.NewGuid().ToString("N") + ".payload");
                string temporary = path + ".tmp";
                try
                {
                    using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        using (var writer = new BinaryWriter(file, System.Text.Encoding.UTF8, true))
                        {
                            writer.Write(1);
                            writer.Write(bytes.Length);
                            writer.Write(digest);
                            writer.Write(bytes);
                        }

                        file.Flush(true);
                    }

                    File.Move(temporary, path);
                    m_Files++;
                    return path;
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
        }

        internal byte[] Read(string path, int length, byte[] expected, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(file))
            {
                if (file.Length != 40L + length || reader.ReadInt32() != 1 || reader.ReadInt32() != length || !TopologyState.Equal(reader.ReadBytes(32), expected)) throw new InvalidDataException("History file header failed verification.");
                var bytes = new byte[length];
                for (int at = 0; at < length;)
                {
                    token.ThrowIfCancellationRequested();
                    int read = file.Read(bytes, at, Math.Min(65536, length - at));
                    if (read == 0) throw new EndOfStreamException();
                    at += read;
                }

                using (var sha = SHA256.Create())
                    if (!TopologyState.Equal(sha.ComputeHash(bytes), expected))
                        throw new InvalidDataException("History file checksum failed.");
                token.ThrowIfCancellationRequested();
                return bytes;
            }
        }

        internal void Delete(string path)
        {
            lock (m_Gate)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                finally
                {
                    m_Files--;
                    TryCleanup();
                }
            }
        }

        private void TryCleanup()
        {
            if (!m_Disposed || m_Files != 0 || m_Directory == null) return;
            m_Lease?.Dispose();
            m_Lease = null;
            try
            {
                Directory.Delete(m_Directory, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            m_Directory = null;
        }

        public void Dispose()
        {
            lock (m_Gate)
            {
                m_Disposed = true;
                TryCleanup();
            }
        }
    }
}