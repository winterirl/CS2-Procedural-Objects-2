using System;
using System.Collections.Generic;
using System.Threading;
using PO2.Persistence.Model;
using PO2.Persistence.PreparedTopology;

namespace PO2.Persistence.Format
{
    // Header-only result used during recovery discovery.
    // No geometry has been decoded just to populate this record.
    internal sealed class SnapshotHeaderInfo
    {
        internal SnapshotHeaderInfo(Guid saveGuid, Guid sessionGuid, int objectCount, long directoryOffset, long directoryLength, ushort containerVersion, SnapshotMetadata metadata, SnapshotMetadataStatus metadataStatus, string metadataWarning, uint directoryChecksum = 0)
        {
            DirectoryChecksum = directoryChecksum;
            SaveGuid = saveGuid;
            SessionGuid = sessionGuid;
            ObjectCount = objectCount;
            DirectoryOffset = directoryOffset;
            DirectoryLength = directoryLength;
            ContainerVersion = containerVersion;
            Metadata = metadata;
            MetadataStatus = metadataStatus;
            MetadataWarning = metadataWarning ?? string.Empty;
        }

        internal uint DirectoryChecksum { get; }
        public Guid SaveGuid { get; }
        public Guid SessionGuid { get; }
        public int ObjectCount { get; }
        public long DirectoryOffset { get; }
        public long DirectoryLength { get; }
        internal ushort ContainerVersion { get; }
        internal SnapshotMetadata Metadata { get; }
        internal SnapshotMetadataStatus MetadataStatus { get; }
        internal string MetadataWarning { get; }
    }

    internal sealed class SnapshotObjectError
    {
        // Object block failures are reported separately so other valid POs can still be recovered from the snapshot.
        public Guid PersistentId { get; }
        public int DirectoryIndex { get; }
        public string Message { get; }

        internal SnapshotObjectError(Guid persistentId, int directoryIndex, string message)
        {
            PersistentId = persistentId;
            DirectoryIndex = directoryIndex;
            Message = message ?? string.Empty;
        }
    }

    internal sealed class SnapshotReadResult
    {
        // The reader only returns detached persistence data.
        // Applying it to the current city is a separate coordinator decision.
        public Guid SaveGuid { get; }
        public Guid SessionGuid { get; }
        internal SnapshotHeaderInfo Header { get; }
        internal ushort ContainerVersion => Header.ContainerVersion;
        internal SnapshotMetadata Metadata => Header.Metadata;
        internal SnapshotMetadataStatus MetadataStatus => Header.MetadataStatus;
        internal string MetadataWarning => Header.MetadataWarning;
        private PersistenceObjectData[] m_Objects;
        private EncodedPartTopology[][] m_Prepared;
        public IReadOnlyList<PersistenceObjectData> Objects => m_Objects ?? throw new InvalidOperationException("Snapshot objects have already been consumed.");
        internal int ObjectCount { get; }

        internal OwnedSnapshotObjects TakeObjectsForRestore(TopologyEncodingSession session = null)
        {
            var objects = Interlocked.Exchange(ref m_Objects, null) ?? throw new InvalidOperationException("Snapshot objects have already been consumed.");
            var prepared = m_Prepared;
            m_Prepared = null;
            return new OwnedSnapshotObjects(objects, prepared, session);
        }
        public IReadOnlyList<SnapshotObjectError> SkippedObjects { get; }

        internal SnapshotReadResult(SnapshotHeaderInfo header, IReadOnlyList<PersistenceObjectData> objects, IReadOnlyList<SnapshotObjectError> skippedObjects, EncodedPartTopology[][] prepared = null)
        {
            m_Prepared = prepared;
            Header = header ?? throw new ArgumentNullException(nameof(header));
            SaveGuid = header.SaveGuid;
            SessionGuid = header.SessionGuid;
            ObjectCount = objects?.Count ?? 0;
            m_Objects = new PersistenceObjectData[ObjectCount];
            for (int i = 0; i < ObjectCount; i++) m_Objects[i] = objects[i];
            SkippedObjects = skippedObjects ?? Array.Empty<SnapshotObjectError>();
        }
    }

    internal sealed class OwnedSnapshotObjects : IDisposable
    {
        private readonly PersistenceObjectData[] m_Objects;
        private readonly EncodedPartTopology[][] m_Prepared;
        internal readonly TopologyEncodingSession Session;
        internal EncodedPartTopology TakePrepared(int obj, int part)
        {
            if (m_Prepared == null) return null;
            var result = m_Prepared[obj][part];
            m_Prepared[obj][part] = null;
            return result;
        }
        internal int Count => m_Objects.Length;
        internal PersistenceObjectData Peek(int index) => m_Objects[index] ?? throw new InvalidOperationException("Snapshot object already consumed.");
        internal PersistenceObjectData Take(int index)
        {
            var result = Peek(index);
            m_Objects[index] = null;
            return result;
        }
        internal OwnedSnapshotObjects(PersistenceObjectData[] objects, EncodedPartTopology[][] prepared = null, TopologyEncodingSession session = null)
        { m_Objects = objects; m_Prepared = prepared; Session = session; }
        public void Dispose()
        {
            Array.Clear(m_Objects, 0, m_Objects.Length);
            if (m_Prepared != null) Array.Clear(m_Prepared, 0, m_Prepared.Length);
        }
    }

    internal sealed class SnapshotDirectoryEntry
    {
        // Mutable only while the fixed-size directory is being built or parsed.
        internal Guid PersistentId;
        internal ushort ObjectSchemaVersion;
        internal ushort Flags;
        internal long BlockOffset;
        internal long StoredLength;
        internal long DecodedLength;
        internal uint BlockChecksum;
        internal uint Reserved;
    }
}