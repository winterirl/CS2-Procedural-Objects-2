using UnityEngine;
using PO2.Geometry.Picking;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private readonly PendingObjectPick m_PendingObjectPick = new PendingObjectPick();

        private ObjectPickStatus ResolveObjectPick(Ray mouseRay, ref bool primaryPressed, out int objectId)
        {
            int selectedId = m_ProceduralObjectSystem.SelectedObjectId ?? -1;
            uint sceneVersion = m_ProceduralObjectSystem.PickSceneVersion;
            m_PendingObjectPick.Validate((int)m_Mode, selectedId, sceneVersion);
            if (primaryPressed)
            {
                m_PendingObjectPick.Set(mouseRay, (int)m_Mode, selectedId, sceneVersion);
            }

            Ray queryRay = m_PendingObjectPick.HasClick ? m_PendingObjectPick.Ray : mouseRay;
            ObjectPickStatus status = m_ProceduralObjectSystem.FindClosestObject(queryRay, out objectId);
            primaryPressed = status != ObjectPickStatus.Pending && m_PendingObjectPick.HasClick;
            if (status != ObjectPickStatus.Pending) m_PendingObjectPick.Clear();
            return status;
        }

        private void UpdateObjectSelection(Ray mouseRay, bool primaryPressed)
        {
            ObjectPickStatus status = ResolveObjectPick(mouseRay, ref primaryPressed, out int objectId);
            if (status == ObjectPickStatus.Hit && !m_ProceduralObjectSystem.SelectedObjectId.HasValue)
            {
                m_ProceduralObjectSystem.SetHoveredObject(objectId);
            }
            else
            {
                m_ProceduralObjectSystem.ClearHoveredObject();
            }

            if (!primaryPressed) return;
            if (status == ObjectPickStatus.Hit && m_ProceduralObjectSystem.SelectObject(objectId))
            {
                m_Mode = ObjectToolMode.Transform;
            }
            else if (status == ObjectPickStatus.Miss)
            {
                m_ProceduralObjectSystem.Deselect();
                m_Mode = ObjectToolMode.Select;
            }
        }
    }
}