using System;
using System.IO;
using PO2.Core.Lod;

namespace PO2.Persistence.Format
{
    internal static class LodProfileCodec
    {
        // Presence + two 18-byte levels + transition presence + optional distances.
        internal static int DecodedBytes(LodProfile profile) =>
            profile == null ? 1 : 38 + (profile.Transitions == null ? 0 : 8);

        internal static void Write(Stream stream, LodProfile profile)
        {
            stream.WriteByte(profile == null ? (byte)0 : (byte)1);
            if (profile == null) return;
            WriteLevel(stream, profile.Lod1);
            WriteLevel(stream, profile.Lod2);
            stream.WriteByte(profile.Transitions == null ? (byte)0 : (byte)1);
            if (profile.Transitions != null)
            {
                SnapshotBinary.WriteSingle(stream, profile.Transitions.Lod1Distance);
                SnapshotBinary.WriteSingle(stream, profile.Transitions.Lod2Distance);
            }
        }

        internal static LodProfile Read(Stream stream)
        {
            if (!ReadBool(stream)) return null;
            try
            {
                LodLevelSettings lod1 = ReadLevel(stream);
                LodLevelSettings lod2 = ReadLevel(stream);
                LodTransitionSettings transitions = ReadBool(stream) ? new LodTransitionSettings(SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream)) : null;
                return new LodProfile(lod1, lod2, transitions);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Invalid saved LOD settings.", exception);
            }
        }

        private static void WriteLevel(Stream stream, LodLevelSettings level)
        {
            stream.WriteByte(level.Enabled ? (byte)1 : (byte)0);
            stream.WriteByte((byte)level.Method);
            SnapshotBinary.WriteSingle(stream, level.TriangleRatio);
            SnapshotBinary.WriteUInt32(stream, (uint)(level.TriangleCap ?? 0));
            SnapshotBinary.WriteSingle(stream, level.MaximumRelativeError);
            SnapshotBinary.WriteSingle(stream, level.PruneRelativeError);
        }

        private static LodLevelSettings ReadLevel(Stream stream)
        {
            bool enabled = ReadBool(stream);
            var method = (LodMethod)ReadByte(stream);
            float ratio = SnapshotBinary.ReadSingle(stream);
            uint cap = SnapshotBinary.ReadUInt32(stream);
            if (cap > LodLevelSettings.kMaximumTriangleCap)
            {
                throw new InvalidDataException("Saved LOD triangle cap exceeds the index limit.");
            }

            return new LodLevelSettings(enabled, method, ratio, cap == 0 ? (int?)null : (int)cap, SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream));
        }

        private static bool ReadBool(Stream stream)
        {
            int value = ReadByte(stream);
            if (value > 1) throw new InvalidDataException("Invalid saved LOD flag.");
            return value == 1;
        }

        private static int ReadByte(Stream stream)
        {
            int value = stream.ReadByte();
            if (value < 0) throw new EndOfStreamException("Saved LOD settings are incomplete.");
            return value;
        }
    }
}