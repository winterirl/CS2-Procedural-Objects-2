using System;
using System.Threading;
using PO2.Core.Operations;
using PO2.Geometry.Editing;
using PO2.History;
using PO2.Localization;
using PO2.Rendering;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        public void DeleteSelectedVertices() => BeginVertexRemoval(false);
        public void DissolveSelectedVertices() => BeginVertexRemoval(true);

        private void BeginVertexRemoval(bool dissolve)
        {
            if (!(dissolve ? CanDissolveSelectedVertices : CanDeleteSelectedVertices) || !TryGetMeshEditObject(out var obj)) return;
            var plan = dissolve ? m_VertexDissolvePlan.Removal : m_VertexDeletePlan;
            var dissolvePlan = dissolve ? m_VertexDissolvePlan : null;
            var selected = m_TopologySelected;
            var size = TopologySize.ForOutput(plan.Source, plan.VertexCount, plan.Source.LogicalVertexCount, checked(plan.TriangleCount * 3), plan.FaceCount);
            if (!m_TopologyEdit.TryBegin(obj, m_TopologyEditPart, plan.Source, m_TopologyEditStamp, dissolve ? LocalizedText.For("PO2.History.Action.Topology.DissolveVertices") : LocalizedText.For("PO2.History.Action.Topology.DeleteVertices"), size, (key, token) => BuildVertexRemoval(plan, dissolvePlan, selected, key, token), m_ProceduralObjectSystem, out m_TopologyEditStatus)) return;
            m_TopologyOperation = dissolve ? 1 : 0;
            CancelBoxSelection();
            ClearGizmoHover();
        }

        private static TopologyPreparedResult BuildVertexRemoval(VertexDeletePlan plan, VertexDissolvePlan dissolve, int[] selected, HistoryPartKey key, CancellationToken token)
        {
            var result = new TopologyPreparedResult();
            try
            {
                var items = new TopologySelectionItem[selected.Length];
                for (int i = 0; i < selected.Length; i++)
                {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    items[i] = new TopologySelectionItem(key, selected[i], 0);
                }

                result.BeforeSelection = new TopologySelection((int)MeshSelectionMode.Vertex, items);
                result.AfterSelection = new TopologySelection((int)MeshSelectionMode.Vertex, Array.Empty<TopologySelectionItem>());
                var mutation = dissolve == null ? TopologyEditor.CreateVertexDeletionMutation(plan, token) : TopologyEditor.CreateVertexDissolutionMutation(dissolve, token);
                var data = TopologyEditor.PrepareVertexRemoval(plan, mutation);
                token.ThrowIfCancellationRequested();
                result.Part = data.Restore();
                token.ThrowIfCancellationRequested();
                result.OldToNew = mutation.HistoryOldToNew;
                result.Appearance = dissolve == null ? null : new DissolveAppearanceCheck(dissolve);
                result.Delta = new ReversibleTopologyDelta(TopologyState.Capture(plan.Source), TopologyState.Capture(data), mutation.HistoryOldToNew);
                token.ThrowIfCancellationRequested();
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }
    }
}