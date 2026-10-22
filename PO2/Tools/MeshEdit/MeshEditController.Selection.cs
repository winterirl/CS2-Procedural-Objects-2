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
using MeshTopology = PO2.Geometry.Topology.MeshTopology;

namespace PO2.Tools.MeshEdit
{
    public sealed partial class MeshEditController
    {
        private readonly VertexSelection m_VertexSelection = new VertexSelection();

        private readonly StructuralEdgeSelection m_EdgeSelection = new StructuralEdgeSelection();

        // Edge mode still uses vertex-based transforms. This is the deduped set
        // of endpoints consumed by gizmo/centroid code, not another UI selection.
        private readonly VertexSelection m_EffectiveEdgeVertices = new VertexSelection();

        private readonly SurfaceSelection m_SurfaceSelection = new SurfaceSelection();

        // Numeric drafts should survive hover changes, so they cannot use the
        // broader visual version as their selection-change key.
        private int m_SelectionVersion;

        public int SelectionCount => m_VertexSelection.Count;
        public int EdgeSelectionCount => m_EdgeSelection.Count;

        public int EffectiveEdgeVertexCount =>
            m_EffectiveEdgeVertices.Count;

        public int SurfaceTriangleSelectionCount => m_SurfaceSelection.Count;

        public int EffectiveSelectionCount =>
            GetEffectiveVertexSelection().Count;

        public int SelectionVersion => m_SelectionVersion;

        public bool ApplySelectionClick(ProceduralObject proceduralObject, bool shiftPressed, bool controlPressed)
        {
            if (!ValidateSelectionTarget(proceduralObject) || SelectionMode != MeshSelectionMode.Vertex)
            {
                return false;
            }

            bool changed = false;

            if (m_HoveredVertex.HasValue)
            {
                VertexRef hovered = m_HoveredVertex.Value;

                if (controlPressed)
                {
                    changed = m_VertexSelection.Remove(hovered);
                }
                else if (shiftPressed)
                {
                    changed = m_VertexSelection.Toggle(hovered);
                }
                else
                {
                    changed = m_VertexSelection.SelectOnly(hovered);
                }
            }
            else if (!shiftPressed && !controlPressed)
            {
                changed = m_VertexSelection.Clear();
            }

            return FinishSelectionChange(proceduralObject, changed);
        }

        public bool SelectAll(ProceduralObject proceduralObject)
        {
            if (!ValidateSelectionTarget(proceduralObject))
            {
                return false;
            }

            bool changed = false;

            if (SelectionMode == MeshSelectionMode.Edge)
            {
                for (int partIndex = 0; partIndex < proceduralObject.Parts.Count; partIndex++)
                {
                    if (!IsPartInVertexScope(partIndex))
                    {
                        continue;
                    }

                    StructuralTopology topology = proceduralObject.Parts[partIndex].StructuralTopology;

                    for (int edgeId = 0; edgeId < topology.StructuralEdgeCount; edgeId++)
                    {
                        changed |= m_EdgeSelection.Add(new StructuralEdgeRef(partIndex, edgeId));
                    }
                }

                if (changed)
                {
                    RebuildEffectiveEdgeVertices(proceduralObject);
                }
            }
            else if (SelectionMode == MeshSelectionMode.Vertex)
            {
                for (int partIndex = 0; partIndex < proceduralObject.Parts.Count; partIndex++)
                {
                    if (!IsPartInVertexScope(partIndex))
                    {
                        continue;
                    }

                    MeshTopology topology = proceduralObject.Parts[partIndex].Topology;

                    for (int vertexId = 0; vertexId < topology.LogicalVertexCount; vertexId++)
                    {
                        changed |= m_VertexSelection.Add(new VertexRef(partIndex, vertexId));
                    }
                }
            }

            return FinishSelectionChange(proceduralObject, changed);
        }

