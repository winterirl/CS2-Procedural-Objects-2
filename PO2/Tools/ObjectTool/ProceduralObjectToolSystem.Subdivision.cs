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
        public void SubdivideSelectedEdges()
        {
            if (!CanSubdivideSelectedEdges || !TryGetMeshEditObject(out var obj)) return;
            var plan = m_SubdivisionPlan;
            if (!m_TopologyEdit.TryBegin(obj, m_TopologyEditPart, plan.Source, m_TopologyEditStamp, LocalizedText.For("PO2.History.Action.Topology.SubdivideEdges"), plan.Size, (key, token) => BuildEdgeSubdivision(plan, key, token), m_ProceduralObjectSystem, out m_TopologyEditStatus)) return;
            m_TopologyOperation = 2;
            CancelBoxSelection();
            ClearGizmoHover();
        }

        private static TopologyPreparedResult BuildEdgeSubdivision(EdgeSubdivisionPlan plan, HistoryPartKey key, CancellationToken token)
        {
            var result = new TopologyPreparedResult();
            try
            {
                var mutation = TopologyEditor.CreateEdgeSubdivisionMutation(plan, token, out var children, out var provenance);
                var data = TopologyEditor.PrepareTopology(plan.Source, mutation);
                token.ThrowIfCancellationRequested();
                result.Part = data.Restore();
                token.ThrowIfCancellationRequested();
                var before = new TopologySelectionItem[plan.Edges.Length];
                var after = new TopologySelectionItem[checked(children.Length * 2)];
                for (int i = 0; i < children.Length; i++)
                {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    before[i] = new TopologySelectionItem(key, plan.Edges[i].Id, 1);
                    var edge = children[i];
                    if (!result.Part.StructuralTopology.TryGetEdgeByLogicalVertices(edge.LogicalA, edge.LogicalMidpoint, out var first) || !result.Part.StructuralTopology.TryGetEdgeByLogicalVertices(edge.LogicalMidpoint, edge.LogicalB, out var second))
                        throw new InvalidOperationException("Subdivision could not resolve its child edges.");
                    after[i * 2] = new TopologySelectionItem(key, first.Id, 1);
                    after[i * 2 + 1] = new TopologySelectionItem(key, second.Id, 1);
                }

                result.BeforeSelection = new TopologySelection((int)MeshSelectionMode.Edge, before);
                result.AfterSelection = new TopologySelection((int)MeshSelectionMode.Edge, after);
                result.OldToNew = mutation.HistoryOldToNew;
                result.Appearance = plan.Converted ? new SubdivisionAppearanceCheck(plan, provenance) : null;
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