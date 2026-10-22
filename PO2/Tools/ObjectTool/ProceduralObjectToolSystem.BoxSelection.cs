using Game;
using Game.Input;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry.Editing;
using PO2.Tools.MeshEdit;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private readonly BoxSelectionState m_BoxSelection = new BoxSelectionState();
        private Rect m_BoxSelectionNormalizedRect;

        public bool IsBoxSelectionVisible =>
            (m_Mode == ObjectToolMode.MeshEdit || (m_Mode == ObjectToolMode.Transform && m_DeformMode)) && m_BoxSelection.IsActive;

        public Rect BoxSelectionNormalizedRect =>
            m_BoxSelectionNormalizedRect;

        private void CancelBoxSelection()
        {
            if (!m_BoxSelection.IsGestureActive && m_BoxSelectionNormalizedRect == Rect.zero)
            {
                return;
            }

            m_BoxSelection.Reset();
            m_BoxSelectionNormalizedRect = Rect.zero;

            if (m_MeshEditController != null)
            {
                m_MeshEditController.SetVertexHoverSuppressed(false);
                m_MeshEditController.ClearHover();
            }
        }

        private void UpdateBoxSelectionVisualRect(Camera camera)
        {
            if (!m_BoxSelection.IsActive || camera == null)
            {
                m_BoxSelectionNormalizedRect = Rect.zero;
                return;
            }

            Rect pixelRect = camera.pixelRect;
            if (pixelRect.width <= 0f || pixelRect.height <= 0f)
            {
                m_BoxSelectionNormalizedRect = Rect.zero;
                return;
            }

            Rect screenRect = m_BoxSelection.GetScreenRect();

            // Unity screen coordinates start at bottom-left.
            // The React overlay expects normalized top-left coordinates, hence the Y flip below.
            float left = Mathf.Clamp01((screenRect.xMin - pixelRect.xMin) / pixelRect.width);
            float right = Mathf.Clamp01((screenRect.xMax - pixelRect.xMin) / pixelRect.width);
            float bottom = Mathf.Clamp01((screenRect.yMin - pixelRect.yMin) / pixelRect.height);
            float top = Mathf.Clamp01((screenRect.yMax - pixelRect.yMin) / pixelRect.height);

            m_BoxSelectionNormalizedRect = new Rect(left, 1f - top, Mathf.Max(0f, right - left), Mathf.Max(0f, top - bottom));
        }

        private void UpdateMeshBoxSelectionInput(ProceduralObject editedObject, Camera camera, Vector2 mousePosition, bool primaryReleased, bool shiftPressed, bool controlPressed)
        {
            bool becameActive = m_BoxSelection.Update(mousePosition);

            UpdateBoxSelectionVisualRect(camera);

            m_MeshEditController.UpdateFrame(editedObject, camera, mousePosition, updateHover: false);

            if (becameActive || m_BoxSelection.IsActive)
            {
                ClearGizmoHover();
                m_MeshEditController.SetVertexHoverSuppressed(true);
            }

            if (primaryReleased)
            {
                if (m_BoxSelection.IsActive)
                {
                    Rect screenRect = m_BoxSelection.GetScreenRect();

                    m_MeshEditController.ApplyBoxSelection(editedObject, screenRect, m_BoxSelection.Mode);
                }
                else if (m_BoxSelection.Mode == BoxSelectionMode.Replace)
                {
                    // A pending plain box gesture that never crossed the
                    // drag threshold is still the normal empty-space click.
                    // Clear whichever element selection owns this mode.
                    if (m_MeshEditController.SelectionMode == MeshSelectionMode.Edge)
                    {
                        m_MeshEditController.ApplyEdgeSelectionClick(editedObject, shiftPressed: false, controlPressed: false);
                    }
                    else
                    {
                        m_MeshEditController.ApplySelectionClick(editedObject, shiftPressed: false, controlPressed: false);
                    }
                }

                CancelBoxSelection();
            }
        }
    }
}