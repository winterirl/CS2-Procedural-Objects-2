using Game;
using Game.Input;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        public bool IsMeshEditAlignmentTargeting =>
            m_MeshEditController != null && m_MeshEditController.IsAlignmentTargeting;

        public VertexAlignmentAxis MeshEditAlignmentAxis =>
            m_MeshEditController != null ? m_MeshEditController.AlignmentAxis : VertexAlignmentAxis.None;

        public bool IsMeshEditAlignmentTargetCandidate(VertexRef vertex)
        {
            return m_MeshEditController != null && m_MeshEditController.IsAlignmentTargetCandidate(vertex);
        }

        public void BeginMeshVertexAlignmentX()
        {
            BeginMeshVertexAlignment(VertexAlignmentAxis.X);
        }

        public void BeginMeshVertexAlignmentY()
        {
            BeginMeshVertexAlignment(VertexAlignmentAxis.Y);
        }

        public void BeginMeshVertexAlignmentZ()
        {
            BeginMeshVertexAlignment(VertexAlignmentAxis.Z);
        }

        public void CancelMeshVertexAlignment()
        {
            if (m_MeshEditController != null && m_MeshEditController.CancelAlignment())
            {
                ClearGizmoHover();
            }
        }

        private void BeginMeshVertexAlignment(VertexAlignmentAxis axis)
        {
            if (!TryGetMeshEditObject(out ProceduralObject editedObject) || m_MeshEditController.SelectionCount == 0 || IsMeshTopologyInteractionActive)
            {
                return;
            }

            CancelBoxSelection();
            ClearGizmoHover();

            if (m_MeshEditController.BeginAlignment(editedObject, axis))
            {
            }
        }

        private void UpdateMeshAlignmentInput(ProceduralObject editedObject, Camera camera, Vector2 mousePosition, bool primaryPressed, bool primaryReleased, bool shiftPressed, bool controlPressed, bool altPressed)
        {
            ClearGizmoHover();
            m_MeshEditController.SetVertexHoverSuppressed(false);

            if (m_MeshEditController.UsesGpuMarkers)
            {
                HandleGpuPointer(editedObject, camera, mousePosition, primaryPressed, primaryReleased, shiftPressed, controlPressed, altPressed, GpuPointerKind.Alignment);
                return;
            }

            if (primaryPressed && m_MeshEditController.HasHoveredVertex)
            {
                m_MeshEditController.ApplyAlignmentToHoveredVertex(editedObject);
            }
        }
    }
}