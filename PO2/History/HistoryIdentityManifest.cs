using System;
using System.Collections.Generic;
using System.IO;
using PO2.Core;

namespace PO2.History
{
    // This is a history envelope, not an addition to the city save format.
    internal sealed class HistoryIdentityManifest
    {
        private const int kFormatVersion = 1;
        private readonly Guid[] m_Parts;
        internal HistoryObjectKey ObjectKey { get; }
        internal int PartCount => m_Parts.Length;

        internal HistoryIdentityManifest(HistoryObjectKey key, IReadOnlyList<Guid> parts)
        {
            if (!key.IsValid || parts == null || parts.Count < 1 || parts.Count > ProceduralObject.kMaxPartCount)
            {
                throw new ArgumentException("Invalid history identity manifest.");
            }

            ObjectKey = key;
            m_Parts = new Guid[parts.Count];
            var seen = new HashSet<Guid>();
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] == Guid.Empty || !seen.Add(parts[i]))
                {
                    throw new ArgumentException("Missing or repeated history part ID.");
                }

                m_Parts[i] = parts[i];
            }
        }

        internal static HistoryIdentityManifest Capture(ProceduralObject obj)
        {
            var parts = new Guid[obj.Parts.Count];
            for (int i = 0; i < parts.Length; i++) parts[i] = obj.Parts[i].PartHistoryId;
            return new HistoryIdentityManifest(new HistoryObjectKey(obj.Id, obj.PersistentId), parts);
        }

        internal bool Matches(HistoryIdentityManifest other)
        {
            if (other == null || !ObjectKey.Equals(other.ObjectKey) || PartCount != other.PartCount) return false;
            for (int i = 0; i < PartCount; i++)
                if (m_Parts[i] != other.m_Parts[i])
                    return false;
            return true;
        }

        // Only call on a detached staging graph. The caller validates the resident
        // descriptor against the decoded manifest before this step.
        internal bool TryRestoreStaging(ProceduralObject staging, out string reason)
        {
            reason = string.Empty;
            if (staging == null || staging.Id != ObjectKey.RuntimeId || staging.PersistentId != ObjectKey.PersistentId || staging.Parts.Count != PartCount)
            {
                reason = "Decoded object does not match the history identity manifest.";
                return false;
            }

            // Reject aliased child instances before any ID is changed.
            var originalIds = new HashSet<Guid>();
            for (int i = 0; i < PartCount; i++)
            {
                if (!originalIds.Add(staging.Parts[i].PartHistoryId))
                {
                    reason = "Decoded graph contains repeated parts.";
                    return false;
                }
            }

            for (int i = 0; i < PartCount; i++) staging.Parts[i].RestoreHistoryIdentity(m_Parts[i]);
            staging.RebindDeformationParts();
            return true;
        }

        internal void Write(BinaryWriter writer)
        {
            writer.Write(kFormatVersion);
            writer.Write(ObjectKey.RuntimeId);
            writer.Write(ObjectKey.PersistentId.ToByteArray());
            writer.Write(PartCount);
            for (int i = 0; i < PartCount; i++) writer.Write(m_Parts[i].ToByteArray());
        }

        internal static HistoryIdentityManifest Read(BinaryReader reader)
        {
            if (reader.ReadInt32() != kFormatVersion) throw new InvalidDataException("Unknown history identity format.");
            var key = new HistoryObjectKey(reader.ReadInt32(), ReadGuid(reader));
            int count = reader.ReadInt32();
            if (count < 1 || count > ProceduralObject.kMaxPartCount) throw new InvalidDataException("Invalid history part count.");
            var parts = new Guid[count];
            for (int i = 0; i < count; i++) parts[i] = ReadGuid(reader);
            return new HistoryIdentityManifest(key, parts);
        }

        private static Guid ReadGuid(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(16);
            if (bytes.Length != 16) throw new EndOfStreamException("Incomplete history identity.");
            return new Guid(bytes);
        }
    }
}