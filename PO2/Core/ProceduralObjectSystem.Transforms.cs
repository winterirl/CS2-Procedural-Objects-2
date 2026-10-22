using Game;
using Game.Tools;
using UnityEngine;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Tools.ObjectTool;

namespace PO2.Core
{
    public partial class ProceduralObjectSystem
    {
        public void SetSelectedScaleX(float value)
        {
            SetSelectedScaleComponent(0, value);
        }

        public void SetSelectedScaleY(float value)
        {
            SetSelectedScaleComponent(1, value);
        }

        public void SetSelectedScaleZ(float value)
        {
            SetSelectedScaleComponent(2, value);
        }

        public bool TryTranslateSelected(Vector3 worldDelta)
        {
            if (!IsFinite(worldDelta) || worldDelta.sqrMagnitude <= 0f || !TryGetEditableSelectedTransform("apply a numeric move delta", out var selected))
                return false;

            var position = selected.Transform.Position + worldDelta;

            if (!IsFinite(position))
            {
                Mod.log.Warn("[PO Transform] Numeric move would produce an invalid position.");
                return false;
            }

            using (var history = BeginObjectHistory(selected, ObjectValueKind.Transform, LocalizedText.For("PO2.History.Action.Object.Move")))
            {
                if (history == null) return false;
                selected.SetPosition(position);
                return history.Complete();
            }
        }

        public bool TryRotateSelected(Vector3 worldAxis, float deltaDegrees)
        {
            if (!IsFinite(worldAxis) || !IsFinite(deltaDegrees) || worldAxis.sqrMagnitude <= 0.00000001f || Mathf.Approximately(deltaDegrees, 0f) || !TryGetEditableSelectedTransform("apply a numeric rotation delta", out var selected))
                return false;

            var delta = Quaternion.AngleAxis(deltaDegrees, worldAxis.normalized);

            var rotation = delta * selected.Transform.Rotation;

            if (!IsFinite(rotation))
            {
                Mod.log.Warn("[PO Transform] Numeric rotation would produce an invalid orientation.");
                return false;
            }

            using (var history = BeginObjectHistory(selected, ObjectValueKind.Transform, LocalizedText.For("PO2.History.Action.Object.Rotate")))
            {
                if (history == null) return false;
                if (!selected.TrySetRotationAroundPivot(rotation)) return false;
                return history.Complete();
            }
        }

        private void SetSelectedScaleComponent(int axis, float value)
        {
            const float minimumScale = 0.01f;

            if (!IsFinite(value) || !TryGetEditableSelectedTransform("set numeric scale", out var selected))
                return;

            var scale = selected.Transform.Scale;
            if (!SetVectorComponent(ref scale, axis, Mathf.Max(minimumScale, value)))
                return;

            using (var history = BeginObjectHistory(selected, ObjectValueKind.Transform, LocalizedText.For("PO2.History.Action.Object.Scale")))
            {
                if (history == null) return;
                if (!selected.TrySetScaleAroundPivot(scale)) return;
                if (!history.Complete()) return;
            }
        }

        private bool TryGetEditableSelectedTransform(string operation, out ProceduralObject selected)
        {
            if (!TryGetSelected(out selected)) return false;

            if (selected.IsLocked) return false;

            if (m_ProceduralObjectToolSystem != null && (m_ProceduralObjectToolSystem.Mode == ObjectToolMode.MeshEdit || m_ProceduralObjectToolSystem.IsGizmoDragging))
                return false;

            return true;
        }

        private static bool SetVectorComponent(ref Vector3 vector, int axis, float value)
        {
            switch (axis)
            {
                case 0:
                    if (Mathf.Approximately(vector.x, value)) return false;
                    vector.x = value;
                    return true;
                case 1:
                    if (Mathf.Approximately(vector.y, value)) return false;
                    vector.y = value;
                    return true;
                case 2:
                    if (Mathf.Approximately(vector.z, value)) return false;
                    vector.z = value;
                    return true;
                default:
                    return false;
            }
        }
    }
}