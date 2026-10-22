using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using PO2.Core;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;

namespace PO2.Tools.MeshEdit
{
    public sealed partial class MeshEditController
    {
        internal MeshEditGpuSession MeshGpu;
        internal bool UsesGpuMarkers { get; private set; }
        internal bool MarkerPreparationPending => MeshGpu != null && MeshGpu.Preparing;
        internal int ActivePartIndex => m_ActivePartIndex;
        private MeshEditGpuRequest m_GpuBox;
        private BoxSelectionMode m_GpuBoxMode;
        private Task<GpuSelection> m_GpuSelectionTask;
        private Task m_RetiredGpuSelection = Task.CompletedTask;
        private GpuSelection m_PreparedGpuSelection;
        private HashSet<VertexRef>.Enumerator m_GpuCentroidVertices;
        private double m_GpuCentroidX, m_GpuCentroidY, m_GpuCentroidZ;
        internal bool GpuSelectionPending => m_GpuBox != null;

        private sealed class GpuSelection
        {
            internal HashSet<VertexRef> Vertices, Effective;
            internal HashSet<StructuralEdgeRef> Edges;
            internal Vector3 Centroid;
            internal int Count;
        }

        private bool UpdateMeshGpu(ProceduralObject obj, Camera camera, Vector2 mouse, bool hover)
        {
            bool eligible = MeshGpu != null && MeshGpu.Allowed && (SelectionMode == MeshSelectionMode.Vertex || SelectionMode == MeshSelectionMode.Edge);
            if (!eligible || !MeshGpu.Prepare(obj, camera, mouse, m_SolidVisibilityBvh))
            {
                if (UsesGpuMarkers) m_DirtyFlags |= MeshEditCacheDirtyFlags.Projection;
                UsesGpuMarkers = false;
                MeshGpu?.Stop();
                CancelGpuBox();
                return false;
            }

            UsesGpuMarkers = true;
            if ((m_DirtyFlags & MeshEditCacheDirtyFlags.Projection) != 0) m_ProjectionVersion++;
            m_FrameDataCount = 0;
            m_EdgeFrameDataCount = 0;
            CaptureProjectionState(obj, camera);
            m_DirtyFlags &= ~MeshEditCacheDirtyFlags.Projection;
            AdvanceGpuBox(obj);
            if (hover || !MeshGpu.Ready) SetGpuHoveredSlot(MeshGpu.HoverSlot);
            if (m_SelectionWorldCentroidDirty) RecalculateSelectionWorldCentroid(obj);
            FlushVisualDirtyFlags();
            return true;
        }

        internal void SetGpuHoveredSlot(int slot)
        {
            var g = MeshGpu?.Geometry;
            VertexRef? vertex = null;
            StructuralEdgeRef? edge = null;
            if (g != null && g.Mode == SelectionMode && slot >= 0 && slot < g.Candidates.Length)
            {
                if (SelectionMode == MeshSelectionMode.Vertex) vertex = g.Vertices[slot];
                else if (SelectionMode == MeshSelectionMode.Edge) edge = g.Edges[slot];
            }

            if (!Nullable.Equals(vertex, m_HoveredVertex))
            {
                m_HoveredVertex = vertex;
                m_DirtyFlags |= MeshEditCacheDirtyFlags.Hover;
            }

            SetHoveredEdge(edge);
            FlushVisualDirtyFlags();
        }

        internal void GpuTargetVisual(out int slot, out Color color, out bool suppress)
        {
            slot = -1;
            color = Color.white;
            // Edge mode suppresses vertex hover by design; it must not hide edge hover.
            suppress = SelectionMode == MeshSelectionMode.Vertex && m_VertexHoverSuppressed;
            if (m_HoveredVertex.HasValue && MeshGpu?.Geometry != null && MeshGpu.Geometry.VertexSlots.TryGetValue(m_HoveredVertex.Value, out int found))
            {
                if (IsVertexSlideTargetCandidate(MeshGpu.Geometry.Object, m_HoveredVertex.Value))
                {
                    slot = found;
                    color = VertexMarkerVisualStateUtility.s_SlideTargetColor;
                }
                else if (IsAlignmentTargetCandidate(m_HoveredVertex.Value))
                {
                    slot = found;
                    color = VertexMarkerVisualStateUtility.s_AlignmentTargetColor;
                }
            }
        }

        internal void CancelGpuBox()
        {
            if (m_GpuBox != null) m_GpuBox.Invalid = true;
            if (m_GpuSelectionTask != null) m_RetiredGpuSelection = m_GpuSelectionTask;
            m_GpuCentroidVertices.Dispose();
            m_GpuCentroidVertices = default;
            m_PreparedGpuSelection = null;
            m_GpuBox = null;
            m_GpuSelectionTask = null;
        }

        private bool BeginGpuBox(Rect rectangle, BoxSelectionMode mode)
        {
            CancelGpuBox();
            m_GpuBoxMode = mode;
            m_GpuBox = MeshGpu.Request(default, rectangle, true);
            return false;
        }

