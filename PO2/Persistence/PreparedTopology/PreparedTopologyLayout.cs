namespace PO2.Persistence.PreparedTopology
{
    internal static class PreparedTopologyLayout
    {
        internal const ushort FullVersion = 1, CompactVersion = 2;
        internal const uint Revision = 1;
        internal const byte Derived = 3;

        internal static bool Supported(ushort version, uint revision) =>
            (version == FullVersion || version == CompactVersion) && revision == Revision;

        internal static bool IsDerived(ushort version, int id)
        {
            if (version != CompactVersion) return false;
            return id != 7 && id != 8 && id != 9 && id != 10 && id != 16 && id != 20 && id != 21;
        }

        internal static bool ValidCodec(ushort version, int id, int codec)
        {
            return IsDerived(version, id) ? codec == Derived :
                codec >= 0 && codec <= 2 && (codec != 2 || id != 17);
        }
    }
}
