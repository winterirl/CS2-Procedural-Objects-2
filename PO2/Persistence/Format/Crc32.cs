using System;

namespace PO2.Persistence.Format
{
    // Small dependency-free CRC32 implementation for accidental corruption detection of stored snapshot blocks.
    internal static class Crc32
    {
        private const uint kPolynomial = 0xedb88320u;

        // Same CRC32 polynomial and byte order, with eight bytes handled per step.
        private static readonly uint[] s_Table = BuildTable();

        internal static uint Compute(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            return Compute(bytes, 0, bytes.Length);
        }

        internal static uint Compute(byte[] bytes, int offset, int count)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (offset < 0 || count < 0 || offset > bytes.Length - count)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            return Append(0, bytes, offset, count);
        }

        internal static uint Append(uint previous, byte[] bytes, int offset, int count)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (offset < 0 || count < 0 || offset > bytes.Length - count) throw new ArgumentOutOfRangeException(nameof(count));
            uint crc = ~previous;
            int end = offset + count;
            int i = offset;
            while (i <= end - 8)
            {
                uint first = crc ^ (uint)(bytes[i] | bytes[i + 1] << 8 | bytes[i + 2] << 16 | bytes[i + 3] << 24);
                crc = s_Table[1792 + (first & 255)] ^ s_Table[1536 + ((first >> 8) & 255)] ^
                    s_Table[1280 + ((first >> 16) & 255)] ^ s_Table[1024 + (first >> 24)] ^
                    s_Table[768 + bytes[i + 4]] ^ s_Table[512 + bytes[i + 5]] ^
                    s_Table[256 + bytes[i + 6]] ^ s_Table[bytes[i + 7]];
                i += 8;
            }
            for (; i < end; i++) crc = (crc >> 8) ^ s_Table[(crc ^ bytes[i]) & 255];

            return ~crc;
        }

        private static uint[] BuildTable()
        {
            uint[] table = new uint[8 * 256];
            for (uint i = 0; i < 256; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = (value & 1u) != 0 ? (value >> 1) ^ kPolynomial : value >> 1;
                }

                table[i] = value;
            }

            for (int slice = 1; slice < 8; slice++)
            for (int i = 0; i < 256; i++)
            {
                uint previous = table[(slice - 1) * 256 + i];
                table[slice * 256 + i] = (previous >> 8) ^ table[previous & 255];
            }
            return table;
        }
    }
}