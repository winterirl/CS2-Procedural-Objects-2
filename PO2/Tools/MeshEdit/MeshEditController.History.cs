using System;
using System.Collections.Generic;
using PO2.Core;
using PO2.Geometry.Editing;
using PO2.History;
using PO2.Localization;

namespace PO2.Tools.MeshEdit
{
    public sealed partial class MeshEditController
    {
        private readonly MeshVertexEditPreparation m_VertexEditPreparation = new MeshVertexEditPreparation();

        private VertexHistoryEdit CaptureSelectionHistory(ProceduralObject obj, LocalizedText description, VertexTransformDragState capture = null)
        {
            if (!CanEditSelectedGeometry(obj)) return null;
            // BeginVertexHistory invokes this now, after reserving the transaction.
            // Validation and action creation cannot straddle a tool update.
            return HistorySystem.BeginVertexHistory(obj, description, layoutCache =>
            {
                var selection = GetEffectiveVertexSelection();
                int version = unchecked(SelectionVersion + ((m_DirtyFlags & MeshEditCacheDirtyFlags.Selection) != 0 ? 1 : 0));
                return m_VertexEditPreparation.Acquire(obj, selection, version, (int)SelectionMode, (int)VertexScope, m_ActivePartIndex, selection.Count, () => CreateSelectionHistoryIndices(obj, capture), layoutCache);
            });
        }

        private Dictionary<int, int[]> CreateSelectionHistoryIndices(ProceduralObject obj, VertexTransformDragState capture)
        {
            if (capture != null) return capture.CreateHistoryIndices();
            // Align and numeric translation do not have a drag capture.
            var indices = new Dictionary<int, HashSet<int>>();
            foreach (var vertexRef in GetEffectiveVertexSelection().Vertices)
            {
                if (vertexRef.PartIndex < 0 || vertexRef.PartIndex >= obj.Parts.Count) continue;
                var part = obj.Parts[vertexRef.PartIndex];
                if (vertexRef.VertexId < 0 || vertexRef.VertexId >= part.Topology.LogicalVertexCount) continue;
                if (!indices.TryGetValue(vertexRef.PartIndex, out var set))
                {
                    set = new HashSet<int>();
                    indices.Add(vertexRef.PartIndex, set);
                }

                foreach (int index in part.Topology.GetVertex(vertexRef.VertexId).RenderVertexIndices)
                    if (index >= 0 && index < part.Mesh.Vertices.Length)
                        set.Add(index);
            }

            var result = new Dictionary<int, int[]>();
            foreach (var pair in indices)
            {
                var values = new int[pair.Value.Count];
                pair.Value.CopyTo(values);
                Array.Sort(values);
                result.Add(pair.Key, values);
            }

            return result;
        }
    }
}