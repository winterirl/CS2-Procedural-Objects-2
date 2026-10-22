using UnityEngine;
using PO2.Geometry.Topology;

namespace PO2.Tools.MeshEdit
{
    // Stable scoped structural-edge data for Mesh Edit > Edge mode. Solid
    // keeps screen projection and visibility here. X-Ray keeps its camera-only
    // projection in native job buffers while sharing these refs and endpoints.
    public struct EdgeFrameData
    {
        public StructuralEdgeRef Edge;
        public Vector3 LocalStart;
        public Vector3 LocalEnd;
        public Vector3 WorldStart;
        public Vector3 WorldEnd;
        public Vector2 ScreenStart;
        public Vector2 ScreenEnd;
        public float CameraDepthStart;
        public float CameraDepthEnd;

        // Solid mode shares the same interval mask between rendering and picking.
        // X-Ray uses one interval for the full edge.
        public int VisibleIntervalCount;
        public uint VisibleIntervalMask;
    }
}