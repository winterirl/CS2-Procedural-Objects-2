using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.History;
using PO2.Localization;
using PO2.Tools.Gizmos;

namespace PO2.Tools.MeshEdit
{
    public sealed partial class MeshEditController
    {
        private ProceduralObject m_DeformationSelectionObject;
        private uint m_DeformationSelectionRevision;
        private int m_DeformationSelectionVersion = -1;
        private bool m_DeformationSelectionEditable;

        internal bool CanEditSelectedGeometry(ProceduralObject obj)
        {
            if (MarkerPreparationPending || obj == null) return false;
            if (!obj.HasDeformation) return true;
            int scope = VertexScope == MeshEditVertexScope.AllParts ? -1 : m_ActivePartIndex;
            if (!obj.CanEditGeometry(scope, out _)) return false;
            var selection = GetEffectiveVertexSelection();
            if (ReferenceEquals(obj, m_DeformationSelectionObject) && m_DeformationSelectionRevision == obj.DeformationVersion && m_DeformationSelectionVersion == SelectionVersion) return m_DeformationSelectionEditable;
            m_DeformationSelectionObject = obj;
            m_DeformationSelectionRevision = obj.DeformationVersion;
            m_DeformationSelectionVersion = SelectionVersion;
            m_DeformationSelectionEditable = true;
            foreach (var vertex in selection.Vertices)
            {
                if (!obj.CanEditGeometry(vertex.PartIndex, out _))
                {
                    m_DeformationSelectionEditable = false;
                    break;
                }
            }

            return m_DeformationSelectionEditable;
        }

        private readonly System.Collections.Generic.List<int> m_NumericMoveAffectedParts = new System.Collections.Generic.List<int>();

        public bool CaptureSelectedVertices(ProceduralObject proceduralObject, VertexTransformDragState dragState, LocalizedText historyDescription = default)
        {
            if (proceduralObject == null || dragState == null || !IsActive || !m_ObjectId.HasValue || proceduralObject.Id != m_ObjectId.Value || EffectiveSelectionCount == 0)
            {
                return false;
            }

            int capturedLogicalVertices = 0;

            // Vertex mode uses the real vertex selection. Edge mode feeds this
            // the deduped endpoints, so all movement code below stays shared.
            VertexSelection effectiveSelection = GetEffectiveVertexSelection();

            foreach (VertexRef vertexRef in effectiveSelection.Vertices)
            {
                int snapSlot = dragState.SnapVertices != null ? dragState.SnapVertexCount++ : -1;
                if (snapSlot >= 0) dragState.SnapVertices[snapSlot].Ref = vertexRef;
                if (vertexRef.PartIndex < 0 || vertexRef.PartIndex >= proceduralObject.Parts.Count)
                {
                    continue;
                }

                MeshPart part = proceduralObject.Parts[vertexRef.PartIndex];

                if (vertexRef.VertexId < 0 || vertexRef.VertexId >= part.Topology.LogicalVertexCount)
                {
                    continue;
                }

                Vertex vertex = part.Topology.GetVertex(vertexRef.VertexId);

                for (int i = 0; i < vertex.RenderVertexIndices.Count; i++)
                {
                    int renderVertexIndex = vertex.RenderVertexIndices[i];

                    if (renderVertexIndex < 0 || renderVertexIndex >= part.Mesh.Vertices.Length)
                    {
                        continue;
                    }

                    if (snapSlot >= 0 && i == 0)
                    {
                        dragState.SnapVertices[snapSlot].Position = part.Mesh.Vertices[renderVertexIndex];
                        dragState.SnapVertices[snapSlot].Valid = true;
                    }

                    dragState.AddRenderVertex(vertexRef.PartIndex, renderVertexIndex, part.Mesh.Vertices[renderVertexIndex]);
                }

                capturedLogicalVertices++;
            }

            dragState.LogicalVertexCount = capturedLogicalVertices;

            if (capturedLogicalVertices == 0 || dragState.RenderVertexCount == 0 || dragState.AffectedPartCount == 0) return false;
            dragState.CaptureTargets(proceduralObject, SelectionVersion);
            dragState.History = CaptureSelectionHistory(proceduralObject, historyDescription.IsEmpty ? LocalizedText.For("PO2.History.Action.Vertices.Move") : historyDescription, dragState);
            return dragState.History != null;
        }

