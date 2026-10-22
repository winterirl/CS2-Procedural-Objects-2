using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using PO2.Core;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;

namespace PO2.Tools.MeshEdit
{
    // Caches selection and camera projections. Validate the active object first
    // because its mesh can change outside this controller.
    public sealed partial class MeshEditController
    {
        internal ProceduralObjectSystem HistorySystem { private get; set; }

        public void OnGeometryChanged(ProceduralObject obj)
        {
            if (!IsActive || obj == null || !m_ObjectId.HasValue || obj.Id != m_ObjectId.Value) return;
            MeshGpu?.InvalidateGeometry();
            m_SelectionWorldCentroidDirty = true;
            m_DirtyFlags |= MeshEditCacheDirtyFlags.Geometry | MeshEditCacheDirtyFlags.Hover;
            InvalidateXRayEdgeCache();
            // Part/source versions now distinguish a refit from a topology rebuild.
            m_HasLastMousePosition = false;
            FlushVisualDirtyFlags();
        }

        private int? m_ObjectId;

        // Projection work is much more expensive than changing marker colors,
        // so these dirty causes are intentionally tracked separately.
        private MeshEditCacheDirtyFlags m_DirtyFlags = MeshEditCacheDirtyFlags.All;
        private uint m_VisualVersion;

        public bool IsActive => m_ObjectId.HasValue;
        public int? ObjectId => m_ObjectId;

        public MeshSelectionMode SelectionMode { get; private set; } = MeshSelectionMode.Vertex;

        public MeshEditVisibilityMode VisibilityMode { get; private set; } = MeshEditVisibilityMode.XRay;

        public MeshEditVertexScope VertexScope { get; private set; } = MeshEditVertexScope.CurrentPart;

        private int m_ActivePartIndex;
        public uint VisualVersion => m_VisualVersion;

        public bool Enter(ProceduralObject proceduralObject, int activePartIndex)
        {
            if (proceduralObject == null)
            {
                return false;
            }

            m_SelectionCentroid.Dispose();
            m_VertexEditPreparation.Dispose();
            MeshGpu?.Invalidate();
            m_ObjectId = proceduralObject.Id;
            SelectionMode = MeshSelectionMode.Vertex;
            VisibilityMode = MeshEditVisibilityMode.XRay;
            VertexScope = MeshEditVertexScope.CurrentPart;
            m_ActivePartIndex = ClampPartIndex(proceduralObject, activePartIndex);

            m_VertexSelection.Clear();
            m_EdgeSelection.Clear();
            m_EffectiveEdgeVertices.Clear();
            m_SurfaceSelection.Clear();
            m_HoveredVertex = null;
            m_HoveredEdge = null;
            m_HoveredSurface = null;
            m_FrameDataCount = 0;
            m_EdgeFrameDataCount = 0;
            InvalidateXRayEdgeCache();
            m_SolidVisibilityBvh.Invalidate();

            m_DirtyFlags = MeshEditCacheDirtyFlags.All;
            m_HasLastTransform = false;
            m_HasLastCameraState = false;
            m_HasLastMousePosition = false;

            m_SelectionWorldCentroid = Vector3.zero;
            m_HasSelectionWorldCentroid = false;
            m_SelectionWorldCentroidDirty = true;
            m_VertexHoverSuppressed = false;
            m_AlignmentAxis = VertexAlignmentAxis.None;
            m_VertexSlideTargeting = false;
            m_AlignmentAffectedParts.Clear();
            m_NumericMoveAffectedParts.Clear();

            // Keep versions increasing across sessions so renderers can detect a new target.
            m_VisualVersion++;

            return true;
        }

        public void Exit()
        {
            m_SelectionCentroid.Dispose();
            m_VertexEditPreparation.Dispose();
            UsesGpuMarkers = false;
            MeshGpu?.Stop();
            CancelGpuBox();
            m_ObjectId = null;
            m_VertexSelection.Clear();
            m_EdgeSelection.Clear();
            m_EffectiveEdgeVertices.Clear();
            m_SurfaceSelection.Clear();
            m_HoveredVertex = null;
            m_HoveredEdge = null;
            m_HoveredSurface = null;
            m_FrameDataCount = 0;
            m_EdgeFrameDataCount = 0;
            InvalidateXRayEdgeCache();
            m_SolidVisibilityBvh.Invalidate();

            m_DirtyFlags = MeshEditCacheDirtyFlags.All;
            m_HasLastTransform = false;
            m_HasLastCameraState = false;
            m_HasLastMousePosition = false;

            m_SelectionWorldCentroid = Vector3.zero;
            m_HasSelectionWorldCentroid = false;
            m_SelectionWorldCentroidDirty = true;
            m_VertexHoverSuppressed = false;
            m_AlignmentAxis = VertexAlignmentAxis.None;
            m_VertexSlideTargeting = false;
            m_AlignmentAffectedParts.Clear();
            m_NumericMoveAffectedParts.Clear();
            VertexScope = MeshEditVertexScope.CurrentPart;
            m_ActivePartIndex = 0;

            m_VisualVersion++;
        }

