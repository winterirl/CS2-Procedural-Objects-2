using Game;
using Game.Input;
using Game.Tools;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.History;
using PO2.Tools.Gizmos;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        public void MoveSelectedX(float deltaMeters)
        {
            ApplySelectedMoveDelta(TransformAxis.X, deltaMeters);
        }

        public void MoveSelectedY(float deltaMeters)
        {
            ApplySelectedMoveDelta(TransformAxis.Y, deltaMeters);
        }

        public void MoveSelectedZ(float deltaMeters)
        {
            ApplySelectedMoveDelta(TransformAxis.Z, deltaMeters);
        }

        public void RotateSelectedX(float deltaDegrees)
        {
            ApplySelectedRotationDelta(TransformAxis.X, deltaDegrees);
        }

        public void RotateSelectedY(float deltaDegrees)
        {
            ApplySelectedRotationDelta(TransformAxis.Y, deltaDegrees);
        }

        public void RotateSelectedZ(float deltaDegrees)
        {
            ApplySelectedRotationDelta(TransformAxis.Z, deltaDegrees);
        }

        public void SetSelectedScaleX(float value)
        {
            ApplySelectedScaleValue(TransformAxis.X, value, m_ProceduralObjectSystem.SetSelectedScaleX);
        }

        public void SetSelectedScaleY(float value)
        {
            ApplySelectedScaleValue(TransformAxis.Y, value, m_ProceduralObjectSystem.SetSelectedScaleY);
        }

        public void SetSelectedScaleZ(float value)
        {
            ApplySelectedScaleValue(TransformAxis.Z, value, m_ProceduralObjectSystem.SetSelectedScaleZ);
        }

        private void ApplySelectedMoveDelta(TransformAxis axis, float deltaMeters)
        {
            if (MirrorMode) return;
            if (DeformMode)
            {
                ApplyNumericCageTransform(axis, deltaMeters, false);
                return;
            }

            if (!IsFinite(deltaMeters) || Mathf.Approximately(deltaMeters, 0f))
            {
                return;
            }

            SynchronizeCustomTransformOrientationTarget();

            if (m_Mode == ObjectToolMode.MeshEdit)
            {
                ApplyMeshEditMoveDelta(axis, deltaMeters);
                return;
            }

            if (m_Mode == ObjectToolMode.Texturing)
            {
                return;
            }

            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (selected == null)
            {
                return;
            }

            TransformFrame frame = ResolveActiveTransformFrame(selected);
            Vector3 worldDelta = frame.GetAxis(axis) * deltaMeters;

            if (m_TransformTarget == TransformTarget.ActivePart)
            {
                if (!TryGetNumericPartTarget(selected, out int partIndex, out Vector3 pivot))
                {
                    return;
                }

                if (!TryApplyNumericPartTransform(selected, partIndex, pivot, Matrix4x4.Translate(worldDelta), PartTransformKind.Translation, $"move {deltaMeters:0.###} m on " + $"{m_TransformOrientation} {axis}"))
                {
                    return;
                }

                return;
            }

            if (!m_ProceduralObjectSystem.TryTranslateSelected(worldDelta))
            {
                return;
            }
        }

        private void ApplySelectedRotationDelta(TransformAxis axis, float deltaDegrees)
        {
            if (DeformMode || MirrorMode) return;
            if (m_Mode == ObjectToolMode.MeshEdit)
            {
                ApplyMeshRotationDelta(axis, deltaDegrees);
                return;
            }

            if (m_Mode != ObjectToolMode.Transform || !IsFinite(deltaDegrees) || Mathf.Approximately(deltaDegrees, 0f))
            {
                return;
            }

            SynchronizeCustomTransformOrientationTarget();

            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (selected == null)
            {
                return;
            }

            TransformFrame frame = ResolveActiveTransformFrame(selected);
            Vector3 worldAxis = frame.GetAxis(axis);

            if (m_TransformTarget == TransformTarget.ActivePart)
            {
                if (!TryGetNumericPartTarget(selected, out int partIndex, out Vector3 pivot))
                {
                    return;
                }

                Quaternion delta = Quaternion.AngleAxis(deltaDegrees, worldAxis.normalized);

                Matrix4x4 worldFromStart = Matrix4x4.Translate(pivot) * Matrix4x4.Rotate(delta) * Matrix4x4.Translate(-pivot);

                TryApplyNumericPartTransform(selected, partIndex, pivot, worldFromStart, PartTransformKind.Rotation, $"rotate {deltaDegrees:0.###} degrees on " + $"{m_TransformOrientation} {axis}");
                return;
            }

            if (!m_ProceduralObjectSystem.TryRotateSelected(worldAxis, deltaDegrees))
            {
                return;
            }
        }

        private void ApplySelectedScaleValue(TransformAxis axis, float value, System.Action<float> applyObjectScale)
        {
            if (MirrorMode) return;
            if (DeformMode)
            {
                ApplyNumericCageTransform(axis, value, true);
                return;
            }

            if (m_Mode == ObjectToolMode.MeshEdit)
            {
                ApplyMeshScaleDelta((int)axis, value);
                return;
            }

            if (m_Mode != ObjectToolMode.Transform || !IsFinite(value))
            {
                return;
            }

            if (m_TransformTarget == TransformTarget.Object)
            {
                applyObjectScale(value);
                return;
            }

            const float minimumScaleFactor = 0.01f;

            if (Mathf.Approximately(value, 0f))
            {
                return;
            }

            SynchronizeCustomTransformOrientationTarget();

            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (!TryGetNumericPartTarget(selected, out int partIndex, out Vector3 pivot))
            {
                return;
            }

            float scaleFactor = Mathf.Max(minimumScaleFactor, 1f + value);

            Vector3 scaleFactors = Vector3.one;
            switch (axis)
            {
                case TransformAxis.X:
                    scaleFactors.x = scaleFactor;
                    break;

                case TransformAxis.Y:
                    scaleFactors.y = scaleFactor;
                    break;

                case TransformAxis.Z:
                    scaleFactors.z = scaleFactor;
                    break;

                default:
                    return;
            }

            TransformFrame frame = ResolveActiveTransformFrame(selected);

            TryApplyNumericPartTransform(selected, partIndex, pivot, CreateWorldScaleTransform(pivot, frame, scaleFactors), PartTransformKind.Scale, $"scale {axis} by delta {value:0.###} " + $"in {m_TransformOrientation}");
        }

        private bool TryGetNumericPartTarget(ProceduralObject selected, out int partIndex, out Vector3 pivot)
        {
            partIndex = -1;
            pivot = Vector3.zero;

            if (m_Mode != ObjectToolMode.Transform || selected == null || selected.IsLocked || m_IsGizmoDragging)
            {
                return false;
            }

            partIndex = m_ProceduralObjectSystem.SelectedPartIndex;

            if (!TryGetPartGizmoPivot(selected, partIndex, out pivot))
            {
                Mod.log.Warn("[PO Part] The active part has no valid transform pivot.");
                return false;
            }

            return true;
        }

        private bool TryApplyNumericPartTransform(ProceduralObject selected, int partIndex, Vector3 startPivot, Matrix4x4 worldFromStart, PartTransformKind transformKind, string operation)
        {
            if (m_IsGizmoDragging || m_IsVertexSlideDragging || m_ProceduralObjectSystem.ActionHistory.IsBusy) return false;
            if (!m_PartTransformDrag.TryBegin(selected, partIndex, startPivot, transformKind, m_ProceduralObjectSystem, out string failureReason))
            {
                Mod.log.Warn($"[PO Part] Could not {operation}: " + failureReason);
                return false;
            }

            try
            {
                if (!m_PartTransformDrag.TryApplyWorldTransform(selected, worldFromStart, out bool _, out failureReason) || !m_PartTransformDrag.TryComplete(selected, out failureReason))
                {
                    m_PartTransformDrag.TryCancel(selected, out string _);

                    Mod.log.Error($"[PO Part] Could not {operation} for object " + $"{selected.Id} part {partIndex}: " + failureReason);
                    return false;
                }

                return true;
            }
            finally
            {
                m_PartTransformDrag.Reset();
            }
        }
    }
}