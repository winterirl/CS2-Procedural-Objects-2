using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using PO2.Core;
using PO2.Geometry.Topology;
using PO2.Materials;
using PO2.Persistence.Model;

namespace PO2.Persistence.Format
{
    //Production encoder for one independently checksummed PO object block.
    // It has no save-lifecycle or runtime-system dependencies.
    internal static class ObjectBlockWriter
    {
        private static readonly byte[] s_Magic = { (byte)'P', (byte)'O', (byte)'2', (byte)'O' };

        internal static byte[] Write(PersistenceObjectData data, SnapshotEncodingOptions options = null, EncodingMetrics metrics = null)
        {
            return WriteForSchema(data, SnapshotFormat.kObjectSchemaVersion, options, metrics);
        }

        // Kept internal so the compatibility gate can produce exact v2/v3
        // blocks without checking binary fixtures into the project.
        internal static byte[] WriteForSchema(PersistenceObjectData data, ushort schemaVersion, SnapshotEncodingOptions options = null, EncodingMetrics metrics = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (!SnapshotFormat.IsSupportedObjectSchemaVersion(schemaVersion))
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            if (data.PersistentId == Guid.Empty)
            {
                throw new InvalidDataException("Cannot encode an object with an empty persistent ID.");
            }

            SnapshotValidation.ValidateCount(data.Parts.Length, SnapshotFormat.kMaxPartsPerObject, "Part");

            if (schemaVersion < SnapshotFormat.kDeformationObjectSchemaVersion && data.Deformations.Length != 0)
            {
                throw new InvalidDataException("An older schema cannot retain an editable deformation.");
            }

            if (schemaVersion < SnapshotFormat.kTexCoord1ObjectSchemaVersion)
            {
                foreach (var part in data.Parts)
                {
                    if (part.TexCoord1 != null) throw new InvalidDataException("An older schema cannot retain UV1.");
                    foreach (var area in part.SurfaceAreas)
                    {
                        if ((area.SourceVertexAttributes & VertexAttributeFlags.TexCoord1) != 0)
                            throw new InvalidDataException("An older schema cannot retain UV1 attributes.");
                    }
                }
            }

            if (schemaVersion < SnapshotFormat.kSurfaceUVObjectSchemaVersion)
            {
                foreach (var part in data.Parts)
                {
                    foreach (var area in part.SurfaceAreas)
                    {
                        if ((area.SourceVertexAttributes & VertexAttributeFlags.SurfaceUVCoordinates) != 0)
                            throw new InvalidDataException("An older schema cannot retain authored surface UV mapping.");
                    }
                }
            }

            if (schemaVersion < SnapshotFormat.kLodProfileObjectSchemaVersion && data.LodProfile != null)
            {
                throw new InvalidDataException("An older schema cannot retain custom LOD settings.");
            }

            DeformationCodec.Validate(data.Deformations, data.Parts);

            // Reserve the header in the same buffer as the body. Fill it after
            // encoding, so the checksum does not need a second copy of the object.
            var context = new EncodingContext(options?.Compression ?? SaveCompression.Compact, metrics);
            using (MemoryStream output = new MemoryStream())
            {
                output.SetLength(SnapshotFormat.kObjectBlockHeaderSize);
                output.Position = SnapshotFormat.kObjectBlockHeaderSize;
                SnapshotBinary.WriteGuid(output, data.PersistentId);
                WriteMetadata(output, data, schemaVersion, context);
                SnapshotBinary.WriteUInt32(output, checked((uint)data.Parts.Length));
                PartEncoding.Write(output, data.Parts, options, metrics);

                if (schemaVersion >= SnapshotFormat.kDeformationObjectSchemaVersion)
                {
                    using (var writer = new BinaryWriter(output, Encoding.UTF8, true))
                    {
                        writer.Write(data.Deformations.Length);
                        foreach (var cage in data.Deformations) DeformationCodec.Write(writer, cage, context);
                    }
                }

                int bodyLength = checked((int)(output.Length - SnapshotFormat.kObjectBlockHeaderSize));
                if (output.Length > SnapshotFormat.kMaxStoredObjectBytes)
                    throw new InvalidDataException("Encoded object block exceeds safety ceiling.");
                uint crc = Crc32.Compute(output.GetBuffer(), SnapshotFormat.kObjectBlockHeaderSize, bodyLength);
                output.Position = 0;
                output.Write(s_Magic, 0, s_Magic.Length);
                SnapshotBinary.WriteUInt16(output, schemaVersion);
                SnapshotBinary.WriteUInt16(output, 0);
                SnapshotBinary.WriteUInt32(output, checked((uint)bodyLength));
                SnapshotBinary.WriteUInt32(output, crc);
                return output.ToArray();
            }
        }

