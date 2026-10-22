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
using PO2.Localization;
using MeshTopology = PO2.Geometry.Topology.MeshTopology;

namespace PO2.Tools.MeshEdit
{
    public sealed partial class MeshEditController
    {
        private bool m_VertexSlideTargeting;
        public bool IsVertexSlideTargeting => m_VertexSlideTargeting;

        public bool BeginVertexSlide(ProceduralObject proceduralObject)
        {
            if (!ValidateSelectionTarget(proceduralObject) || !CanEditSelectedGeometry(proceduralObject) || SelectionMode != MeshSelectionMode.Vertex || m_VertexSelection.Count != 1)
            {
                return false;
            }

            if (IsAlignmentTargeting)
            {
                CancelAlignment();
            }

            if (m_VertexSlideTargeting)
            {
                return true;
            }

            m_VertexSlideTargeting = true;
            m_DirtyFlags |= MeshEditCacheDirtyFlags.Hover;
            FlushVisualDirtyFlags();
            return true;
        }

        public bool CancelVertexSlide()
        {
            if (!m_VertexSlideTargeting)
            {
                return false;
            }

            m_VertexSlideTargeting = false;
            m_DirtyFlags |= MeshEditCacheDirtyFlags.Hover;
            FlushVisualDirtyFlags();
            return true;
        }

        public bool TryGetSingleSelectedVertex(out VertexRef vertex)
        {
            vertex = default;

            if (m_VertexSelection.Count != 1)
            {
                return false;
            }

            foreach (VertexRef selected in m_VertexSelection.Vertices)
            {
                vertex = selected;
                return true;
            }

            return false;
        }

        public bool TryGetHoveredSlideTarget(ProceduralObject proceduralObject, out VertexRef source, out VertexRef target)
        {
            source = default;
            target = default;

            if (!m_VertexSlideTargeting || !m_HoveredVertex.HasValue || !TryGetSingleSelectedVertex(out source) || source.PartIndex != m_HoveredVertex.Value.PartIndex)
            {
                return false;
            }

            target = m_HoveredVertex.Value;

            if (source.VertexId == target.VertexId || source.PartIndex < 0 || source.PartIndex >= proceduralObject.Parts.Count)
            {
                return false;
            }

            MeshTopology topology = proceduralObject.Parts[source.PartIndex].Topology;

            return topology.TryGetEdge(source.VertexId, target.VertexId, out LogicalEdge _);
        }

        public bool CaptureVertexSlide(ProceduralObject proceduralObject, VertexRef target, VertexSlideState slideState)
        {
            if (slideState == null || !ValidateSelectionTarget(proceduralObject) || !TryGetSingleSelectedVertex(out VertexRef source) || source.PartIndex != target.PartIndex || source.PartIndex < 0 || source.PartIndex >= proceduralObject.Parts.Count)
            {
                return false;
            }

            MeshPart part = proceduralObject.Parts[source.PartIndex];

            if (!part.Topology.TryGetEdge(source.VertexId, target.VertexId, out LogicalEdge _))
            {
                return false;
            }

            Vertex sourceVertex = part.Topology.GetVertex(source.VertexId);
            Vertex targetVertex = part.Topology.GetVertex(target.VertexId);

            Vector3 startLocal = sourceVertex.GetPosition(part.Mesh);
            Vector3 targetLocal = targetVertex.GetPosition(part.Mesh);
            Vector3 worldStart = proceduralObject.Transform.TransformPoint(startLocal);
            Vector3 worldTarget = proceduralObject.Transform.TransformPoint(targetLocal);

            if ((worldTarget - worldStart).sqrMagnitude < 0.000001f)
            {
                return false;
            }

            slideState.BeginCapture(proceduralObject.Id, source, target, startLocal, targetLocal, worldStart, worldTarget, proceduralObject.Transform);

            // Capture every render copy of the logical source vertex. Moving
            // only the first one would tear open UV or hard-normal seams.
            for (int i = 0; i < sourceVertex.RenderVertexIndices.Count; i++)
            {
                int renderIndex = sourceVertex.RenderVertexIndices[i];

                if (renderIndex < 0 || renderIndex >= part.Mesh.Vertices.Length)
                {
                    continue;
                }

                slideState.AddRenderVertex(source.PartIndex, renderIndex, part.Mesh.Vertices[renderIndex]);
            }

            if (slideState.RenderVertexCount == 0)
            {
                slideState.Reset();
                return false;
            }

            var slideIndices = new int[slideState.RenderVertexCount];
            for (int i = 0; i < slideIndices.Length; i++) slideIndices[i] = slideState.GetRenderVertex(i).RenderVertexIndex;
            slideState.History = HistorySystem.BeginVertexHistory(proceduralObject, new Dictionary<int, int[]> { { source.PartIndex, slideIndices } }, LocalizedText.For("PO2.History.Action.Vertex.Slide"));
            if (slideState.History == null)
            {
                slideState.Reset();
                return false;
            }

            m_VertexSlideTargeting = false;
            m_DirtyFlags |= MeshEditCacheDirtyFlags.Hover;
            FlushVisualDirtyFlags();
            return true;
        }

        public bool ApplyVertexSlide(ProceduralObject proceduralObject, VertexSlideState slideState, float t)
        {
            if (slideState == null || !ValidateSelectionTarget(proceduralObject) || slideState.ObjectId != proceduralObject.Id)
            {
                return false;
            }

            const float kMaximumT = 0.999f;
            const float kChangeEpsilon = 0.000001f;

            float clampedT = Mathf.Clamp(t, 0f, kMaximumT);

            if (Mathf.Abs(clampedT - slideState.LastAppliedT) <= kChangeEpsilon)
            {
                return false;
            }

            Vector3 localPosition = Vector3.LerpUnclamped(slideState.StartLocalPosition, slideState.TargetLocalPosition, clampedT);

            int partIndex = slideState.SourceVertex.PartIndex;
            MeshPart part = proceduralObject.Parts[partIndex];

            for (int i = 0; i < slideState.RenderVertexCount; i++)
            {
                VertexSlideState.CapturedRenderVertex captured = slideState.GetRenderVertex(i);

                part.Mesh.Vertices[captured.RenderVertexIndex] = localPosition;
            }

            if (!FinishVertexChange(proceduralObject, partIndex, slideState.History)) return false;
            proceduralObject.MarkDirty(ProceduralObjectDirtyFlags.Geometry);

            slideState.LastAppliedT = clampedT;
            slideState.HasAppliedSlide = true;
            m_DirtyFlags |= MeshEditCacheDirtyFlags.Geometry;
            m_SelectionWorldCentroidDirty = true;
            return true;
        }

        public bool RestoreVertexSlide(ProceduralObject proceduralObject, VertexSlideState slideState)
        {
            if (slideState == null || !ValidateSelectionTarget(proceduralObject) || slideState.ObjectId != proceduralObject.Id || !slideState.HasAppliedSlide)
            {
                return false;
            }

            slideState.History?.Dispose();
            slideState.History = null;

            m_DirtyFlags |= MeshEditCacheDirtyFlags.Geometry;
            m_SelectionWorldCentroidDirty = true;
            return true;
        }

        public bool IsVertexSlideTargetCandidate(ProceduralObject proceduralObject, VertexRef vertex)
        {
            if (!m_VertexSlideTargeting || !m_HoveredVertex.HasValue || m_HoveredVertex.Value != vertex)
            {
                return false;
            }

            return TryGetHoveredSlideTarget(proceduralObject, out VertexRef _, out VertexRef _);
        }
    }
}