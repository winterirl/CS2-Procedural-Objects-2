using System;
using Game.Input;
using Game.Tools;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.History;
using PO2.Localization;
using PO2.Rendering.Objects;
using PO2.Tools.Arrays;
using PO2.Tools.Gizmos;
using PO2.Tools.Snapping;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private ProceduralObject m_ArrayObject;
        private int m_ArrayPartIndex;
        private TopologyTargetStamp m_ArrayStamp;
        private ObjectTransform m_ArrayOwnerTransform;
        private ProceduralObjectRenderer.ArrayPreview m_ArrayPreview;
        private PartArrayLayout m_ArrayLayout;
        private Vector3 m_ArrayStart, m_ArrayEndpoint, m_ArrayDragStart, m_ArrayMouseStart;
        private Quaternion m_ArrayRotation = Quaternion.identity;
        private Plane m_ArrayPlane, m_ArrayDragPlane;
        private TranslationConstraint m_ArrayConstraint;
        private GizmoDragModifierState m_ArrayMoveModifier, m_ArrayPlaneModifier;
        private float m_ArrayAxisStart, m_ArrayGap;
        private bool m_ArrayDrawing, m_ArrayDragging, m_ArrayLayoutValid, m_ArrayWasApplying, m_ArraySceneReady;
        private LocalizedText m_ArrayStatus = LocalizedText.Empty;
        private ArrayGestureState m_ArrayGesture;

        private struct ArrayGestureState
        {
            internal bool Active;
            internal Vector3 Endpoint, Control1, Control2;
            internal Quaternion Rotation, SourceRotation;
            internal float Angle;
            internal ArrayControlPoint SelectedPoint;
            internal PartArrayCurve Curve;
            internal PartArrayPlacements Placements;
            internal ArrayCurveRequest DisplayedRequest;
            internal LocalizedText Status;
        }

        public bool ArrayActive => m_ArrayObject != null;
        public bool ArrayBusy => m_ProceduralObjectSystem?.ArrayEditBusy == true;
        public bool ArrayDrawing => ArrayActive && m_ArrayDrawing;
        public float ArrayGap => m_ArrayGap;
        public int ArrayCount => ArrayActive && m_ArrayLayoutValid ? (ArrayIsCurve && m_ArrayPlacements != null ? m_ArrayPlacements.Count : m_ArrayLayout.Count) : 0;
        public float ArrayEffectiveGap => (float)(ArrayIsCurve ? m_ArrayPlacements?.Gap ?? 0 : m_ArrayLayout.Gap);
        public bool CanArraySelectedPart => !ArrayActive && m_Mode == ObjectToolMode.Transform && m_ProceduralObjectSystem.CanDuplicateSelectedPart && !MeshMutationBusy && !AxisPickingActive && !m_ProceduralObjectSystem.ActionHistory.IsBusy && m_ProceduralObjectSystem.SelectedObject.CanEditGeometry(-1, out _);
        public bool CanApplyArray => ArrayActive && !ArrayBusy && !m_ArrayDrawing && !ArrayDragging && m_ArrayLayoutValid && ArrayCount > 0 && m_ArrayPreview?.Ready == true && (!ArrayIsCurve || m_ArrayPlacements?.Revision == m_ArrayCurveRevision);
        public LocalizedText ArrayStatus => ArrayBusy ? m_ProceduralObjectSystem.ArrayEditStatus : m_ArrayStatus;

        public void BeginArray()
        {
            if (!CanArraySelectedPart) return;
            var obj = m_ProceduralObjectSystem.SelectedObject;
            try
            {
                ClearGizmoHover();
                CancelBoxSelection();
                m_DeformMode = false;
                m_MirrorMode = false;
                m_ArrayPartIndex = m_ProceduralObjectSystem.SelectedPartIndex;
                m_ArrayPreview = m_ProceduralObjectSystem.CreateArrayPreview(obj, m_ArrayPartIndex);
                m_ArrayObject = obj;
                m_ArrayOwnerTransform = obj.Transform;
                m_ArrayStamp = new TopologyTargetStamp(obj, m_ArrayPartIndex);
                m_ArrayRotation = Quaternion.identity;
                m_ArrayStart = obj.Transform.TransformPoint(obj.Parts[m_ArrayPartIndex].LocalPivot);
                m_ArrayEndpoint = m_ArrayStart;
                InitializeArrayPath();
                InitializeArrayCurve();
                m_ArrayDrawing = true;
                m_ArrayDragging = false;
                m_ArraySceneReady = false;
                m_ArrayWasApplying = false;
                m_ArrayLayoutValid = false;
                m_ObjectTranslationSnapTargetCache.Clear();
                m_ArrayStatus = LocalizedText.For("PO2.PartArray.Endpoint.Instruction");
            }
            catch (Exception e)
            {
                CancelArray();
                m_ArrayStatus = LocalizedText.FromException(e);
            }
        }

        public void SetArrayGap(float value)
        {
            if (!CanEditArrayPath || !PartArrayLayout.Finite(value) || value < 0) return;
            m_ArrayGap = value;
            RefreshArrayLayout();
        }

        public void ChooseArrayEndpoint()
        {
            if (!ArrayActive || ArrayBusy || ArrayDragging || m_ArrayDrawing) return;
            BeginArrayGesture();
            m_ArrayControlPoint = PO2.Tools.ObjectTool.ArrayControlPoint.Endpoint;
            m_ArrayDrawing = true;
            m_ArrayDragging = false;
            m_ArraySceneReady = false;
            ClearGizmoHover();
        }

        public void ApplyArray()
        {
            if (!CanApplyArray) return;
            if (!ArraySourceCurrent())
            {
                CancelArray();
                return;
            }

            LocalizedText reason;
            bool started = ArrayIsCurve ? m_ProceduralObjectSystem.TryBeginArray(m_ArrayObject, m_ArrayPartIndex, m_ArrayPlacements, out reason) : m_ProceduralObjectSystem.TryBeginArray(m_ArrayObject, m_ArrayPartIndex, m_ArrayLayout, out reason);
            if (started)
                m_ArrayWasApplying = true;
            else m_ArrayStatus = reason;
        }

        public void CancelArray()
        {
            m_ProceduralObjectSystem?.ResetArrayEdit();
            EndArrayPreview();
            m_ArrayStatus = LocalizedText.Empty;
        }

        private void BeginArrayGesture()
        {
            // Save value state and immutable preview references, never meshes.
            m_ArrayGesture = new ArrayGestureState
            {
                Active = true, Endpoint = m_ArrayEndpoint, Control1 = m_ArrayControl1, Control2 = m_ArrayControl2,
                Rotation = m_ArrayRotation, Angle = m_ArrayPathAngle, SelectedPoint = m_ArrayControlPoint,
                Curve = m_ArrayCurveCache, Placements = m_ArrayPlacements, DisplayedRequest = m_ArrayDisplayedRequest,
                SourceRotation = m_ArraySourceBaseRotation, Status = m_ArrayStatus,
            };
        }

        private void FinishArrayGesture()
        {
            m_ArrayGesture = default;
            m_ArrayDragging = false;
            m_ArrayRotating = false;
            m_ArrayRotationVisualDelta = Quaternion.identity;
            m_ArrayRotationModifier.Reset();
            m_ArrayMoveModifier.Reset();
            m_ArrayPlaneModifier.Reset();
            m_ActiveGizmoHandle = GizmoHandle.None;
            ClearGizmoHover();
        }

        private void BackOutArray()
        {
            if (!m_ArrayGesture.Active || ArrayBusy)
            {
                CancelArray();
                return;
            }

            var saved = m_ArrayGesture;
            FinishArrayGesture();
            m_ArrayDrawing = false;
            m_ArraySceneReady = false;
            m_ArrayEndpoint = saved.Endpoint;
            m_ArrayControl1 = saved.Control1;
            m_ArrayControl2 = saved.Control2;
            m_ArrayRotation = saved.Rotation;
            m_ArrayPathAngle = saved.Angle;
            m_ArrayControlPoint = saved.SelectedPoint;
            // A completed result from the abandoned drag must not replace the
            // restored preview. Clearing the worker invalidates its generation.
            m_ArrayCurveWorker?.Clear();
            m_ArrayCurvePresentedRevision = ++m_ArrayCurveRevision;
            m_ArrayCurveCache = saved.Curve;
            m_ArrayPlacements = saved.Placements;
            m_ArrayDisplayedRequest = saved.DisplayedRequest;
            m_ArraySourceBaseRotation = saved.SourceRotation;
            m_ObjectTranslationSnapTargetCache.Clear();
            if (ArrayIsCurve) PresentArrayCurve(saved.Status);
            RefreshArrayLayout();
        }

        private void EndArrayPreview()
        {
            FinishArrayGesture();
            ClearArrayCurve();
            m_ArrayPreview?.Dispose();
            m_ArrayPreview = null;
            m_ArrayObject = null;
            m_ArrayStamp = null;
            m_ArrayDrawing = false;
            m_ArrayWasApplying = false;
            m_ArrayLayoutValid = false;
            m_ObjectTranslationSnapTargetCache.Clear();
        }

        internal void ArrayApplied()
        {
            var obj = m_ArrayObject;
            EndArrayPreview();
            m_ArrayStatus = LocalizedText.Empty;
            if (obj != null) NotifyHistoryGeometryChanged(obj);
        }

        private bool ArraySourceCurrent() => ArrayActive && m_ProceduralObjectSystem.IsToolActive && ReferenceEquals(m_ProceduralObjectSystem.SelectedObject, m_ArrayObject) && m_ProceduralObjectSystem.SelectedPartIndex == m_ArrayPartIndex && m_ArrayObject.Transform.Equals(m_ArrayOwnerTransform) && m_ArrayStamp.Matches(m_ArrayObject, m_ArrayPartIndex);

        internal void PollArrayPreview()
        {
            if (!ArrayActive) return;
            if (!ArraySourceCurrent())
            {
                CancelArray();
                return;
            }

            SyncArrayPathFrame();
            if (m_ArrayWasApplying && !ArrayBusy)
            {
                m_ArrayStatus = m_ProceduralObjectSystem.ArrayEditStatus;
                m_ArrayWasApplying = false;
            }

            try
            {
                PollArrayCurve();
                m_ArrayPreview.Poll();
            }
            catch (Exception e)
            {
                CancelArray();
                m_ArrayStatus = LocalizedText.FromException(e);
            }
        }

        private void RefreshArrayLayout()
        {
            if (ArrayIsCurve) RefreshArrayCurve();
            else RefreshArrayLine();
        }

        private void RefreshArrayLine()
        {
            m_ArrayLayoutValid = PartArrayLayout.TryCreate(m_ArrayPreview.Bounds, m_ArrayObject.Parts[m_ArrayPartIndex].LocalPivot, m_ArrayOwnerTransform, m_ArrayEndpoint, m_ArrayRotation, m_ArrayGap, m_ArrayMode, m_ArrayCopies, ProceduralObject.kMaxPartCount - m_ArrayObject.Parts.Count, out m_ArrayLayout, out m_ArrayStatus);
            m_ArrayPreview.SetLayout(m_ArrayLayoutValid ? m_ArrayLayout : default, m_ArrayRotation);
            if (!m_ArrayLayoutValid) return;
            m_ArrayStatus = m_ArrayLayout.CapacityLimited ? LocalizedText.For("PO2.PartArray.Preview.CapacityNotice") : m_ArrayLayout.Count == 0 ? LocalizedText.For("PO2.PartArray.Line.ExtendInstruction") : LocalizedText.Empty;
        }

        internal bool TryGetArrayLine(out Vector3 start, out Vector3 end)
        {
            start = m_ArrayStart;
            end = m_ArrayEndpoint;
            return ArrayActive;
        }

        private bool TryGetArrayGizmoContext(Camera camera, out GizmoContext context)
        {
            context = default;
            if (!ArrayActive || m_ArrayDrawing || ArrayBusy) return false;
            GizmoTargetKind kind = !ArrayIsCurve || m_ArrayControlPoint == PO2.Tools.ObjectTool.ArrayControlPoint.Endpoint ? GizmoTargetKind.ArrayEndpoint : m_ArrayControlPoint == PO2.Tools.ObjectTool.ArrayControlPoint.StartHandle ? GizmoTargetKind.ArrayControl1 : GizmoTargetKind.ArrayControl2;
            context = new GizmoContext(kind, m_ArrayObject.Id, m_ArrayPartIndex, GizmoMode.Translate, ArrayControlPosition, m_ArrayPathFrame, GizmoMath.GetScreenSpaceGizmoSize(camera, ArrayControlPosition));
            return true;
        }

        private void UpdateArrayInput(bool allowPointer)
        {
            if (!ArrayActive) return;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            bool inputFocus = InputManager.instance.hasInputFieldFocus;
            if ((!inputFocus && keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame))
            {
                BackOutArray();
                return;
            }

            if (ArrayBusy) return;
            if (ArrayDragging && (mouse == null || !mouse.leftButton.isPressed))
            {
                FinishArrayGesture();
            }

            SyncArrayPathFrame();
            if (!allowPointer || inputFocus || mouse == null || Camera.main == null) return;
            Camera camera = Camera.main;
            Vector2 cursor = InputManager.instance.mousePosition;
            Ray ray = camera.ScreenPointToRay(cursor);
            bool ctrl = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
            bool precision = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            if (m_ArrayRotating)
            {
                UpdateArrayRotation(ray, precision, m_SnapSettings.IsIncrementSnappingActive(ctrl));
                return;
            }

            if (m_ArrayDrawing)
            {
                Vector3 point;
                if (!TryGetArrayCursorTarget(camera, cursor, ctrl, out point))
                {
                    if (!GizmoMath.TryRayPlane(ray, m_ArrayPlane, out point)) return;
                    point = SnapArrayDrawing(point, ctrl, precision);
                }

                SetArrayEndpoint(point);
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    FinishArrayGesture();
                    m_ArrayDrawing = false;
                    if (ArrayIsCurve) RefreshArrayLayout();
                }

                return;
            }

            if (!TryGetArrayGizmoContext(camera, out var context)) return;
            if (!m_ArrayDragging)
            {
                bool endpoint = GizmoMath.TryPickTranslationHandle(ray, context.Pivot, context.Frame, context.Size, out m_HoveredGizmoHandle, out float distance);
                if (TrySelectArrayControlPoint(ray, camera, endpoint ? distance : float.MaxValue)) return;
                if (TryBeginArrayRotation(ray, camera, endpoint ? distance : float.MaxValue, precision, m_SnapSettings.IsIncrementSnappingActive(ctrl))) return;
                if (!mouse.leftButton.wasPressedThisFrame || !TranslationConstraint.TryCreate(m_HoveredGizmoHandle, context.Frame, out m_ArrayConstraint)) return;
                m_ArrayDragStart = context.Pivot;
                if (m_ArrayConstraint.IsPlane)
                {
                    m_ArrayDragPlane = new Plane(m_ArrayConstraint.Normal, m_ArrayDragStart);
                    if (!GizmoMath.TryRayPlane(ray, m_ArrayDragPlane, out m_ArrayMouseStart)) return;
                }
                else if (!GizmoMath.TryGetClosestRayLineParameter(ray, m_ArrayDragStart, m_ArrayConstraint.UAxis, out m_ArrayAxisStart)) return;

                BeginArrayGesture();
                bool snap = m_SnapSettings.IsIncrementSnappingActive(ctrl);
                m_ArrayMoveModifier.Begin(precision, snap);
                m_ArrayPlaneModifier.Begin(precision, snap);
                m_ArrayDragging = true;
                m_ActiveGizmoHandle = m_HoveredGizmoHandle;
            }

            Vector2 components;
            if (m_ArrayConstraint.IsPlane)
            {
                if (!GizmoMath.TryRayPlane(ray, m_ArrayDragPlane, out var hit)) return;
                components = m_ArrayConstraint.ProjectWorldDelta(hit - m_ArrayMouseStart);
            }
            else
            {
                if (!GizmoMath.TryGetClosestRayLineParameter(ray, m_ArrayDragStart, m_ArrayConstraint.UAxis, out float parameter)) return;
                components = new Vector2(parameter - m_ArrayAxisStart, 0);
            }

            // Use the same precision and increment handling as the other move gizmos.
            // Changing settings during a drag starts from the last visible position.
            bool incrementSnap = m_SnapSettings.IsIncrementSnappingActive(ctrl);
            m_ArrayMoveModifier.ResolveLinear(components.x, precision, incrementSnap, m_SnapSettings.MoveIncrement, m_SnapSettings.PrecisionMoveIncrement, out float u);
            m_ArrayPlaneModifier.ResolveLinear(components.y, precision, incrementSnap, m_SnapSettings.MoveIncrement, m_SnapSettings.PrecisionMoveIncrement, out float v);
            components = new Vector2(u, v);
            if (TryGetArrayCursorTarget(camera, cursor, ctrl, out var target))
                components = m_ArrayConstraint.ProjectWorldDelta(target - m_ArrayDragStart);
            SetArrayControlPosition(m_ArrayDragStart + m_ArrayConstraint.ComposeWorldDelta(components));
        }

        private void SetArrayEndpoint(Vector3 point)
        {
            if (!PartArrayLayout.Finite(point) || point.Equals(m_ArrayEndpoint)) return;
            Vector3 delta = point - m_ArrayEndpoint;
            m_ArrayEndpoint = point;
            MoveArrayCurveEndpoint(delta);
            m_ArrayPathAngle = PartArrayPathMath.Angle(point - m_ArrayStart, m_ArrayPathFrame, m_ArrayPathAngle);
            RefreshArrayLayout();
        }

        private Vector3 SnapArrayDrawing(Vector3 point, bool ctrl, bool precision)
        {
            if (!m_SnapSettings.IsIncrementSnappingActive(ctrl)) return point;
            float distanceStep = precision ? m_SnapSettings.PrecisionMoveIncrement : m_SnapSettings.MoveIncrement;
            float angleStep = precision ? m_SnapSettings.PrecisionRotationIncrement : m_SnapSettings.RotationIncrement;
            return m_ArrayStart + PartArrayPathMath.SnapDrawing(point - m_ArrayStart, m_ArrayPathFrame, m_ArrayPathAngle, distanceStep, angleStep);
        }

        private bool TryGetArrayCursorTarget(Camera camera, Vector2 cursor, bool ctrl, out Vector3 point)
        {
            point = default;
            if (!m_SnapSettings.IsSnappingActive(ctrl))
            {
                m_ArraySceneReady = false;
                return false;
            }

            if (TryQueryPOCursorTarget(camera, m_ArrayObject.Id, m_ArrayPartIndex, cursor, out var target))
            {
                point = target.Position;
                return true;
            }

            bool ready = m_ArraySceneReady;
            m_ArraySceneReady = m_SnapSettings.GameSurfacesEnabled;
            if (ready && m_SnapSettings.GameSurfacesEnabled && GetRaycastResult(out Unity.Entities.Entity _, out Game.Common.RaycastHit hit))
            {
                point = new Vector3(hit.m_HitPosition.x, hit.m_HitPosition.y, hit.m_HitPosition.z);
                return PartArrayLayout.Finite(point);
            }

            return false;
        }

        private bool TryQueryPOCursorTarget(Camera camera, int owner, int excludedPart, Vector2 cursor, out SnapTarget target)
        {
            target = default;
            if (m_SnapIndexSystem == null || (!m_SnapSettings.POVerticesEnabled && !m_SnapSettings.POEdgesEnabled)) return false;
            int stamp = m_SnapIndexSystem.GetObjectTranslationQueryStamp(owner, excludedPart);
            var cameraState = ObjectSnapCameraState.Capture(camera);
            if (m_ObjectTranslationSnapTargetCache.TryGet(owner, excludedPart, cursor, cameraState, stamp, m_SnapSettings.POVerticesEnabled, m_SnapSettings.POEdgesEnabled, out target)) return target.IsValid;
            bool found = m_SnapIndexSystem.TryFindObjectTranslationTarget(camera, owner, excludedPart, cursor, TranslationSnapState.kTargetQueryRadiusPixels, m_SnapSettings.POVerticesEnabled, m_SnapSettings.POEdgesEnabled, out target);
            m_ObjectTranslationSnapTargetCache.Store(owner, excludedPart, cursor, cameraState, stamp, m_SnapSettings.POVerticesEnabled, m_SnapSettings.POEdgesEnabled, target);
            return found;
        }
    }
}