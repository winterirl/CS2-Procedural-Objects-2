using System;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Persistence.PreparedTopology
{
    internal sealed class PartTopologyCapture
    {
        internal readonly MeshTopology Logical;
        internal readonly StructuralTopology Structural;
        internal readonly StructuralFaceTriangles[] FaceTriangles;
        internal readonly int[] TriangleFaces;
        internal readonly StructuralTopologySource Source;
        internal readonly uint Version;
        internal readonly WeakReference<MeshPart> Part;
        internal EncodedPartTopology Candidate;
        internal bool Reusable = true;

        internal PartTopologyCapture(MeshPart part, StructuralFaceTriangles[] faces, int[] triangles)
        {
            Logical = part.Topology;
            Structural = part.StructuralTopology;
            Source = part.StructuralTopologySource;
            Version = part.TopologyVersion;
            FaceTriangles = faces;
            TriangleFaces = triangles;
            Part = new WeakReference<MeshPart>(part);
        }
    }
}
