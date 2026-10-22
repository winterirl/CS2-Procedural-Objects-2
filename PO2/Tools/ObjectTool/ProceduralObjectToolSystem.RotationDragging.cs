using Game;
using Game.Input;
using Game.Tools;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry.Editing;
using PO2.Localization;
using PO2.Tools.Gizmos;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private RotateDragState m_RotateDrag;

        private void BeginRotateDrag(Ray mouseRay, in GizmoContext context, GizmoHandle handle)
        {
            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (selected == null || selected.Id != context.ObjectId)
            {
                return;
            }

            if (!GizmoMath.TryGetTransformAxis(handle, out TransformAxis transformAxis))
            {
                return;
            }

            Vector3 axis = context.Frame.GetAxis(transformAxis);
            Vector3 pivot = context.Pivot;
            Plane dragPlane = GizmoMath.CreateRotationDragPlane(pivot, axis);

            if (!GizmoMath.TryRayPlane(mouseRay, dragPlane, out Vector3 startMouseHit))
            {
                Mod.log.Warn($"[PO Gizmo] Mouse ray did not intersect rotation plane for {handle}.");
                return;
            }

            Vector3 startDirection = startMouseHit - pivot;

            if (startDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            if (context.TargetKind == GizmoTargetKind.Part && !TryBeginPartTransform(selected, context, PartTransformKind.Rotation))
            {
                return;
            }

            if (context.TargetKind == GizmoTargetKind.VertexSelection && !TryBeginSelectionTransform(selected, context, handle, LocalizedText.For("PO2.History.Action.Vertices.Rotate")))
            {
                return;
            }

            m_RotateDrag = new RotateDragState
            {
                ObjectId = selected.Id,
                Handle = handle,
                Pivot = pivot,
                StartTransform = selected.Transform,
                StartRotation = selected.Transform.Rotation,
                Frame = context.Frame,
                Axis = axis.normalized,
                DragPlane = dragPlane,
                StartDirection = startDirection.normalized,
                GizmoSize = context.Size,
            };

            StartGizmoDrag(handle, context.TargetKind);
        }

        private void UpdateRotateDrag(Ray mouseRay, bool precision, bool snapEnabled)
        {
            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (selected == null || selected.Id != m_RotateDrag.ObjectId || (m_ActiveGizmoTargetKind == GizmoTargetKind.Part && !m_PartTransformDrag.Matches(selected)))
            {
                CancelGizmoDrag(restoreTransform: false);
                return;
            }

            if (!GizmoMath.TryRayPlane(mouseRay, m_RotateDrag.DragPlane, out Vector3 currentMouseHit))
            {
                return;
            }

            Vector3 currentDirection = currentMouseHit - m_RotateDrag.Pivot;

            if (currentDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            currentDirection.Normalize();

            float rawAngleDegrees = GizmoMath.CalculateSignedAngleRadians(m_RotateDrag.Axis, m_RotateDrag.StartDirection, currentDirection) * Mathf.Rad2Deg;

            // Rotation uses the same modifier rebasing as linear drags.
            // Marking it cyclic keeps the signed angle continuous across +/-180 degrees.
            if (!m_DragModifierState.ResolveLinear(rawAngleDegrees, precision, snapEnabled, m_SnapSettings.RotationIncrement, m_SnapSettings.PrecisionRotationIncrement, out float angleDegrees, cyclicDegrees: true))
            {
                return;
            }

            if (m_ActiveGizmoTargetKind == GizmoTargetKind.VertexSelection)
            {
                Matrix4x4 worldFromStart = SelectionTransformMath.CreateWorldRotation(m_RotateDrag.Pivot, m_RotateDrag.Axis, angleDegrees, out bool neutral);
                TryApplySelectionTransformPreview(selected, worldFromStart, neutral);
                return;
            }

            Quaternion delta = Quaternion.AngleAxis(angleDegrees, m_RotateDrag.Axis);

            if (m_ActiveGizmoTargetKind == GizmoTargetKind.Part)
            {
                Matrix4x4 worldFromStart = Matrix4x4.Translate(m_RotateDrag.Pivot) * Matrix4x4.Rotate(delta) * Matrix4x4.Translate(-m_RotateDrag.Pivot);

                TryApplyPartTransformPreview(selected, worldFromStart);
                return;
            }

            if (!selected.TrySetRotationAroundPivot(delta * m_RotateDrag.StartRotation))
            {
                CancelGizmoDrag(restoreTransform: true);
            }
        }
    }
}