        public void OnTopologyChanged(ProceduralObject proceduralObject)
        {
            MeshGpu?.Invalidate();
            if (!IsActive || !m_ObjectId.HasValue || proceduralObject == null || proceduralObject.Id != m_ObjectId.Value)
            {
                return;
            }

            // Logical vertex ids and frame-cache sizes are derived from
            // topology. Invalidate them explicitly rather than relying on a
            // later geometry-version check to encounter stale selection refs.
            m_VertexSelection.Clear();
            m_EdgeSelection.Clear();
            m_EffectiveEdgeVertices.Clear();
            m_SurfaceSelection.Clear();
            InvalidateTopologyState();
        }

        private void InvalidateTopologyState()
        {
            m_HasPreparedCentroid = false;
            m_HoveredVertex = null;
            m_HoveredEdge = null;
            m_HoveredSurface = null;
            m_FrameDataCount = 0;
            m_EdgeFrameDataCount = 0;
            InvalidateXRayEdgeCache();
            m_SolidVisibilityBvh.Invalidate();
            m_SelectionWorldCentroid = Vector3.zero;
            m_HasSelectionWorldCentroid = false;
            m_SelectionWorldCentroidDirty = true;
            m_AlignmentAxis = VertexAlignmentAxis.None;
            m_VertexSlideTargeting = false;
            m_VertexHoverSuppressed = false;
            m_NumericMoveAffectedParts.Clear();

            m_DirtyFlags = MeshEditCacheDirtyFlags.All;
            m_HasLastMousePosition = false;
            m_HasLastCameraState = false;
            m_HasLastTransform = false;

            m_VisualVersion++;
        }

        public void UpdateFrame(ProceduralObject proceduralObject, Camera camera, Vector2 mousePosition, bool updateHover = true)
        {
            if (!IsActive || !m_ObjectId.HasValue || proceduralObject == null || proceduralObject.Id != m_ObjectId.Value || camera == null)
            {
                return;
            }

            DetectProjectionChanges(proceduralObject, camera);

            MeshEditCacheDirtyFlags centroidCauses = MeshEditCacheDirtyFlags.Geometry | MeshEditCacheDirtyFlags.Transform | MeshEditCacheDirtyFlags.Selection;

            if ((m_DirtyFlags & centroidCauses) != 0)
            {
                m_SelectionWorldCentroidDirty = true;
            }

            if (UpdateMeshGpu(proceduralObject, camera, mousePosition, updateHover)) return;

            // Geometry, transform, camera, or viewport changes rebuild the
            // active projection cache. Selection/hover only bump VisualVersion.
            bool projectionChanged = (m_DirtyFlags & MeshEditCacheDirtyFlags.Projection) != 0;

            if (projectionChanged)
            {
                bool edgeWorldDataChanged = SelectionMode == MeshSelectionMode.Edge && (m_DirtyFlags & (MeshEditCacheDirtyFlags.Geometry | MeshEditCacheDirtyFlags.Transform)) != 0;

                RebuildProjectionCache(proceduralObject, camera, edgeWorldDataChanged);

                m_DirtyFlags &= ~MeshEditCacheDirtyFlags.Projection;

                if (edgeWorldDataChanged)
                {
                    // Camera and viewport changes still rebuild the CPU pick
                    // projection, but GPU endpoint data remains valid.
                    m_EdgeWorldVersion++;
                }

                m_ProjectionVersion++;
            }

            if (m_SelectionWorldCentroidDirty)
            {
                RecalculateSelectionWorldCentroid(proceduralObject);
            }

            bool mouseChanged = !m_HasLastMousePosition || (mousePosition - m_LastMousePosition).sqrMagnitude > kMouseMovementEpsilonSquared;

            if (SelectionMode != MeshSelectionMode.Vertex)
            {
                if (m_HoveredVertex.HasValue)
                {
                    m_HoveredVertex = null;
                    m_DirtyFlags |= MeshEditCacheDirtyFlags.Hover;
                }

                m_LastMousePosition = mousePosition;
                m_HasLastMousePosition = true;
            }
            else if (updateHover && (projectionChanged || mouseChanged))
            {
                UpdateHoveredVertex(camera, mousePosition);

                m_LastMousePosition = mousePosition;
                m_HasLastMousePosition = true;
            }

            FlushVisualDirtyFlags();
        }

        private void FlushVisualDirtyFlags()
        {
            if ((m_DirtyFlags & MeshEditCacheDirtyFlags.Visual) == 0)
            {
                return;
            }

            if ((m_DirtyFlags & MeshEditCacheDirtyFlags.Selection) != 0)
            {
                m_SelectionVersion++;
            }

            // Consumers only need to know that visual state changed, so one
            // monotonic version is enough even if several flags changed at once.
            m_VisualVersion++;
            m_DirtyFlags &= ~MeshEditCacheDirtyFlags.Visual;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        public bool IsEdgeSelected(StructuralEdgeRef edge)
        {
            return m_EdgeSelection.Contains(edge);
        }

        public void Dispose()
        {
            m_SelectionCentroid.Dispose();
            m_VertexEditPreparation.Dispose();
            DisposeProjectionJobBuffers();
            DisposeXRayEdgeProjectionBuffers();
            m_SolidVisibilityBvh.Dispose();
        }
    }
}