using PO2.Rendering.Lod;
using Game.Input;
using UnityEngine.InputSystem;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        internal void NavigateToLodWorkspace()
        {
            if (MeshNavigationBusy || m_ProceduralObjectSystem.HistoryBusy || m_ProceduralObjectSystem.SelectedObject == null)
            {
                return;
            }

            CancelNestedInteractionForWorkspaceChange(preserveWorkspace: true);
            if (IsObjectEditorMode)
            {
                LeaveMeshEdit(commitActiveDrag: false);
            }

            m_Mode = ObjectToolMode.Lod;
            m_ProceduralObjectSystem.BeginSelectedLodPreview();
        }

        private void UpdateLodWorkspace()
        {
            if (m_ProceduralObjectSystem.SelectedObject == null)
            {
                NavigateToSelectWorkspace();
                return;
            }

            // History ends the old session before changing accepted settings.
            if (m_ProceduralObjectSystem.LodPreview == null && !m_ProceduralObjectSystem.HistoryBusy && m_ProceduralObjectSystem.BeginSelectedLodPreview() && m_ProceduralObjectSystem.LodPreview.Status != LodPreviewStatus.Ready)
            {
                m_ProceduralObjectSystem.GenerateLodPreview();
            }

            m_ProceduralObjectSystem.ClearHoveredObject();
            ClearGizmoHover();
            if (!InputManager.instance.hasInputFieldFocus && ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)))
            {
                BackOutLodWorkspace();
            }
        }

        private void BackOutLodWorkspace()
        {
            LodPreviewSession session = m_ProceduralObjectSystem.LodPreview;
            if (session != null && (session.IsBusy || !Equals(session.DraftProfile, m_ProceduralObjectSystem.SelectedObject?.LodProfile)))
            {
                m_ProceduralObjectSystem.CancelLodPreview();
            }
            else
            {
                NavigateToTransformWorkspace();
            }
        }
    }
}