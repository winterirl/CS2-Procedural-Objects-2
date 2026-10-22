using System;
using System.Collections.Generic;
using PO2.Core;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.History;

namespace PO2.Tools.MeshEdit
{
    public sealed partial class MeshEditController
    {
        private bool m_HasPreparedCentroid, m_PreparedCentroidHasValue;
        private UnityEngine.Vector3 m_PreparedCentroid;
        private int m_PreparedCentroidSelectionVersion, m_PreparedCentroidPart, m_PreparedCentroidObject;
        private uint m_PreparedCentroidGeometry, m_PreparedCentroidPartVersion;

        private bool TryUsePreparedCentroid(ProceduralObject obj)
        {
            if (!m_HasPreparedCentroid) return false;
            m_HasPreparedCentroid = false;
            if (obj == null || obj.Id != m_PreparedCentroidObject || obj.PartVersion != m_PreparedCentroidPartVersion || m_SelectionVersion != m_PreparedCentroidSelectionVersion || m_PreparedCentroidPart < 0 || m_PreparedCentroidPart >= obj.Parts.Count || obj.Parts[m_PreparedCentroidPart].GeometryVersion != m_PreparedCentroidGeometry) return false;
            m_HasSelectionWorldCentroid = m_PreparedCentroidHasValue;
            m_SelectionLocalCentroid = m_PreparedCentroid;
            m_SelectionWorldCentroid = m_PreparedCentroidHasValue ? obj.Transform.TransformPoint(m_PreparedCentroid) : UnityEngine.Vector3.zero;
            return true;
        }

        internal TopologySelectionCapture CaptureTopologySelection(ProceduralObject obj) =>
            SelectionMode == MeshSelectionMode.Edge ? new TopologySelectionCapture(obj, m_EdgeSelection.Count, m_EdgeSelection.Edges) : new TopologySelectionCapture(m_VertexSelection.Count, m_VertexSelection.Vertices);

        internal int FirstSelectedPart
        {
            get
            {
                if (SelectionMode == MeshSelectionMode.Edge)
                {
                    foreach (var edge in m_EdgeSelection.Edges) return edge.PartIndex;
                }
                else
                {
                    foreach (var vertex in m_VertexSelection.Vertices) return vertex.PartIndex;
                }

                return -1;
            }
        }

        internal void AdoptPreparedTopologySelection(ProceduralObject obj, TopologyPreparedSelection prepared)
        {
            if (!IsActive || !m_ObjectId.HasValue || obj.Id != m_ObjectId.Value || !prepared.Part.Owner.Equals(new HistoryObjectKey(obj.Id, obj.PersistentId)) || prepared.PartIndex < 0 || prepared.PartIndex >= obj.Parts.Count || obj.Parts[prepared.PartIndex].PartHistoryId != prepared.Part.PartId) return;
            m_VertexSelection.AdoptPrepared(prepared.Vertices);
            m_EdgeSelection.AdoptPrepared(prepared.Edges);
            m_EffectiveEdgeVertices.AdoptPrepared(prepared.Endpoints);
            m_SurfaceSelection.Clear();
            SelectionMode = (MeshSelectionMode)prepared.Selection.Mode;
            m_SelectionVersion++;
            InvalidateTopologyState();
            m_HasPreparedCentroid = true;
            m_PreparedCentroidHasValue = prepared.HasCentroid;
            m_PreparedCentroid = prepared.LocalCentroid;
            m_PreparedCentroidPart = prepared.PartIndex;
            m_PreparedCentroidObject = obj.Id;
            m_PreparedCentroidSelectionVersion = m_SelectionVersion;
            m_PreparedCentroidPartVersion = obj.PartVersion;
            m_PreparedCentroidGeometry = obj.Parts[prepared.PartIndex].GeometryVersion;
        }

        internal TopologySelection CaptureTopologyHistorySelection(ProceduralObject obj)
        {
            if (!IsActive || !m_ObjectId.HasValue || m_ObjectId.Value != obj.Id) return null;
            var items = new List<TopologySelectionItem>();
            var owner = new HistoryObjectKey(obj.Id, obj.PersistentId);
            foreach (var vertex in m_VertexSelection.Vertices)
                if (vertex.PartIndex >= 0 && vertex.PartIndex < obj.Parts.Count)
                    items.Add(new TopologySelectionItem(new HistoryPartKey(owner, obj.Parts[vertex.PartIndex].PartHistoryId, vertex.PartIndex), vertex.VertexId, 0));
            foreach (var edge in m_EdgeSelection.Edges)
                if (edge.PartIndex >= 0 && edge.PartIndex < obj.Parts.Count)
                    items.Add(new TopologySelectionItem(new HistoryPartKey(owner, obj.Parts[edge.PartIndex].PartHistoryId, edge.PartIndex), edge.EdgeId, 1));
            foreach (var surface in m_SurfaceSelection.Surfaces)
                if (surface.PartIndex >= 0 && surface.PartIndex < obj.Parts.Count)
                    items.Add(new TopologySelectionItem(new HistoryPartKey(owner, obj.Parts[surface.PartIndex].PartHistoryId, surface.PartIndex), surface.TriangleId, 2));
            return new TopologySelection((int)SelectionMode, items.ToArray());
        }

        internal void RestoreTopologyHistorySelection(ProceduralObject obj, TopologySelection selection)
        {
            OnTopologyChanged(obj);
            if (selection == null || !IsActive || !m_ObjectId.HasValue || m_ObjectId.Value != obj.Id) return;
            var owner = new HistoryObjectKey(obj.Id, obj.PersistentId);
            var parts = new Dictionary<Guid, int>();
            for (int i = 0; i < obj.Parts.Count; i++) parts.Add(obj.Parts[i].PartHistoryId, i);
            if (!Enum.IsDefined(typeof(MeshSelectionMode), selection.Mode)) return;
            SelectionMode = (MeshSelectionMode)selection.Mode;
            foreach (var item in selection.Items)
            {
                if (!item.Part.Owner.Equals(owner) || !parts.TryGetValue(item.Part.PartId, out int index) || item.Element < 0) continue;
                var part = obj.Parts[index];
                if (item.Kind == 0 && item.Element < part.Topology.LogicalVertexCount) m_VertexSelection.Add(new VertexRef(index, item.Element));
                if (item.Kind == 1 && item.Element < part.StructuralTopology.StructuralEdgeCount) m_EdgeSelection.Add(new StructuralEdgeRef(index, item.Element));
                if (item.Kind == 2 && item.Element < part.Mesh.Triangles.Length / 3) m_SurfaceSelection.Add(new SurfaceRef(index, item.Element));
            }

            RebuildEffectiveEdgeVertices(obj);
            m_SelectionVersion++;
            m_VisualVersion++;
            m_DirtyFlags = MeshEditCacheDirtyFlags.All;
        }
    }
}