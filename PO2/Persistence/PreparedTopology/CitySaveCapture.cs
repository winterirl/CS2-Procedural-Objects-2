using System;
using System.Collections.Generic;
using System.IO;
using PO2.Core;
using PO2.Persistence.Model;

namespace PO2.Persistence.PreparedTopology
{
    // Created on the owning thread. Geometry is detached; topology records are
    // immutable and remain tied to exactly the part state captured with it.
    internal sealed class CitySaveCapture
    {
        internal readonly List<PersistenceObjectData> Objects;
        internal readonly PartTopologyCapture[][] Parts;
        internal CitySaveCapture(IReadOnlyList<ProceduralObject> live, TopologyEncodingSession session)
        {
            session?.Retain(live);
            Objects = new List<PersistenceObjectData>(live.Count);
            Parts = new PartTopologyCapture[live.Count][];
            for (int i = 0; i < live.Count; i++)
            {
                Objects.Add(PersistenceObjectData.Capture(live[i]));
                Parts[i] = new PartTopologyCapture[live[i].Parts.Count];
                for (int j = 0; j < Parts[i].Length; j++)
                {
                    var part = live[i].Parts[j].CapturePersistenceTopology();
                    part.Candidate = session?.Find(part);
                    Parts[i][j] = part;
                }
            }
        }

        internal void CheckObjects(IReadOnlyList<PersistenceObjectData> objects)
        {
            if (!ReferenceEquals(Objects, objects)) throw new ArgumentException("City capture must use its own detached objects.");
        }

        internal void VerifyInputs(int index, PersistenceObjectData decoded)
        {
            var expected = Objects[index];
            if (expected.PersistentId != decoded.PersistentId || expected.Parts.Length != decoded.Parts.Length)
                throw new InvalidDataException("Saved topology object binding changed.");
            for (int i = 0; i < expected.Parts.Length; i++)
            {
                var a = expected.Parts[i]; var b = decoded.Parts[i];
                if (a.Vertices.Length != b.Vertices.Length || a.LogicalVertexCount != b.LogicalVertexCount ||
                    a.StructuralTopologySource != b.StructuralTopologySource || a.StructuralFaces.Length != b.StructuralFaces.Length)
                    throw new InvalidDataException("Saved topology inputs changed.");
                Equal(a.Triangles, b.Triangles);
                Equal(a.RenderToLogical, b.RenderToLogical);
                for (int f = 0; f < a.StructuralFaces.Length; f++)
                {
                    var x = a.StructuralFaces[f]; var y = b.StructuralFaces[f];
                    if (x.Id != y.Id || x.CornerCount != y.CornerCount) throw new InvalidDataException("Saved face changed.");
                    for (int k = 0; k < x.CornerCount; k++)
                        if (x.GetRenderVertexIndex(k) != y.GetRenderVertexIndex(k)) throw new InvalidDataException("Saved face corner changed.");
                }
            }
        }

        private static void Equal(int[] a, int[] b)
        {
            if (a.Length != b.Length) throw new InvalidDataException("Saved topology stream length changed.");
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) throw new InvalidDataException("Saved topology stream changed.");
        }
    }
}
