using System;
using UnityEngine;
using PO2.Core;
using PO2.Core.Operations;
using PO2.Geometry;

namespace PO2.Core.Deformation
{
    // Baseline arrays are never published as live mesh arrays.
    // Corner edits share them ~ only creation, loading and duplication copy the streams.
    internal sealed class DeformationBaseline
    {
        internal readonly int PartIndex;
        internal readonly Vector3[] Positions;
        internal readonly Vector3[] Normals;
        internal readonly Vector4[] Tangents;
        internal Guid PartId;
        internal long RawBytes => 12L * (Positions.Length + (Normals?.Length ?? 0)) + 16L * (Tangents?.Length ?? 0);

        internal DeformationBaseline(int index, Guid partId, Vector3[] positions, Vector3[] normals, Vector4[] tangents)
        {
            PartIndex = index;
            PartId = partId;
            Positions = positions;
            Normals = normals;
            Tangents = tangents;
        }

        internal DeformationBaseline Copy(Guid partId) => new DeformationBaseline(PartIndex, partId, (Vector3[])Positions.Clone(), Normals == null ? null : (Vector3[])Normals.Clone(), Tangents == null ? null : (Vector4[])Tangents.Clone());

        internal bool Matches(ProceduralObject obj)
        {
            if (PartIndex < 0 || PartIndex >= obj.Parts.Count) return false;
            MeshPart part = obj.Parts[PartIndex];
            return part.PartHistoryId == PartId && part.Mesh.Vertices.Length == Positions.Length && (part.Mesh.Normals?.Length ?? -1) == (Normals?.Length ?? -1) && (part.Mesh.Tangents?.Length ?? -1) == (Tangents?.Length ?? -1);
        }
    }

    internal sealed class DeformationCage
    {
        internal readonly Guid Id;
        internal readonly int PartIndex;
        internal readonly Matrix4x4 LocalFromReference;
        internal readonly Vector3[] Corners;
        internal readonly DeformationBaseline[] Parts;

        internal long RawBytes
        {
            get
            {
                long bytes = 256;
                foreach (var part in Parts) bytes = checked(bytes + 64 + part.RawBytes);
                return bytes;
            }
        }

        internal DeformationCage(Guid id, int partIndex, Matrix4x4 reference, Vector3[] corners, DeformationBaseline[] parts)
        {
            Id = id;
            PartIndex = partIndex;
            LocalFromReference = reference;
            Corners = (Vector3[])corners.Clone();
            Parts = parts;
        }

        internal DeformationCage WithCorners(Vector3[] corners) =>
            new DeformationCage(Id, PartIndex, LocalFromReference, corners, Parts);

        internal DeformationCage Copy(ProceduralObject obj, bool newIdentity)
        {
            var parts = new DeformationBaseline[Parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = Parts[i].Copy(obj.Parts[Parts[i].PartIndex].PartHistoryId);
            }

            return new DeformationCage(newIdentity ? Guid.NewGuid() : Id, PartIndex, LocalFromReference, Corners, parts);
        }

        internal static Vector3 UnitCorner(int corner) => new Vector3((corner & 1) != 0 ? 1f : 0f, (corner & 2) != 0 ? 1f : 0f, (corner & 4) != 0 ? 1f : 0f);

        internal static Vector3[] ReferenceCorners(Matrix4x4 reference)
        {
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++) corners[i] = reference.MultiplyPoint3x4(UnitCorner(i));
            return corners;
        }

        internal static DeformationCage Capture(ProceduralObject obj, int index, Matrix4x4 reference, PartGeometrySnapshot[] snapshots)
        {
            var parts = new DeformationBaseline[snapshots.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                int slot = index < 0 ? i : index;
                var snapshot = snapshots[i];
                parts[i] = new DeformationBaseline(slot, obj.Parts[slot].PartHistoryId, snapshot.Positions, snapshot.Normals, snapshot.Tangents);
            }

            return new DeformationCage(Guid.NewGuid(), index, reference, ReferenceCorners(reference), parts);
        }

        internal bool SameCorners(Vector3[] corners)
        {
            if (corners == null || corners.Length != 8) return false;
            for (int i = 0; i < 8; i++)
                if (!Corners[i].Equals(corners[i]))
                    return false;
            return true;
        }

        internal bool Matches(ProceduralObject obj)
        {
            if (PartIndex < 0 && Parts.Length != obj.Parts.Count) return false;
            foreach (var part in Parts)
                if (!part.Matches(obj))
                    return false;
            return true;
        }
    }
}