using System;
using PO2.Core;
using PO2.Core.Deformation;
using PO2.Geometry;
using PO2.Persistence.Format;
using PO2.Persistence.Model;

namespace PO2.History
{
    // Reuse the strict mesh stream codec inside a separate history envelope.
    // History carries its own identities and exact custom orientation values.
    internal static class LifetimeModelCodec
    {
        internal static byte[] Encode(ProceduralObject obj)
        {
            var parts = new PersistencePartData[obj.Parts.Count];
            for (int i = 0; i < parts.Length; i++) parts[i] = PersistencePartData.Capture(obj.Parts[i]);
            var data = PersistenceObjectData.FromDecoded(obj.PersistentId, obj.Name, obj.IsLocked, obj.ReceivesDecals, obj.Transform, Array.Empty<PersistenceCustomOrientationData>(), parts, obj.LocalPivot, obj.LodProfile);
            data.Deformations = new DeformationCage[obj.Deformations.Count];
            for (int i = 0; i < data.Deformations.Length; i++) data.Deformations[i] = obj.Deformations[i].Copy(obj, false);
            return ObjectBlockWriter.Write(data);
        }

        internal static byte[] EncodePart(HistoryObjectKey owner, MeshPart part)
        {
            return ObjectBlockWriter.Write(PersistenceObjectData.FromDecoded(owner.PersistentId, "History Part", false, false, ObjectTransform.IdentityAt(UnityEngine.Vector3.zero), Array.Empty<PersistenceCustomOrientationData>(), new[] { PersistencePartData.Capture(part) }));
        }

        internal static ProceduralObject Decode(byte[] bytes, int runtimeId) =>
            ObjectBlockReader.Read(bytes, SnapshotFormat.kObjectSchemaVersion).Restore(runtimeId);

        internal static long Estimate(MeshPart part)
        {
            var mesh = part.Mesh;
            long bytes = 65536 + 4L * part.Name.Length + 96L * mesh.Vertices.Length + 8L * mesh.Triangles.Length + 32L * part.StructuralTopology.FaceCount;
            foreach (var area in part.SurfaceAreas)
            {
                bytes += 4096 + 4L * (area.Material.DefinitionId?.Length ?? 0) + (area.MappingSeed?.Bytes ?? 0);
                if (area.HasOriginalState) bytes += 4L * (area.OriginalState.CreateMaterialClone().DefinitionId?.Length ?? 0);
            }

            return bytes;
        }

        internal static long Estimate(ProceduralObject obj)
        {
            long bytes = 65536 + 4L * obj.Name.Length;
            foreach (var part in obj.Parts) bytes = checked(bytes + Estimate(part));
            foreach (var cage in obj.Deformations) bytes = checked(bytes + cage.RawBytes + cage.RawBytes / 100 + 65536);
            return bytes;
        }
    }
}