        private static void WriteMetadata(Stream stream, PersistenceObjectData data, ushort schemaVersion, EncodingContext context)
        {
            // Names, transforms, materials, and Surface Areas are grouped before the large mesh streams.
            // One codec covers the whole metadata section.
            byte[] raw;
            using (MemoryStream metadata = new MemoryStream())
            {
                WriteString(metadata, data.Name);
                metadata.WriteByte(data.IsLocked ? (byte)1 : (byte)0);
                if (schemaVersion >= SnapshotFormat.kDecalReceptionObjectSchemaVersion)
                {
                    metadata.WriteByte(data.ReceivesDecals ? (byte)1 : (byte)0);
                }

                WriteTransform(metadata, data.Transform);
                if (schemaVersion >= SnapshotFormat.kPivotObjectSchemaVersion)
                {
                    WriteVector3(metadata, data.LocalPivot);
                }

                WriteCustomOrientations(metadata, data.CustomOrientations);
                if (schemaVersion >= SnapshotFormat.kLodProfileObjectSchemaVersion)
                {
                    LodProfileCodec.Write(metadata, data.LodProfile);
                }

                SnapshotBinary.WriteVarUInt(metadata, checked((uint)data.Parts.Length));
                for (int i = 0; i < data.Parts.Length; i++)
                {
                    WritePartMetadata(metadata, data.Parts[i], schemaVersion);
                }

                raw = metadata.ToArray();
            }

            byte[] deflated = raw.Length >= 64 ? context.Deflate(raw) : null;
            SnapshotCodec codec = SnapshotCodec.Raw;
            byte[] stored = raw;
            if (deflated != null && deflated.Length < raw.Length)
            {
                codec = SnapshotCodec.Deflate;
                stored = deflated;
            }

            stream.WriteByte((byte)codec);
            SnapshotBinary.WriteUInt32(stream, checked((uint)raw.Length));
            SnapshotBinary.WriteUInt32(stream, checked((uint)stored.Length));
            stream.Write(stored, 0, stored.Length);
        }

        private static void WriteCustomOrientations(Stream stream, PersistenceCustomOrientationData[] orientations)
        {
            if (orientations == null)
            {
                throw new InvalidDataException("Custom-orientation persistence array is null.");
            }

            SnapshotValidation.ValidateCount(orientations.Length, SnapshotFormat.kMaxCustomOrientationsPerObject, "Custom orientation");
            SnapshotBinary.WriteVarUInt(stream, checked((uint)orientations.Length));

            for (int i = 0; i < orientations.Length; i++)
            {
                PersistenceCustomOrientationData orientation = orientations[i];
                if (orientation == null || !CustomOrientation.TryCreate(orientation.Name, orientation.LocalRotation, out CustomOrientation validated))
                {
                    throw new InvalidDataException($"Custom orientation {i} has an invalid name or rotation.");
                }

                WriteString(stream, validated.Name);
                SnapshotBinary.WriteSingle(stream, validated.LocalRotation.x);
                SnapshotBinary.WriteSingle(stream, validated.LocalRotation.y);
                SnapshotBinary.WriteSingle(stream, validated.LocalRotation.z);
                SnapshotBinary.WriteSingle(stream, validated.LocalRotation.w);
            }
        }

        internal static void WritePartMetadata(Stream stream, PersistencePartData part)
        {
            WritePartMetadata(stream, part, SnapshotFormat.kObjectSchemaVersion);
        }

