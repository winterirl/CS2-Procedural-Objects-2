using System;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.Tools.Gizmos;

namespace PO2.Tools.MeshEdit
{
    internal enum FaceOperationKind
    {
        None = 0,
        Extrude = 1,
        Inset = 2,
    }

    internal enum FaceOperationPhase
    {
        None = 0,
        Selecting = 1,
        Positioning = 2,
    }

    // Face selection belongs to the operation that requested it. It does not
    // replace or clear the user's normal vertex/edge selection.
    internal sealed class FaceOperationState
    {
        private bool[] m_SelectedFaces = Array.Empty<bool>();
        private StructuralFacePickCache m_PickCache;
        private bool m_HasLastRay;
        private Vector3 m_LastLocalRayOrigin;
        private Vector3 m_LastLocalRayDirection;
        private ObjectTransform m_LastTransform;
        private bool m_HasLastTransform;
        private uint m_VisualVersion;
        private int m_SelectionVersion;

        private FaceRegionPlan m_AnalysisPlan;
        private FaceInsetPlan m_InsetPlan;
        private FaceInsetEvaluation m_InsetEvaluation;
        private int m_AnalysisSelectionVersion = -1;
        private ObjectTransform m_AnalysisTransform;
        private bool m_HasAnalysisTransform;

        private LoopCutPreviewLine[] m_PreviewLines = Array.Empty<LoopCutPreviewLine>();
        private uint m_PreviewVersion;
        private bool m_WaitingForPrimaryRelease;
        private bool m_NeedsPointerRebase;
        private float m_PointerOriginParameter;
        private float m_PointerBaseDistance;
        private Vector3 m_PositionWorldOrigin;
        private Vector3 m_PositionWorldAxis;
        private GizmoDragModifierState m_DragModifierState;
        private float m_OperationAmount;

        public FaceOperationPhase Phase { get; private set; }
        public bool IsActive => Phase != FaceOperationPhase.None;

        public bool IsSelecting =>
            Phase == FaceOperationPhase.Selecting;

        public bool IsPositioning =>
            Phase == FaceOperationPhase.Positioning;

        public FaceOperationKind Kind { get; private set; }
        public int ObjectId { get; private set; } = -1;
        public int PartIndex { get; private set; } = -1;
        public MeshPart SourcePart { get; private set; }
        public uint SourceObjectPartVersion { get; private set; }
        public uint SourcePartGeometryVersion { get; private set; }
        public uint SourcePartTopologyVersion { get; private set; }
        public uint SourceSurfaceAreaMembershipVersion { get; private set; }
        public int HoveredFaceId { get; private set; } = -1;
        public int SelectedFaceCount { get; private set; }
        public uint VisualVersion => m_VisualVersion;
        public bool[] SelectedFaces => m_SelectedFaces;

        public FaceRegionPlan ExtrusionPlan =>
            IsPositioning && Kind == FaceOperationKind.Extrude ? m_AnalysisPlan : null;

        public FaceInsetPlan InsetPlan =>
            IsPositioning && Kind == FaceOperationKind.Inset ? m_InsetPlan : null;

        public float ExtrusionDistance =>
            Kind == FaceOperationKind.Extrude ? m_OperationAmount : 0f;

        public float InsetAmount =>
            Kind == FaceOperationKind.Inset ? m_OperationAmount : 0f;

        public LoopCutPreviewLine[] PreviewLines => m_PreviewLines;
        public int PreviewLineCount => m_PreviewLines.Length;
        public uint PreviewVersion => m_PreviewVersion;

        public bool CanAcceptPrimaryCommit =>
            IsPositioning && !m_WaitingForPrimaryRelease;

        public bool CanCommit
        {
            get
            {
                if (!IsPositioning)
                {
                    return false;
                }

                if (Kind == FaceOperationKind.Extrude)
                {
                    return FaceExtrusionEditor.CanCommit(m_AnalysisPlan, m_OperationAmount);
                }

                return Kind == FaceOperationKind.Inset && m_InsetEvaluation != null && m_InsetEvaluation.CanCommit;
            }
        }

