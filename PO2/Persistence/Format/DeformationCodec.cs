using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PO2.Core;
using PO2.Core.Deformation;
using PO2.Localization;
using PO2.Persistence.Model;

namespace PO2.Persistence.Format
{
    // Schema 6 adds a bounded cage section after the existing part records.
    // Baseline vectors use the same lossless codecs as the visible mesh.
    internal static class DeformationCodec
    {
        internal static byte[] Encode(DeformationCage cage)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) Write(writer, cage);
                return stream.ToArray();
            }
        }

        internal static DeformationCage Decode(byte[] bytes, ProceduralObject obj)
        {
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                long remaining = SnapshotFormat.kMaxDecodedObjectBytes;
                DeformationCage cage = Read(reader, obj.Parts.Count, ref remaining);
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing cage history data.");
                foreach (var part in cage.Parts) part.PartId = obj.Parts[part.PartIndex].PartHistoryId;
                if (!cage.Matches(obj)) throw new InvalidDataException("Cage history streams no longer match.");
                return cage;
            }
        }

        internal static void Write(BinaryWriter writer, DeformationCage cage, EncodingContext context = null)
        {
            writer.Write(cage.Id.ToByteArray());
            writer.Write(cage.PartIndex);
            for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                writer.Write(cage.LocalFromReference[r, c]);
            foreach (Vector3 corner in cage.Corners) WriteVector(writer, corner);
            writer.Write(cage.Parts.Length);
            foreach (var part in cage.Parts)
            {
                writer.Write(part.PartIndex);
                WriteStream(writer, ObjectBlockCodec.EncodeVector3(SnapshotStreamSemantic.Positions, part.Positions, context));
                WriteStream(writer, ObjectBlockCodec.EncodeVector3(SnapshotStreamSemantic.Normals, part.Normals, context));
                WriteStream(writer, ObjectBlockCodec.EncodeVector4(SnapshotStreamSemantic.Tangents, part.Tangents, context));
            }
        }

        internal static DeformationCage Read(BinaryReader reader, int objectPartCount, ref long remaining)
        {
            Charge(ref remaining, 256);
            byte[] idBytes = Exact(reader, 16);
            Guid id = new Guid(idBytes);
            int scope = reader.ReadInt32();
            if (id == Guid.Empty || scope < -1 || scope >= objectPartCount) throw new InvalidDataException("Invalid cage identity or scope.");
            Matrix4x4 reference = default(Matrix4x4);
            for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                reference[r, c] = reader.ReadSingle();
            if (reference.m30 != 0f || reference.m31 != 0f || reference.m32 != 0f || reference.m33 != 1f)
            {
                throw new InvalidDataException("The reference cage must be affine.");
            }

            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++) corners[i] = ReadVector(reader);
            if (!DeformationRecipe.TryCreate(reference, corners, out _, out LocalizedText reason)) throw new InvalidDataException(reason.ToString());
            int count = reader.ReadInt32();
            if (count != (scope < 0 ? objectPartCount : 1) || count < 1) throw new InvalidDataException("Invalid cage part count.");
            var parts = new DeformationBaseline[count];
            Matrix4x4 inverse = reference.inverse;
            for (int i = 0; i < count; i++)
            {
                Charge(ref remaining, 64);
                int index = reader.ReadInt32();
                if (index != (scope < 0 ? i : scope)) throw new InvalidDataException("Invalid cage part slot.");
                var positionsStream = ReadStream(reader, SnapshotStreamSemantic.Positions, 12, ref remaining);
                var normalStream = ReadStream(reader, SnapshotStreamSemantic.Normals, 12, ref remaining);
                var tangentStream = ReadStream(reader, SnapshotStreamSemantic.Tangents, 16, ref remaining);
                if (positionsStream == null || positionsStream.ElementCount == 0 || (normalStream != null && normalStream.ElementCount != positionsStream.ElementCount) || (tangentStream != null && tangentStream.ElementCount != positionsStream.ElementCount))
                {
                    throw new InvalidDataException("Invalid cage baseline stream layout.");
                }

                Vector3[] positions = ObjectBlockCodec.DecodeVector3(positionsStream);
                Vector3[] normals = normalStream == null ? null : ObjectBlockCodec.DecodeVector3(normalStream);
                Vector4[] tangents = tangentStream == null ? null : ObjectBlockCodec.DecodeVector4(tangentStream);
                for (int v = 0; v < positions.Length; v++)
                {
                    Vector3 q = inverse.MultiplyPoint3x4(positions[v]);
                    if (!DeformationRecipe.Finite(positions[v]) || !DeformationRecipe.Finite(q) || q.x < -0.001f || q.y < -0.001f || q.z < -0.001f || q.x > 1.001f || q.y > 1.001f || q.z > 1.001f || (normals != null && !DeformationRecipe.Finite(normals[v])) || (tangents != null && (!DeformationRecipe.Finite(new Vector3(tangents[v].x, tangents[v].y, tangents[v].z)) || !DeformationRecipe.Finite(tangents[v].w)))) throw new InvalidDataException("Invalid cage baseline value.");
                }

                parts[i] = new DeformationBaseline(index, Guid.Empty, positions, normals, tangents);
            }

            return new DeformationCage(id, scope, reference, corners, parts);
        }

        internal static void Validate(DeformationCage[] cages, PersistencePartData[] parts)
        {
            var targets = new HashSet<int>();
            var identities = new HashSet<Guid>();
            foreach (var cage in cages)
            {
                if (!identities.Add(cage.Id) || !DeformationRecipe.TryCreate(cage.LocalFromReference, cage.Corners, out var recipe, out _)) throw new InvalidDataException("Invalid or repeated deformation cage.");
                foreach (var baseline in cage.Parts)
                {
                    if (baseline.PartIndex < 0 || baseline.PartIndex >= parts.Length || !targets.Add(baseline.PartIndex))
                    {
                        throw new InvalidDataException("Deformation cages overlap or reference a missing part.");
                    }

                    var part = parts[baseline.PartIndex];
                    if (baseline.Positions.Length != part.Vertices.Length || (baseline.Normals?.Length ?? -1) != (part.Normals?.Length ?? -1) || (baseline.Tangents?.Length ?? -1) != (part.Tangents?.Length ?? -1)) throw new InvalidDataException("Baseline and mesh layouts differ.");
                    for (int i = 0; i < baseline.Positions.Length; i++)
                    {
                        Vector3 evaluated = recipe.Position(baseline.Positions[i]);
                        double magnitudeSquared = (double)evaluated.x * evaluated.x + (double)evaluated.y * evaluated.y + (double)evaluated.z * evaluated.z;
                        double toleranceSquared = 1e-10 * Math.Max(1d, magnitudeSquared);
                        double dx = (double)evaluated.x - part.Vertices[i].x;
                        double dy = (double)evaluated.y - part.Vertices[i].y;
                        double dz = (double)evaluated.z - part.Vertices[i].z;
                        if (!DeformationRecipe.Finite(evaluated) || !DeformationRecipe.Finite(part.Vertices[i]) || dx * dx + dy * dy + dz * dz > toleranceSquared)
                        {
                            throw new InvalidDataException("The saved cage does not match the visible mesh.");
                        }
                    }
                }
            }
        }

        private static void WriteStream(BinaryWriter writer, EncodedStream value)
        {
            writer.Write(value != null);
            if (value == null) return;
            writer.Write((byte)value.Codec);
            writer.Write(value.ElementCount);
            writer.Write(value.DecodedByteLength);
            writer.Write(value.Payload.Length);
            writer.Write(value.Payload.Array, value.Payload.Offset, value.Payload.Length);
        }

        private static EncodedStream ReadStream(BinaryReader reader, SnapshotStreamSemantic semantic, int stride, ref long remaining)
        {
            byte present = reader.ReadByte();
            if (present == 0) return null;
            if (present != 1) throw new InvalidDataException("Invalid baseline stream flag.");
            SnapshotCodec codec = (SnapshotCodec)reader.ReadByte();
            if (codec != SnapshotCodec.Raw && codec != SnapshotCodec.Deflate && codec != SnapshotCodec.ByteShuffleDeflate)
            {
                throw new InvalidDataException("Invalid baseline vector codec.");
            }

            int count = reader.ReadInt32(), decoded = reader.ReadInt32(), stored = reader.ReadInt32();
            if (count < 0 || count > SnapshotFormat.kMaxRenderVerticesPerPart || decoded != checked(count * stride) || stored < 0 || stored > decoded + 65536L || stored > reader.BaseStream.Length - reader.BaseStream.Position)
            {
                throw new InvalidDataException("Invalid baseline stream size.");
            }

            Charge(ref remaining, decoded);
            return new EncodedStream(semantic, codec, count, decoded, Exact(reader, stored));
        }

        private static void Charge(ref long remaining, long bytes)
        {
            remaining -= bytes;
            if (remaining < 0) throw new InvalidDataException("Cage baseline exceeds the decoded size limit.");
        }

        private static byte[] Exact(BinaryReader reader, int count)
        {
            byte[] result = reader.ReadBytes(count);
            if (result.Length != count) throw new EndOfStreamException();
            return result;
        }

        private static void WriteVector(BinaryWriter writer, Vector3 v)
        {
            writer.Write(v.x);
            writer.Write(v.y);
            writer.Write(v.z);
        }

        private static Vector3 ReadVector(BinaryReader reader) => new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }
}