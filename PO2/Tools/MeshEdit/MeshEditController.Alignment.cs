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

namespace PO2.Tools.MeshEdit
{
    public sealed partial class MeshEditController
    {
        private VertexAlignmentAxis m_AlignmentAxis = VertexAlignmentAxis.None;

        private readonly System.Collections.Generic.List<int> m_AlignmentAffectedParts = new System.Collections.Generic.List<int>();

        public bool IsAlignmentTargeting =>
            m_AlignmentAxis != VertexAlignmentAxis.None;

        public VertexAlignmentAxis AlignmentAxis => m_AlignmentAxis;

        public bool BeginAlignment(ProceduralObject proceduralObject, VertexAlignmentAxis axis)
        {
            if (!ValidateSelectionTarget(proceduralObject) || !CanEditSelectedGeometry(proceduralObject) || m_VertexSelection.Count == 0 || axis == VertexAlignmentAxis.None)
            {
                return false;
            }

            if (m_VertexSlideTargeting)
            {
                CancelVertexSlide();
            }

            if (m_AlignmentAxis == axis)
            {
                return true;
            }

            m_AlignmentAxis = axis;
            m_DirtyFlags |= MeshEditCacheDirtyFlags.Hover;
            FlushVisualDirtyFlags();
            return true;
        }

        public bool CancelAlignment()
        {
            if (!IsAlignmentTargeting)
            {
                return false;
            }

            m_AlignmentAxis = VertexAlignmentAxis.None;
            m_DirtyFlags |= MeshEditCacheDirtyFlags.Hover;
            FlushVisualDirtyFlags();
            return true;
        }

        public bool ApplyAlignmentToHoveredVertex(ProceduralObject proceduralObject)
        {
            if (!ValidateSelectionTarget(proceduralObject) || !IsAlignmentTargeting || !m_HoveredVertex.HasValue)
            {
                return false;
            }

            VertexRef targetRef = m_HoveredVertex.Value;

            if (targetRef.PartIndex < 0 || targetRef.PartIndex >= proceduralObject.Parts.Count)
            {
                return false;
            }

            MeshPart targetPart = proceduralObject.Parts[targetRef.PartIndex];

            if (targetRef.VertexId < 0 || targetRef.VertexId >= targetPart.Topology.LogicalVertexCount)
            {
                return false;
            }

            Vector3 targetPosition = targetPart.Topology.GetVertex(targetRef.VertexId).GetPosition(targetPart.Mesh);

            // Axes are object-local. That is important for rotated objects and
            // also why the target coordinate comes straight from the part mesh.
            float targetCoordinate = GetAxisCoordinate(targetPosition, m_AlignmentAxis);

            using (var history = CaptureSelectionHistory(proceduralObject, LocalizedText.For("PO2.History.Action.Vertices.Align")))
            {
                if (history == null) return false;
                const float alignmentEpsilon = 0.000001f;
                bool changed = false;
                m_AlignmentAffectedParts.Clear();

                foreach (VertexRef selectedRef in m_VertexSelection.Vertices)
                {
                    if (selectedRef.PartIndex < 0 || selectedRef.PartIndex >= proceduralObject.Parts.Count)
                    {
                        continue;
                    }

                    MeshPart part = proceduralObject.Parts[selectedRef.PartIndex];

                    if (selectedRef.VertexId < 0 || selectedRef.VertexId >= part.Topology.LogicalVertexCount)
                    {
                        continue;
                    }

                    Vertex vertex = part.Topology.GetVertex(selectedRef.VertexId);

                    Vector3 logicalPosition = vertex.GetPosition(part.Mesh);

                    float currentCoordinate = GetAxisCoordinate(logicalPosition, m_AlignmentAxis);

                    if (Mathf.Abs(currentCoordinate - targetCoordinate) <= alignmentEpsilon)
                    {
                        continue;
                    }

                    for (int i = 0; i < vertex.RenderVertexIndices.Count; i++)
                    {
                        int renderVertexIndex = vertex.RenderVertexIndices[i];

                        if (renderVertexIndex < 0 || renderVertexIndex >= part.Mesh.Vertices.Length)
                        {
                            continue;
                        }

                        Vector3 position = part.Mesh.Vertices[renderVertexIndex];

                        SetAxisCoordinate(ref position, m_AlignmentAxis, targetCoordinate);

                        part.Mesh.Vertices[renderVertexIndex] = position;
                    }

                    if (!m_AlignmentAffectedParts.Contains(selectedRef.PartIndex))
                    {
                        m_AlignmentAffectedParts.Add(selectedRef.PartIndex);
                    }

                    changed = true;
                }

                m_AlignmentAxis = VertexAlignmentAxis.None;
                m_DirtyFlags |= MeshEditCacheDirtyFlags.Hover;

                if (!changed)
                {
                    FlushVisualDirtyFlags();
                    return history.Complete();
                }

                for (int i = 0; i < m_AlignmentAffectedParts.Count; i++)
                {
                    MeshPart part = proceduralObject.Parts[m_AlignmentAffectedParts[i]];

                    if (!FinishVertexChange(proceduralObject, m_AlignmentAffectedParts[i], history)) return false;
                }

                proceduralObject.MarkDirty(ProceduralObjectDirtyFlags.Geometry);

                m_DirtyFlags |= MeshEditCacheDirtyFlags.Geometry;
                m_SelectionWorldCentroidDirty = true;
                RecalculateSelectionWorldCentroid(proceduralObject);
                FlushVisualDirtyFlags();

                return history.Complete();
            }
        }

        public bool IsAlignmentTargetCandidate(VertexRef vertex)
        {
            return IsAlignmentTargeting && m_HoveredVertex.HasValue && m_HoveredVertex.Value == vertex;
        }

        private static float GetAxisCoordinate(Vector3 position, VertexAlignmentAxis axis)
        {
            switch (axis)
            {
                case VertexAlignmentAxis.X:
                    return position.x;

                case VertexAlignmentAxis.Y:
                    return position.y;

                case VertexAlignmentAxis.Z:
                    return position.z;

                default:
                    return 0f;
            }
        }

        private static void SetAxisCoordinate(ref Vector3 position, VertexAlignmentAxis axis, float coordinate)
        {
            switch (axis)
            {
                case VertexAlignmentAxis.X:
                    position.x = coordinate;
                    break;

                case VertexAlignmentAxis.Y:
                    position.y = coordinate;
                    break;

                case VertexAlignmentAxis.Z:
                    position.z = coordinate;
                    break;
            }
        }
    }
}