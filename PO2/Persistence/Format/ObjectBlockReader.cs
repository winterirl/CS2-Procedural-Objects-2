using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using PO2.Core;
using PO2.Core.Deformation;
using PO2.Core.Lod;
using PO2.Geometry.Topology;
using PO2.Materials;
using PO2.Persistence.Model;
using AssetHash128 = Colossal.Hash128;

namespace PO2.Persistence.Format
{
    // Strict decoder for one independently checksummed PO object block.
    // Decoding produces persistence DTOs only and never mutates live runtime state.
    internal static class ObjectBlockReader
    {
        private static readonly byte[] s_Magic = { (byte)'P', (byte)'O', (byte)'2', (byte)'O' };

        internal sealed class PartMetadata
        {
            public string Name;
            public StructuralTopologySource? StructuralTopologySource;
            public Vector3? LocalPivot;
            public int NextSurfaceAreaId;
            public PersistenceSurfaceAreaData[] SurfaceAreas;
        }

        private sealed class ObjectMetadata
        {
            public string Name;
            public bool IsLocked;
            public bool ReceivesDecals;
            public ObjectTransform Transform;
            public Vector3 LocalPivot;
            public LodProfile LodProfile;
            public PersistenceCustomOrientationData[] CustomOrientations;
            public PartMetadata[] Parts;
        }

        private sealed class StreamDescriptor
        {
            public SnapshotStreamSemantic Semantic;
            public SnapshotCodec Codec;
            public int ElementCount;
            public int DecodedByteLength;
            public int StoredByteLength;
            public int PayloadOffset;
        }

        internal static PersistenceObjectData Read(byte[] bytes, ushort expectedSchemaVersion)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using (MemoryStream stream = new MemoryStream(bytes, writable: false))
            {
                return Read(stream, expectedSchemaVersion, bytes);
            }
        }

        private static PersistenceObjectData Read(Stream stream, ushort expectedSchemaVersion, byte[] bytes)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            byte[] magic = SnapshotBinary.ReadExactBytes(stream, s_Magic.Length);
            for (int i = 0; i < s_Magic.Length; i++)
            {
                if (magic[i] != s_Magic[i])
                {
                    throw new InvalidDataException("Invalid PO2 object-block magic.");
                }
            }

            ushort schemaVersion = SnapshotBinary.ReadUInt16(stream);
            if (!SnapshotFormat.IsSupportedObjectSchemaVersion(schemaVersion))
            {
                throw new InvalidDataException($"Unsupported PO2 object schema version {schemaVersion}; this build supports versions {SnapshotFormat.kLegacyObjectSchemaVersion} through {SnapshotFormat.kObjectSchemaVersion}.");
            }

            if (schemaVersion != expectedSchemaVersion)
            {
                throw new InvalidDataException($"Object block schema version {schemaVersion} does not match directory version {expectedSchemaVersion}.");
            }

            ushort flags = SnapshotBinary.ReadUInt16(stream);
            if (flags != 0) throw new InvalidDataException("PO2 object block contains unknown flags.");

            uint bodyLengthRaw = SnapshotBinary.ReadUInt32(stream);
            uint expectedCrc = SnapshotBinary.ReadUInt32(stream);
            if (bodyLengthRaw > SnapshotFormat.kMaxStoredObjectBytes || bodyLengthRaw > int.MaxValue)
            {
                throw new InvalidDataException("PO2 object block exceeds supported stored-size ceiling.");
            }

            // Keep the bounded body view, without copying the entire object.
            int bodyOffset = checked((int)stream.Position);
            int bodyLength = checked((int)bodyLengthRaw);
            if (bodyLength != bytes.Length - bodyOffset)
            {
                throw new InvalidDataException("PO2 object body length does not match its block.");
            }

            uint actualCrc = Crc32.Compute(bytes, bodyOffset, bodyLength);
            if (actualCrc != expectedCrc)
            {
                throw new InvalidDataException($"PO2 object-block checksum mismatch: expected {expectedCrc:x8}, got {actualCrc:x8}.");
            }

