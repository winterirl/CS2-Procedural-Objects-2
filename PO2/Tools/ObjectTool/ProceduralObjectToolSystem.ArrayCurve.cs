using UnityEngine;
using PO2.Localization;
using PO2.Tools.Arrays;
using PO2.Tools.Gizmos;

namespace PO2.Tools.ObjectTool
{
    internal enum ArrayControlPoint
    {
        Endpoint,
        StartHandle,
        EndHandle
    }

    public partial class ProceduralObjectToolSystem
    {
        private PartArrayPathKind m_ArrayPathKind;
        private PartArrayAlignment m_ArrayAlignment;
        private ArrayControlPoint m_ArrayControlPoint;
        private Vector3 m_ArrayControl1, m_ArrayControl2;
        private bool m_ArrayCurveInitialized;
        private PartArraySource m_ArrayCurveSource;
        private PartArrayCurveWorker m_ArrayCurveWorker;
        private PartArrayCurve m_ArrayCurveCache;
        private PartArrayPlacements m_ArrayPlacements;
        private Quaternion m_ArraySourceBaseRotation = Quaternion.identity;
        private int m_ArrayCurveRevision, m_ArrayCurvePresentedRevision = -1;
        private ArrayCurveRequest m_ArrayCurveRequest, m_ArrayDisplayedRequest;
        private Quaternion m_ArrayPreviewOffset = Quaternion.identity;

        private bool ArrayIsCurve => m_ArrayPathKind == PartArrayPathKind.Curve;
        public int ArrayPathKind => (int)m_ArrayPathKind;
        public int ArrayAlignment => (int)m_ArrayAlignment;
        public int ArrayControlPoint => (int)m_ArrayControlPoint;
        public float ArrayCurveLength => (float)(m_ArrayCurveCache?.Length ?? 0);
        private Vector3 ArrayControlPosition => !ArrayIsCurve || m_ArrayControlPoint == PO2.Tools.ObjectTool.ArrayControlPoint.Endpoint ? m_ArrayEndpoint : m_ArrayControlPoint == PO2.Tools.ObjectTool.ArrayControlPoint.StartHandle ? m_ArrayControl1 : m_ArrayControl2;
        private PartArrayBezier ArrayBezier => new PartArrayBezier(m_ArrayStart, m_ArrayControl1, m_ArrayControl2, m_ArrayEndpoint);
        private Quaternion ArraySourceRotation => (ArrayIsCurve ? m_ArraySourceBaseRotation : Quaternion.identity) * m_ArrayRotation;

        private void InitializeArrayCurve()
        {
            m_ArrayPathKind = PartArrayPathKind.Line;
            m_ArrayControlPoint = PO2.Tools.ObjectTool.ArrayControlPoint.Endpoint;
            m_ArrayCurveInitialized = false;
            m_ArrayControl1 = m_ArrayControl2 = m_ArrayStart;
            m_ArraySourceBaseRotation = Quaternion.identity;
        }

        private void ClearArrayCurve()
        {
            m_ArrayCurveRevision++;
            m_ArrayCurveWorker?.Dispose();
            m_ArrayCurveWorker = null;
            m_ArrayCurveSource = null;
            m_ArrayCurveCache = null;
            m_ArrayPlacements = null;
            m_ArrayCurveRequest = null;
            m_ArrayDisplayedRequest = null;
            m_ArrayCurvePresentedRevision = -1;
            m_ArrayPreviewOffset = Quaternion.identity;
            m_ArrayCurveInitialized = false;
            m_ArraySourceBaseRotation = Quaternion.identity;
        }

        public void SetArrayPathKind(int value)
        {
            if (!CanEditArrayPath || value < 0 || value > (int)PartArrayPathKind.Curve || value == ArrayPathKind) return;
            m_ArrayPathKind = (PartArrayPathKind)value;
            m_ArrayControlPoint = PO2.Tools.ObjectTool.ArrayControlPoint.Endpoint;
            m_ArrayCurveRevision++;
            m_ArrayCurveWorker?.Clear();
            m_ArrayLayoutValid = false;
            if (ArrayIsCurve && !m_ArrayCurveInitialized)
            {
                SetStraightArrayHandles();
                m_ArrayCurveInitialized = true;
            }

            ClearGizmoHover();
            RefreshArrayLayout();
        }