        public bool InvertSelection(ProceduralObject proceduralObject)
        {
            if (!ValidateSelectionTarget(proceduralObject))
            {
                return false;
            }

            bool changed = false;

            if (SelectionMode == MeshSelectionMode.Edge)
            {
                for (int partIndex = 0; partIndex < proceduralObject.Parts.Count; partIndex++)
                {
                    if (!IsPartInVertexScope(partIndex))
                    {
                        continue;
                    }

                    StructuralTopology topology = proceduralObject.Parts[partIndex].StructuralTopology;

                    for (int edgeId = 0; edgeId < topology.StructuralEdgeCount; edgeId++)
                    {
                        StructuralEdgeRef edge = new StructuralEdgeRef(partIndex, edgeId);

                        if (m_EdgeSelection.Contains(edge))
                        {
                            changed |= m_EdgeSelection.Remove(edge);
                        }
                        else
                        {
                            changed |= m_EdgeSelection.Add(edge);
                        }
                    }
                }

                if (changed)
                {
                    RebuildEffectiveEdgeVertices(proceduralObject);
                }
            }
            else if (SelectionMode == MeshSelectionMode.Vertex)
            {
                for (int partIndex = 0; partIndex < proceduralObject.Parts.Count; partIndex++)
                {
                    if (!IsPartInVertexScope(partIndex))
                    {
                        continue;
                    }

                    MeshTopology topology = proceduralObject.Parts[partIndex].Topology;

                    for (int vertexId = 0; vertexId < topology.LogicalVertexCount; vertexId++)
                    {
                        VertexRef vertex = new VertexRef(partIndex, vertexId);

                        if (m_VertexSelection.Contains(vertex))
                        {
                            changed |= m_VertexSelection.Remove(vertex);
                        }
                        else
                        {
                            changed |= m_VertexSelection.Add(vertex);
                        }
                    }
                }
            }

            return FinishSelectionChange(proceduralObject, changed);
        }

        public bool SelectConnected(ProceduralObject proceduralObject)
        {
            if (!ValidateSelectionTarget(proceduralObject) || (SelectionMode != MeshSelectionMode.Vertex && SelectionMode != MeshSelectionMode.Edge) || m_ActivePartIndex < 0 || m_ActivePartIndex >= proceduralObject.Parts.Count)
            {
                return false;
            }

            bool changed = false;

            if (SelectionMode == MeshSelectionMode.Edge)
            {
                changed = m_EdgeSelection.Clear();
                StructuralTopology topology = proceduralObject.Parts[m_ActivePartIndex].StructuralTopology;

                for (int edgeId = 0; edgeId < topology.StructuralEdgeCount; edgeId++)
                {
                    changed |= m_EdgeSelection.Add(new StructuralEdgeRef(m_ActivePartIndex, edgeId));
                }

                RebuildEffectiveEdgeVertices(proceduralObject);
            }
            else
            {
                changed = m_VertexSelection.Clear();
                MeshTopology topology = proceduralObject.Parts[m_ActivePartIndex].Topology;

                for (int vertexId = 0; vertexId < topology.LogicalVertexCount; vertexId++)
                {
                    changed |= m_VertexSelection.Add(new VertexRef(m_ActivePartIndex, vertexId));
                }
            }

            return FinishSelectionChange(proceduralObject, changed);
        }

        private bool ValidateSelectionTarget(ProceduralObject proceduralObject)
        {
            return proceduralObject != null && IsActive && m_ObjectId.HasValue && proceduralObject.Id == m_ObjectId.Value;
        }

        private bool FinishSelectionChange(ProceduralObject proceduralObject, bool changed)
        {
            if (!changed)
            {
                return false;
            }

            m_DirtyFlags |= MeshEditCacheDirtyFlags.Selection;

            m_SelectionWorldCentroidDirty = true;
            RecalculateSelectionWorldCentroid(proceduralObject);

            FlushVisualDirtyFlags();
            return true;
        }

        private VertexSelection GetEffectiveVertexSelection()
        {
            if (SelectionMode == MeshSelectionMode.Edge)
            {
                return m_EffectiveEdgeVertices;
            }

            if (SelectionMode == MeshSelectionMode.Vertex)
            {
                return m_VertexSelection;
            }

            // Surface mode has no meaningful vertex transform selection.
            // Returning the normally-empty edge set keeps centroid/gizmo code
            // on the same path without allocating a dummy selection.
            return m_EffectiveEdgeVertices;
        }

