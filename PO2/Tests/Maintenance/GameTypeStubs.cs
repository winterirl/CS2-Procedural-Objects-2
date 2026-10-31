// These types only satisfy unused geometry and game-ID dependencies in the format files.
// Retention, discovery, deletion and the worker below all use production code.
namespace PO2.Core
{
    internal class ProceduralObject { internal const int kMaxPartCount = 4096; }
}
namespace PO2.Persistence.Model
{
    internal class PersistenceObjectData { }
}
namespace Colossal
{
    public struct Hash128
    {
        public UInt4 value;
        public bool isValid => value.x != 0 || value.y != 0 || value.z != 0 || value.w != 0;
        public override string ToString() => PO2.Persistence.SaveIdentity.ToFileKey(PO2.Persistence.SaveIdentity.ToSnapshotGuid(this));
        internal Hash128(uint x, uint y, uint z, uint w) { value = new UInt4 { x = x, y = y, z = z, w = w }; }
    }
    public struct UInt4
    {
        internal uint x, y, z, w;
    }
}

// Discovery never restores optional geometry; the loading suite tests these types.
namespace PO2.Persistence.PreparedTopology
{
    internal sealed class EncodedPartTopology { }
    internal sealed class TopologyEncodingSession { }
}
