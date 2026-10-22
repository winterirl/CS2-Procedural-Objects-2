using Game.Input;
using Game.Common;
using UnityEngine;
using PO2.Geometry.Picking;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        public bool DeleteCursorVisible => m_Mode == ObjectToolMode.Delete && m_Workspace == Workspace.Delete && m_ProceduralObjectSystem.IsToolActive && !m_ManagementWorkspaceActive && !m_ProceduralObjectSystem.HistoryBusy && !MeshMutationBusy && !InputManager.instance.hasInputFieldFocus && (m_ToolRaycastSystem.raycastFlags & (RaycastFlags.UIDisable | RaycastFlags.DebugDisable | RaycastFlags.ToolDisable | RaycastFlags.FreeCameraDisable)) == 0;

        public void NavigateToDeleteWorkspace()
        {
            if (m_ProceduralObjectSystem.HistoryBusy || MeshNavigationBusy) return;
            NavigateToSelectWorkspace();
            m_Mode = ObjectToolMode.Delete;
        }

        private void UpdateDeleteMode(bool primaryPressed)
        {
            if (!DeleteCursorVisible || Camera.main == null)
            {
                m_PendingObjectPick.Clear();
                m_ProceduralObjectSystem.ClearHoveredObject();
                return;
            }

            UpdateDeleteTarget(Camera.main.ScreenPointToRay(InputManager.instance.mousePosition), primaryPressed);
        }

        private void UpdateDeleteTarget(Ray ray, bool primaryPressed)
        {
            ObjectPickStatus status = ResolveObjectPick(ray, ref primaryPressed, out int objectId);
            if (status != ObjectPickStatus.Hit || !m_ProceduralObjectSystem.TryGetObject(objectId, out var obj) || obj.IsLocked)
            {
                m_ProceduralObjectSystem.ClearHoveredObject();
                return;
            }

            m_ProceduralObjectSystem.SetDeleteHoveredObject(objectId);
            if (primaryPressed) m_ProceduralObjectSystem.TryDeleteObject(objectId);
        }
    }
}