        private void AdvanceGpuBox(ProceduralObject obj)
        {
            if (m_GpuBox == null) return;
            if (MeshGpu.Obsolete(m_GpuBox))
            {
                CancelGpuBox();
                return;
            }

            if (!m_GpuBox.Complete) return;
            if (!MeshGpu.Valid(m_GpuBox))
            {
                CancelGpuBox();
                return;
            }

            if (m_GpuSelectionTask == null)
            {
                if (!m_RetiredGpuSelection.IsCompleted) return;
                if (m_RetiredGpuSelection.IsFaulted)
                {
                    var observed = m_RetiredGpuSelection.Exception;
                }

                var request = m_GpuBox;
                var vertices = GetSelectionSnapshot();
                var edges = GetEdgeSelectionSnapshot();
                var mode = m_GpuBoxMode;
                // The worker only reads immutable IDs, endpoint mappings, and its detached reply.
                m_GpuSelectionTask = Task.Run(() => PrepareGpuSelection(request, vertices, edges, mode));
            }

            if (!m_GpuSelectionTask.IsCompleted) return;
            if (m_GpuSelectionTask.IsFaulted || m_GpuSelectionTask.IsCanceled)
            {
                Mod.log.Warn("[PO Mesh GPU] Selection preparation failed; previous selection kept.");
                CancelGpuBox();
                return;
            }

            if (m_PreparedGpuSelection == null)
            {
                m_PreparedGpuSelection = m_GpuSelectionTask.Result;
                if (m_PreparedGpuSelection == null)
                {
                    CancelGpuBox();
                    return;
                }

                var effective = SelectionMode == MeshSelectionMode.Vertex ? m_PreparedGpuSelection.Vertices : m_PreparedGpuSelection.Effective;
                m_GpuCentroidVertices = effective.GetEnumerator();
                m_GpuCentroidX = m_GpuCentroidY = m_GpuCentroidZ = 0;
            }

            if (!AdvanceGpuCentroid(obj)) return;
            if (!MeshGpu.Valid(m_GpuBox))
            {
                CancelGpuBox();
                return;
            }

            var selection = m_PreparedGpuSelection;
            if (SelectionMode == MeshSelectionMode.Vertex) m_VertexSelection.AdoptPrepared(selection.Vertices);
            else
            {
                m_EdgeSelection.AdoptPrepared(selection.Edges);
                m_EffectiveEdgeVertices.AdoptPrepared(selection.Effective);
            }

            m_SelectionLocalCentroid = selection.Centroid;
            m_SelectionWorldCentroid = obj.Transform.TransformPoint(selection.Centroid);
            m_HasSelectionWorldCentroid = selection.Count != 0;
            m_SelectionWorldCentroidDirty = false;
            m_DirtyFlags |= MeshEditCacheDirtyFlags.Selection;
            FlushVisualDirtyFlags();
            CachePreparedSelectionCentroid(obj, selection.Centroid, selection.Count);
            CancelGpuBox();
            MeshGpu.CancelAction();
        }

        private bool AdvanceGpuCentroid(ProceduralObject obj)
        {
            // Main-thread slices read live positions only while the request's
            // geometry/camera/scope/selection revision still matches.
            var selection = m_PreparedGpuSelection;
            for (int i = 0; i < 4096; i++)
            {
                if (!m_GpuCentroidVertices.MoveNext())
                {
                    if (selection.Count != 0) selection.Centroid = new Vector3((float)(m_GpuCentroidX / selection.Count), (float)(m_GpuCentroidY / selection.Count), (float)(m_GpuCentroidZ / selection.Count));
                    return true;
                }

                var vertex = m_GpuCentroidVertices.Current;
                if ((uint)vertex.PartIndex >= (uint)obj.Parts.Count) continue;
                var part = obj.Parts[vertex.PartIndex];
                if ((uint)vertex.VertexId >= (uint)part.Topology.LogicalVertexCount) continue;
                var position = part.Topology.GetVertex(vertex.VertexId).GetPosition(part.Mesh);
                m_GpuCentroidX += position.x;
                m_GpuCentroidY += position.y;
                m_GpuCentroidZ += position.z;
                selection.Count++;
            }

            return false;
        }

        private static GpuSelection PrepareGpuSelection(MeshEditGpuRequest request, VertexRef[] oldVertices, StructuralEdgeRef[] oldEdges, BoxSelectionMode mode)
        {
            var g = request.Geometry;
            var result = new GpuSelection
            {
                Vertices = mode == BoxSelectionMode.Replace ? new HashSet<VertexRef>() : new HashSet<VertexRef>(oldVertices),
                Edges = mode == BoxSelectionMode.Replace ? new HashSet<StructuralEdgeRef>() : new HashSet<StructuralEdgeRef>(oldEdges),
                Effective = new HashSet<VertexRef>(),
            };
            for (int i = 0; i < g.Candidates.Length; i++)
            {
                if ((i & 255) == 0 && request.Invalid) return null;
                if ((request.Data[4 + (i >> 5)] & (1u << (i & 31))) == 0) continue;
                if (g.Mode == MeshSelectionMode.Vertex)
                {
                    if (mode == BoxSelectionMode.Remove) result.Vertices.Remove(g.Vertices[i]);
                    else result.Vertices.Add(g.Vertices[i]);
                }
                else if (mode == BoxSelectionMode.Remove) result.Edges.Remove(g.Edges[i]);
                else result.Edges.Add(g.Edges[i]);
            }

            if (g.Mode == MeshSelectionMode.Edge)
                foreach (var edge in result.Edges)
                {
                    if (request.Invalid) return null;
                    if (!g.EdgeSlots.TryGetValue(edge, out int slot)) continue;
                    var endpoints = g.Endpoints[slot];
                    result.Effective.Add(endpoints.A);
                    result.Effective.Add(endpoints.B);
                }

            return result;
        }
    }
}