using PO2.Core;

namespace PO2.Persistence.Format
{
    /*
     *
     * PO2 persistence binary-format constants.
     *
     * All integer fields are little-endian.
     * GUID/Hash128 values are the  16-byte representations written by SnapshotBinary.
     * Float values are float32 bit patterns ~ v1 performs no lossy quantization.
     *
     * Snapshot header, 80 bytes:
     *   0   magic[8] = "PO2SNAP\0"
     *   8   containerVersion u16
     *   10  snapshotSchemaVersion u16
     *   12  headerSize u16
     *   14  flags u16 (must be 0)
     *   16  exactSaveGuid[16]
     *   32  sessionGuid[16]
     *   48  objectCount u32
     *   52  directoryEntrySize u16
     *   54  reserved u16 (must be 0)
     *   56  directoryOffset u64
     *   64  directoryLength u64
     *   72  directoryCrc32 u32
     *   76  headerCrc32 u32 over bytes [0, 76)
     *
     * Container 1 puts the directory at byte 80. Container 2 inserts a
     * metadata section at byte 80 and sets directoryOffset to its end.
     * Metadata prefix, 16 bytes: PO2M[4], schema u16, flags u16 (0),
     * payloadLength u32, payloadCrc32 u32. See SnapshotMetadataCodec.
     * Container 3 appends optional prepared topology after the required blocks.
     * See PreparedTopologyExtension and PreparedTopologyCodec for that layout.
     * Header layout/checksum and all required object encodings remain unchanged.
     *
     * Directory entry, 52 bytes each:
     *   0   persistentId[16]
     *   16  objectSchemaVersion u16
     *   18  flags u16 (must be 0)
     *   20  objectBlockOffset u64
     *   28  storedLength u64
     *   36  decodedFootprint u64
     *   44  storedBlockCrc32 u32
     *   48  reserved u32 (must be 0)
     *
     * Object block header, 16 bytes:
     *   0   magic[4] = "PO2O"
     *   4   objectSchemaVersion u16
     *   6   flags u16 (must be 0)
     *   8   bodyLength u32
     *   12  bodyCrc32 u32
     *   16  body bytes
     *
     * Each part record begins with five u32 counts, then streamCount u16 and reserved u16.
     *
     * Every stream descriptor is 20 bytes:
     * semantic u8, codec u8, reserved u16, elementCount u32,
     * decodedByteLength u32, storedByteLength u32, payloadOffset u32.
     *
     * Descriptor payload offsets are relative to the beginning of that part's
     * contiguous stream-payload region.
     *
     * Object metadata v2 adds a bounded custom-orientation array after the
     * root transform. Each entry stores a UTF-8 name followed by four float32
     * components for the object-relative rotation.
     *
     * Object metadata v3 adds one structural-topology source byte after each
     * part name. This prevents mixed inferred faces from being mistaken for
     * authored topology after save/load. The v2 object schema remains readable.
     *
     * Object metadata v4 adds the object decal-reception flag.
     *
     * Object metadata v5 adds one object-local pivot after the root transform
     * and one object-local pivot after each part's topology source. Older
     * schemas retain the root origin and reconstruct the legacy part centroid.
     *
     * Object metadata v6 adds saved deformation state.
     * Object metadata v7 adds the optional TexCoord1 stream.
     * Object metadata v8 records authored surface UV layouts on the UV0 stream.
     * Object metadata v9 adds generated-mapping seeds to Surface Areas.
     *
     * The enum numeric values below are part of the frozen v2 contract.
     * New semantics/codecs require a compatible versioning decision ~ old readers
     * should never guess unknown values.
     *
     * AKA - this persistence model is locked in and any future changes need a clear path to migrating
     * this schema to the new one.
     */
    internal static class SnapshotFormat
    {
        internal static readonly byte[] s_Magic =
        {
            (byte)'P', (byte)'O', (byte)'2', (byte)'S',
            (byte)'N', (byte)'A', (byte)'P', 0,
        };