        private static void WritePartMetadata(Stream stream, PersistencePartData part, ushort schemaVersion)
        {
            if (part == null)
            {
                throw new InvalidDataException("Persistence part is null.");
            }

            WriteString(stream, part.Name);
            if (schemaVersion >= SnapshotFormat.kStructuralTopologyObjectSchemaVersion)
            {
                if (!part.StructuralTopologySource.HasValue)
                {
                    throw new InvalidDataException("Persistence part is missing structural topology provenance.");
                }

                StructuralTopologySource topologySource = SnapshotValidation.ValidateStructuralTopologySource((byte)part.StructuralTopologySource.Value);
                stream.WriteByte((byte)topologySource);
            }

            if (schemaVersion >= SnapshotFormat.kPivotObjectSchemaVersion)
            {
                if (!part.LocalPivot.HasValue)
                {
                    throw new InvalidDataException("Persistence part is missing its pivot.");
                }

                WriteVector3(stream, part.LocalPivot.Value);
            }

            SnapshotBinary.WriteVarUInt(stream, checked((uint)part.NextSurfaceAreaId));
            SnapshotBinary.WriteVarUInt(stream, checked((uint)part.SurfaceAreas.Length));
            for (int i = 0; i < part.SurfaceAreas.Length; i++)
            {
                PersistenceSurfaceAreaData area = part.SurfaceAreas[i];
                SnapshotBinary.WriteVarUInt(stream, checked((uint)area.Id));
                SnapshotBinary.WriteUInt32(stream, (uint)area.SourceVertexAttributes);
                stream.WriteByte((byte)area.NativeWindowKind);
                stream.WriteByte(area.HasOriginalState ? (byte)1 : (byte)0);
                WriteMaterial(stream, area.Material);
                WriteColorMask(stream, area.ColorMask);
                if (area.HasOriginalState)
                {
                    WriteMaterial(stream, area.OriginalMaterial);
                    WriteColorMask(stream, area.OriginalColorMask);
                }

                if (schemaVersion >= SnapshotFormat.kMappingSeedObjectSchemaVersion) GeneratedMappingSeedCodec.Write(stream, area.MappingSeed);
                else if (area.MappingSeed != null) throw new InvalidDataException("Preserved mapping requires object schema 9.");
            }
        }

