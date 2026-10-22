using Game;
using Game.Input;
using Game.Prefabs;
using Game.Tools;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.Localization;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        public void DissolveSelectedLoop()
        {
            if (MeshEditRequiresBake) return;
            if (!TryGetMeshEditObject(out ProceduralObject editedObject) || m_MeshEditController == null || m_IsGizmoDragging || m_IsVertexSlideDragging || m_MeshEditController.IsAlignmentTargeting || m_MeshEditController.IsVertexSlideTargeting || IsMeshTopologyInteractionActive)
            {
                return;
            }

            int partIndex;
            int[] logicalVertexIds;

            if (m_MeshEditController.SelectionMode == MeshSelectionMode.Edge)
            {
                if (!TryGetDissolveLoopVerticesFromEdgeSelection(editedObject, out partIndex, out logicalVertexIds))
                {
                    Mod.log.Warn("[PO Topology] Dissolve Loop rejected the current edge selection because it is not one complete closed structural edge loop.");
                    return;
                }
            }
            else
            {
                VertexRef[] selection = m_MeshEditController.GetSelectionSnapshot();

                if (selection.Length < 3)
                {
                    Mod.log.Warn("[PO Topology] Dissolve Loop requires one complete selected structural loop.");
                    return;
                }

                partIndex = selection[0].PartIndex;
                if (partIndex < 0 || partIndex >= editedObject.Parts.Count)
                {
                    return;
                }

                logicalVertexIds = new int[selection.Length];

                for (int i = 0; i < selection.Length; i++)
                {
                    if (selection[i].PartIndex != partIndex)
                    {
                        Mod.log.Warn("[PO Topology] Dissolve Loop rejected a selection spanning multiple mesh parts.");
                        return;
                    }

                    logicalVertexIds[i] = selection[i].VertexId;
                }
            }

            MeshPart part = editedObject.Parts[partIndex];

            if (!editedObject.CanEditGeometry(partIndex, out LocalizedText deformationReason))
            {
                Mod.log.Warn(deformationReason.ToString());
                return;
            }

            if (!TopologyEditor.TryCreateLoopDissolveMutation(part, logicalVertexIds, out MeshMutationResult mutation, out LoopDissolvePlan plan))
            {
                Mod.log.Warn("[PO Topology] Dissolve Loop rejected the current selection because it is not one complete dissolvable structural loop.");
                return;
            }

            CancelBoxSelection();
            ClearGizmoHover();

            using (var history = m_ProceduralObjectSystem.BeginTopologyHistory(editedObject, partIndex, mutation, LocalizedText.For("PO2.History.Action.Topology.DissolveLoop")))
            {
                if (history == null) return;
                part.ApplyTopologyMutation(mutation);
                editedObject.MarkDirty(ProceduralObjectDirtyFlags.Topology);
                m_MeshEditController.OnTopologyChanged(editedObject);

                if (!history.Complete()) return;
            }
        }

        public void DissolveSelectedSurfaceCut()
        {
            if (MeshEditRequiresBake) return;
            if (!TryGetMeshEditObject(out ProceduralObject editedObject) || m_MeshEditController == null || m_IsGizmoDragging || m_IsVertexSlideDragging || m_MeshEditController.IsAlignmentTargeting || m_MeshEditController.IsVertexSlideTargeting || IsMeshTopologyInteractionActive)
            {
                return;
            }

            int partIndex;
            int[] logicalVertexIds;

            if (m_MeshEditController.SelectionMode == MeshSelectionMode.Edge)
            {
                if (!TryGetDissolveSurfaceCutVerticesFromEdgeSelection(editedObject, out partIndex, out logicalVertexIds))
                {
                    Mod.log.Warn("[PO Topology] Dissolve Cut rejected the current edge selection because it is not one complete open structural edge chain.");
                    return;
                }
            }
            else
            {
                VertexRef[] selection = m_MeshEditController.GetSelectionSnapshot();

                if (selection.Length < 2)
                {
                    Mod.log.Warn("[PO Topology] Dissolve Cut requires one complete selected open structural cut.");
                    return;
                }

                partIndex = selection[0].PartIndex;
                if (partIndex < 0 || partIndex >= editedObject.Parts.Count)
                {
                    return;
                }

                logicalVertexIds = new int[selection.Length];

                for (int i = 0; i < selection.Length; i++)
                {
                    if (selection[i].PartIndex != partIndex)
                    {
                        Mod.log.Warn("[PO Topology] Dissolve Cut rejected a selection spanning multiple mesh parts.");
                        return;
                    }

                    logicalVertexIds[i] = selection[i].VertexId;
                }
            }

            MeshPart part = editedObject.Parts[partIndex];

            if (!editedObject.CanEditGeometry(partIndex, out LocalizedText deformationReason))
            {
                Mod.log.Warn(deformationReason.ToString());
                return;
            }

            if (!TopologyEditor.TryCreateSurfaceCutDissolveMutation(part, logicalVertexIds, out MeshMutationResult mutation, out SurfaceCutDissolvePlan plan))
            {
                Mod.log.Warn("[PO Topology] Dissolve Cut rejected the current selection because it is not one complete dissolvable boundary-to-boundary structural cut.");
                return;
            }

            CancelBoxSelection();
            ClearGizmoHover();

            using (var history = m_ProceduralObjectSystem.BeginTopologyHistory(editedObject, partIndex, mutation, LocalizedText.For("PO2.History.Action.Topology.DissolveSurfaceCut")))
            {
                if (history == null) return;
                part.ApplyTopologyMutation(mutation);
                editedObject.MarkDirty(ProceduralObjectDirtyFlags.Topology);
                m_MeshEditController.OnTopologyChanged(editedObject);

                if (!history.Complete()) return;
            }
        }

        public void MergeSelectedVertices()
        {
            if (MeshEditRequiresBake) return;
            if (!TryGetMeshEditObject(out ProceduralObject editedObject) || m_MeshEditController == null || m_MeshEditController.SelectionMode != MeshSelectionMode.Vertex || m_MeshEditController.SelectionCount < 2 || m_IsGizmoDragging || m_IsVertexSlideDragging || m_MeshEditController.IsAlignmentTargeting || m_MeshEditController.IsVertexSlideTargeting || IsMeshTopologyInteractionActive)
            {
                return;
            }

            VertexRef[] selection = m_MeshEditController.GetSelectionSnapshot();

            if (selection.Length < 2)
            {
                return;
            }

            int partIndex = selection[0].PartIndex;
            if (partIndex < 0 || partIndex >= editedObject.Parts.Count)
            {
                return;
            }

            int[] logicalVertexIds = new int[selection.Length];

            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i].PartIndex != partIndex)
                {
                    Mod.log.Warn("[PO Topology] Merge Vertices rejected a selection spanning multiple mesh parts.");
                    return;
                }

                logicalVertexIds[i] = selection[i].VertexId;
            }

            MeshPart part = editedObject.Parts[partIndex];

            if (!editedObject.CanEditGeometry(partIndex, out LocalizedText deformationReason))
            {
                Mod.log.Warn(deformationReason.ToString());
                return;
            }

            if (!TopologyEditor.TryCreateVertexMergeMutation(part, logicalVertexIds, out MeshMutationResult mutation, out int[] mergedRenderVertexIndices, out VertexMergePlan plan) || mergedRenderVertexIndices.Length == 0)
            {
                Mod.log.Warn("[PO Topology] Merge Vertices rejected the current vertex selection.");
                return;
            }

            CancelBoxSelection();
            ClearGizmoHover();

            using (var history = m_ProceduralObjectSystem.BeginTopologyHistory(editedObject, partIndex, mutation, LocalizedText.For("PO2.History.Action.Topology.MergeVertices")))
            {
                if (history == null) return;
                part.ApplyTopologyMutation(mutation);
                editedObject.MarkDirty(ProceduralObjectDirtyFlags.Topology);
                m_MeshEditController.OnTopologyChanged(editedObject);

                int mergedLogicalVertexId = part.Topology.GetLogicalVertexIdForRenderVertex(mergedRenderVertexIndices[0]);

                for (int i = 1; i < mergedRenderVertexIndices.Length; i++)
                {
                    if (part.Topology.GetLogicalVertexIdForRenderVertex(mergedRenderVertexIndices[i]) != mergedLogicalVertexId)
                    {
                        Mod.log.Error("[PO Topology] Merge Vertices produced inconsistent logical identity for the merged render corners.");
                        return;
                    }
                }

                m_MeshEditController.ReplaceSelection(editedObject, new[]
                {
                    new VertexRef(partIndex, mergedLogicalVertexId),
                });
                m_MeshEditController.ClearHover();

                if (!history.Complete()) return;
            }
        }

        private bool TryGetDissolveLoopVerticesFromEdgeSelection(ProceduralObject editedObject, out int partIndex, out int[] logicalVertexIds)
        {
            partIndex = -1;
            logicalVertexIds = System.Array.Empty<int>();

            int firstPart = m_MeshEditController.FirstSelectedPart;
            if (firstPart < 0 || firstPart >= editedObject.Parts.Count || !editedObject.Parts[firstPart].SupportsDestructiveTopologyEditing) return false;

            StructuralEdgeRef[] selection = m_MeshEditController.GetEdgeSelectionSnapshot();

            if (selection.Length < 3)
            {
                return false;
            }

            partIndex = selection[0].PartIndex;
            if (partIndex < 0 || partIndex >= editedObject.Parts.Count)
            {
                return false;
            }

            System.Collections.Generic.HashSet<int> selectedEdgeIds = new System.Collections.Generic.HashSet<int>();

            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i].PartIndex != partIndex || !selectedEdgeIds.Add(selection[i].EdgeId))
                {
                    return false;
                }
            }

            MeshPart part = editedObject.Parts[partIndex];

            // Converted edge graphs can be enormous.
            // They cannot be changed by dissolve anyway, so do not walk one just to disable the button.
            if (!part.SupportsDestructiveTopologyEditing)
            {
                return false;
            }

            if (!TopologyEditor.TryBuildStructuralEdgeLoop(part.StructuralTopology, selection[0].EdgeId, out int[] loopEdgeIds, out bool isClosed) || !isClosed || loopEdgeIds.Length != selection.Length)
            {
                return false;
            }

            for (int i = 0; i < loopEdgeIds.Length; i++)
            {
                if (!selectedEdgeIds.Contains(loopEdgeIds[i]))
                {
                    return false;
                }
            }

            System.Collections.Generic.HashSet<int> logicalIds = new System.Collections.Generic.HashSet<int>();

            for (int i = 0; i < loopEdgeIds.Length; i++)
            {
                StructuralEdge edge = part.StructuralTopology.GetEdge(loopEdgeIds[i]);

                logicalIds.Add(edge.LogicalVertexA);
                logicalIds.Add(edge.LogicalVertexB);
            }

            if (logicalIds.Count != loopEdgeIds.Length)
            {
                return false;
            }

            logicalVertexIds = new int[logicalIds.Count];
            logicalIds.CopyTo(logicalVertexIds);
            System.Array.Sort(logicalVertexIds);
            return true;
        }

        private bool TryGetDissolveSurfaceCutVerticesFromEdgeSelection(ProceduralObject editedObject, out int partIndex, out int[] logicalVertexIds)
        {
            partIndex = -1;
            logicalVertexIds = System.Array.Empty<int>();

            int firstPart = m_MeshEditController.FirstSelectedPart;
            if (firstPart < 0 || firstPart >= editedObject.Parts.Count || !editedObject.Parts[firstPart].SupportsDestructiveTopologyEditing) return false;

            StructuralEdgeRef[] selection = m_MeshEditController.GetEdgeSelectionSnapshot();

            if (selection.Length < 1)
            {
                return false;
            }

            partIndex = selection[0].PartIndex;
            if (partIndex < 0 || partIndex >= editedObject.Parts.Count)
            {
                return false;
            }

            System.Collections.Generic.HashSet<int> selectedEdgeIds = new System.Collections.Generic.HashSet<int>();

            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i].PartIndex != partIndex || !selectedEdgeIds.Add(selection[i].EdgeId))
                {
                    return false;
                }
            }

            MeshPart part = editedObject.Parts[partIndex];

            if (!part.SupportsDestructiveTopologyEditing)
            {
                return false;
            }

            if (!TopologyEditor.TryBuildStructuralEdgeLoop(part.StructuralTopology, selection[0].EdgeId, out int[] cutEdgeIds, out bool isClosed) || isClosed || cutEdgeIds.Length != selection.Length)
            {
                return false;
            }

            for (int i = 0; i < cutEdgeIds.Length; i++)
            {
                if (!selectedEdgeIds.Contains(cutEdgeIds[i]))
                {
                    return false;
                }
            }

            System.Collections.Generic.HashSet<int> logicalIds = new System.Collections.Generic.HashSet<int>();

            for (int i = 0; i < cutEdgeIds.Length; i++)
            {
                StructuralEdge edge = part.StructuralTopology.GetEdge(cutEdgeIds[i]);

                logicalIds.Add(edge.LogicalVertexA);
                logicalIds.Add(edge.LogicalVertexB);
            }

            if (logicalIds.Count != cutEdgeIds.Length + 1)
            {
                return false;
            }

            logicalVertexIds = new int[logicalIds.Count];
            logicalIds.CopyTo(logicalVertexIds);
            System.Array.Sort(logicalVertexIds);
            return true;
        }
    }
}