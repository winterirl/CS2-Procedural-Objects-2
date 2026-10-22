using System;
using System.IO;
using PO2.Geometry.Topology;
using PO2.Materials;

namespace PO2.Persistence.Format
{
    // Shared checks for values that can otherwise become allocations or invalid runtime enum states.
    // Unknown values from v1 schema are rejected instead of guessed.
    internal static class SnapshotValidation
    {
        private const VertexAttributeFlags kKnownVertexAttributeFlags = VertexAttributeFlags.UV0FourComponent | VertexAttributeFlags.VertexColor | VertexAttributeFlags.TexCoord3 | VertexAttributeFlags.TexCoord1 | VertexAttributeFlags.SurfaceUVCoordinates;

        internal static int CheckedByteLength(int count, int elementSize, int maximumBytes)
        {
            if (count < 0) throw new InvalidDataException("Negative element count.");
            if (elementSize <= 0) throw new ArgumentOutOfRangeException(nameof(elementSize));
            int bytes = checked(count * elementSize);
            if (bytes > maximumBytes)
            {
                throw new InvalidDataException($"Decoded byte count {bytes} exceeds safety ceiling {maximumBytes}.");
            }

            return bytes;
        }

        internal static long CheckedByteLength(long count, long elementSize, long maximumBytes)
        {
            if (count < 0) throw new InvalidDataException("Negative element count.");
            if (elementSize <= 0) throw new ArgumentOutOfRangeException(nameof(elementSize));
            long bytes = checked(count * elementSize);
            if (bytes > maximumBytes)
            {
                throw new InvalidDataException($"Decoded byte count {bytes} exceeds safety ceiling {maximumBytes}.");
            }

            return bytes;
        }

        internal static void ValidateCount(int value, int maximum, string label)
        {
            if (value < 0 || value > maximum)
            {
                throw new InvalidDataException($"{label} count {value} is outside the supported safety range 0..{maximum}.");
            }
        }

        internal static SnapshotStreamSemantic ValidateStreamSemantic(byte raw)
        {
            SnapshotStreamSemantic value = (SnapshotStreamSemantic)raw;
            switch (value)
            {
                case SnapshotStreamSemantic.Positions:
                case SnapshotStreamSemantic.Normals:
                case SnapshotStreamSemantic.Tangents:
                case SnapshotStreamSemantic.UV0:
                case SnapshotStreamSemantic.VertexColors:
                case SnapshotStreamSemantic.TexCoord1:
                case SnapshotStreamSemantic.TexCoord3:
                case SnapshotStreamSemantic.Triangles:
                case SnapshotStreamSemantic.RenderToLogical:
                case SnapshotStreamSemantic.TriangleSurfaceAreaIds:
                case SnapshotStreamSemantic.StructuralFaceIds:
                case SnapshotStreamSemantic.StructuralFaceCornerCounts:
                case SnapshotStreamSemantic.StructuralFaceCorners:
                    return value;
                default:
                    throw new InvalidDataException($"Unknown snapshot stream semantic {raw}.");
            }
        }

        internal static SnapshotCodec ValidateCodec(byte raw)
        {
            SnapshotCodec value = (SnapshotCodec)raw;
            switch (value)
            {
                case SnapshotCodec.Raw:
                case SnapshotCodec.Deflate:
                case SnapshotCodec.ByteShuffleDeflate:
                case SnapshotCodec.UnsignedFixed8:
                case SnapshotCodec.UnsignedFixed16:
                case SnapshotCodec.UnsignedFixed32:
                case SnapshotCodec.DeltaZigZagVarInt:
                case SnapshotCodec.DeltaZigZagVarIntDeflate:
                case SnapshotCodec.RleVarUInt:
                case SnapshotCodec.RleVarUIntDeflate:
                case SnapshotCodec.UniformColor32:
                    return value;
                default:
                    throw new InvalidDataException($"Unknown snapshot codec {raw}.");
            }
        }

        internal static MaterialSourceKind ValidateMaterialSourceKind(byte raw)
        {
            MaterialSourceKind value = (MaterialSourceKind)raw;
            switch (value)
            {
                case MaterialSourceKind.LibraryMaterial:
                case MaterialSourceKind.SurfaceAsset:
                case MaterialSourceKind.VanillaSpecial:
                    return value;
                default:
                    throw new InvalidDataException($"Unknown material source kind {raw}.");
            }
        }

