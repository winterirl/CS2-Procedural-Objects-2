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
        private void CommitLoopCut(ProceduralObject editedObject)
        {
            if (!m_LoopCut.IsPositioning || editedObject.Id != m_LoopCut.ObjectId || m_MeshEditController == null || !IsLoopCutSourceCurrent(editedObject))
            {
                Mod.log.Warn("[PO Mesh Edit] Loop Cut commit rejected because its source topology is no longer current.");
                CancelMeshLoopCut();
                return;
            }

            int partIndex = m_LoopCut.PartIndex;
            int structuralEdgeId = m_LoopCut.StructuralEdgeId;
            float t = m_LoopCut.CurrentT;
            int cutCount = m_LoopCut.CutCount;
            MeshSelectionMode commitSelectionMode = m_MeshEditController.SelectionMode;
            MeshPart part = editedObject.Parts[partIndex];

            if (!editedObject.CanEditGeometry(partIndex, out LocalizedText deformationReason))
            {
                Mod.log.Warn(deformationReason.ToString());
                return;
            }

            if (!TopologyEditor.TryCreateLoopCutMutation(part, structuralEdgeId, cutCount, t, out MeshMutationResult mutation, out int[] newRenderVertexIndices, out StructuralLoop loop))
            {
                Mod.log.Warn("[PO Mesh Edit] Loop Cut could not build a valid topology mutation at the selected position.");
                CancelMeshLoopCut();
                return;
            }

            // Build and validate the complete replacement before touching the live part.
            // A failed cut therefore leaves the source mesh unchanged.
            CancelBoxSelection();
            ClearGizmoHover();

            using (var history = m_ProceduralObjectSystem.BeginTopologyHistory(editedObject, partIndex, mutation, LocalizedText.For("PO2.History.Action.Topology.LoopCut")))
            {
                if (history == null) return;
                part.ApplyTopologyMutation(mutation);
                editedObject.MarkDirty(ProceduralObjectDirtyFlags.Topology);
                m_MeshEditController.OnTopologyChanged(editedObject);

                System.Collections.Generic.HashSet<int> logicalIds = new System.Collections.Generic.HashSet<int>();

                for (int i = 0; i < newRenderVertexIndices.Length; i++)
                {
                    logicalIds.Add(part.Topology.GetLogicalVertexIdForRenderVertex(newRenderVertexIndices[i]));
                }

                // Render seams can create several new render vertices for one logical point.
                // Select the deduplicated logical result after the mutation.
                int[] sortedLogicalIds = new int[logicalIds.Count];
                logicalIds.CopyTo(sortedLogicalIds);
                System.Array.Sort(sortedLogicalIds);

                int selectedElementCount;
                string selectedElementLabel;

                m_LoopCut.Reset();
                RefreshMeshCutInput();

                if (commitSelectionMode == MeshSelectionMode.Edge)
                {
                    int[] cutEdgeIds = TopologyEditor.ResolveCutEdges(part, newRenderVertexIndices);
                    StructuralEdgeRef[] edgeSelection = new StructuralEdgeRef[cutEdgeIds.Length];
                    for (int i = 0; i < cutEdgeIds.Length; i++)
                    {
                        edgeSelection[i] = new StructuralEdgeRef(partIndex, cutEdgeIds[i]);
                    }

                    m_MeshEditController.ReplaceEdgeSelection(editedObject, edgeSelection);

                    selectedElementCount = edgeSelection.Length;
                    selectedElementLabel = "new structural loop edges selected";
                }
                else
                {
                    VertexRef[] newSelection = new VertexRef[sortedLogicalIds.Length];

                    for (int i = 0; i < sortedLogicalIds.Length; i++)
                    {
                        newSelection[i] = new VertexRef(partIndex, sortedLogicalIds[i]);
                    }

                    m_MeshEditController.ReplaceSelection(editedObject, newSelection);

                    selectedElementCount = newSelection.Length;
                    selectedElementLabel = "new logical vertices selected";
                }

                m_MeshEditController.SetVertexHoverSuppressed(false);
                m_MeshEditController.ClearHover();

                if (!history.Complete()) return;
            }
        }

        private void CommitSurfaceCut(ProceduralObject editedObject)
        {
            if (!m_SurfaceCut.IsPositioning || editedObject.Id != m_SurfaceCut.ObjectId || m_MeshEditController == null || !IsSurfaceCutSourceCurrent(editedObject))
            {
                Mod.log.Warn("[PO Mesh Edit] Surface Cut commit rejected because its source topology is no longer current.");
                CancelMeshSurfaceCut();
                return;
            }

            int partIndex = m_SurfaceCut.PartIndex;
            int structuralEdgeId = m_SurfaceCut.StructuralEdgeId;
            float t = m_SurfaceCut.CurrentT;
            int cutCount = m_SurfaceCut.CutCount;
            MeshSelectionMode commitSelectionMode = m_MeshEditController.SelectionMode;
            MeshPart part = editedObject.Parts[partIndex];

            if (!editedObject.CanEditGeometry(partIndex, out LocalizedText deformationReason))
            {
                Mod.log.Warn(deformationReason.ToString());
                return;
            }

            if (!TopologyEditor.TryCreateSurfaceCutMutation(part, structuralEdgeId, cutCount, t, out MeshMutationResult mutation, out int[] newRenderVertexIndices, out StructuralLoop loop))
            {
                Mod.log.Warn("[PO Mesh Edit] Surface Cut could not build a valid topology mutation at the selected position.");
                CancelMeshSurfaceCut();
                return;
            }

            // As with Loop Cut, build the whole replacement first.
            // The live mesh is not touched if any part of the cut is invalid.
            CancelBoxSelection();
            ClearGizmoHover();

            using (var history = m_ProceduralObjectSystem.BeginTopologyHistory(editedObject, partIndex, mutation, LocalizedText.For("PO2.History.Action.Topology.SurfaceCut")))
            {
                if (history == null) return;
                part.ApplyTopologyMutation(mutation);
                editedObject.MarkDirty(ProceduralObjectDirtyFlags.Topology);
                m_MeshEditController.OnTopologyChanged(editedObject);

                System.Collections.Generic.HashSet<int> logicalIds = new System.Collections.Generic.HashSet<int>();

                for (int i = 0; i < newRenderVertexIndices.Length; i++)
                {
                    logicalIds.Add(part.Topology.GetLogicalVertexIdForRenderVertex(newRenderVertexIndices[i]));
                }

                int[] sortedLogicalIds = new int[logicalIds.Count];
                logicalIds.CopyTo(sortedLogicalIds);
                System.Array.Sort(sortedLogicalIds);

                int selectedElementCount;
                string selectedElementLabel;

                m_SurfaceCut.Reset();
                RefreshMeshCutInput();

                if (commitSelectionMode == MeshSelectionMode.Edge)
                {
                    int[] cutEdgeIds = TopologyEditor.ResolveCutEdges(part, newRenderVertexIndices);
                    StructuralEdgeRef[] edgeSelection = new StructuralEdgeRef[cutEdgeIds.Length];
                    for (int i = 0; i < cutEdgeIds.Length; i++)
                    {
                        edgeSelection[i] = new StructuralEdgeRef(partIndex, cutEdgeIds[i]);
                    }

                    m_MeshEditController.ReplaceEdgeSelection(editedObject, edgeSelection);

                    selectedElementCount = edgeSelection.Length;
                    selectedElementLabel = "new structural cut edges selected";
                }
                else
                {
                    VertexRef[] newSelection = new VertexRef[sortedLogicalIds.Length];

                    for (int i = 0; i < sortedLogicalIds.Length; i++)
                    {
                        newSelection[i] = new VertexRef(partIndex, sortedLogicalIds[i]);
                    }

                    m_MeshEditController.ReplaceSelection(editedObject, newSelection);

                    selectedElementCount = newSelection.Length;
                    selectedElementLabel = "new logical vertices selected";
                }

                m_MeshEditController.SetVertexHoverSuppressed(false);
                m_MeshEditController.ClearHover();

                if (!history.Complete()) return;
            }
        }
    }
}