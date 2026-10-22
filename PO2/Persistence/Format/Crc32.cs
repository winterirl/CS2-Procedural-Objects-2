using System;

namespace PO2.Persistence.Format
{
    // Small dependency-free CRC32 implementation for accidental corruption
    // detection of stored snapshot blocks.
    internal static class Crc32
    {
        private const uint kPolynomial = 0xedb88320u;

        // Build this once rather than walking all eight polynomial bits for every byte in every object block.
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

            uint crc = 0xffffffffu;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                crc = (crc >> 8) ^ s_Table[(crc ^ bytes[i]) & 0xffu];
            }

            return ~crc;
        }

        private static uint[] BuildTable()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = (value & 1u) != 0 ? (value >> 1) ^ kPolynomial : value >> 1;
                }

                table[i] = value;
            }

            return table;
        }
    }
}