        public bool Begin(FaceOperationKind kind, ProceduralObject proceduralObject, int partIndex)
        {
            if (kind == FaceOperationKind.None || proceduralObject == null || partIndex < 0 || partIndex >= proceduralObject.Parts.Count)
            {
                return false;
            }

            MeshPart part = proceduralObject.Parts[partIndex];
            if (!StructuralFacePickCache.TryCreate(part, out StructuralFacePickCache pickCache))
            {
                return false;
            }

            Reset();

            Phase = FaceOperationPhase.Selecting;
            Kind = kind;
            ObjectId = proceduralObject.Id;
            PartIndex = partIndex;
            SourcePart = part;
            SourceObjectPartVersion = proceduralObject.PartVersion;
            SourcePartGeometryVersion = part.GeometryVersion;
            SourcePartTopologyVersion = part.TopologyVersion;
            SourceSurfaceAreaMembershipVersion = part.SurfaceAreaMembershipVersion;
            HoveredFaceId = -1;
            SelectedFaceCount = 0;
            m_SelectedFaces = new bool[part.StructuralTopology.FaceCount];
            m_PickCache = pickCache;
            m_HasLastRay = false;
            m_LastTransform = proceduralObject.Transform;
            m_HasLastTransform = true;
            m_SelectionVersion = 0;
            InvalidateAnalysis();
            SetPreviewLines(Array.Empty<LoopCutPreviewLine>());
            NotifyVisualChanged();
            return true;
        }

        public bool IsSourceCurrent(ProceduralObject proceduralObject, int activePartIndex)
        {
            if (!IsActive || proceduralObject == null || proceduralObject.IsLocked || proceduralObject.Id != ObjectId || proceduralObject.PartVersion != SourceObjectPartVersion || activePartIndex != PartIndex || PartIndex < 0 || PartIndex >= proceduralObject.Parts.Count)
            {
                return false;
            }

            MeshPart part = proceduralObject.Parts[PartIndex];
            if (!ReferenceEquals(part, SourcePart) || part.GeometryVersion != SourcePartGeometryVersion || part.TopologyVersion != SourcePartTopologyVersion || part.SurfaceAreaMembershipVersion != SourceSurfaceAreaMembershipVersion || part.StructuralTopology.FaceCount != m_SelectedFaces.Length)
            {
                return false;
            }

            // Positioning freezes the analyzed world-space plane. Mesh Edit
            // prevents concurrent object transforms, but reject an external
            // transform change instead of applying a stale normal.
            return !IsPositioning || (m_AnalysisPlan != null && SameTransform(proceduralObject.Transform, m_AnalysisPlan.SourceTransform));
        }

        public bool CanContinue(ProceduralObject proceduralObject)
        {
            return IsSelecting && RefreshAnalysis(proceduralObject);
        }

        public bool TryContinue(ProceduralObject proceduralObject, bool primaryIsPressed)
        {
            if (!IsSelecting || !RefreshAnalysis(proceduralObject) || m_AnalysisPlan == null)
            {
                return false;
            }

            Vector3 positionWorldOrigin = m_AnalysisPlan.WorldPivot;
            Vector3 positionWorldAxis = m_AnalysisPlan.WorldNormal;
            if (Kind == FaceOperationKind.Inset)
            {
                bool hasWorldRay = TryGetLastWorldRay(proceduralObject.Transform, out Ray worldRay);
                if (m_InsetPlan == null || !FaceInsetEditor.TryGetDragReference(m_InsetPlan, hasWorldRay, worldRay, out positionWorldOrigin, out positionWorldAxis))
                {
                    return false;
                }
            }

            Phase = FaceOperationPhase.Positioning;
            HoveredFaceId = -1;
            m_HasLastRay = false;
            m_OperationAmount = 0f;
            m_PositionWorldOrigin = positionWorldOrigin;
            m_PositionWorldAxis = positionWorldAxis;
            m_WaitingForPrimaryRelease = primaryIsPressed;
            m_NeedsPointerRebase = !primaryIsPressed;
            m_PointerOriginParameter = 0f;
            m_PointerBaseDistance = 0f;
            m_DragModifierState.Reset();
            RebuildPreview();
            NotifyVisualChanged();
            return true;
        }

        public bool BackToSelection()
        {
            if (!IsPositioning)
            {
                return false;
            }

            Phase = FaceOperationPhase.Selecting;
            m_OperationAmount = 0f;
            m_InsetEvaluation = null;
            m_WaitingForPrimaryRelease = false;
            m_NeedsPointerRebase = false;
            m_PointerOriginParameter = 0f;
            m_PointerBaseDistance = 0f;
            m_DragModifierState.Reset();
            SetPreviewLines(Array.Empty<LoopCutPreviewLine>());
            NotifyVisualChanged();
            return true;
        }