        public void SetArrayAlignment(int value)
        {
            if (!CanEditArrayPath || !ArrayIsCurve || value < 0 || value > (int)PartArrayAlignment.FixedOrientation || value == ArrayAlignment) return;
            m_ArrayAlignment = (PartArrayAlignment)value;
            RefreshArrayLayout();
        }

        public void SelectArrayControlPoint(int value)
        {
            if (!CanEditArrayPath || !ArrayIsCurve || value < 0 || value > (int)PO2.Tools.ObjectTool.ArrayControlPoint.EndHandle) return;
            FinishNumericArrayEndpoint();
            if (m_ArrayDrawing) return;
            m_ArrayControlPoint = (ArrayControlPoint)value;
            m_ArraySceneReady = false;
            ClearGizmoHover();
        }

        private void SetStraightArrayHandles()
        {
            var straight = PartArrayBezier.Straight(m_ArrayStart, m_ArrayEndpoint);
            m_ArrayControl1 = straight.Control1;
            m_ArrayControl2 = straight.Control2;
        }

        private void MoveArrayCurveEndpoint(Vector3 delta)
        {
            if (!m_ArrayCurveInitialized) return;
            if (m_ArrayCurveSource == null) SetStraightArrayHandles();
            else m_ArrayControl2 += delta;
        }

        private void SetArrayControlPosition(Vector3 point)
        {
            if (!ArrayIsCurve || m_ArrayControlPoint == PO2.Tools.ObjectTool.ArrayControlPoint.Endpoint)
            {
                SetArrayEndpoint(point);
                return;
            }

            if (!PartArrayLayout.Finite(point) || point.Equals(ArrayControlPosition)) return;
            if (m_ArrayControlPoint == PO2.Tools.ObjectTool.ArrayControlPoint.StartHandle) m_ArrayControl1 = point;
            else m_ArrayControl2 = point;
            RefreshArrayLayout();
        }

        private void RefreshArrayCurve()
        {
            int revision = ++m_ArrayCurveRevision;
            if (m_ArrayCurveSource == null)
            {
                // The first draw is still choosing the reference direction. Show
                // the straight preview until the endpoint is confirmed.
                if (m_ArrayDrawing)
                {
                    RefreshArrayLine();
                    return;
                }

                if (!PartArraySource.TryCreate(m_ArrayPreview.Bounds, m_ArrayObject.Parts[m_ArrayPartIndex].LocalPivot, m_ArrayOwnerTransform, m_ArrayEndpoint - m_ArrayStart, m_ArrayPathFrame.YAxis, out m_ArrayCurveSource, out m_ArrayStatus))
                {
                    m_ArrayLayoutValid = false;
                    m_ArrayPreview.SetPlacements(null);
                    return;
                }
            }

            var request = new ArrayCurveRequest(revision, ArrayBezier, m_ArrayCurveSource, m_ArrayAlignment, m_ArrayMode, m_ArrayRotation, m_ArrayGap, m_ArrayCopies, ArrayCapacity);
            m_ArrayCurveRequest = request;
            if (m_ArrayPlacements?.CanReuseRotation == true && request.SameFit(m_ArrayDisplayedRequest))
            {
                // The positions are already solved. Changing the offset only
                // needs a small wrapper, not another worker round trip.
                m_ArrayCurveWorker?.Clear();
                m_ArrayPlacements = m_ArrayPlacements.WithRotationOffset(m_ArrayRotation, revision);
                m_ArrayDisplayedRequest = request;
                m_ArrayCurvePresentedRevision = revision;
                PresentArrayCurve();
                return;
            }

            if (m_ArrayPlacements != null && SameArrayCurveMode(m_ArrayDisplayedRequest) && !m_ArrayRotation.Equals(m_ArrayPreviewOffset))
            {
                // Fixed Orientation with gap spacing still needs a new fit, but
                // the visible parts can rotate immediately while that runs.
                m_ArrayPreview.SetPlacements(m_ArrayPlacements.WithPreviewRotationOffset(m_ArrayRotation));
                m_ArrayPreviewOffset = m_ArrayRotation;
            }

            if (m_ArrayCurveWorker == null) m_ArrayCurveWorker = new PartArrayCurveWorker();
            m_ArrayCurveWorker.Request(request);
        }

