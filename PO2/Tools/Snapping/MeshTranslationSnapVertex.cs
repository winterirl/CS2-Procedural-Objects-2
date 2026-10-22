using UnityEngine;
using PO2.Geometry.Topology;

namespace PO2.Tools.Snapping
{
    internal struct MeshTranslationSnapVertex
    {
        internal VertexRef Ref;
        internal Vector3 Position;
        internal bool Valid;
    }
}