        public bool SetExtrusionDistance(float distance)
        {
            return Kind == FaceOperationKind.Extrude && SetOperationAmount(distance);
        }

        public bool SetInsetAmount(float amount)
        {
            return Kind == FaceOperationKind.Inset && SetOperationAmount(Mathf.Max(0f, amount));
        }

        public bool ReleasePrimaryLatchIfReady(bool primaryIsPressed)
        {
            if (!m_WaitingForPrimaryRelease || primaryIsPressed)
            {
                return false;
            }

            m_WaitingForPrimaryRelease = false;
            m_NeedsPointerRebase = true;
            return true;
        }

        public bool UpdatePosition(Camera camera, Ray worldRay, bool precision, bool snapEnabled, float normalIncrement, float precisionIncrement)
        {
            if (!IsPositioning || m_AnalysisPlan == null || m_WaitingForPrimaryRelease || !TryGetPointerParameter(camera, worldRay, m_PositionWorldOrigin, m_PositionWorldAxis, out float currentParameter))
            {
                return false;
            }

            if (m_NeedsPointerRebase)
            {
                m_NeedsPointerRebase = false;
                m_PointerOriginParameter = currentParameter;
                m_PointerBaseDistance = m_OperationAmount;
                m_DragModifierState.Begin(precision, snapEnabled);
                return false;
            }

            float rawDistance = currentParameter - m_PointerOriginParameter;
            if (!m_DragModifierState.ResolveLinear(rawDistance, precision, snapEnabled, normalIncrement, precisionIncrement, out float resolvedDelta))
            {
                return false;
            }

            float nextDistance = m_PointerBaseDistance + resolvedDelta;
            if (Kind == FaceOperationKind.Inset)
            {
                nextDistance = Mathf.Max(0f, nextDistance);
            }

            if (!IsFinite(nextDistance) || Mathf.Abs(nextDistance - m_OperationAmount) <= 0.000001f)
            {
                return false;
            }

            m_OperationAmount = nextDistance;
            RebuildPreview();
            NotifyVisualChanged();
            return true;
        }

        public void UpdateHover(ProceduralObject proceduralObject, Ray worldRay)
        {
            if (!IsSelecting || proceduralObject == null || m_PickCache == null)
            {
                ClearHover();
                return;
            }

            ObjectTransform transform = proceduralObject.Transform;
            bool transformChanged = !m_HasLastTransform || !SameTransform(transform, m_LastTransform);

            Vector3 localOrigin = transform.InverseTransformPoint(worldRay.origin);
            Vector3 localDirection = transform.InverseTransformVector(worldRay.direction);

            bool rayChanged = !m_HasLastRay || !ApproximatelyEqual(localOrigin, m_LastLocalRayOrigin) || !ApproximatelyEqual(localDirection, m_LastLocalRayDirection);

            m_LastTransform = transform;
            m_HasLastTransform = true;

            if (!rayChanged)
            {
                if (transformChanged)
                {
                    InvalidateAnalysis();
                    NotifyVisualChanged();
                }

                return;
            }

            m_LastLocalRayOrigin = localOrigin;
            m_LastLocalRayDirection = localDirection;
            m_HasLastRay = true;

            int nextHoveredFaceId = -1;
            m_PickCache.TryPick(localOrigin, localDirection, out nextHoveredFaceId);

            bool hoverChanged = nextHoveredFaceId != HoveredFaceId;
            HoveredFaceId = nextHoveredFaceId;
            if (hoverChanged || transformChanged)
            {
                if (transformChanged)
                {
                    InvalidateAnalysis();
                }

                NotifyVisualChanged();
            }
        }

        public bool ApplySelectionClick(bool shiftPressed, bool controlPressed)
        {
            if (!IsSelecting)
            {
                return false;
            }

            if (HoveredFaceId < 0 || HoveredFaceId >= m_SelectedFaces.Length)
            {
                if (shiftPressed || controlPressed)
                {
                    return false;
                }

                return ClearSelection();
            }

            bool changed;
            if (controlPressed)
            {
                changed = SetSelected(HoveredFaceId, false);
            }
            else if (shiftPressed)
            {
                changed = SetSelected(HoveredFaceId, true);
            }
            else
            {
                changed = ReplaceSelection(HoveredFaceId);
            }

            if (changed)
            {
                SelectionChanged();
            }

            return changed;
        }

