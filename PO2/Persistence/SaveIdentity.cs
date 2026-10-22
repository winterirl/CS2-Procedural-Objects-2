using System;
using Colossal;
using PO2.Persistence.Format;

namespace PO2.Persistence
{
    // Preserves the raw Hash128 bytes in the snapshot's Guid field.
    // Filenames still use the vanilla Hash128 string.
    internal static class SaveIdentity
    {
        internal static Guid ToSnapshotGuid(Hash128 saveGuid)
        {
            // Guid(byte[]) preserves these bytes as written.
            // Using Guid parsing or Hash128.ToString would change the representation
            byte[] bytes = new byte[SnapshotFormat.kGuidByteCount];
            WriteUInt32LittleEndian(bytes, 0, saveGuid.value.x);
            WriteUInt32LittleEndian(bytes, 4, saveGuid.value.y);
            WriteUInt32LittleEndian(bytes, 8, saveGuid.value.z);
            WriteUInt32LittleEndian(bytes, 12, saveGuid.value.w);
            return new Guid(bytes);
        }

        internal static string ToFileKey(Guid snapshotGuid)
        {
            // Hash128.ToString prints each byte low nibble first. Guid.ToString
            // uses a different order, so it cannot be used to check filenames.
            const string hex = "0123456789abcdef";
            byte[] bytes = snapshotGuid.ToByteArray();
            char[] key = new char[32];
            for (int i = 0; i < bytes.Length; i++)
            {
                key[i * 2] = hex[bytes[i] & 15];
                key[i * 2 + 1] = hex[bytes[i] >> 4];
            }

            return new string(key);
        }

        private static void WriteUInt32LittleEndian(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }
    }
}