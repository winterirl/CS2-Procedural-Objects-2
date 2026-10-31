using System.IO;
using PO2.Persistence.Format;

namespace PO2.Persistence.PreparedTopology
{
    internal sealed class EncodedTopologySection
    {
        internal readonly int Count;
        internal readonly byte Codec;
        internal readonly byte[] Bytes;
        internal EncodedTopologySection(int count, byte codec, byte[] bytes) { Count = count; Codec = codec; Bytes = bytes; }
    }

    internal sealed class EncodedPartTopology
    {
        internal readonly byte[] Header;
        internal readonly EncodedTopologySection[] Sections;
        internal readonly long Length, DecodedLength;
        internal readonly uint? ExpectedChecksum;
        internal ushort Version => (ushort)(Header[8] | Header[9] << 8);
        internal uint Revision => (uint)(Header[12] | Header[13] << 8 | Header[14] << 16 | Header[15] << 24);
        internal EncodedPartTopology(byte[] header, EncodedTopologySection[] sections, uint? expectedChecksum = null)
        {
            ExpectedChecksum = expectedChecksum;
            Header = header;
            Sections = sections;
            long stored = header.Length, decoded = 0;
            for (int i = 0; i < sections.Length; i++)
            {
                stored = checked(stored + sections[i].Bytes.Length);
                decoded = checked(decoded + (long)sections[i].Count * (i == 16 ? 1 : 4));
            }
            Length = stored;
            DecodedLength = decoded;
        }

        internal void WriteTo(Stream output)
        {
            output.Write(Header, 0, Header.Length);
            foreach (var section in Sections) output.Write(section.Bytes, 0, section.Bytes.Length);
        }

        internal uint Checksum()
        {
            uint state = Crc32.Append(0, Header, 0, Header.Length);
            foreach (var section in Sections) state = Crc32.Append(state, section.Bytes, 0, section.Bytes.Length);
            return state;
        }
    }
}
