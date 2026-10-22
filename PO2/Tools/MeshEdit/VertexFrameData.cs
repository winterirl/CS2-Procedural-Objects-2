using UnityEngine;
using PO2.Geometry.Topology;

namespace PO2.Tools.MeshEdit
{
    // Per-projected-vertex scratch data. The marker renderer and picking both
    // read this so they agree about position, depth, and Solid visibility.
    public struct VertexFrameData
    {
        public VertexRef Vertex;
        public Vector3 WorldPosition;
        public Vector2 ScreenPosition;
        public float CameraDepth;
        public float MarkerRadius;
        public bool SolidVisible;
    }
}