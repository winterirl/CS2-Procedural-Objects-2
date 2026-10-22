using PO2.Core;
using PO2.Tools.Snapping;

namespace PO2.Tools.MeshEdit
{
    public sealed partial class MeshEditController
    {
        internal bool TranslationSourceMatches(MeshTranslationSnapStamp stamp, ProceduralObject obj) =>
            stamp != null && stamp.Matches(obj, SelectionVersion, SelectionMode, VertexScope, m_ActivePartIndex, EffectiveSelectionCount, m_EdgeSelection.Count);

        internal MeshTranslationSnapVertex[] BeginTranslationSource(MeshTranslationSnapPreparation preparation, ProceduralObject obj) => preparation.BeginGesture(obj, SelectionVersion, SelectionMode, VertexScope, m_ActivePartIndex, EffectiveSelectionCount, m_EdgeSelection.Count);

        internal void PrepareTranslationSource(MeshTranslationSnapPreparation preparation, ProceduralObject obj, bool request, bool advance)
        {
            preparation.Refresh(obj, SelectionVersion, SelectionMode, VertexScope, m_ActivePartIndex, EffectiveSelectionCount, GetEffectiveVertexSelection().Vertices, m_EdgeSelection.Count, m_EdgeSelection.Edges, request, advance);
        }
    }
}