        internal static void WritePart(Stream stream, PersistencePartData part, EncodingContext context)
        {
            if (part == null) throw new InvalidDataException("Persistence part is null.");
            int renderVertexCount = part.Vertices != null ? part.Vertices.Length : 0;
            int triangleCount = part.Triangles != null ? part.Triangles.Length / 3 : 0;
            int structuralFaceCount = part.StructuralFaces != null ? part.StructuralFaces.Length : 0;

            SnapshotValidation.ValidateCount(renderVertexCount, SnapshotFormat.kMaxRenderVerticesPerPart, "Render vertex");
            SnapshotValidation.ValidateCount(part.LogicalVertexCount, SnapshotFormat.kMaxLogicalVerticesPerPart, "Logical vertex");
            SnapshotValidation.ValidateCount(triangleCount, SnapshotFormat.kMaxTrianglesPerPart, "Triangle");
            SnapshotValidation.ValidateCount(structuralFaceCount, SnapshotFormat.kMaxStructuralFacesPerPart, "Structural face");
            SnapshotValidation.ValidateCount(part.SurfaceAreas.Length, SnapshotFormat.kMaxSurfaceAreasPerPart, "Surface Area");

            if (part.Triangles.Length % 3 != 0)
            {
                throw new InvalidDataException("Triangle index stream length is not divisible by three.");
            }

            if (part.RenderToLogical.Length != renderVertexCount)
            {
                throw new InvalidDataException("Render-to-logical stream length does not match render vertex count.");
            }

            if (part.TriangleSurfaceAreaIds.Length != triangleCount)
            {
                throw new InvalidDataException("Surface Area membership length does not match triangle count.");
            }

            // Validate relationships before writing any descriptors.
            // A block that can't reconstruct edit topology should never make it to disk.
            bool[] logicalSeen = new bool[part.LogicalVertexCount];
            for (int i = 0; i < part.Triangles.Length; i++)
            {
                if (part.Triangles[i] < 0 || part.Triangles[i] >= renderVertexCount)
                {
                    throw new InvalidDataException($"Triangle index {part.Triangles[i]} is outside render vertex range.");
                }
            }

            for (int i = 0; i < part.RenderToLogical.Length; i++)
            {
                int logicalId = part.RenderToLogical[i];
                if (logicalId < 0 || logicalId >= part.LogicalVertexCount)
                {
                    throw new InvalidDataException($"Logical vertex ID {logicalId} is outside logical vertex range.");
                }

                logicalSeen[logicalId] = true;
            }

            for (int i = 0; i < logicalSeen.Length; i++)
            {
                if (!logicalSeen[i])
                {
                    throw new InvalidDataException($"Logical vertex {i} has no render-vertex membership.");
                }
            }

            HashSet<int> areaIds = new HashSet<int>();
            int maxAreaIdForValidation = -1;
            for (int i = 0; i < part.SurfaceAreas.Length; i++)
            {
                PersistenceSurfaceAreaData area = part.SurfaceAreas[i];
                if (area == null || !areaIds.Add(area.Id))
                {
                    throw new InvalidDataException("Surface Area records are null or contain duplicate IDs.");
                }

                if (area.Id > maxAreaIdForValidation) maxAreaIdForValidation = area.Id;
            }

            if (!areaIds.Contains(0))
            {
                throw new InvalidDataException("Mesh part is missing required Surface Area 0.");
            }

            if (part.NextSurfaceAreaId <= maxAreaIdForValidation)
            {
                throw new InvalidDataException("Next Surface Area ID must exceed all existing Surface Area IDs.");
            }

            for (int i = 0; i < part.TriangleSurfaceAreaIds.Length; i++)
            {
                if (!areaIds.Contains(part.TriangleSurfaceAreaIds[i]))
                {
                    throw new InvalidDataException($"Triangle membership references nonexistent Surface Area {part.TriangleSurfaceAreaIds[i]}.");
                }
            }

            List<EncodedStream> streams = new List<EncodedStream>();
            Add(streams, ObjectBlockCodec.EncodeVector3(SnapshotStreamSemantic.Positions, part.Vertices, context));
            Add(streams, ObjectBlockCodec.EncodeVector3(SnapshotStreamSemantic.Normals, part.Normals, context));
            Add(streams, ObjectBlockCodec.EncodeVector4(SnapshotStreamSemantic.Tangents, part.Tangents, context));
            Add(streams, ObjectBlockCodec.EncodeVector4(SnapshotStreamSemantic.UV0, part.UV0, context));
            Add(streams, ObjectBlockCodec.EncodeColors(part.VertexColors, context));
            Add(streams, ObjectBlockCodec.EncodeVector4(SnapshotStreamSemantic.TexCoord1, part.TexCoord1, context));
            Add(streams, ObjectBlockCodec.EncodeVector4(SnapshotStreamSemantic.TexCoord3, part.TexCoord3, context));
            Add(streams, ObjectBlockCodec.EncodeTriangles(part.Triangles, renderVertexCount, context));
            Add(streams, ObjectBlockCodec.EncodeRenderToLogical(part.RenderToLogical, part.LogicalVertexCount, context));

            Add(streams, ObjectBlockCodec.EncodeMembership(part.TriangleSurfaceAreaIds, maxAreaIdForValidation, context));

            // Structural faces have variable corner counts. Split them into three
            // parallel streams so each can use the codec that fits it best.
            int[] structuralIds = new int[structuralFaceCount];
            byte[] cornerCounts = new byte[structuralFaceCount];
            int cornerTotal = 0;
            for (int i = 0; i < structuralFaceCount; i++)
            {
                StructuralFace face = part.StructuralFaces[i];
                if (face.CornerCount != 3 && face.CornerCount != 4)
                {
                    throw new InvalidDataException("Structural face must contain exactly three or four corners.");
                }

                structuralIds[i] = face.Id;
                cornerCounts[i] = (byte)face.CornerCount;
                cornerTotal = checked(cornerTotal + face.CornerCount);
            }

            int[] structuralCorners = new int[cornerTotal];
            int cornerCursor = 0;
            for (int i = 0; i < structuralFaceCount; i++)
            {
                StructuralFace face = part.StructuralFaces[i];
                for (int c = 0; c < face.CornerCount; c++)
                {
                    structuralCorners[cornerCursor++] = face.GetRenderVertexIndex(c);
                }
            }

            Add(streams, ObjectBlockCodec.EncodeStructuralFaceIds(structuralIds, context));
            Add(streams, ObjectBlockCodec.EncodeStructuralCornerCounts(cornerCounts, context));
            Add(streams, ObjectBlockCodec.EncodeStructuralCorners(structuralCorners, renderVertexCount, context));

            SnapshotBinary.WriteUInt32(stream, checked((uint)renderVertexCount));
            SnapshotBinary.WriteUInt32(stream, checked((uint)part.LogicalVertexCount));
            SnapshotBinary.WriteUInt32(stream, checked((uint)triangleCount));
            SnapshotBinary.WriteUInt32(stream, checked((uint)structuralFaceCount));
            SnapshotBinary.WriteUInt32(stream, checked((uint)part.SurfaceAreas.Length));
            SnapshotBinary.WriteUInt16(stream, checked((ushort)streams.Count));
            SnapshotBinary.WriteUInt16(stream, 0);

            // Descriptors come first, then one contiguous payload region.
            // Readers can validate every offset before copying/decoding a stream.
            int payloadOffset = 0;
            for (int i = 0; i < streams.Count; i++)
            {
                EncodedStream encoded = streams[i];
                stream.WriteByte((byte)encoded.Semantic);
                stream.WriteByte((byte)encoded.Codec);
                SnapshotBinary.WriteUInt16(stream, 0);
                SnapshotBinary.WriteUInt32(stream, checked((uint)encoded.ElementCount));
                SnapshotBinary.WriteUInt32(stream, checked((uint)encoded.DecodedByteLength));
                SnapshotBinary.WriteUInt32(stream, checked((uint)encoded.Payload.Length));
                SnapshotBinary.WriteUInt32(stream, checked((uint)payloadOffset));
                payloadOffset = checked(payloadOffset + encoded.Payload.Length);
            }

            for (int i = 0; i < streams.Count; i++)
            {
                byte[] payload = streams[i].Payload;
                stream.Write(payload, 0, payload.Length);
            }
        }