        internal static TextureMappingMode ValidateMappingMode(byte raw)
        {
            TextureMappingMode value = (TextureMappingMode)raw;
            switch (value)
            {
                case TextureMappingMode.Original:
                case TextureMappingMode.Repeat:
                case TextureMappingMode.Stretch:
                    return value;
                default:
                    throw new InvalidDataException($"Unknown texture mapping mode {raw}.");
            }
        }

        internal static ColorMaskMode ValidateColorMaskMode(byte raw)
        {
            ColorMaskMode value = (ColorMaskMode)raw;
            switch (value)
            {
                case ColorMaskMode.Disabled:
                case ColorMaskMode.Source:
                case ColorMaskMode.Override:
                    return value;
                default:
                    throw new InvalidDataException($"Unknown color mask mode {raw}.");
            }
        }

        internal static NativeWindowKind ValidateNativeWindowKind(byte raw)
        {
            NativeWindowKind value = (NativeWindowKind)raw;
            switch (value)
            {
                case NativeWindowKind.None:
                case NativeWindowKind.Window:
                case NativeWindowKind.FrostedWindow:
                    return value;
                default:
                    throw new InvalidDataException($"Unknown native window kind {raw}.");
            }
        }

        internal static VanillaSpecialMaterialKind ValidateVanillaSpecialKind(byte raw)
        {
            VanillaSpecialMaterialKind value = (VanillaSpecialMaterialKind)raw;
            switch (value)
            {
                case VanillaSpecialMaterialKind.None:
                case VanillaSpecialMaterialKind.Glass:
                case VanillaSpecialMaterialKind.Grass:
                case VanillaSpecialMaterialKind.Water:
                    return value;
                default:
                    throw new InvalidDataException($"Unknown vanilla special material kind {raw}.");
            }
        }

        internal static VertexAttributeFlags ValidateVertexAttributeFlags(uint raw)
        {
            VertexAttributeFlags value = (VertexAttributeFlags)raw;
            if ((value & ~kKnownVertexAttributeFlags) != 0)
            {
                throw new InvalidDataException($"Vertex attribute flags contain unknown bits: 0x{raw:x8}.");
            }

            return value;
        }

        internal static StructuralTopologySource ValidateStructuralTopologySource(byte raw)
        {
            StructuralTopologySource value = (StructuralTopologySource)raw;
            switch (value)
            {
                case PO2.Geometry.Topology.StructuralTopologySource.Authored:
                case StructuralTopologySource.ConvertedTriangleBaseline:
                case PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred:
                    return value;
                default:
                    throw new InvalidDataException($"Unknown structural topology source {raw}.");
            }
        }

        internal static void ValidateMaterialPayload(MaterialSourceKind sourceKind, string definitionId, VanillaSpecialMaterialKind vanillaSpecialKind)
        {
            // The fixed material record has room for several identities,
            // but only the one selected by SourceKind may carry a value.
            switch (sourceKind)
            {
                case MaterialSourceKind.LibraryMaterial:
                    if (string.IsNullOrWhiteSpace(definitionId))
                    {
                        throw new InvalidDataException("Library material persistence record requires a definition ID.");
                    }

                    if (vanillaSpecialKind != VanillaSpecialMaterialKind.None)
                    {
                        throw new InvalidDataException("Library material record cannot contain a vanilla-special kind.");
                    }

                    break;

                case MaterialSourceKind.SurfaceAsset:
                    if (!string.IsNullOrEmpty(definitionId) || vanillaSpecialKind != VanillaSpecialMaterialKind.None)
                    {
                        throw new InvalidDataException("SurfaceAsset material record contains incompatible source payload.");
                    }

                    break;

                case MaterialSourceKind.VanillaSpecial:
                    if (!string.IsNullOrEmpty(definitionId) || vanillaSpecialKind == VanillaSpecialMaterialKind.None)
                    {
                        throw new InvalidDataException("VanillaSpecial material record requires a non-None special kind and no definition ID.");
                    }

                    break;

                default:
                    throw new InvalidDataException($"Unsupported material source kind {sourceKind}.");
            }
        }
    }
}