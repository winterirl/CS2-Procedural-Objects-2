using Game.Common;
using Game.Input;
using PO2.Geometry.Picking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private int m_ClipboardCancelFrame = -1;
        private bool IsClipboardMode => m_Mode == ObjectToolMode.Copy || m_Mode == ObjectToolMode.Paste;

        public void NavigateToCopyWorkspace()
        {
            if (m_ProceduralObjectSystem.HistoryBusy || MeshNavigationBusy) return;
            NavigateToSelectWorkspace();
            m_Mode = ObjectToolMode.Copy;
        }

        public void NavigateToPasteWorkspace()
        {
            if (!m_ProceduralObjectSystem.HasClipboard || m_ProceduralObjectSystem.HistoryBusy || MeshNavigationBusy) return;
            NavigateToSelectWorkspace();
            m_Mode = ObjectToolMode.Paste;
        }

        public void CopySelectedObject()
        {
            if (MeshMutationBusy || AxisPickingActive || m_Mode != ObjectToolMode.Transform) return;
            if (m_ProceduralObjectSystem.SelectedObjectId.HasValue)
                m_ProceduralObjectSystem.TryCopyObject(m_ProceduralObjectSystem.SelectedObjectId.Value);
        }

        internal bool ProcessClipboardInput()
        {
            if (m_ClipboardCancelFrame == UnityEngine.Time.frameCount) return true;
            if (!m_ProceduralObjectSystem.ClipboardBusy) return false;
            bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && !InputManager.instance.hasInputFieldFocus;
            bool rightClick = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
            if (escape || rightClick)
            {
                m_ProceduralObjectSystem.CancelClipboardOperation();
                m_ClipboardCancelFrame = UnityEngine.Time.frameCount;
            }

            return true;
        }

        private void UpdateClipboardMode(bool primaryPressed)
        {
            if (InputManager.instance.hasInputFieldFocus || m_ManagementWorkspaceActive || (m_ToolRaycastSystem.raycastFlags & (RaycastFlags.UIDisable | RaycastFlags.DebugDisable | RaycastFlags.ToolDisable | RaycastFlags.FreeCameraDisable)) != 0)
            {
                m_PendingObjectPick.Clear();
                m_ProceduralObjectSystem.ClearHoveredObject();
                return;
            }

            if (m_Mode == ObjectToolMode.Copy)
            {
                if (Camera.main == null)
                {
                    m_PendingObjectPick.Clear();
                    m_ProceduralObjectSystem.ClearHoveredObject();
                    return;
                }

                UpdateCopyTarget(Camera.main.ScreenPointToRay(InputManager.instance.mousePosition), primaryPressed);
            }
            else if (primaryPressed && m_ProceduralObjectSystem.HasClipboard && GetRaycastResult(out Unity.Entities.Entity _, out Game.Common.RaycastHit hit))
            {
                m_ProceduralObjectSystem.TryPasteObject(new Vector3(hit.m_HitPosition.x, hit.m_HitPosition.y, hit.m_HitPosition.z));
            }
        }

        private void UpdateCopyTarget(Ray ray, bool primaryPressed)
        {
            var status = ResolveObjectPick(ray, ref primaryPressed, out int objectId);
            if (status != ObjectPickStatus.Hit)
            {
                m_ProceduralObjectSystem.ClearHoveredObject();
                return;
            }

            m_ProceduralObjectSystem.SetHoveredObject(objectId);
            if (primaryPressed)
            {
                m_ProceduralObjectSystem.TryCopyObject(objectId);
                m_ProceduralObjectSystem.ClearHoveredObject();
            }
        }
    }
}