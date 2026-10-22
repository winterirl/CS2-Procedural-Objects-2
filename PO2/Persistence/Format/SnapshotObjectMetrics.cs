using System;
using System.IO;
using System.Text;
using PO2.Geometry.Topology;
using PO2.Materials;
using PO2.Persistence.Model;

namespace PO2.Persistence.Format
{
    // Computes a deterministic raw data footprint used as the  container directory's decoded-length guard.
    // This is not managed heap size and is not used to serialize object content.

    internal static class SnapshotObjectMetrics
    {
        internal static long ComputeDecodedFootprint(PersistenceObjectData data, ushort schemaVersion)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (!SnapshotFormat.IsSupportedObjectSchemaVersion(schemaVersion))
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            // Count the stable data model, not compression buffers or CLR object overhead.
            // Writer and reader can reproduce the same number exactly.
            long total = 0;
            total = Add(total, SnapshotFormat.kGuidByteCount);
            total = AddString(total, data.Name);
            total = Add(total, 1 + (10 * SnapshotFormat.kFloatByteCount));
            if (schemaVersion >= SnapshotFormat.kDecalReceptionObjectSchemaVersion)
            {
                total = Add(total, 1);
            }

            if (schemaVersion >= SnapshotFormat.kPivotObjectSchemaVersion)
            {
                total = Add(total, 3 * SnapshotFormat.kFloatByteCount);
            }

            total = Add(total, 4);
            for (int i = 0; i < data.CustomOrientations.Length; i++)
            {
                PersistenceCustomOrientationData orientation = data.CustomOrientations[i];
                if (orientation == null)
                {
                    throw new InvalidDataException("Persistence custom orientation is null.");
                }

                total = AddString(total, orientation.Name);
                total = Add(total, 4 * SnapshotFormat.kFloatByteCount);
            }

            if (schemaVersion >= SnapshotFormat.kLodProfileObjectSchemaVersion)
            {
                total = Add(total, LodProfileCodec.DecodedBytes(data.LodProfile));
            }

            total = Add(total, 4);

            for (int i = 0; i < data.Parts.Length; i++)
            {
                PersistencePartData part = data.Parts[i];
                if (part == null) throw new InvalidDataException("Persistence part is null.");

                total = AddString(total, part.Name);
                if (schemaVersion >= SnapshotFormat.kStructuralTopologyObjectSchemaVersion)
                {
                    total = Add(total, 1);
                }

                if (schemaVersion >= SnapshotFormat.kPivotObjectSchemaVersion)
                {
                    total = Add(total, 3 * SnapshotFormat.kFloatByteCount);
                }

                total = Add(total, 6 * 4L);
                total = AddArray(total, part.Vertices?.Length ?? 0, 12);
                total = AddArray(total, part.Normals?.Length ?? 0, 12);
                total = AddArray(total, part.Tangents?.Length ?? 0, 16);
                total = AddArray(total, part.UV0?.Length ?? 0, 16);
                total = AddArray(total, part.VertexColors?.Length ?? 0, 4);
                total = AddArray(total, part.TexCoord1?.Length ?? 0, 16);
                total = AddArray(total, part.TexCoord3?.Length ?? 0, 16);
                total = AddArray(total, part.Triangles?.Length ?? 0, 4);
                total = AddArray(total, part.RenderToLogical?.Length ?? 0, 4);
                total = AddArray(total, part.TriangleSurfaceAreaIds?.Length ?? 0, 4);

                if (part.StructuralFaces != null)
                {
                    for (int faceIndex = 0; faceIndex < part.StructuralFaces.Length; faceIndex++)
                    {
                        StructuralFace face = part.StructuralFaces[faceIndex];
                        total = Add(total, 5L + (face.CornerCount * 4L));
                    }
                }

                if (part.SurfaceAreas != null)
                {
                    for (int areaIndex = 0; areaIndex < part.SurfaceAreas.Length; areaIndex++)
                    {
                        total = AddSurfaceArea(total, part.SurfaceAreas[areaIndex], schemaVersion);
                    }
                }
            }

            if (schemaVersion >= SnapshotFormat.kDeformationObjectSchemaVersion)
            {
                total = Add(total, 4);
                foreach (var cage in data.Deformations) total = Add(total, cage.RawBytes);
            }

            if (total > SnapshotFormat.kMaxDecodedObjectBytes)
            {
                throw new InvalidDataException($"Decoded PO footprint {total} exceeds safety ceiling.");
            }

            return total;
        }

        private static long AddSurfaceArea(long total, PersistenceSurfaceAreaData area, ushort schemaVersion)
        {
            if (area == null) throw new InvalidDataException("Persistence Surface Area is null.");
            total = Add(total, 4 + 4 + 1 + 1);
            if (schemaVersion >= SnapshotFormat.kMappingSeedObjectSchemaVersion) total = Add(total, 1 + (area.MappingSeed?.Bytes ?? 0));
            total = AddMaterial(total, area.Material);
            total = AddColorMask(total, area.ColorMask);
            if (area.HasOriginalState)
            {
                total = AddMaterial(total, area.OriginalMaterial);
                total = AddColorMask(total, area.OriginalColorMask);
            }

            return total;
        }

        private static long AddMaterial(long total, PersistenceMaterialData material)
        {
            if (material == null) throw new InvalidDataException("Persistence material is null.");
            total = Add(total, 1 + 1 + (7 * 4L));
            switch (material.SourceKind)
            {
                case MaterialSourceKind.LibraryMaterial:
                    total = AddString(total, material.DefinitionId);
                    break;
                case MaterialSourceKind.SurfaceAsset:
                    total = Add(total, SnapshotFormat.kHash128ByteCount);
                    break;
                case MaterialSourceKind.VanillaSpecial:
                    total = Add(total, 1);
                    break;
            }

            return total;
        }

        private static long AddColorMask(long total, PersistenceColorMaskData mask)
        {
            if (mask == null) throw new InvalidDataException("Persistence color mask is null.");
            total = Add(total, 2);
            if (mask.HasSourceMask) total = Add(total, 3 * 4 * 4L);
            total = Add(total, 3 * 4 * 4L);
            return total;
        }

        private static long AddString(long total, string value)
        {
            int bytes = Encoding.UTF8.GetByteCount(value ?? string.Empty);
            if (bytes > SnapshotFormat.kMaxStringUtf8Bytes)
            {
                throw new InvalidDataException("Persistence string exceeds UTF-8 safety ceiling.");
            }

            return Add(total, 4L + bytes);
        }

        private static long AddArray(long total, int count, int elementSize)
        {
            if (count < 0) throw new InvalidDataException("Negative persistence array count.");
            return Add(total, checked((long)count * elementSize));
        }

        private static long Add(long total, long amount)
        {
            long result = checked(total + amount);
            if (result > SnapshotFormat.kMaxDecodedObjectBytes)
            {
                throw new InvalidDataException("Decoded PO footprint exceeds safety ceiling.");
            }

            return result;
        }
    }
}