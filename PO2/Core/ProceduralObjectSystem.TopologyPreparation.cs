using System;
using PO2.Core.Operations;
using PO2.Geometry;

namespace PO2.Core
{
    public partial class ProceduralObjectSystem
    {
        internal ITopologyRenderCapture CaptureTopologyRenderer(ProceduralObject obj, int index, MeshPart candidate, TopologyReplayProof proof = null, TopologyRenderCaptureMode mode = TopologyRenderCaptureMode.Full) =>
            m_Renderer != null ? m_Renderer.CaptureTopology(obj, index, candidate, proof, mode) : throw new InvalidOperationException("The renderer is not available.");

        internal ITopologyPublication StageTopologyRenderer(ProceduralObject obj, int index, MeshPart candidate, ITopologyRenderData data) =>
            m_Renderer != null ? m_Renderer.StageTopology(obj, index, candidate, data) : throw new InvalidOperationException("The renderer is not available.");
    }
}