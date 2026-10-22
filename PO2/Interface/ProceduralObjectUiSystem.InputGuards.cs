using System;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private bool HistoryInputBlocked =>
            (m_ProceduralObjectSystem != null && m_ProceduralObjectSystem.HistoryBusy) || (m_ProceduralObjectToolSystem != null && m_ProceduralObjectToolSystem.MeshMutationBusy);

        private Action GuardHistory(Action action) => () =>
        {
            if (!HistoryInputBlocked && !m_ProceduralObjectToolSystem.AxisPickingActive) action();
        };

        private Action<T0> GuardHistory<T0>(Action<T0> action) => (v0) =>
        {
            if (!HistoryInputBlocked && !m_ProceduralObjectToolSystem.AxisPickingActive) action(v0);
        };

        private Action GuardHistoryReplay(Action action) => () =>
        {
            bool arrayPreview = m_ProceduralObjectToolSystem.ArrayActive && !m_ProceduralObjectToolSystem.ArrayBusy;
            if (arrayPreview || (!HistoryInputBlocked && !m_ProceduralObjectToolSystem.AxisPickingActive)) action();
        };

        private bool NavigationInputBlocked =>
            (m_ProceduralObjectSystem != null && m_ProceduralObjectSystem.HistoryBusy) || (m_ProceduralObjectToolSystem != null && m_ProceduralObjectToolSystem.MeshNavigationBusy);

        private Action GuardMeshView(Action action) => () =>
        {
            if (!NavigationInputBlocked && !m_ProceduralObjectToolSystem.AxisPickingActive) action();
        };

        private Action<T> GuardMeshView<T>(Action<T> action) => (value) =>
        {
            if (!NavigationInputBlocked && !m_ProceduralObjectToolSystem.AxisPickingActive) action(value);
        };

        private Action GuardPicker(Action action) => () =>
        {
            if (!NavigationInputBlocked) action();
        };

        private Action GuardNavigation(Action action) => () =>
        {
            if (NavigationInputBlocked) return;
            m_ProceduralObjectToolSystem.CancelAxisPicking();
            action();
        };
    }
}