            using (MemoryStream bodyStream = new MemoryStream(bytes, bodyOffset, bodyLength, writable: false))
            {
                Guid persistentId = SnapshotBinary.ReadGuid(bodyStream);
                if (persistentId == Guid.Empty)
                {
                    throw new InvalidDataException("PO2 object block contains an empty persistent ID.");
                }

                ObjectMetadata metadata = ReadMetadata(bodyStream, schemaVersion);
                int partCount = ReadCount(bodyStream, SnapshotFormat.kMaxPartsPerObject, "Part");
                if (partCount != metadata.Parts.Length)
                {
                    throw new InvalidDataException("Object metadata part count does not match binary part record count.");
                }

                PersistencePartData[] parts = new PersistencePartData[partCount];
                for (int i = 0; i < partCount; i++)
                {
                    parts[i] = ReadPart(bodyStream, metadata.Parts[i], schemaVersion);
                }

                var data = PersistenceObjectData.FromDecoded(persistentId, metadata.Name, metadata.IsLocked, metadata.ReceivesDecals, metadata.Transform, metadata.CustomOrientations, parts, metadata.LocalPivot, metadata.LodProfile);
                if (schemaVersion >= SnapshotFormat.kDeformationObjectSchemaVersion)
                {
                    using (var reader = new BinaryReader(bodyStream, System.Text.Encoding.UTF8, true))
                    {
                        int count = reader.ReadInt32();
                        if (count < 0 || count > partCount) throw new InvalidDataException("Invalid deformation count.");
                        long remaining = SnapshotFormat.kMaxDecodedObjectBytes - SnapshotObjectMetrics.ComputeDecodedFootprint(data, schemaVersion);
                        data.Deformations = new DeformationCage[count];
                        for (int i = 0; i < count; i++) data.Deformations[i] = DeformationCodec.Read(reader, partCount, ref remaining);
                    }

                    DeformationCodec.Validate(data.Deformations, parts);
                }

                if (bodyStream.Position != bodyStream.Length) throw new InvalidDataException("Unconsumed object body data.");
                return data;
            }
        }

        private static ObjectMetadata ReadMetadata(Stream stream, ushort schemaVersion)
        {
            SnapshotCodec codec = SnapshotValidation.ValidateCodec((byte)ReadByte(stream));
            if (codec != SnapshotCodec.Raw && codec != SnapshotCodec.Deflate)
            {
                throw new InvalidDataException($"Codec {codec} is not valid for object metadata.");
            }

            int decodedLength = ReadLength(stream, "Metadata decoded");
            int storedLength = ReadLength(stream, "Metadata stored");
            // Metadata is decoded before parts so names/materials are available
            // when the matching mesh streams are reconstructed.
            byte[] stored = SnapshotBinary.ReadExactBytes(stream, storedLength);
            byte[] raw = codec == SnapshotCodec.Raw ? stored : SnapshotBinary.Inflate(stored, decodedLength);
            if (raw.Length != decodedLength)
            {
                throw new InvalidDataException("Metadata decoded length mismatch.");
            }

            using (MemoryStream metadata = new MemoryStream(raw, writable: false))
            {
                ObjectMetadata result = new ObjectMetadata();
                result.Name = ReadString(metadata);
                result.IsLocked = ReadBool(metadata, "Object lock flag");
                result.ReceivesDecals = schemaVersion >= SnapshotFormat.kDecalReceptionObjectSchemaVersion && ReadBool(metadata, "Object decal reception flag");
                result.Transform = ReadTransform(metadata);
                result.LocalPivot = schemaVersion >= SnapshotFormat.kPivotObjectSchemaVersion ? ReadVector3(metadata) : Vector3.zero;
                result.CustomOrientations = ReadCustomOrientations(metadata);
                if (schemaVersion >= SnapshotFormat.kLodProfileObjectSchemaVersion)
                {
                    result.LodProfile = LodProfileCodec.Read(metadata);
                }

                int partCount = ReadVarCount(metadata, SnapshotFormat.kMaxPartsPerObject, "Metadata part");
                result.Parts = new PartMetadata[partCount];
                for (int i = 0; i < partCount; i++)
                {
                    result.Parts[i] = ReadPartMetadata(metadata, schemaVersion);
                }

                if (metadata.Position != metadata.Length)
                {
                    throw new InvalidDataException("Object metadata contains trailing bytes.");
                }

                return result;
            }
        }

        private static PersistenceCustomOrientationData[] ReadCustomOrientations(Stream stream)
        {
            int count = ReadVarCount(stream, SnapshotFormat.kMaxCustomOrientationsPerObject, "Custom orientation");
            PersistenceCustomOrientationData[] orientations = new PersistenceCustomOrientationData[count];

            for (int i = 0; i < orientations.Length; i++)
            {
                string name = ReadString(stream, SnapshotFormat.kMaxCustomOrientationNameUtf8Bytes);
                Quaternion localRotation = new Quaternion(SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream));
                orientations[i] = PersistenceCustomOrientationData.FromDecoded(name, localRotation);
            }

            return orientations;
        }

        internal static PartMetadata ReadPartMetadata(Stream stream, ushort schemaVersion)
        {
            PartMetadata result = new PartMetadata();
            result.Name = ReadString(stream);
            if (schemaVersion >= SnapshotFormat.kStructuralTopologyObjectSchemaVersion)
            {
                result.StructuralTopologySource = SnapshotValidation.ValidateStructuralTopologySource((byte)ReadByte(stream));
            }

            if (schemaVersion >= SnapshotFormat.kPivotObjectSchemaVersion)
            {
                result.LocalPivot = ReadVector3(stream);
            }

            result.NextSurfaceAreaId = ReadVarInt32(stream, "Next Surface Area ID");
            int areaCount = ReadVarCount(stream, SnapshotFormat.kMaxSurfaceAreasPerPart, "Surface Area");
            result.SurfaceAreas = new PersistenceSurfaceAreaData[areaCount];
            HashSet<int> areaIds = new HashSet<int>();
            int maxAreaId = -1;
            for (int i = 0; i < areaCount; i++)
            {
                int id = ReadVarInt32(stream, "Surface Area ID");
                if (!areaIds.Add(id))
                {
                    throw new InvalidDataException($"Duplicate Surface Area ID {id}.");
                }

                if (id > maxAreaId) maxAreaId = id;
                VertexAttributeFlags attributes = SnapshotValidation.ValidateVertexAttributeFlags(SnapshotBinary.ReadUInt32(stream));
                if (schemaVersion < SnapshotFormat.kTexCoord1ObjectSchemaVersion && (attributes & VertexAttributeFlags.TexCoord1) != 0)
                    throw new InvalidDataException("TexCoord1 requires object schema 7.");
                if (schemaVersion < SnapshotFormat.kSurfaceUVObjectSchemaVersion && (attributes & VertexAttributeFlags.SurfaceUVCoordinates) != 0)
                    throw new InvalidDataException("Surface UV coordinates require object schema 8.");
                NativeWindowKind windowKind = SnapshotValidation.ValidateNativeWindowKind((byte)ReadByte(stream));
                bool hasOriginal = ReadBool(stream, "OriginalState flag");
                PersistenceMaterialData material = ReadMaterial(stream);
                PersistenceColorMaskData mask = ReadColorMask(stream);
                PersistenceMaterialData originalMaterial = null;
                PersistenceColorMaskData originalMask = null;
                if (hasOriginal)
                {
                    originalMaterial = ReadMaterial(stream);
                    originalMask = ReadColorMask(stream);
                }

                result.SurfaceAreas[i] = PersistenceSurfaceAreaData.FromDecoded(id, attributes, windowKind, material, mask, originalMaterial, originalMask, schemaVersion >= SnapshotFormat.kMappingSeedObjectSchemaVersion ? GeneratedMappingSeedCodec.Read(stream) : null);
            }

            if (!areaIds.Contains(0))
            {
                throw new InvalidDataException("Mesh part is missing required Surface Area 0.");
            }

            if (result.NextSurfaceAreaId <= maxAreaId)
            {
                throw new InvalidDataException($"Next Surface Area ID {result.NextSurfaceAreaId} must exceed maximum existing ID {maxAreaId}.");
            }

            return result;
        }

        private static PersistencePartData ReadPart(Stream stream, PartMetadata metadata, ushort schemaVersion)
        {
            int renderVertexCount = ReadCount(stream, SnapshotFormat.kMaxRenderVerticesPerPart, "Render vertex");
            int logicalVertexCount = ReadCount(stream, SnapshotFormat.kMaxLogicalVerticesPerPart, "Logical vertex");
            int triangleCount = ReadCount(stream, SnapshotFormat.kMaxTrianglesPerPart, "Triangle");
            int structuralFaceCount = ReadCount(stream, SnapshotFormat.kMaxStructuralFacesPerPart, "Structural face");
            int areaCount = ReadCount(stream, SnapshotFormat.kMaxSurfaceAreasPerPart, "Surface Area");
            if (areaCount != metadata.SurfaceAreas.Length)
            {
                throw new InvalidDataException("Part Surface Area count does not match metadata.");
            }

            int streamCount = SnapshotBinary.ReadUInt16(stream);
            ushort flags = SnapshotBinary.ReadUInt16(stream);
            if (flags != 0) throw new InvalidDataException("Part record contains unknown flags.");
            if (streamCount < 6 || streamCount > (schemaVersion >= SnapshotFormat.kTexCoord1ObjectSchemaVersion ? 13 : 12))
            {
                throw new InvalidDataException($"Unexpected part stream count {streamCount}.");
            }

            // Validate the descriptor table as a whole before allocating decoded mesh arrays.
            // Payloads must be unique, contiguous, and bounded.
            StreamDescriptor[] descriptors = new StreamDescriptor[streamCount];
            HashSet<SnapshotStreamSemantic> semantics = new HashSet<SnapshotStreamSemantic>();
            int payloadLength = 0;
            for (int i = 0; i < streamCount; i++)
            {
                SnapshotStreamSemantic semantic = SnapshotValidation.ValidateStreamSemantic((byte)ReadByte(stream));
                if (semantic == SnapshotStreamSemantic.TexCoord1 && schemaVersion < SnapshotFormat.kTexCoord1ObjectSchemaVersion)
                    throw new InvalidDataException("TexCoord1 requires object schema 7.");
                SnapshotCodec codec = SnapshotValidation.ValidateCodec((byte)ReadByte(stream));
                ushort descriptorFlags = SnapshotBinary.ReadUInt16(stream);
                if (descriptorFlags != 0)
                {
                    throw new InvalidDataException("Stream descriptor contains unknown flags.");
                }

                if (!semantics.Add(semantic))
                {
                    throw new InvalidDataException($"Duplicate stream semantic {semantic}.");
                }

                int elementCount = ReadLength(stream, $"{semantic} element");
                int decodedLength = ReadLength(stream, $"{semantic} decoded");
                int storedLength = ReadLength(stream, $"{semantic} stored");
                int offset = ReadLength(stream, $"{semantic} payload offset");
                if (offset != payloadLength)
                {
                    throw new InvalidDataException($"Stream {semantic} payload offset {offset} is not contiguous expected offset {payloadLength}.");
                }

                payloadLength = checked(payloadLength + storedLength);

                descriptors[i] = new StreamDescriptor
                {
                    Semantic = semantic,
                    Codec = codec,
                    ElementCount = elementCount,
                    DecodedByteLength = decodedLength,
                    StoredByteLength = storedLength,
                    PayloadOffset = offset,
                };
            }

            if (payloadLength > stream.Length - stream.Position)
                throw new EndOfStreamException("Part payload extends beyond the object body.");
            Dictionary<SnapshotStreamSemantic, EncodedStream> decodedStreams = new Dictionary<SnapshotStreamSemantic, EncodedStream>();
            for (int i = 0; i < descriptors.Length; i++)
            {
                StreamDescriptor descriptor = descriptors[i];
                byte[] payload = SnapshotBinary.ReadExactBytes(stream, descriptor.StoredByteLength);
                decodedStreams.Add(descriptor.Semantic, new EncodedStream(descriptor.Semantic, descriptor.Codec, descriptor.ElementCount, descriptor.DecodedByteLength, payload));
            }

            // Optional render streams may be absent.
            // These streams are required because geometry and editable topology cannot exist without them.
            RequireStream(decodedStreams, SnapshotStreamSemantic.Positions);
            RequireStream(decodedStreams, SnapshotStreamSemantic.Triangles);
            RequireStream(decodedStreams, SnapshotStreamSemantic.RenderToLogical);
            RequireStream(decodedStreams, SnapshotStreamSemantic.TriangleSurfaceAreaIds);
            RequireStream(decodedStreams, SnapshotStreamSemantic.StructuralFaceIds);
            RequireStream(decodedStreams, SnapshotStreamSemantic.StructuralFaceCornerCounts);
            RequireStream(decodedStreams, SnapshotStreamSemantic.StructuralFaceCorners);

            Vector3[] vertices = ObjectBlockCodec.DecodeVector3(decodedStreams[SnapshotStreamSemantic.Positions]);
            if (vertices.Length != renderVertexCount)
            {
                throw new InvalidDataException("Position count does not match render vertex count.");
            }

            Vector3[] normals = DecodeOptionalVector3(decodedStreams, SnapshotStreamSemantic.Normals, renderVertexCount);
            Vector4[] tangents = DecodeOptionalVector4(decodedStreams, SnapshotStreamSemantic.Tangents, renderVertexCount);
            Vector4[] uv0 = DecodeOptionalVector4(decodedStreams, SnapshotStreamSemantic.UV0, renderVertexCount);
            if (uv0 == null)
            {
                foreach (var area in metadata.SurfaceAreas)
                {
                    if ((area.SourceVertexAttributes & VertexAttributeFlags.SurfaceUVCoordinates) != 0)
                        throw new InvalidDataException("A Surface Area requires the missing UV0 surface coordinates.");
                }
            }

            Color32[] colors = DecodeOptionalColors(decodedStreams, renderVertexCount);
            Vector4[] texCoord1 = DecodeOptionalVector4(decodedStreams, SnapshotStreamSemantic.TexCoord1, renderVertexCount);
            if (texCoord1 == null)
            {
                foreach (var area in metadata.SurfaceAreas)
                {
                    if ((area.SourceVertexAttributes & VertexAttributeFlags.TexCoord1) != 0)
                        throw new InvalidDataException("A Surface Area requires the missing TexCoord1 stream.");
                }
            }

            Vector4[] texCoord3 = DecodeOptionalVector4(decodedStreams, SnapshotStreamSemantic.TexCoord3, renderVertexCount);

            int[] triangles = ObjectBlockCodec.DecodeInteger(decodedStreams[SnapshotStreamSemantic.Triangles]);
            if (triangles.Length != checked(triangleCount * 3))
            {
                throw new InvalidDataException("Triangle index count does not match triangle count.");
            }

            for (int i = 0; i < triangles.Length; i++)
            {
                if (triangles[i] < 0 || triangles[i] >= renderVertexCount)
                {
                    throw new InvalidDataException($"Triangle index {triangles[i]} is outside render vertex range.");
                }
            }

            int[] renderToLogical = ObjectBlockCodec.DecodeInteger(decodedStreams[SnapshotStreamSemantic.RenderToLogical]);
            if (renderToLogical.Length != renderVertexCount)
            {
                throw new InvalidDataException("Render-to-logical count does not match render vertex count.");
            }

            // Reject orphan logical IDs.
            // Restoring them would create a topology that no render vertex can ever select or move.
            bool[] logicalSeen = new bool[logicalVertexCount];
            for (int i = 0; i < renderToLogical.Length; i++)
            {
                int logicalId = renderToLogical[i];
                if (logicalId < 0 || logicalId >= logicalVertexCount)
                {
                    throw new InvalidDataException($"Logical vertex ID {logicalId} is out of range.");
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

            int[] memberships = ObjectBlockCodec.DecodeInteger(decodedStreams[SnapshotStreamSemantic.TriangleSurfaceAreaIds]);
            if (memberships.Length != triangleCount)
            {
                throw new InvalidDataException("Triangle Surface Area membership count mismatch.");
            }

            HashSet<int> areaIds = new HashSet<int>();
            for (int i = 0; i < metadata.SurfaceAreas.Length; i++) areaIds.Add(metadata.SurfaceAreas[i].Id);
            for (int i = 0; i < memberships.Length; i++)
            {
                if (!areaIds.Contains(memberships[i]))
                {
                    throw new InvalidDataException($"Triangle membership references nonexistent Surface Area {memberships[i]}.");
                }
            }

            int[] faceIds = ObjectBlockCodec.DecodeInteger(decodedStreams[SnapshotStreamSemantic.StructuralFaceIds]);
            byte[] cornerCounts = ObjectBlockCodec.DecodeByteStream(decodedStreams[SnapshotStreamSemantic.StructuralFaceCornerCounts]);
            int[] corners = ObjectBlockCodec.DecodeInteger(decodedStreams[SnapshotStreamSemantic.StructuralFaceCorners]);
            if (faceIds.Length != structuralFaceCount || cornerCounts.Length != structuralFaceCount)
            {
                throw new InvalidDataException("Structural face stream counts do not match declared face count.");
            }

            HashSet<int> faceIdSet = new HashSet<int>();
            int expectedCornerCount = 0;
            for (int i = 0; i < structuralFaceCount; i++)
            {
                if (!faceIdSet.Add(faceIds[i]))
                {
                    throw new InvalidDataException($"Duplicate structural face ID {faceIds[i]}.");
                }

                if (cornerCounts[i] != 3 && cornerCounts[i] != 4)
                {
                    throw new InvalidDataException($"Structural face corner count {cornerCounts[i]} is not supported.");
                }

                expectedCornerCount = checked(expectedCornerCount + cornerCounts[i]);
            }

            if (corners.Length != expectedCornerCount)
            {
                throw new InvalidDataException("Structural corner stream length mismatch.");
            }

            StructuralFace[] faces = new StructuralFace[structuralFaceCount];
            int cornerCursor = 0;
            for (int i = 0; i < faces.Length; i++)
            {
                int count = cornerCounts[i];
                for (int c = 0; c < count; c++)
                {
                    int vertex = corners[cornerCursor + c];
                    if (vertex < 0 || vertex >= renderVertexCount)
                    {
                        throw new InvalidDataException($"Structural face corner index {vertex} is outside render vertex range.");
                    }
                }

                if (count == 3)
                {
                    faces[i] = new StructuralFace(faceIds[i], corners[cornerCursor], corners[cornerCursor + 1], corners[cornerCursor + 2]);
                }
                else
                {
                    faces[i] = new StructuralFace(faceIds[i], corners[cornerCursor], corners[cornerCursor + 1], corners[cornerCursor + 2], corners[cornerCursor + 3]);
                }

                cornerCursor += count;
            }

            return PersistencePartData.FromDecoded(metadata.Name, vertices, normals, tangents, uv0, colors, texCoord1, texCoord3, triangles, renderToLogical, logicalVertexCount, faces, metadata.StructuralTopologySource, metadata.SurfaceAreas, memberships, metadata.NextSurfaceAreaId, metadata.LocalPivot);
        }

        private static PersistenceMaterialData ReadMaterial(Stream stream)
        {
            MaterialSourceKind sourceKind = SnapshotValidation.ValidateMaterialSourceKind((byte)ReadByte(stream));
            string definitionId = null;
            AssetHash128 surfaceGuid = default;
            VanillaSpecialMaterialKind specialKind = VanillaSpecialMaterialKind.None;
            switch (sourceKind)
            {
                case MaterialSourceKind.LibraryMaterial:
                    definitionId = ReadString(stream);
                    break;
                case MaterialSourceKind.SurfaceAsset:
                    surfaceGuid = SnapshotBinary.ReadHash128(stream);
                    break;
                case MaterialSourceKind.VanillaSpecial:
                    specialKind = SnapshotValidation.ValidateVanillaSpecialKind((byte)ReadByte(stream));
                    break;
            }

            SnapshotValidation.ValidateMaterialPayload(sourceKind, definitionId, specialKind);
            TextureMappingMode mappingMode = SnapshotValidation.ValidateMappingMode((byte)ReadByte(stream));
            Vector2 scale = new Vector2(SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream));
            float rotation = SnapshotBinary.ReadSingle(stream);
            Vector2 offset = new Vector2(SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream));
            float metallic = SnapshotBinary.ReadSingle(stream);
            float smoothness = SnapshotBinary.ReadSingle(stream);
            float coat = SnapshotBinary.ReadSingle(stream);
            return PersistenceMaterialData.FromDecoded(sourceKind, definitionId, surfaceGuid, specialKind, mappingMode, scale, rotation, offset, metallic, smoothness, coat);
        }

        private static PersistenceColorMaskData ReadColorMask(Stream stream)
        {
            ColorMaskMode mode = SnapshotValidation.ValidateColorMaskMode((byte)ReadByte(stream));
            bool hasSource = ReadBool(stream, "Color-mask source flag");
            // Runtime color-mask state canonicalizes (canonizes? what's the word?) missing source masks to white.
            // The binary writer omits source colors when HasSourceMask is false, so
            // restore the canonical values rather than default(Color).
            Color source1 = Color.white;
            Color source2 = Color.white;
            Color source3 = Color.white;
            if (hasSource)
            {
                source1 = ReadColor(stream);
                source2 = ReadColor(stream);
                source3 = ReadColor(stream);
            }

            Color override1 = ReadColor(stream);
            Color override2 = ReadColor(stream);
            Color override3 = ReadColor(stream);
            if (mode == ColorMaskMode.Source && !hasSource)
            {
                throw new InvalidDataException("Source color-mask mode requires source mask data.");
            }

            return PersistenceColorMaskData.FromDecoded(mode, hasSource, source1, source2, source3, override1, override2, override3);
        }

        private static ObjectTransform ReadTransform(Stream stream)
        {
            Vector3 position = new Vector3(SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream));
            Quaternion rotation = new Quaternion(SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream));
            Vector3 scale = new Vector3(SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream));
            return new ObjectTransform(position, rotation, scale);
        }

        private static Vector3 ReadVector3(Stream stream)
        {
            Vector3 value = new Vector3(SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream));

            if (float.IsNaN(value.x) || float.IsInfinity(value.x) || float.IsNaN(value.y) || float.IsInfinity(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.z))
            {
                throw new InvalidDataException("PO2 metadata contains a non-finite pivot.");
            }

            return value;
        }

        private static Color ReadColor(Stream stream)
        {
            return new Color(SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream), SnapshotBinary.ReadSingle(stream));
        }

        private static Vector3[] DecodeOptionalVector3(Dictionary<SnapshotStreamSemantic, EncodedStream> streams, SnapshotStreamSemantic semantic, int expectedCount)
        {
            if (!streams.TryGetValue(semantic, out EncodedStream encoded)) return null;
            Vector3[] result = ObjectBlockCodec.DecodeVector3(encoded);
            if (result.Length != expectedCount)
            {
                throw new InvalidDataException($"{semantic} count does not match render vertex count.");
            }

            return result;
        }

        private static Vector4[] DecodeOptionalVector4(Dictionary<SnapshotStreamSemantic, EncodedStream> streams, SnapshotStreamSemantic semantic, int expectedCount)
        {
            if (!streams.TryGetValue(semantic, out EncodedStream encoded)) return null;
            Vector4[] result = ObjectBlockCodec.DecodeVector4(encoded);
            if (result.Length != expectedCount)
            {
                throw new InvalidDataException($"{semantic} count does not match render vertex count.");
            }

            return result;
        }

        private static Color32[] DecodeOptionalColors(Dictionary<SnapshotStreamSemantic, EncodedStream> streams, int expectedCount)
        {
            if (!streams.TryGetValue(SnapshotStreamSemantic.VertexColors, out EncodedStream encoded)) return null;
            Color32[] result = ObjectBlockCodec.DecodeColors(encoded);
            if (result.Length != expectedCount)
            {
                throw new InvalidDataException("Vertex color count does not match render vertex count.");
            }

            return result;
        }

        private static void RequireStream(Dictionary<SnapshotStreamSemantic, EncodedStream> streams, SnapshotStreamSemantic semantic)
        {
            if (!streams.ContainsKey(semantic))
            {
                throw new InvalidDataException($"Required stream {semantic} is missing.");
            }
        }

        private static string ReadString(Stream stream)
        {
            return ReadString(stream, SnapshotFormat.kMaxStringUtf8Bytes);
        }

        private static string ReadString(Stream stream, int maximumUtf8Bytes)
        {
            uint lengthRaw = SnapshotBinary.ReadVarUInt(stream);
            if (lengthRaw > maximumUtf8Bytes || lengthRaw > int.MaxValue)
            {
                throw new InvalidDataException("UTF-8 string exceeds snapshot safety ceiling.");
            }

            byte[] bytes = SnapshotBinary.ReadExactBytes(stream, checked((int)lengthRaw));
            return Encoding.UTF8.GetString(bytes);
        }

        private static bool ReadBool(Stream stream, string label)
        {
            int raw = ReadByte(stream);
            if (raw == 0) return false;
            if (raw == 1) return true;
            throw new InvalidDataException($"{label} has invalid value {raw}.");
        }

        private static int ReadCount(Stream stream, int maximum, string label)
        {
            uint raw = SnapshotBinary.ReadUInt32(stream);
            if (raw > int.MaxValue) throw new InvalidDataException($"{label} count exceeds Int32 range.");
            int value = (int)raw;
            SnapshotValidation.ValidateCount(value, maximum, label);
            return value;
        }

        private static int ReadVarCount(Stream stream, int maximum, string label)
        {
            uint raw = SnapshotBinary.ReadVarUInt(stream);
            if (raw > int.MaxValue) throw new InvalidDataException($"{label} count exceeds Int32 range.");
            int value = (int)raw;
            SnapshotValidation.ValidateCount(value, maximum, label);
            return value;
        }

        private static int ReadVarInt32(Stream stream, string label)
        {
            uint raw = SnapshotBinary.ReadVarUInt(stream);
            if (raw > int.MaxValue) throw new InvalidDataException($"{label} exceeds Int32 range.");
            return (int)raw;
        }

        private static int ReadLength(Stream stream, string label)
        {
            uint raw = SnapshotBinary.ReadUInt32(stream);
            if (raw > int.MaxValue) throw new InvalidDataException($"{label} length exceeds Int32 range.");
            return (int)raw;
        }

        private static int ReadByte(Stream stream)
        {
            int value = stream.ReadByte();
            if (value < 0) throw new EndOfStreamException("Unexpected end of PO2 object block.");
            return value;
        }
    }
}