        public bool ApplyVertexMove(ProceduralObject proceduralObject, VertexTransformDragState dragState, Vector3 localDelta)
        {
            const float deltaEpsilonSquared = 0.000000000001f;

            if (!ValidateVertexMoveTarget(proceduralObject, dragState))
            {
                return false;
            }

            if (dragState.HasAppliedDelta && (localDelta - dragState.LastAppliedLocalDelta).sqrMagnitude <= deltaEpsilonSquared)
            {
                return false;
            }

            // Always derive from the captured start position. Adding deltas to
            // the current mesh every frame would accumulate input/float error.
            for (int i = 0; i < dragState.RenderVertexCount; i++)
            {
                VertexTransformDragState.CapturedRenderVertex entry = dragState.GetRenderVertex(i);

                MeshPart part = proceduralObject.Parts[entry.PartIndex];

                part.Mesh.Vertices[entry.RenderVertexIndex] = entry.StartLocalPosition + localDelta;
            }

            if (!RefreshAffectedParts(proceduralObject, dragState)) return false;

            dragState.LastAppliedLocalDelta = localDelta;
            dragState.HasAppliedDelta = true;

            return true;
        }

        internal bool TryApplyVertexTransform(ProceduralObject proceduralObject, VertexTransformDragState dragState, Matrix4x4 worldFromStart, bool neutral)
        {
            bool changed;
            if (!ValidateVertexMoveTarget(proceduralObject, dragState) || dragState.History == null || !dragState.MatchesTargets(proceduralObject, SelectionVersion) || !SelectionTransformMath.TryGetLocalLinearTransform(Matrix4x4.TRS(Vector3.zero, dragState.StartObjectTransform.Rotation, dragState.StartObjectTransform.Scale), worldFromStart, neutral, out Matrix4x4 localFromStart) || !dragState.TryPrepareTransform(localFromStart, neutral, out changed))
            {
                return false;
            }

            if (!changed)
            {
                return true;
            }

            // Validate the entire update before publishing any of it. The
            // scratch buffer belongs to the capture and is reused each frame.
            for (int i = 0; i < dragState.RenderVertexCount; i++)
            {
                VertexTransformDragState.CapturedRenderVertex entry = dragState.GetRenderVertex(i);
                proceduralObject.Parts[entry.PartIndex].Mesh.Vertices[entry.RenderVertexIndex] = dragState.GetTransformedPosition(i);
            }

            if (!RefreshAffectedParts(proceduralObject, dragState)) return false;
            dragState.HasAppliedDelta = true;
            return dragState.History.HasFiniteNormals(proceduralObject);
        }