        private static void Add(List<EncodedStream> streams, EncodedStream encoded)
        {
            if (encoded != null) streams.Add(encoded);
        }

        private static void WriteTransform(Stream stream, ObjectTransform transform)
        {
            SnapshotBinary.WriteSingle(stream, transform.Position.x);
            SnapshotBinary.WriteSingle(stream, transform.Position.y);
            SnapshotBinary.WriteSingle(stream, transform.Position.z);
            SnapshotBinary.WriteSingle(stream, transform.Rotation.x);
            SnapshotBinary.WriteSingle(stream, transform.Rotation.y);
            SnapshotBinary.WriteSingle(stream, transform.Rotation.z);
            SnapshotBinary.WriteSingle(stream, transform.Rotation.w);
            SnapshotBinary.WriteSingle(stream, transform.Scale.x);
            SnapshotBinary.WriteSingle(stream, transform.Scale.y);
            SnapshotBinary.WriteSingle(stream, transform.Scale.z);
        }

        private static void WriteVector3(Stream stream, Vector3 value)
        {
            SnapshotBinary.WriteSingle(stream, value.x);
            SnapshotBinary.WriteSingle(stream, value.y);
            SnapshotBinary.WriteSingle(stream, value.z);
        }

        private static void WriteMaterial(Stream stream, PersistenceMaterialData material)
        {
            if (material == null) throw new InvalidDataException("Material persistence record is null.");
            SnapshotValidation.ValidateMaterialPayload(material.SourceKind, material.DefinitionId, material.VanillaSpecialKind);
            // Only write the identity selected by SourceKind.
            // Old/stale fields don't become part of the frozen binary record.
            stream.WriteByte((byte)material.SourceKind);
            switch (material.SourceKind)
            {
                case MaterialSourceKind.LibraryMaterial:
                    WriteString(stream, material.DefinitionId);
                    break;
                case MaterialSourceKind.SurfaceAsset:
                    SnapshotBinary.WriteHash128(stream, material.SurfaceGuid);
                    break;
                case MaterialSourceKind.VanillaSpecial:
                    stream.WriteByte((byte)material.VanillaSpecialKind);
                    break;
            }

            stream.WriteByte((byte)material.MappingMode);
            SnapshotBinary.WriteSingle(stream, material.TextureScale.x);
            SnapshotBinary.WriteSingle(stream, material.TextureScale.y);
            SnapshotBinary.WriteSingle(stream, material.Rotation);
            SnapshotBinary.WriteSingle(stream, material.Offset.x);
            SnapshotBinary.WriteSingle(stream, material.Offset.y);
            SnapshotBinary.WriteSingle(stream, material.Metallic);
            SnapshotBinary.WriteSingle(stream, material.Smoothness);
            SnapshotBinary.WriteSingle(stream, material.CoatStrength);
        }

        private static void WriteColorMask(Stream stream, PersistenceColorMaskData mask)
        {
            if (mask == null) throw new InvalidDataException("Color-mask persistence record is null.");
            stream.WriteByte((byte)mask.Mode);
            stream.WriteByte(mask.HasSourceMask ? (byte)1 : (byte)0);
            if (mask.HasSourceMask)
            {
                WriteColor(stream, mask.SourceMask1);
                WriteColor(stream, mask.SourceMask2);
                WriteColor(stream, mask.SourceMask3);
            }

            WriteColor(stream, mask.OverrideMask1);
            WriteColor(stream, mask.OverrideMask2);
            WriteColor(stream, mask.OverrideMask3);
        }

        private static void WriteColor(Stream stream, Color color)
        {
            SnapshotBinary.WriteSingle(stream, color.r);
            SnapshotBinary.WriteSingle(stream, color.g);
            SnapshotBinary.WriteSingle(stream, color.b);
            SnapshotBinary.WriteSingle(stream, color.a);
        }

        private static void WriteString(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            if (bytes.Length > SnapshotFormat.kMaxStringUtf8Bytes)
            {
                throw new InvalidDataException("UTF-8 string exceeds snapshot safety ceiling.");
            }

            SnapshotBinary.WriteVarUInt(stream, checked((uint)bytes.Length));
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}