        public bool ClearHover()
        {
            m_HasLastRay = false;
            if (HoveredFaceId < 0)
            {
                return false;
            }

            HoveredFaceId = -1;
            NotifyVisualChanged();
            return true;
        }

        public bool IsSelected(int faceId)
        {
            return faceId >= 0 && faceId < m_SelectedFaces.Length && m_SelectedFaces[faceId];
        }

        public StructuralFaceRef[] GetSelectionSnapshot()
        {
            if (!IsActive || SelectedFaceCount == 0)
            {
                return Array.Empty<StructuralFaceRef>();
            }

            StructuralFaceRef[] result = new StructuralFaceRef[SelectedFaceCount];
            int resultIndex = 0;
            for (int faceId = 0; faceId < m_SelectedFaces.Length; faceId++)
            {
                if (m_SelectedFaces[faceId])
                {
                    result[resultIndex++] = new StructuralFaceRef(PartIndex, faceId);
                }
            }

            return result;
        }

        public void Reset()
        {
            bool hadState = IsActive || HoveredFaceId >= 0 || SelectedFaceCount > 0 || m_PickCache != null || m_PreviewLines.Length > 0;

            Phase = FaceOperationPhase.None;
            Kind = FaceOperationKind.None;
            ObjectId = -1;
            PartIndex = -1;
            SourcePart = null;
            SourceObjectPartVersion = 0;
            SourcePartGeometryVersion = 0;
            SourcePartTopologyVersion = 0;
            SourceSurfaceAreaMembershipVersion = 0;
            HoveredFaceId = -1;
            SelectedFaceCount = 0;
            m_SelectedFaces = Array.Empty<bool>();
            m_PickCache = null;
            m_HasLastRay = false;
            m_LastLocalRayOrigin = Vector3.zero;
            m_LastLocalRayDirection = Vector3.zero;
            m_HasLastTransform = false;
            m_SelectionVersion = 0;
            InvalidateAnalysis();
            m_OperationAmount = 0f;
            m_PositionWorldOrigin = Vector3.zero;
            m_PositionWorldAxis = Vector3.zero;
            m_WaitingForPrimaryRelease = false;
            m_NeedsPointerRebase = false;
            m_PointerOriginParameter = 0f;
            m_PointerBaseDistance = 0f;
            m_DragModifierState.Reset();
            SetPreviewLines(Array.Empty<LoopCutPreviewLine>());

            if (hadState)
            {
                NotifyVisualChanged();
            }
        }

        private bool RefreshAnalysis(ProceduralObject proceduralObject)
        {
            if (!IsSelecting || proceduralObject == null || proceduralObject.Id != ObjectId || SelectedFaceCount == 0)
            {
                m_AnalysisPlan = null;
                m_InsetPlan = null;
                return false;
            }

            ObjectTransform transform = proceduralObject.Transform;
            if (m_AnalysisSelectionVersion == m_SelectionVersion && m_HasAnalysisTransform && SameTransform(transform, m_AnalysisTransform))
            {
                return m_AnalysisPlan != null && (Kind != FaceOperationKind.Inset || m_InsetPlan != null);
            }

            m_AnalysisSelectionVersion = m_SelectionVersion;
            m_AnalysisTransform = transform;
            m_HasAnalysisTransform = true;
            m_InsetPlan = null;
            if (!FaceRegionAnalyzer.TryAnalyze(proceduralObject, PartIndex, m_SelectedFaces, out m_AnalysisPlan))
            {
                return false;
            }

            return Kind != FaceOperationKind.Inset || FaceInsetEditor.TryCreatePlan(proceduralObject.Parts[PartIndex], m_AnalysisPlan, out m_InsetPlan);
        }

        private void InvalidateAnalysis()
        {
            m_AnalysisPlan = null;
            m_InsetPlan = null;
            m_InsetEvaluation = null;
            m_AnalysisSelectionVersion = -1;
            m_HasAnalysisTransform = false;
        }

        private void SelectionChanged()
        {
            unchecked
            {
                m_SelectionVersion++;
            }

            InvalidateAnalysis();
            NotifyVisualChanged();
        }

