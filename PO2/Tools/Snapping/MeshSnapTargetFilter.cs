using System.Collections.Generic;
using PO2.Geometry.Topology;

namespace PO2.Tools.Snapping
{
    // Selection can change as soon as the drag ends, so the target session
    // owns this small snapshot instead of consulting the live editor state.
    internal sealed class MeshSnapTargetFilter
    {
        private readonly HashSet<VertexRef> m_ExcludedVertices = new HashSet<VertexRef>();

        internal MeshSnapTargetFilter(IReadOnlyList<VertexRef> excludedVertices, System.Threading.CancellationToken token = default)
        {
            if (excludedVertices == null)
            {
                return;
            }

            for (int vertexIndex = 0; vertexIndex < excludedVertices.Count; vertexIndex++)
            {
                if ((vertexIndex & 4095) == 0) token.ThrowIfCancellationRequested();
                m_ExcludedVertices.Add(excludedVertices[vertexIndex]);
            }
        }

        internal bool ExcludesVertex(int partIndex, int vertexId)
        {
            return m_ExcludedVertices.Contains(new VertexRef(partIndex, vertexId));
        }
    }
}