        public bool ApplySelectedMoveDelta(ProceduralObject proceduralObject, Vector3 localDelta, out int movedLogicalVertexCount, out int movedRenderVertexCount, LocalizedText historyDescription = default)
        {
            const float deltaEpsilonSquared = 0.000000000001f;

            movedLogicalVertexCount = 0;
            movedRenderVertexCount = 0;

            if (!ValidateSelectionTarget(proceduralObject) || SelectionMode == MeshSelectionMode.SurfaceArea || EffectiveSelectionCount == 0 || !IsFinite(localDelta) || localDelta.sqrMagnitude <= deltaEpsilonSquared)
            {
                return false;
            }

            using (var history = CaptureSelectionHistory(proceduralObject, historyDescription.IsEmpty ? LocalizedText.For("PO2.History.Action.Vertices.Move") : historyDescription))
            {
                if (history == null) return false;
                VertexSelection effectiveSelection = GetEffectiveVertexSelection();

                m_NumericMoveAffectedParts.Clear();

                foreach (VertexRef vertexRef in effectiveSelection.Vertices)
                {
                    if (vertexRef.PartIndex < 0 || vertexRef.PartIndex >= proceduralObject.Parts.Count)
                    {
                        continue;
                    }

                    MeshPart part = proceduralObject.Parts[vertexRef.PartIndex];

                    if (vertexRef.VertexId < 0 || vertexRef.VertexId >= part.Topology.LogicalVertexCount)
                    {
                        continue;
                    }

                    Vertex vertex = part.Topology.GetVertex(vertexRef.VertexId);

                    int movedCopies = 0;

                    for (int i = 0; i < vertex.RenderVertexIndices.Count; i++)
                    {
                        int renderVertexIndex = vertex.RenderVertexIndices[i];

                        if (renderVertexIndex < 0 || renderVertexIndex >= part.Mesh.Vertices.Length)
                        {
                            continue;
                        }

                        part.Mesh.Vertices[renderVertexIndex] += localDelta;

                        movedCopies++;
                    }

                    if (movedCopies == 0)
                    {
                        continue;
                    }

                    if (!m_NumericMoveAffectedParts.Contains(vertexRef.PartIndex))
                    {
                        m_NumericMoveAffectedParts.Add(vertexRef.PartIndex);
                    }

                    movedLogicalVertexCount++;
                    movedRenderVertexCount += movedCopies;
                }

                if (movedLogicalVertexCount == 0 || movedRenderVertexCount == 0)
                {
                    return false;
                }

                // Logical vertices can have several render copies at UV or normal
                // seams. They all moved above; refresh each touched part only once.
                for (int i = 0; i < m_NumericMoveAffectedParts.Count; i++)
                {
                    MeshPart part = proceduralObject.Parts[m_NumericMoveAffectedParts[i]];

                    if (!FinishVertexChange(proceduralObject, m_NumericMoveAffectedParts[i], history)) return false;
                }

                proceduralObject.MarkDirty(ProceduralObjectDirtyFlags.Geometry);

                m_DirtyFlags |= MeshEditCacheDirtyFlags.Geometry;
                m_SelectionWorldCentroidDirty = true;
                RecalculateSelectionWorldCentroid(proceduralObject);

                return history.Complete();
            }
        }

        public bool RestoreVertexMove(ProceduralObject proceduralObject, VertexTransformDragState dragState)
        {
            if (!ValidateVertexMoveTarget(proceduralObject, dragState) || !dragState.HasAppliedDelta)
            {
                return false;
            }

            dragState.History?.Dispose();
            dragState.History = null;
            OnGeometryChanged(proceduralObject);

            dragState.LastAppliedLocalDelta = Vector3.zero;
            dragState.HasAppliedDelta = false;

            return true;
        }

        private bool ValidateVertexMoveTarget(ProceduralObject proceduralObject, VertexTransformDragState dragState)
        {
            if (proceduralObject == null || dragState == null || !IsActive || !m_ObjectId.HasValue || proceduralObject.Id != m_ObjectId.Value || proceduralObject.Id != dragState.ObjectId || dragState.RenderVertexCount == 0)
            {
                return false;
            }

            return true;
        }

        private bool FinishVertexChange(ProceduralObject obj, int partIndex, VertexHistoryEdit history)
        {
            var part = obj.Parts[partIndex];
            uint before = part.GeometryVersion;
            if (history == null || !history.Recalculate(obj, partIndex))
            {
                OnGeometryChanged(obj);
                return false;
            }

            part.MarkPositionsAndDirectionsChanged();
            var change = history.Change(obj, partIndex, before);
            HistorySystem?.NotifyRendererGeometryChange(obj, change);
            MeshGpu?.Notice(obj, change);
            return true;
        }

        private bool RefreshAffectedParts(ProceduralObject proceduralObject, VertexTransformDragState dragState)
        {
            // Recalculate once per part, not once per logical/render vertex.
            for (int i = 0; i < dragState.AffectedPartCount; i++)
            {
                int partIndex = dragState.GetAffectedPartIndex(i);

                if (partIndex < 0 || partIndex >= proceduralObject.Parts.Count)
                {
                    continue;
                }

                MeshPart part = proceduralObject.Parts[partIndex];

                if (!FinishVertexChange(proceduralObject, partIndex, dragState.History)) return false;
            }

            proceduralObject.MarkDirty(ProceduralObjectDirtyFlags.Geometry);
            return true;
        }
    }
}