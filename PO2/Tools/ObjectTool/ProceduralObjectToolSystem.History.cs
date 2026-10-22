using Game;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry;
using PO2.History;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        internal TopologySelection CaptureTopologyHistorySelection(ProceduralObject obj) => m_MeshEditController?.CaptureTopologyHistorySelection(obj);

        internal void RestoreTopologyHistorySelection(ProceduralObject obj, TopologySelection selection)
        {
            m_MeshEditController?.RestoreTopologyHistorySelection(obj, selection);
            ClearGizmoHover();
        }

        internal void NotifyHistoryGeometryChanged(ProceduralObject obj, GeometryChange change)
        {
            m_MeshEditController?.MeshGpu?.Notice(obj, change);
            NotifyHistoryGeometryChanged(obj);
        }

        internal void NotifyHistoryGeometryChanged(ProceduralObject obj)
        {
            m_MeshEditController?.OnGeometryChanged(obj);
            if (m_ProceduralObjectSystem.SelectedObject == obj)
            {
                ClearGizmoHover();
            }
        }
    }
}