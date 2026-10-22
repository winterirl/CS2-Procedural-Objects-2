using PO2.Core;
using PO2.Localization;
using PO2.Tools.Gizmos;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private bool m_MirrorMode;
        private LocalizedText m_MirrorStatus = LocalizedText.Empty;
        public bool MirrorMode => m_MirrorMode && m_Mode == ObjectToolMode.Transform;
        public LocalizedText MirrorStatus => MirrorMode ? (m_ProceduralObjectSystem.MirrorEditBusy ? m_ProceduralObjectSystem.MirrorEditStatus : m_MirrorStatus) : LocalizedText.Empty;
        private int MirrorPartIndex => m_TransformTarget == TransformTarget.ActivePart ? m_ProceduralObjectSystem.SelectedPartIndex : -1;
        public bool MirrorRequiresBake => m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObject.CanEditGeometry(MirrorPartIndex, out _);
        public bool CanMirrorSelected => m_Mode == ObjectToolMode.Transform && m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObject.IsLocked && !MirrorRequiresBake && !MeshMutationBusy && !m_ProceduralObjectSystem.ActionHistory.IsBusy && !AxisPickingActive && !m_IsGizmoDragging;

        public void SetMirrorMode()
        {
            if (m_Mode != ObjectToolMode.Transform || MeshMutationBusy || AxisPickingActive || m_ProceduralObjectSystem.ActionHistory.IsBusy || m_ProceduralObjectSystem.SelectedObject == null || m_ProceduralObjectSystem.SelectedObject.IsLocked) return;
            CancelGizmoDrag(true);
            CancelBoxSelection();
            ClearGizmoHover();
            m_DeformMode = false;
            m_MirrorMode = true;
            m_MirrorStatus = LocalizedText.Empty;
            m_Mode = ObjectToolMode.Transform;
        }

        public void MirrorSelected(int axisIndex)
        {
            if (!MirrorMode || !CanMirrorSelected || axisIndex < 0 || axisIndex > 2) return;
            var obj = m_ProceduralObjectSystem.SelectedObject;
            SynchronizeCustomTransformOrientationTarget();
            var axis = (TransformAxis)axisIndex;
            LocalizedText description = MirrorPartIndex < 0 ? LocalizedText.For("PO2.History.Action.Object.Mirror", ("axis", axis.ToString())) : LocalizedText.For("PO2.History.Action.Part.Mirror", ("axis", axis.ToString()));
            m_MirrorStatus = LocalizedText.Empty;
            if (!m_ProceduralObjectSystem.TryMirrorSelected(MirrorPartIndex, ResolveActiveTransformFrame(obj).GetAxis(axis), description, out m_MirrorStatus))
            {
                Mod.log.Warn("[PO Mirror] " + m_MirrorStatus);
            }

            ClearGizmoHover();
        }

        private bool ProcessPendingMirrorEdit()
        {
            if (!m_ProceduralObjectSystem.MirrorEditBusy) return false;
            OperationOutcome outcome = m_ProceduralObjectSystem.PollMirrorEdit(out LocalizedText reason);
            if (outcome != OperationOutcome.Pending)
            {
                m_MirrorStatus = reason;
                if (outcome == OperationOutcome.Succeeded) NotifyHistoryGeometryChanged(m_ProceduralObjectSystem.SelectedObject);
                else if (outcome == OperationOutcome.Failed) Mod.log.Warn("[PO Mirror] " + reason);
                ClearGizmoHover();
            }

            return true;
        }
    }
}