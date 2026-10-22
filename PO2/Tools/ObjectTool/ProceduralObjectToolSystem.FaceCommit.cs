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
using PO2.Tools.MeshEdit;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private void CommitActiveMeshFaceOperation(ProceduralObject editedObject)
        {
            if (m_FaceOperation.Kind == FaceOperationKind.Inset)
            {
                CommitMeshFaceInset(editedObject);
            }
            else if (m_FaceOperation.Kind == FaceOperationKind.Extrude)
            {
                CommitMeshFaceExtrude(editedObject);
            }
        }

        private void CommitMeshFaceExtrude(ProceduralObject editedObject)
        {
            if (m_FaceOperation.Kind != FaceOperationKind.Extrude || !m_FaceOperation.IsPositioning || editedObject == null || editedObject.Id != m_FaceOperation.ObjectId || m_MeshEditController == null || !m_FaceOperation.IsSourceCurrent(editedObject, m_ProceduralObjectSystem.SelectedPartIndex))
            {
                Mod.log.Warn("[PO Mesh Edit] Extrude commit rejected because its source topology is no longer current.");
                CancelMeshFaceOperation();
                return;
            }

            int partIndex = m_FaceOperation.PartIndex;
            float distance = m_FaceOperation.ExtrusionDistance;
            FaceRegionPlan plan = m_FaceOperation.ExtrusionPlan;
            MeshSelectionMode commitSelectionMode = m_MeshEditController.SelectionMode;
            MeshPart part = editedObject.Parts[partIndex];

            if (!editedObject.CanEditGeometry(partIndex, out LocalizedText deformationReason))
            {
                Mod.log.Warn(deformationReason.ToString());
                return;
            }

            if (!FaceExtrusionEditor.TryCreateMutation(part, plan, distance, out FaceExtrusionMutation extrusion))
            {
                Mod.log.Warn("[PO Mesh Edit] Extrude could not build a valid topology mutation at the selected distance.");
                return;
            }

            CancelBoxSelection();
            ClearGizmoHover();

            using (var history = m_ProceduralObjectSystem.BeginTopologyHistory(editedObject, partIndex, extrusion.MeshMutation, LocalizedText.For("PO2.History.Action.Topology.Extrude")))
            {
                if (history == null) return;
                try
                {
                    part.ApplyTopologyMutation(extrusion.MeshMutation);
                }
                catch (System.Exception exception)
                {
                    Mod.log.Error($"[PO Mesh Edit] Extrude replacement validation failed: {exception}");
                    return;
                }

                editedObject.MarkDirty(ProceduralObjectDirtyFlags.Topology);
                m_MeshEditController.OnTopologyChanged(editedObject);

                int selectedElementCount = 0;
                string selectedElementLabel = "selection cleared";

                if (commitSelectionMode == MeshSelectionMode.Vertex)
                {
                    System.Collections.Generic.HashSet<int> logicalVertexIds = new System.Collections.Generic.HashSet<int>();

                    for (int i = 0; i < extrusion.CapRenderVertexIndices.Length; i++)
                    {
                        logicalVertexIds.Add(part.Topology.GetLogicalVertexIdForRenderVertex(extrusion.CapRenderVertexIndices[i]));
                    }

                    int[] sortedLogicalVertexIds = new int[logicalVertexIds.Count];
                    logicalVertexIds.CopyTo(sortedLogicalVertexIds);
                    System.Array.Sort(sortedLogicalVertexIds);

                    VertexRef[] vertexSelection = new VertexRef[sortedLogicalVertexIds.Length];
                    for (int i = 0; i < sortedLogicalVertexIds.Length; i++)
                    {
                        vertexSelection[i] = new VertexRef(partIndex, sortedLogicalVertexIds[i]);
                    }

                    m_MeshEditController.ReplaceSelection(editedObject, vertexSelection);
                    selectedElementCount = vertexSelection.Length;
                    selectedElementLabel = "cap logical vertices selected";
                }
                else if (commitSelectionMode == MeshSelectionMode.Edge)
                {
                    System.Collections.Generic.HashSet<int> boundaryEdgeIds = new System.Collections.Generic.HashSet<int>();
                    int[] pairs = extrusion.CapBoundaryRenderVertexPairs;

                    for (int i = 0; i + 1 < pairs.Length; i += 2)
                    {
                        int logicalA = part.Topology.GetLogicalVertexIdForRenderVertex(pairs[i]);
                        int logicalB = part.Topology.GetLogicalVertexIdForRenderVertex(pairs[i + 1]);
                        if (part.StructuralTopology.TryGetEdgeByLogicalVertices(logicalA, logicalB, out StructuralEdge edge))
                        {
                            boundaryEdgeIds.Add(edge.Id);
                        }
                    }

                    int[] sortedBoundaryEdgeIds = new int[boundaryEdgeIds.Count];
                    boundaryEdgeIds.CopyTo(sortedBoundaryEdgeIds);
                    System.Array.Sort(sortedBoundaryEdgeIds);

                    StructuralEdgeRef[] edgeSelection = new StructuralEdgeRef[sortedBoundaryEdgeIds.Length];
                    for (int i = 0; i < sortedBoundaryEdgeIds.Length; i++)
                    {
                        edgeSelection[i] = new StructuralEdgeRef(partIndex, sortedBoundaryEdgeIds[i]);
                    }

                    m_MeshEditController.ReplaceEdgeSelection(editedObject, edgeSelection);
                    selectedElementCount = edgeSelection.Length;
                    selectedElementLabel = "cap perimeter edges selected";
                }

                m_FaceOperation.Reset();
                m_MeshEditController.SetVertexHoverSuppressed(false);
                m_MeshEditController.ClearHover();

                if (!history.Complete()) return;
            }
        }

        private void CommitMeshFaceInset(ProceduralObject editedObject)
        {
            if (m_FaceOperation.Kind != FaceOperationKind.Inset || !m_FaceOperation.IsPositioning || editedObject == null || editedObject.Id != m_FaceOperation.ObjectId || m_MeshEditController == null || !m_FaceOperation.IsSourceCurrent(editedObject, m_ProceduralObjectSystem.SelectedPartIndex))
            {
                Mod.log.Warn("[PO Mesh Edit] Inset commit rejected because its source topology is no longer current.");
                CancelMeshFaceOperation();
                return;
            }

            int partIndex = m_FaceOperation.PartIndex;
            float amount = m_FaceOperation.InsetAmount;
            FaceInsetPlan plan = m_FaceOperation.InsetPlan;
            MeshSelectionMode commitSelectionMode = m_MeshEditController.SelectionMode;
            MeshPart part = editedObject.Parts[partIndex];

            if (!editedObject.CanEditGeometry(partIndex, out LocalizedText deformationReason))
            {
                Mod.log.Warn(deformationReason.ToString());
                return;
            }

            if (!FaceInsetEditor.TryCreateMutation(part, plan, amount, out FaceInsetMutation inset))
            {
                Mod.log.Warn("[PO Mesh Edit] Inset could not build a valid topology mutation at the selected amount.");
                return;
            }

            CancelBoxSelection();
            ClearGizmoHover();

            using (var history = m_ProceduralObjectSystem.BeginTopologyHistory(editedObject, partIndex, inset.MeshMutation, LocalizedText.For("PO2.History.Action.Topology.Inset")))
            {
                if (history == null) return;
                try
                {
                    part.ApplyTopologyMutation(inset.MeshMutation);
                }
                catch (System.Exception exception)
                {
                    Mod.log.Error($"[PO Mesh Edit] Inset replacement validation failed: {exception}");
                    return;
                }

                editedObject.MarkDirty(ProceduralObjectDirtyFlags.Topology);
                m_MeshEditController.OnTopologyChanged(editedObject);

                int selectedElementCount = 0;
                string selectedElementLabel = "selection cleared";

                if (commitSelectionMode == MeshSelectionMode.Vertex)
                {
                    System.Collections.Generic.HashSet<int> logicalVertexIds = new System.Collections.Generic.HashSet<int>();

                    for (int i = 0; i < inset.InnerRenderVertexIndices.Length; i++)
                    {
                        logicalVertexIds.Add(part.Topology.GetLogicalVertexIdForRenderVertex(inset.InnerRenderVertexIndices[i]));
                    }

                    int[] sortedLogicalVertexIds = new int[logicalVertexIds.Count];
                    logicalVertexIds.CopyTo(sortedLogicalVertexIds);
                    System.Array.Sort(sortedLogicalVertexIds);

                    VertexRef[] vertexSelection = new VertexRef[sortedLogicalVertexIds.Length];
                    for (int i = 0; i < sortedLogicalVertexIds.Length; i++)
                    {
                        vertexSelection[i] = new VertexRef(partIndex, sortedLogicalVertexIds[i]);
                    }

                    m_MeshEditController.ReplaceSelection(editedObject, vertexSelection);
                    selectedElementCount = vertexSelection.Length;
                    selectedElementLabel = "inner logical vertices selected";
                }
                else if (commitSelectionMode == MeshSelectionMode.Edge)
                {
                    System.Collections.Generic.HashSet<int> boundaryEdgeIds = new System.Collections.Generic.HashSet<int>();
                    int[] pairs = inset.InnerBoundaryRenderVertexPairs;

                    for (int i = 0; i + 1 < pairs.Length; i += 2)
                    {
                        int logicalA = part.Topology.GetLogicalVertexIdForRenderVertex(pairs[i]);
                        int logicalB = part.Topology.GetLogicalVertexIdForRenderVertex(pairs[i + 1]);
                        if (part.StructuralTopology.TryGetEdgeByLogicalVertices(logicalA, logicalB, out StructuralEdge edge))
                        {
                            boundaryEdgeIds.Add(edge.Id);
                        }
                    }

                    int[] sortedBoundaryEdgeIds = new int[boundaryEdgeIds.Count];
                    boundaryEdgeIds.CopyTo(sortedBoundaryEdgeIds);
                    System.Array.Sort(sortedBoundaryEdgeIds);

                    StructuralEdgeRef[] edgeSelection = new StructuralEdgeRef[sortedBoundaryEdgeIds.Length];
                    for (int i = 0; i < sortedBoundaryEdgeIds.Length; i++)
                    {
                        edgeSelection[i] = new StructuralEdgeRef(partIndex, sortedBoundaryEdgeIds[i]);
                    }

                    m_MeshEditController.ReplaceEdgeSelection(editedObject, edgeSelection);
                    selectedElementCount = edgeSelection.Length;
                    selectedElementLabel = "inner perimeter edges selected";
                }

                m_FaceOperation.Reset();
                m_MeshEditController.SetVertexHoverSuppressed(false);
                m_MeshEditController.ClearHover();

                if (!history.Complete()) return;
            }
        }
    }
}