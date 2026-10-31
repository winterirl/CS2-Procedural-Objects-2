using System;

namespace PO2.Persistence
{
    internal sealed partial class SnapshotFileStore
    {
        private readonly string m_RootPath;
        private readonly SnapshotFileIO m_IO;

        internal SnapshotFileStore(string rootPath, SnapshotFileIO io = null)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException("PO2 snapshot root path cannot be blank.", nameof(rootPath));
            }

            m_RootPath = rootPath;
            m_IO = io ?? new SnapshotFileIO();
        }

        internal string RootPath => m_RootPath;
    }
}