        private bool SameArrayCurveMode(ArrayCurveRequest request) => request != null && ReferenceEquals(request.Source, m_ArrayCurveSource) && request.Alignment == m_ArrayAlignment && request.Mode == m_ArrayMode;

        private void PollArrayCurve()
        {
            if (m_ArrayCurveWorker == null || !m_ArrayCurveWorker.Poll(out var result) || !ArrayIsCurve || result.Request.Revision <= m_ArrayCurvePresentedRevision || !SameArrayCurveMode(result.Request)) return;
            // An older completed frame is useful during a continuous drag. Only
            // final errors require the latest input, so a passing bad position
            // cannot clear a preview after the mouse has already moved away.
            if (result.Placements == null && result.Request.Revision != m_ArrayCurveRevision) return;
            m_ArrayCurveCache = result.Curve;
            m_ArrayPlacements = result.Placements;
            m_ArrayDisplayedRequest = result.Request;
            m_ArrayCurvePresentedRevision = result.Request.Revision;
            m_ArraySourceBaseRotation = m_ArrayPlacements != null ? m_ArrayPlacements.Source.Rotation * Quaternion.Inverse(result.Request.Rotation) : Quaternion.identity;
            if (m_ArrayPlacements?.CanReuseRotation == true && m_ArrayCurveRequest.SameFit(result.Request))
            {
                m_ArrayPlacements = m_ArrayPlacements.WithRotationOffset(m_ArrayRotation, m_ArrayCurveRevision);
                m_ArrayDisplayedRequest = m_ArrayCurveRequest;
                m_ArrayCurvePresentedRevision = m_ArrayCurveRevision;
                m_ArrayCurveWorker.Clear();
            }

            PresentArrayCurve(result.Reason);
        }

        private void PresentArrayCurve(LocalizedText reason = default)
        {
            m_ArrayLayoutValid = m_ArrayPlacements != null;
            m_ArrayPreview.SetPlacements(m_ArrayPlacements?.WithPreviewRotationOffset(m_ArrayRotation));
            m_ArrayPreviewOffset = m_ArrayRotation;
            m_ArrayStatus = !m_ArrayLayoutValid ? reason : m_ArrayPlacements.CapacityLimited ? LocalizedText.For("PO2.PartArray.Preview.CapacityNotice") : m_ArrayPlacements.Count == 0 ? LocalizedText.For("PO2.PartArray.Curve.ExtendInstruction") : m_ArrayPlacements.EndpointFitFallback ? LocalizedText.For("PO2.PartArray.Preview.EndpointGapNotice") : LocalizedText.Empty;
        }

        internal bool TryGetArrayCurveOverlay(out PartArrayCurve curve, out PartArrayBezier controls, out int selected)
        {
            curve = m_ArrayCurveCache;
            controls = ArrayBezier;
            selected = ArrayControlPoint;
            return ArrayActive && ArrayIsCurve;
        }

        // Markers choose which point owns the standard translation gizmo. A
        // screen-sized hit target remains usable while zooming or moving in 3D.
        private bool TrySelectArrayControlPoint(Ray ray, Camera camera, float gizmoDistance)
        {
            if (!ArrayIsCurve || !UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame) return false;
            int closest = -1;
            float distance = gizmoDistance;
            for (int i = 0; i < 3; i++)
            {
                if (i == ArrayControlPoint) continue;
                Vector3 point = i == 0 ? m_ArrayEndpoint : i == 1 ? m_ArrayControl1 : m_ArrayControl2;
                float along = Vector3.Dot(point - ray.origin, ray.direction);
                float radius = GizmoMath.GetWorldSizeForPixels(camera, point, 10f);
                if (along >= 0 && along < distance && (ray.GetPoint(along) - point).sqrMagnitude <= radius * radius)
                {
                    closest = i;
                    distance = along;
                }
            }

            if (closest < 0) return false;
            SelectArrayControlPoint(closest);
            return true;
        }
    }
}