        private bool ReplaceSelection(int faceId)
        {
            if (SelectedFaceCount == 1 && m_SelectedFaces[faceId])
            {
                return false;
            }

            Array.Clear(m_SelectedFaces, 0, m_SelectedFaces.Length);
            m_SelectedFaces[faceId] = true;
            SelectedFaceCount = 1;
            return true;
        }

        private bool SetSelected(int faceId, bool selected)
        {
            if (m_SelectedFaces[faceId] == selected)
            {
                return false;
            }

            m_SelectedFaces[faceId] = selected;
            SelectedFaceCount += selected ? 1 : -1;
            return true;
        }

        private bool ClearSelection()
        {
            if (SelectedFaceCount == 0)
            {
                return false;
            }

            Array.Clear(m_SelectedFaces, 0, m_SelectedFaces.Length);
            SelectedFaceCount = 0;
            SelectionChanged();
            return true;
        }

        private bool SetOperationAmount(float amount)
        {
            if (!IsPositioning || !IsFinite(amount) || Mathf.Abs(amount - m_OperationAmount) <= 0.000001f)
            {
                return false;
            }

            m_OperationAmount = amount;
            m_NeedsPointerRebase = true;
            m_DragModifierState.Reset();
            RebuildPreview();
            NotifyVisualChanged();
            return true;
        }

        private void RebuildPreview()
        {
            if (Kind == FaceOperationKind.Extrude)
            {
                m_InsetEvaluation = null;
                SetPreviewLines(FaceExtrusionEditor.BuildPreviewLines(m_AnalysisPlan, m_OperationAmount));
                return;
            }

            if (Kind == FaceOperationKind.Inset && FaceInsetEditor.TryEvaluate(SourcePart, m_InsetPlan, m_OperationAmount, out FaceInsetEvaluation evaluation))
            {
                m_InsetEvaluation = evaluation;
                SetPreviewLines(evaluation.PreviewLines);
                return;
            }

            m_InsetEvaluation = null;
            SetPreviewLines(Array.Empty<LoopCutPreviewLine>());
        }

        private void SetPreviewLines(LoopCutPreviewLine[] previewLines)
        {
            m_PreviewLines = previewLines ?? Array.Empty<LoopCutPreviewLine>();
            unchecked
            {
                m_PreviewVersion++;
                if (m_PreviewVersion == 0)
                {
                    m_PreviewVersion = 1;
                }
            }
        }

        private bool TryGetLastWorldRay(ObjectTransform transform, out Ray worldRay)
        {
            worldRay = default;
            if (!m_HasLastRay)
            {
                return false;
            }

            Vector3 worldOrigin = transform.TransformPoint(m_LastLocalRayOrigin);
            Vector3 worldDirection = transform.TransformVector(m_LastLocalRayDirection);
            if (!IsFinite(worldOrigin) || !IsFinite(worldDirection) || worldDirection.sqrMagnitude <= 0.000000000001f)
            {
                return false;
            }

            worldRay = new Ray(worldOrigin, worldDirection.normalized);
            return true;
        }

        private static bool TryGetPointerParameter(Camera camera, Ray worldRay, Vector3 worldPivot, Vector3 worldAxis, out float parameter)
        {
            if (GizmoMath.TryGetClosestRayLineParameter(worldRay, worldPivot, worldAxis, out parameter))
            {
                return true;
            }

            parameter = 0f;
            if (!GizmoMath.TryCreateTranslationDragPlane(camera, worldPivot, worldAxis, out Plane dragPlane) || !GizmoMath.TryRayPlane(worldRay, dragPlane, out Vector3 hit))
            {
                return false;
            }

            parameter = Vector3.Dot(hit - worldPivot, worldAxis.normalized);
            return IsFinite(parameter);
        }

        private void NotifyVisualChanged()
        {
            unchecked
            {
                m_VisualVersion++;
                if (m_VisualVersion == 0)
                {
                    m_VisualVersion = 1;
                }
            }
        }

        private static bool SameTransform(ObjectTransform left, ObjectTransform right)
        {
            return ApproximatelyEqual(left.Position, right.Position) && ApproximatelyEqual(left.Scale, right.Scale) && Mathf.Abs(Quaternion.Dot(left.Rotation, right.Rotation)) >= 0.999999f;
        }

        private static bool ApproximatelyEqual(Vector3 left, Vector3 right)
        {
            return (left - right).sqrMagnitude <= 0.000000000001f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}