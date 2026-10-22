using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.Geometry.Transforms;

namespace PO2.Tools.MeshEdit
{
    public sealed partial class MeshEditController
    {
        private Vector3 m_SelectionWorldCentroid;
        private Vector3 m_SelectionLocalCentroid;
        private bool m_HasSelectionWorldCentroid;
        private bool m_SelectionWorldCentroidDirty = true;

        public bool TryGetSelectionWorldCentroid(out Vector3 centroid)
        {
            centroid = m_SelectionWorldCentroid;

            return IsActive && EffectiveSelectionCount > 0 && m_HasSelectionWorldCentroid && !m_SelectionWorldCentroidDirty;
        }

        public bool HasPartialPivotSelection(ProceduralObject proceduralObject)
        {
            return ValidateSelectionTarget(proceduralObject) && TryGetPivotSelectionCounts(proceduralObject, out int selectedCount, out int scopedCount) && PivotSelectionMath.UsesSelection(selectedCount, scopedCount);
        }

        public bool TryGetPivotLocalPosition(ProceduralObject proceduralObject, out Vector3 localPivot, out bool usesSelection)
        {
            localPivot = Vector3.zero;
            usesSelection = false;

            if (!ValidateSelectionTarget(proceduralObject) || !TryGetPivotSelectionCounts(proceduralObject, out int selectedCount, out int scopedCount) || scopedCount <= 0)
            {
                return false;
            }

            usesSelection = PivotSelectionMath.UsesSelection(selectedCount, scopedCount);

            if (usesSelection)
            {
                return TryCalculateSelectionLocalCentroid(proceduralObject, out localPivot);
            }

            Bounds bounds;
            if (VertexScope == MeshEditVertexScope.CurrentPart)
            {
                if (m_ActivePartIndex < 0 || m_ActivePartIndex >= proceduralObject.Parts.Count || !PartGeometryTransform.TryGetLogicalBounds(proceduralObject.Parts[m_ActivePartIndex], out bounds))
                {
                    return false;
                }
            }
            else if (!proceduralObject.TryGetLocalBounds(out bounds))
            {
                return false;
            }

            localPivot = bounds.center;
            return IsFinite(localPivot);
        }

        private bool TryGetPivotSelectionCounts(ProceduralObject proceduralObject, out int selectedCount, out int scopedCount)
        {
            selectedCount = 0;
            scopedCount = 0;

            if (SelectionMode != MeshSelectionMode.Vertex && SelectionMode != MeshSelectionMode.Edge)
            {
                return false;
            }

            for (int partIndex = 0; partIndex < proceduralObject.Parts.Count; partIndex++)
            {
                if (!IsPartInVertexScope(partIndex))
                {
                    continue;
                }

                MeshPart part = proceduralObject.Parts[partIndex];
                scopedCount += SelectionMode == MeshSelectionMode.Edge ? part.StructuralTopology.StructuralEdgeCount : part.Topology.LogicalVertexCount;
            }

            selectedCount = SelectionMode == MeshSelectionMode.Edge ? m_EdgeSelection.Count : m_VertexSelection.Count;
            return true;
        }

        private bool TryCalculateSelectionLocalCentroid(ProceduralObject proceduralObject, out Vector3 localCentroid)
        {
            localCentroid = Vector3.zero;
            VertexSelection selection = GetEffectiveVertexSelection();

            double x = 0.0;
            double y = 0.0;
            double z = 0.0;
            int validCount = 0;

            foreach (VertexRef vertexRef in selection.Vertices)
            {
                if (vertexRef.PartIndex < 0 || vertexRef.PartIndex >= proceduralObject.Parts.Count || !IsPartInVertexScope(vertexRef.PartIndex))
                {
                    continue;
                }

                MeshPart part = proceduralObject.Parts[vertexRef.PartIndex];
                if (vertexRef.VertexId < 0 || vertexRef.VertexId >= part.Topology.LogicalVertexCount)
                {
                    continue;
                }

                Vector3 position = part.Topology.GetVertex(vertexRef.VertexId).GetPosition(part.Mesh);
                if (!IsFinite(position))
                {
                    continue;
                }

                x += position.x;
                y += position.y;
                z += position.z;
                validCount++;
            }

            if (validCount <= 0)
            {
                return false;
            }

            double inverseCount = 1.0 / validCount;
            localCentroid = new Vector3((float)(x * inverseCount), (float)(y * inverseCount), (float)(z * inverseCount));
            return IsFinite(localCentroid);
        }

        internal bool TryGetSelectionLocalCentroid(out Vector3 centroid)
        {
            centroid = m_SelectionLocalCentroid;
            return IsActive && EffectiveSelectionCount > 0 && m_HasSelectionWorldCentroid && !m_SelectionWorldCentroidDirty;
        }

        private readonly MeshSelectionCentroid m_SelectionCentroid = new MeshSelectionCentroid();

        private void RecalculateSelectionWorldCentroid(ProceduralObject proceduralObject)
        {
            m_SelectionWorldCentroidDirty = false;
            m_HasSelectionWorldCentroid = false;
            m_SelectionWorldCentroid = m_SelectionLocalCentroid = Vector3.zero;
            if (TryUsePreparedCentroid(proceduralObject))
            {
                CachePreparedSelectionCentroid(proceduralObject, m_SelectionLocalCentroid, m_HasSelectionWorldCentroid ? GetEffectiveVertexSelection().Count : 0);
                return;
            }

            // FlushVisualDirtyFlags may publish this selection version later in
            // the same update. Cache the version consumers will observe.
            int version = unchecked(SelectionVersion + ((m_DirtyFlags & MeshEditCacheDirtyFlags.Selection) != 0 ? 1 : 0));
            m_HasSelectionWorldCentroid = m_SelectionCentroid.TryGet(proceduralObject, GetEffectiveVertexSelection(), version, SelectionMode, VertexScope, m_ActivePartIndex, out m_SelectionLocalCentroid, out m_SelectionWorldCentroid);
        }

        private void CachePreparedSelectionCentroid(ProceduralObject obj, Vector3 local, int count)
        {
            int version = unchecked(SelectionVersion + ((m_DirtyFlags & MeshEditCacheDirtyFlags.Selection) != 0 ? 1 : 0));
            m_SelectionCentroid.TryPrime(obj, GetEffectiveVertexSelection(), version, SelectionMode, VertexScope, m_ActivePartIndex, local, count);
        }
    }
}