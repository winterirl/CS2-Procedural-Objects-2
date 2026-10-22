using System;
using System.Collections.Generic;
using PO2.Persistence.Model;

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
        public IReadOnlyList<PersistenceObjectData> Objects { get; }
        public IReadOnlyList<SnapshotObjectError> SkippedObjects { get; }

        internal SnapshotReadResult(SnapshotHeaderInfo header, IReadOnlyList<PersistenceObjectData> objects, IReadOnlyList<SnapshotObjectError> skippedObjects)
        {
            Header = header ?? throw new ArgumentNullException(nameof(header));
            SaveGuid = header.SaveGuid;
            SessionGuid = header.SessionGuid;
            Objects = objects ?? Array.Empty<PersistenceObjectData>();
            SkippedObjects = skippedObjects ?? Array.Empty<SnapshotObjectError>();
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