        public bool SetSelectionMode(ProceduralObject proceduralObject, MeshSelectionMode mode)
        {
            if (!ValidateSelectionTarget(proceduralObject) || (mode != MeshSelectionMode.Vertex && mode != MeshSelectionMode.Edge && mode != MeshSelectionMode.SurfaceArea) || mode == SelectionMode)
            {
                return false;
            }

            CancelAlignment();
            CancelVertexSlide();
            m_HoveredVertex = null;
            m_HoveredEdge = null;
            m_HoveredSurface = null;
            m_HasLastMousePosition = false;

            if (mode == MeshSelectionMode.Edge)
            {
                // Vertex -> Edge selects every structural edge whose two logical
                // endpoints are selected. This mirrors the existing Edge -> Vertex
                // endpoint promotion without trying to remember a previous edge set.
                if (SelectionMode == MeshSelectionMode.Vertex)
                {
                    PromoteSelectedVertexPairsToEdges(proceduralObject);
                }
                else
                {
                    m_EdgeSelection.Clear();
                    m_EffectiveEdgeVertices.Clear();
                }

                m_VertexSelection.Clear();
                m_SurfaceSelection.Clear();
            }
            else if (mode == MeshSelectionMode.Vertex)
            {
                // Edge -> Vertex keeps the endpoints selected. It makes mode
                // switching useful instead of feeling like data loss.
                if (SelectionMode == MeshSelectionMode.Edge)
                {
                    PromoteSelectedEdgeEndpointsToVertices(proceduralObject);
                }
                else
                {
                    m_VertexSelection.Clear();
                }

                m_EdgeSelection.Clear();
                m_EffectiveEdgeVertices.Clear();
                m_SurfaceSelection.Clear();
            }
            else
            {
                m_VertexSelection.Clear();
                m_EdgeSelection.Clear();
                m_EffectiveEdgeVertices.Clear();
                m_SurfaceSelection.Clear();
            }

            SelectionMode = mode;
            m_SelectionWorldCentroid = Vector3.zero;
            m_HasSelectionWorldCentroid = false;
            m_SelectionWorldCentroidDirty = true;
            m_DirtyFlags |= MeshEditCacheDirtyFlags.Selection | MeshEditCacheDirtyFlags.Hover | MeshEditCacheDirtyFlags.Projection;

            // Rebuild the destination mode's cache. Edge mode leaves the vertex frame empty.

            FlushVisualDirtyFlags();
            return true;
        }

        public bool SetVertexScope(ProceduralObject proceduralObject, MeshEditVertexScope scope, int activePartIndex)
        {
            if (!ValidateSelectionTarget(proceduralObject) || (scope != MeshEditVertexScope.CurrentPart && scope != MeshEditVertexScope.AllParts) || scope == VertexScope)
            {
                return false;
            }

            m_ActivePartIndex = ClampPartIndex(proceduralObject, activePartIndex);
            VertexScope = scope;

            bool selectionChanged = false;
            if (scope == MeshEditVertexScope.CurrentPart)
            {
                selectionChanged = RemoveSelectionOutsideActivePart(proceduralObject);
            }

            ClearTransientHover();
            m_DirtyFlags |= MeshEditCacheDirtyFlags.Projection | MeshEditCacheDirtyFlags.Hover;

            if (selectionChanged)
            {
                m_DirtyFlags |= MeshEditCacheDirtyFlags.Selection;
                m_SelectionWorldCentroidDirty = true;
            }

            FlushVisualDirtyFlags();
            return true;
        }

