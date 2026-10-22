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
using PO2.Tools.Gizmos;
using PO2.Tools.MeshEdit;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private void UpdateMeshEdgeSelectionInput(ProceduralObject editedObject, Camera camera, Vector2 mousePosition, bool primaryPressed, bool primaryReleased, bool shiftPressed, bool controlPressed, bool altPressed)
        {
            m_MeshEditController.SetVertexHoverSuppressed(true);

            bool hasEdgeGizmoContext = TryGetGizmoContext(camera, out GizmoContext edgeGizmoContext);

            Ray edgeMouseRay = default;

            if (hasEdgeGizmoContext)
            {
                edgeMouseRay = camera.ScreenPointToRay(mousePosition);

                UpdateGizmoHover(edgeMouseRay, edgeGizmoContext);
            }
            else
            {
                ClearGizmoHover();
            }

            if (m_HoveredGizmoHandle == GizmoHandle.None)
            {
                if (m_MeshEditController.TryPickEdge(mousePosition, out StructuralEdgeRef hoveredEdge))
                {
                    m_MeshEditController.SetHoveredEdge(hoveredEdge);
                }
                else
                {
                    m_MeshEditController.SetHoveredEdge(null);
                }
            }
            else
            {
                m_MeshEditController.SetHoveredEdge(null);
            }

            if (m_MeshEditController.UsesGpuMarkers && (m_GpuPointer != null || !primaryPressed || m_HoveredGizmoHandle == GizmoHandle.None))
            {
                HandleGpuPointer(editedObject, camera, mousePosition, primaryPressed, primaryReleased, shiftPressed, controlPressed, altPressed, GpuPointerKind.Edge);
                return;
            }

            if (!primaryPressed)
            {
                return;
            }

            if (hasEdgeGizmoContext && m_HoveredGizmoHandle != GizmoHandle.None)
            {
                BeginGizmoDrag(camera, edgeMouseRay, edgeGizmoContext, m_HoveredGizmoHandle);

                return;
            }

            if (!m_MeshEditController.HasHoveredEdge)
            {
                BoxSelectionMode mode = controlPressed ? BoxSelectionMode.Remove : shiftPressed ? BoxSelectionMode.Add : BoxSelectionMode.Replace;

                m_BoxSelection.Begin(mousePosition, mode);
                m_BoxSelectionNormalizedRect = Rect.zero;

                return;
            }

            if (altPressed && m_MeshEditController.TryGetHoveredEdge(out StructuralEdgeRef loopSource))
            {
                MeshPart loopPart = editedObject.Parts[loopSource.PartIndex];

                if (TopologyEditor.TryBuildStructuralEdgeLoop(loopPart.StructuralTopology, loopSource.EdgeId, out int[] loopEdgeIds, out bool loopClosed))
                {
                    StructuralEdgeRef[] loopSelection = new StructuralEdgeRef[loopEdgeIds.Length];

                    for (int i = 0; i < loopEdgeIds.Length; i++)
                    {
                        loopSelection[i] = new StructuralEdgeRef(loopSource.PartIndex, loopEdgeIds[i]);
                    }

                    m_MeshEditController.ApplyEdgeSelectionSet(editedObject, loopSelection, shiftPressed, controlPressed);
                }
            }
            else
            {
                m_MeshEditController.ApplyEdgeSelectionClick(editedObject, shiftPressed, controlPressed);
            }
        }
    }
}