        internal const ushort kLegacyContainerVersion = 1;
        internal const ushort kContainerVersion = 2;
        internal const ushort kPreparedContainerVersion = 3;
        internal static bool IsSupportedContainerVersion(ushort version) => version >= kLegacyContainerVersion && version <= kPreparedContainerVersion;
        internal static bool HasMetadata(ushort version) => version == kContainerVersion || version == kPreparedContainerVersion;
        internal const ushort kMetadataSchemaVersion = 1;
        internal const int kMetadataPrefixSize = 16;
        internal const int kMaxMetadataPayloadBytes = 64 * 1024;
        internal const int kMaxMetadataStringBytes = 16 * 1024;
        internal const ushort kSnapshotSchemaVersion = 2;
        internal const ushort kLegacyObjectSchemaVersion = 2;
        internal const ushort kStructuralTopologyObjectSchemaVersion = 3;
        internal const ushort kDecalReceptionObjectSchemaVersion = 4;
        internal const ushort kPivotObjectSchemaVersion = 5;
        internal const ushort kDeformationObjectSchemaVersion = 6;

        internal const ushort kTexCoord1ObjectSchemaVersion = 7;

        // Schema 8 permits the explicit authored-layout flag on the existing UV0 stream.
        internal const ushort kSurfaceUVObjectSchemaVersion = 8;
        internal const ushort kMappingSeedObjectSchemaVersion = 9;
        internal const ushort kLodProfileObjectSchemaVersion = 10;
        internal const ushort kObjectSchemaVersion = kLodProfileObjectSchemaVersion;

        internal static bool IsSupportedObjectSchemaVersion(ushort version)
        {
            return version == kLegacyObjectSchemaVersion || version == kStructuralTopologyObjectSchemaVersion || version == kDecalReceptionObjectSchemaVersion || version == kPivotObjectSchemaVersion || version == kDeformationObjectSchemaVersion || version == kTexCoord1ObjectSchemaVersion || version == kSurfaceUVObjectSchemaVersion || version == kMappingSeedObjectSchemaVersion || version == kLodProfileObjectSchemaVersion;
        }

        internal const int kGuidByteCount = 16;
        internal const int kHash128ByteCount = 16;
        internal const int kFloatByteCount = 4;

        internal const ushort kSnapshotHeaderSize = 80;
        internal const ushort kDirectoryEntrySize = 52;
        internal const ushort kObjectBlockHeaderSize = 16;
        internal const ushort kStreamDescriptorSize = 20;

        // Reject corrupt counts before allocating. The part cap matches the runtime limit;
        // other ceilings bound decoded data and size calculations.
        internal const int kMaxObjectsPerSnapshot = 1_000_000;
        internal const int kMaxPartsPerObject = ProceduralObject.kMaxPartCount;
        internal const int kMaxCustomOrientationsPerObject = 12;
        internal const int kMaxCustomOrientationNameUtf8Bytes = 64;
        internal const int kMaxRenderVerticesPerPart = 16_000_000;
        internal const int kMaxLogicalVerticesPerPart = 16_000_000;
        internal const int kMaxTrianglesPerPart = 32_000_000;
        internal const int kMaxStructuralFacesPerPart = 32_000_000;
        internal const int kMaxSurfaceAreasPerPart = 1_000_000;
        internal const int kMaxStringUtf8Bytes = 1_048_576;
        internal const long kMaxStoredObjectBytes = 2L * 1024L * 1024L * 1024L;
        internal const long kMaxDecodedObjectBytes = 4L * 1024L * 1024L * 1024L;
        internal const long kMaxDecodedSnapshotBytes = 16L * 1024L * 1024L * 1024L;
    }

    internal enum SnapshotStreamSemantic : byte
    {
        Positions = 1,
        Normals = 2,
        Tangents = 3,
        UV0 = 4,
        VertexColors = 5,
        TexCoord3 = 6,
        Triangles = 7,
        RenderToLogical = 8,
        TriangleSurfaceAreaIds = 9,
        StructuralFaceIds = 10,
        StructuralFaceCornerCounts = 11,
        StructuralFaceCorners = 12,
        TexCoord1 = 13,
    }

    internal enum SnapshotCodec : byte
    {
        Raw = 0,
        Deflate = 1,
        ByteShuffleDeflate = 2,
        UnsignedFixed8 = 3,
        UnsignedFixed16 = 4,
        UnsignedFixed32 = 5,
        DeltaZigZagVarInt = 6,
        DeltaZigZagVarIntDeflate = 7,
        RleVarUInt = 8,
        RleVarUIntDeflate = 9,
        UniformColor32 = 10,
    }
}