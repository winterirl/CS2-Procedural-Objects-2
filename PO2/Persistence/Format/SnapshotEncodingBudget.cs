using System;
using PO2.Persistence.Model;

namespace PO2.Persistence.Format
{
    internal static class SnapshotEncodingBudget
    {
        internal static long PartBytes(PersistencePartData part)
        {
            long bytes = 12L * ((part.Vertices?.Length ?? 0) + (long)(part.Normals?.Length ?? 0)) +
                16L * ((part.Tangents?.Length ?? 0) + (long)(part.UV0?.Length ?? 0) + (part.TexCoord1?.Length ?? 0) + (long)(part.TexCoord3?.Length ?? 0)) +
                4L * ((part.VertexColors?.Length ?? 0) + (long)(part.Triangles?.Length ?? 0) + (part.RenderToLogical?.Length ?? 0) + (long)(part.TriangleSurfaceAreaIds?.Length ?? 0));
            if (part.StructuralFaces != null)
                foreach (var face in part.StructuralFaces) bytes = checked(bytes + 5L + face.CornerCount * 4L);
            return bytes;
        }

        // This is temporary work, not the persisted decoded-footprint contract.
        // Include candidate buffers, retained capacity, and validation scratch.
        internal static long Estimate(long bytes) => checked(Math.Max(0, bytes) * 8 + 65536);
    }
}