        public void OnActivePartChanged(ProceduralObject proceduralObject, int activePartIndex)
        {
            if (!ValidateSelectionTarget(proceduralObject))
            {
                return;
            }

            int nextPartIndex = ClampPartIndex(proceduralObject, activePartIndex);
            if (nextPartIndex == m_ActivePartIndex)
            {
                return;
            }

            m_ActivePartIndex = nextPartIndex;
            ClearTransientHover();

            bool selectionChanged = false;
            if (VertexScope == MeshEditVertexScope.CurrentPart)
            {
                selectionChanged = m_VertexSelection.Clear();
                selectionChanged |= m_EdgeSelection.Clear();
                selectionChanged |= m_EffectiveEdgeVertices.Clear();
                m_DirtyFlags |= MeshEditCacheDirtyFlags.Projection;
            }

            if (m_SurfaceSelection.Clear())
            {
                selectionChanged = true;
            }

            if (selectionChanged)
            {
                m_DirtyFlags |= MeshEditCacheDirtyFlags.Selection;
                m_SelectionWorldCentroid = Vector3.zero;
                m_HasSelectionWorldCentroid = false;
                m_SelectionWorldCentroidDirty = true;
            }

            m_DirtyFlags |= MeshEditCacheDirtyFlags.Hover;
            FlushVisualDirtyFlags();
        }

        private bool RemoveSelectionOutsideActivePart(ProceduralObject proceduralObject)
        {
            bool changed = false;

            VertexRef[] vertices = GetSelectionSnapshot();
            for (int i = 0; i < vertices.Length; i++)
            {
                if (vertices[i].PartIndex != m_ActivePartIndex)
                {
                    changed |= m_VertexSelection.Remove(vertices[i]);
                }
            }

            StructuralEdgeRef[] edges = GetEdgeSelectionSnapshot();
            for (int i = 0; i < edges.Length; i++)
            {
                if (edges[i].PartIndex != m_ActivePartIndex)
                {
                    changed |= m_EdgeSelection.Remove(edges[i]);
                }
            }

            if (changed && SelectionMode == MeshSelectionMode.Edge)
            {
                RebuildEffectiveEdgeVertices(proceduralObject);
            }

            return changed;
        }

        public bool IsPartInCurrentScope(int partIndex)
        {
            return IsActive && partIndex >= 0 && IsPartInVertexScope(partIndex);
        }

        private bool IsPartInVertexScope(int partIndex)
        {
            return VertexScope == MeshEditVertexScope.AllParts || partIndex == m_ActivePartIndex;
        }

        private static int ClampPartIndex(ProceduralObject proceduralObject, int partIndex)
        {
            if (proceduralObject == null || proceduralObject.Parts.Count == 0)
            {
                return 0;
            }

            return Mathf.Clamp(partIndex, 0, proceduralObject.Parts.Count - 1);
        }

        public StructuralEdgeRef[] GetEdgeSelectionSnapshot()
        {
            StructuralEdgeRef[] snapshot = new StructuralEdgeRef[m_EdgeSelection.Count];
            int index = 0;

            foreach (StructuralEdgeRef edge in m_EdgeSelection.Edges)
            {
                snapshot[index++] = edge;
            }

            return snapshot;
        }

        public bool TryGetSingleSelectedEdge(out StructuralEdgeRef edge)
        {
            edge = default;

            if (m_EdgeSelection.Count != 1)
            {
                return false;
            }

            foreach (StructuralEdgeRef selected in m_EdgeSelection.Edges)
            {
                edge = selected;
                return true;
            }

            return false;
        }

        public bool ReplaceEdgeSelection(ProceduralObject proceduralObject, StructuralEdgeRef[] edges)
        {
            if (!ValidateSelectionTarget(proceduralObject) || SelectionMode != MeshSelectionMode.Edge)
            {
                return false;
            }

            m_EdgeSelection.Clear();
            bool changed = false;

            if (edges != null)
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    StructuralEdgeRef edge = edges[i];

                    if (edge.PartIndex < 0 || edge.PartIndex >= proceduralObject.Parts.Count)
                    {
                        continue;
                    }

                    StructuralTopology topology = proceduralObject.Parts[edge.PartIndex].StructuralTopology;

                    if (edge.EdgeId < 0 || edge.EdgeId >= topology.StructuralEdgeCount)
                    {
                        continue;
                    }

                    changed |= m_EdgeSelection.Add(edge);
                }
            }

