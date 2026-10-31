using System;
using PO2.Geometry.Topology;

namespace PO2.Conversion
{
    internal sealed class ConvertedStructuralTopologyResult
    {
        public StructuralTopology Topology { get; }
        public StructuralFaceTriangles[] FaceTriangles { get; }
        public int[] TriangleFaceIds { get; }
        public StructuralTopologySource TopologySource { get; }

        public ConvertedStructuralTopologyResult(StructuralTopology topology, StructuralFaceTriangles[] faceTriangles, int[] triangleFaceIds, StructuralTopologySource topologySource)
        {
            Topology = topology ?? throw new ArgumentNullException(nameof(topology));
            FaceTriangles = faceTriangles ?? throw new ArgumentNullException(nameof(faceTriangles));
            TriangleFaceIds = triangleFaceIds ?? throw new ArgumentNullException(nameof(triangleFaceIds));
            TopologySource = topologySource;
        }
    }

}
