using UnityEngine;
using PO2.Tools.Gizmos;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private RotateDragState m_ArrayRotateDrag;
        private GizmoDragModifierState m_ArrayRotationModifier;
        private bool m_ArrayRotating;
        private Quaternion m_ArrayRotationDragBasis = Quaternion.identity;
        private Quaternion m_ArrayRotationVisualDelta = Quaternion.identity;

        public bool ArrayDragging => m_ArrayDragging || m_ArrayRotating;
        public bool CanResetArrayRotation => ArrayActive && !ArrayBusy && !ArrayDragging && !m_ArrayRotation.Equals(Quaternion.identity);

        public void ResetArrayRotation()
        {
            if (!CanResetArrayRotation) return;
            m_ArrayRotation = Quaternion.identity;
            RefreshArrayLayout();
        }

        internal bool TryGetArrayRotationContext(Camera camera, out GizmoContext context)
        {
            context = default;
            if (!ArrayActive || m_ArrayDrawing || ArrayBusy || !m_ArrayLayoutValid) return false;
            float size = m_ArrayRotating ? m_ArrayRotateDrag.GizmoSize : GizmoMath.GetScreenSpaceGizmoSize(camera, m_ArrayStart);
            // This is an input handle, not the physical pivot of the copies.
            // Keep it behind the start, separate from the endpoint handle.
            Vector3 forward = ArrayIsCurve && m_ArrayCurveCache != null ? m_ArrayCurveCache.Frame(0) * Vector3.forward : m_ArrayLayout.Direction;
            Vector3 anchor = m_ArrayRotating ? m_ArrayRotateDrag.Pivot : m_ArrayStart - forward * (size * 1.4f);
            // Picking keeps the starting frame; drawing follows the live drag.
            TransformFrame frame = m_ArrayRotating ? TransformFrame.FromObjectRotation(m_ArrayRotationVisualDelta * m_ArrayRotateDrag.Frame.ToRotation()) : m_TransformOrientation == TransformOrientation.Local ? TransformFrame.FromObjectRotation(ArraySourceRotation * m_ArrayOwnerTransform.Rotation) : ResolveActiveTransformFrame(m_ArrayObject);
            context = new GizmoContext(GizmoTargetKind.ArrayRotation, m_ArrayObject.Id, m_ArrayPartIndex, GizmoMode.Rotate, anchor, frame, size);
            return true;
        }

        private bool TryBeginArrayRotation(Ray ray, Camera camera, float endpointDistance, bool precision, bool snap)
        {
            if (!TryGetArrayRotationContext(camera, out var context) || !GizmoMath.TryPickRotationHandle(ray, context.Pivot, context.Frame, GizmoMath.GetRotationRadius(context.Size), out var handle, out _) || !GizmoMath.TryGetTransformAxis(handle, out var axisKind)) return false;
            Vector3 axis = context.Frame.GetAxis(axisKind);
            Plane plane = GizmoMath.CreateRotationDragPlane(context.Pivot, axis);
            // Rotation's pick score is radial error, while translation uses ray
            // distance. Compare actual ray distances between the two gizmos.
            if (!plane.Raycast(ray, out float distance) || distance > endpointDistance) return false;
            m_HoveredGizmoHandle = handle;
            if (!UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame) return true;
            Vector3 direction = ray.GetPoint(distance) - context.Pivot;
            if (direction.sqrMagnitude < .0001f) return true;
            BeginArrayGesture();
            m_ArrayRotateDrag = new RotateDragState
            {
                ObjectId = m_ArrayObject.Id, Handle = handle, Pivot = context.Pivot,
                StartRotation = m_ArrayRotation, Frame = context.Frame, Axis = axis.normalized,
                DragPlane = plane, StartDirection = direction.normalized, GizmoSize = context.Size,
            };
            m_ArrayRotationDragBasis = ArrayIsCurve ? m_ArraySourceBaseRotation : Quaternion.identity;
            m_ArrayRotationVisualDelta = Quaternion.identity;
            m_ArrayRotationModifier.Begin(precision, snap);
            m_ArrayRotating = true;
            m_ActiveGizmoHandle = handle;
            return true;
        }

        private void UpdateArrayRotation(Ray ray, bool precision, bool snap)
        {
            if (!GizmoMath.TryRayPlane(ray, m_ArrayRotateDrag.DragPlane, out var hit)) return;
            Vector3 direction = hit - m_ArrayRotateDrag.Pivot;
            if (direction.sqrMagnitude < .0001f) return;
            float raw = GizmoMath.CalculateSignedAngleRadians(m_ArrayRotateDrag.Axis, m_ArrayRotateDrag.StartDirection, direction.normalized) * Mathf.Rad2Deg;
            if (!m_ArrayRotationModifier.ResolveLinear(raw, precision, snap, m_SnapSettings.RotationIncrement, m_SnapSettings.PrecisionRotationIncrement, out float angle, cyclicDegrees: true)) return;
            m_ArrayRotationVisualDelta = Quaternion.AngleAxis(angle, m_ArrayRotateDrag.Axis);
            m_ArrayRotation = Quaternion.Inverse(m_ArrayRotationDragBasis) * m_ArrayRotationVisualDelta * m_ArrayRotationDragBasis * m_ArrayRotateDrag.StartRotation;
            RefreshArrayLayout();
        }
    }
}