            RebuildEffectiveEdgeVertices(proceduralObject);
            m_SelectionWorldCentroid = Vector3.zero;
            m_HasSelectionWorldCentroid = false;
            m_SelectionWorldCentroidDirty = true;
            m_DirtyFlags |= MeshEditCacheDirtyFlags.Selection;
            FlushVisualDirtyFlags();
            return changed;
        }

        private void RebuildEffectiveEdgeVertices(ProceduralObject proceduralObject)
        {
            m_EffectiveEdgeVertices.Clear();

            if (proceduralObject == null)
            {
                return;
            }

            foreach (StructuralEdgeRef edgeRef in m_EdgeSelection.Edges)
            {
                if (edgeRef.PartIndex < 0 || edgeRef.PartIndex >= proceduralObject.Parts.Count)
                {
                    continue;
                }

                StructuralTopology topology = proceduralObject.Parts[edgeRef.PartIndex].StructuralTopology;

                if (edgeRef.EdgeId < 0 || edgeRef.EdgeId >= topology.StructuralEdgeCount)
                {
                    continue;
                }

                StructuralEdge edge = topology.GetEdge(edgeRef.EdgeId);

                // HashSet semantics collapse endpoints shared by several edges.
                m_EffectiveEdgeVertices.Add(new VertexRef(edgeRef.PartIndex, edge.LogicalVertexA));
                m_EffectiveEdgeVertices.Add(new VertexRef(edgeRef.PartIndex, edge.LogicalVertexB));
            }
        }

        private void PromoteSelectedEdgeEndpointsToVertices(ProceduralObject proceduralObject)
        {
            RebuildEffectiveEdgeVertices(proceduralObject);
            m_VertexSelection.Clear();

            foreach (VertexRef vertex in m_EffectiveEdgeVertices.Vertices)
            {
                m_VertexSelection.Add(vertex);
            }
        }

        private void PromoteSelectedVertexPairsToEdges(ProceduralObject proceduralObject)
        {
            m_EdgeSelection.Clear();
            m_EffectiveEdgeVertices.Clear();

            if (proceduralObject == null || m_VertexSelection.Count < 2)
            {
                return;
            }

            for (int partIndex = 0; partIndex < proceduralObject.Parts.Count; partIndex++)
            {
                // Edge mode only exposes structural edges in the active scope.
                // Do not create hidden selections on parts the user cannot see.
                if (!IsPartInVertexScope(partIndex))
                {
                    continue;
                }

                StructuralTopology topology = proceduralObject.Parts[partIndex].StructuralTopology;

                for (int edgeId = 0; edgeId < topology.StructuralEdgeCount; edgeId++)
                {
                    StructuralEdge edge = topology.GetEdge(edgeId);

                    VertexRef vertexA = new VertexRef(partIndex, edge.LogicalVertexA);
                    VertexRef vertexB = new VertexRef(partIndex, edge.LogicalVertexB);

                    if (!m_VertexSelection.Contains(vertexA) || !m_VertexSelection.Contains(vertexB))
                    {
                        continue;
                    }

                    m_EdgeSelection.Add(new StructuralEdgeRef(partIndex, edgeId));
                }
            }

            RebuildEffectiveEdgeVertices(proceduralObject);
        }

        public bool ApplyEdgeSelectionClick(ProceduralObject proceduralObject, bool shiftPressed, bool controlPressed)
        {
            if (!ValidateSelectionTarget(proceduralObject) || SelectionMode != MeshSelectionMode.Edge)
            {
                return false;
            }

            bool changed = false;

            if (m_HoveredEdge.HasValue)
            {
                StructuralEdgeRef hovered = m_HoveredEdge.Value;

                if (controlPressed)
                {
                    changed = m_EdgeSelection.Remove(hovered);
                }
                else if (shiftPressed)
                {
                    changed = m_EdgeSelection.Toggle(hovered);
                }
                else
                {
                    changed = m_EdgeSelection.SelectOnly(hovered);
                }
            }
            else if (!shiftPressed && !controlPressed)
            {
                changed = m_EdgeSelection.Clear();
            }

            if (changed)
            {
                RebuildEffectiveEdgeVertices(proceduralObject);
            }

            return FinishSelectionChange(proceduralObject, changed);
        }

        public bool ApplyEdgeSelectionSet(ProceduralObject proceduralObject, StructuralEdgeRef[] edges, bool shiftPressed, bool controlPressed)
        {
            if (!ValidateSelectionTarget(proceduralObject) || SelectionMode != MeshSelectionMode.Edge || edges == null || edges.Length == 0)
            {
                return false;
            }

            bool changed = false;

            if (controlPressed)
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    changed |= m_EdgeSelection.Remove(edges[i]);
                }
            }
            else if (shiftPressed)
            {
                bool allSelected = true;

                for (int i = 0; i < edges.Length; i++)
                {
                    if (!m_EdgeSelection.Contains(edges[i]))
                    {
                        allSelected = false;
                        break;
                    }
                }

                for (int i = 0; i < edges.Length; i++)
                {
                    changed |= allSelected ? m_EdgeSelection.Remove(edges[i]) : m_EdgeSelection.Add(edges[i]);
                }
            }
            else
            {
                changed |= m_EdgeSelection.Clear();

                for (int i = 0; i < edges.Length; i++)
                {
                    changed |= m_EdgeSelection.Add(edges[i]);
                }
            }

            if (changed)
            {
                RebuildEffectiveEdgeVertices(proceduralObject);
            }

            return FinishSelectionChange(proceduralObject, changed);
        }

        public bool ApplySurfaceSelectionSet(ProceduralObject proceduralObject, SurfaceRef[] surfaces, bool shiftPressed, bool controlPressed)
        {
            if (!ValidateSelectionTarget(proceduralObject) || SelectionMode != MeshSelectionMode.SurfaceArea)
            {
                return false;
            }

            bool changed = false;
            if (surfaces == null || surfaces.Length == 0)
            {
                if (!shiftPressed && !controlPressed)
                {
                    changed = m_SurfaceSelection.Clear();
                }

                return FinishSelectionChange(proceduralObject, changed);
            }

            if (controlPressed)
            {
                for (int i = 0; i < surfaces.Length; i++)
                {
                    changed |= m_SurfaceSelection.Remove(surfaces[i]);
                }
            }
            else if (shiftPressed)
            {
                for (int i = 0; i < surfaces.Length; i++)
                {
                    changed |= m_SurfaceSelection.Add(surfaces[i]);
                }
            }
            else
            {
                changed |= m_SurfaceSelection.Clear();
                for (int i = 0; i < surfaces.Length; i++)
                {
                    changed |= m_SurfaceSelection.Add(surfaces[i]);
                }
            }

            return FinishSelectionChange(proceduralObject, changed);
        }

        public bool SelectAllSurfaces(ProceduralObject proceduralObject, int partIndex)
        {
            if (!ValidateSelectionTarget(proceduralObject) || SelectionMode != MeshSelectionMode.SurfaceArea || partIndex < 0 || partIndex >= proceduralObject.Parts.Count)
            {
                return false;
            }

            bool changed = false;
            int triangleCount = proceduralObject.Parts[partIndex].Mesh.Triangles.Length / 3;
            for (int triangleId = 0; triangleId < triangleCount; triangleId++)
            {
                changed |= m_SurfaceSelection.Add(new SurfaceRef(partIndex, triangleId));
            }

            return FinishSelectionChange(proceduralObject, changed);
        }

        public bool InvertSurfaceSelection(ProceduralObject proceduralObject, int partIndex)
        {
            if (!ValidateSelectionTarget(proceduralObject) || SelectionMode != MeshSelectionMode.SurfaceArea || partIndex < 0 || partIndex >= proceduralObject.Parts.Count)
            {
                return false;
            }

            bool changed = false;
            int triangleCount = proceduralObject.Parts[partIndex].Mesh.Triangles.Length / 3;
            for (int triangleId = 0; triangleId < triangleCount; triangleId++)
            {
                SurfaceRef surface = new SurfaceRef(partIndex, triangleId);
                changed |= m_SurfaceSelection.Contains(surface) ? m_SurfaceSelection.Remove(surface) : m_SurfaceSelection.Add(surface);
            }

            return FinishSelectionChange(proceduralObject, changed);
        }

        public int CountSelectedSurfaceUnits(ProceduralObject proceduralObject)
        {
            if (proceduralObject == null)
            {
                return 0;
            }

            // UI selection is triangles, but a structural quad should count as
            // one surface. Only its first triangle contributes to this number.
            int count = 0;
            foreach (SurfaceRef surface in m_SurfaceSelection.Surfaces)
            {
                if (surface.PartIndex < 0 || surface.PartIndex >= proceduralObject.Parts.Count)
                {
                    continue;
                }

                MeshPart part = proceduralObject.Parts[surface.PartIndex];
                if (part.TryGetTriangleStructuralFaceId(surface.TriangleId, out int faceId) && part.TryGetStructuralFaceTriangles(faceId, out StructuralFaceTriangles faceTriangles))
                {
                    if (surface.TriangleId == faceTriangles.TriangleA)
                    {
                        count++;
                    }
                }
                else
                {
                    count++;
                }
            }

            return count;
        }

        public SurfaceRef[] GetSurfaceSelectionSnapshot()
        {
            SurfaceRef[] snapshot = new SurfaceRef[m_SurfaceSelection.Count];
            int index = 0;
            foreach (SurfaceRef surface in m_SurfaceSelection.Surfaces)
            {
                snapshot[index++] = surface;
            }

            return snapshot;
        }

        public bool ClearSelection()
        {
            bool changed;
            if (SelectionMode == MeshSelectionMode.Edge)
            {
                changed = m_EdgeSelection.Clear();
            }
            else if (SelectionMode == MeshSelectionMode.SurfaceArea)
            {
                changed = m_SurfaceSelection.Clear();
            }
            else
            {
                changed = m_VertexSelection.Clear();
            }

            if (!changed)
            {
                return false;
            }

            if (SelectionMode == MeshSelectionMode.Edge)
            {
                m_EffectiveEdgeVertices.Clear();
            }

            m_DirtyFlags |= MeshEditCacheDirtyFlags.Selection;

            m_SelectionWorldCentroid = Vector3.zero;
            m_HasSelectionWorldCentroid = false;
            m_SelectionWorldCentroidDirty = false;

            FlushVisualDirtyFlags();
            return true;
        }

        public VertexRef[] GetSelectionSnapshot()
        {
            VertexRef[] snapshot = new VertexRef[m_VertexSelection.Count];
            int index = 0;

            foreach (VertexRef vertex in m_VertexSelection.Vertices)
            {
                snapshot[index++] = vertex;
            }

            return snapshot;
        }

        public VertexRef[] GetEffectiveSelectionSnapshot()
        {
            VertexSelection selection = GetEffectiveVertexSelection();

            VertexRef[] snapshot = new VertexRef[selection.Count];

            int index = 0;

            foreach (VertexRef vertex in selection.Vertices)
            {
                snapshot[index++] = vertex;
            }

            return snapshot;
        }

        public bool ReplaceSelection(ProceduralObject proceduralObject, VertexRef[] vertices)
        {
            if (!ValidateSelectionTarget(proceduralObject) || SelectionMode != MeshSelectionMode.Vertex)
            {
                return false;
            }

            m_VertexSelection.Clear();
            bool changed = false;

            if (vertices != null)
            {
                for (int i = 0; i < vertices.Length; i++)
                {
                    VertexRef vertex = vertices[i];

                    if (vertex.PartIndex < 0 || vertex.PartIndex >= proceduralObject.Parts.Count)
                    {
                        continue;
                    }

                    MeshTopology topology = proceduralObject.Parts[vertex.PartIndex].Topology;

                    if (vertex.VertexId < 0 || vertex.VertexId >= topology.LogicalVertexCount)
                    {
                        continue;
                    }

                    changed |= m_VertexSelection.Add(vertex);
                }
            }

            m_DirtyFlags |= MeshEditCacheDirtyFlags.Selection;
            m_SelectionWorldCentroidDirty = true;
            RecalculateSelectionWorldCentroid(proceduralObject);
            FlushVisualDirtyFlags();
            return changed;
        }

        public bool IsSelected(VertexRef vertex)
        {
            return m_VertexSelection.Contains(vertex);
        }
    }
}