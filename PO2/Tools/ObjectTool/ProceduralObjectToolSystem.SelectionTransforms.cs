using Game;
using Game.Input;
using Game.Tools;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.History;
using PO2.Localization;
using PO2.Tools.Gizmos;
using PO2.Tools.MeshEdit;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private bool TryBeginSelectionTransform(ProceduralObject selected, in GizmoContext context, GizmoHandle handle, LocalizedText description)
        {
            if (!CanApplyMeshNumericMove || selected.Id != context.ObjectId || !SelectionTransformMath.TryGetLocalTransform(Matrix4x4.TRS(selected.Transform.Position, selected.Transform.Rotation, selected.Transform.Scale), Matrix4x4.identity, true, out _))
            {
                return false;
            }

            if (!m_MeshEditController.TryGetSelectionLocalCentroid(out Vector3 localPivot))
            {
                return false;
            }

            if (m_MeshTransformPivot == SelectionTransformPivot.SavedPivot)
            {
                if (m_MeshEditController.VertexScope == MeshEditVertexScope.CurrentPart)
                {
                    int partIndex = m_ProceduralObjectSystem.SelectedPartIndex;
                    if (partIndex < 0 || partIndex >= selected.Parts.Count) return false;
                    localPivot = selected.Parts[partIndex].LocalPivot;
                }
                else
                {
                    localPivot = selected.LocalPivot;
                }
            }

            m_VertexTransformDrag.Reset();
            m_VertexTransformDrag.LocalPivot = localPivot;
            m_VertexTransformDrag.BeginCapture(selected.Id, handle, context.Frame, default, context.Pivot, 0f, default, Vector3.zero, context.Size, selected.Transform, m_MeshEditController.EffectiveSelectionCount);

            if (!m_MeshEditController.CaptureSelectedVertices(selected, m_VertexTransformDrag, description))
            {
                m_VertexTransformDrag.Reset();
                return false;
            }

            m_MeshEditController.SetVertexHoverSuppressed(true);
            return true;
        }

        private void TryApplySelectionTransformPreview(ProceduralObject selected, Matrix4x4 worldFromStart, bool neutral)
        {
            if (!m_MeshEditController.TryApplyVertexTransform(selected, m_VertexTransformDrag, worldFromStart, neutral))
            {
                Mod.log.Warn("[PO Mesh Edit] Selection transform was cancelled because its target or result was invalid.");
                CancelGizmoDrag(restoreTransform: true);
            }
        }

        private void ApplyMeshRotationDelta(TransformAxis axis, float degrees)
        {
            if (!IsFinite(degrees) || degrees % 360f == 0f || !TryGetNumericSelectionContext(out ProceduralObject selected, out GizmoContext context))
            {
                return;
            }

            Matrix4x4 worldFromStart = SelectionTransformMath.CreateWorldRotation(context.Pivot, context.Frame.GetAxis(axis), degrees, out bool neutral);
            ApplyNumericSelectionTransform(selected, context, worldFromStart, neutral, LocalizedText.For("PO2.History.Action.Vertices.Rotate"));
        }

        public void ScaleMeshSelectionUniform(float delta)
        {
            ApplyMeshScaleDelta(-1, delta);
        }

        private void ApplyMeshScaleDelta(int axisIndex, float delta)
        {
            if (!IsFinite(delta) || delta == 0f || axisIndex < -1 || axisIndex > 2 || !TryGetNumericSelectionContext(out ProceduralObject selected, out GizmoContext context))
            {
                return;
            }

            float factor = Mathf.Max(0.01f, 1f + delta);
            Vector3 factors = Vector3.one;
            switch (axisIndex)
            {
                case -1: factors = Vector3.one * factor; break;
                case 0: factors.x = factor; break;
                case 1: factors.y = factor; break;
                case 2: factors.z = factor; break;
            }

            ApplyNumericSelectionTransform(selected, context, CreateWorldScaleTransform(context.Pivot, context.Frame, factors), factor == 1f, LocalizedText.For("PO2.History.Action.Vertices.Scale"));
        }

        private bool TryGetNumericSelectionContext(out ProceduralObject selected, out GizmoContext context)
        {
            selected = null;
            context = default;
            if (!CanApplyMeshNumericMove || !TryGetMeshEditObject(out selected))
            {
                return false;
            }

            SynchronizeCustomTransformOrientationTarget();
            if (!TryGetMeshTransformPivot(selected, out Vector3 pivot))
            {
                return false;
            }

            context = new GizmoContext(GizmoTargetKind.VertexSelection, selected.Id, -1, m_MeshTransformMode, pivot, ResolveActiveTransformFrame(selected), 0f);
            return true;
        }

        private void ApplyNumericSelectionTransform(ProceduralObject selected, in GizmoContext context, Matrix4x4 worldFromStart, bool neutral, LocalizedText description)
        {
            if (!TryBeginSelectionTransform(selected, context, GizmoHandle.None, description))
            {
                return;
            }

            try
            {
                if (!m_MeshEditController.TryApplyVertexTransform(selected, m_VertexTransformDrag, worldFromStart, neutral) || !m_VertexTransformDrag.History.Complete())
                {
                    Mod.log.Warn($"[PO Mesh Edit] {description} could not be applied and was restored.");
                }
            }
            finally
            {
                // Disposing an incomplete edit restores all affected parts.
                m_VertexTransformDrag.Reset();
                m_MeshEditController.SetVertexHoverSuppressed(false);
                m_MeshEditController.OnGeometryChanged(selected);
            }
        }

        private readonly VertexTransformDragState m_VertexTransformDrag = new VertexTransformDragState();

        private GizmoMode m_MeshTransformMode = GizmoMode.Translate;
        private SelectionTransformPivot m_MeshTransformPivot;
        public GizmoMode MeshTransformMode => m_MeshTransformMode;
        public SelectionTransformPivot MeshTransformPivot => m_MeshTransformPivot;

        public bool CanApplyMeshNumericMove
        {
            get
            {
                if (!TryGetMeshEditObject(out ProceduralObject editedObject) || editedObject.IsLocked || !m_MeshEditController.CanEditSelectedGeometry(editedObject) || m_MeshEditController.EffectiveSelectionCount == 0 || (m_MeshEditController.SelectionMode != MeshSelectionMode.Vertex && m_MeshEditController.SelectionMode != MeshSelectionMode.Edge))
                {
                    return false;
                }

                return !DeformationCommitBusy && !m_ProceduralObjectSystem.ActionHistory.IsBusy && !m_IsGizmoDragging && !m_BoxSelection.IsGestureActive && !m_MeshEditController.IsAlignmentTargeting && !m_MeshEditController.IsVertexSlideTargeting && !m_IsVertexSlideDragging && !IsMeshTopologyInteractionActive;
            }
        }

        private bool TryGetMeshTransformPivot(ProceduralObject editedObject, out Vector3 pivot)
        {
            pivot = Vector3.zero;
            if (m_MeshEditController == null || !m_MeshEditController.TryGetSelectionWorldCentroid(out pivot))
            {
                return false;
            }

            if (m_MeshTransformMode != GizmoMode.Translate && m_MeshTransformPivot == SelectionTransformPivot.SavedPivot)
            {
                return TryGetDisplayedPivotWorld(out pivot);
            }

            if (m_MeshTransformMode != GizmoMode.Translate && m_MeshEditController.TryGetSelectionLocalCentroid(out Vector3 localPivot))
            {
                pivot = editedObject.Transform.TransformPoint(localPivot);
            }

            return IsFinite(pivot);
        }

        public void SetMeshTransformMode(int value)
        {
            if (m_Mode != ObjectToolMode.MeshEdit || value < (int)GizmoMode.Translate || value > (int)GizmoMode.Scale || IsMeshTopologyInteractionActive || DeformationCommitBusy)
            {
                return;
            }

            CancelGizmoDrag(restoreTransform: true);
            ClearGizmoHover();
            m_MeshTransformMode = (GizmoMode)value;
        }

        public void SetMeshTransformPivot(int value)
        {
            if (m_Mode != ObjectToolMode.MeshEdit || value < (int)SelectionTransformPivot.SelectionCenter || value > (int)SelectionTransformPivot.SavedPivot || IsMeshTopologyInteractionActive || DeformationCommitBusy)
            {
                return;
            }

            CancelGizmoDrag(restoreTransform: true);
            ClearGizmoHover();
            m_MeshTransformPivot = (SelectionTransformPivot)value;
        }

        private void ApplyMeshEditMoveDelta(TransformAxis axis, float deltaMeters)
        {
            if (!CanApplyMeshNumericMove || !TryGetMeshEditObject(out ProceduralObject editedObject))
            {
                Mod.log.Warn("[PO Mesh Edit] Cannot apply a numeric move delta " + "without an editable vertex or edge selection.");
                return;
            }

            TransformFrame frame = ResolveActiveTransformFrame(editedObject);

            Vector3 worldDelta = frame.GetAxis(axis) * deltaMeters;

            Vector3 localDelta = editedObject.Transform.InverseTransformVector(worldDelta);

            if (!m_MeshEditController.ApplySelectedMoveDelta(editedObject, localDelta, out int movedLogicalVertexCount, out int movedRenderVertexCount))
            {
                return;
            }
        }

        private void ActivateVertexMove(Camera camera, Ray mouseRay, in GizmoContext context, GizmoHandle handle)
        {
            if (!TryGetMeshEditObject(out ProceduralObject editedObject) || editedObject.Id != context.ObjectId || m_MeshEditController == null)
            {
                return;
            }

            if (!TranslationConstraint.TryCreate(handle, context.Frame, out TranslationConstraint constraint))
            {
                return;
            }

            Vector3 axis = constraint.UAxis;
            Plane dragPlane;

            if (constraint.IsPlane)
            {
                dragPlane = new Plane(constraint.Normal, context.Pivot);
            }
            else
            {
                if (!GizmoMath.TryCreateTranslationDragPlane(camera, context.Pivot, axis, out dragPlane))
                {
                    Mod.log.Warn($"[PO Mesh Edit] Could not create vertex drag plane for {handle}.");
                    return;
                }
            }

            Vector3 startMouseHit;

            bool hasStartMouseHit = constraint.IsPlane ? GizmoMath.TryRayPlane(mouseRay, dragPlane, GizmoMath.kMinimumPlaneRayIncidence, out startMouseHit) : GizmoMath.TryRayPlane(mouseRay, dragPlane, out startMouseHit);

            if (!hasStartMouseHit)
            {
                Mod.log.Warn($"[PO Mesh Edit] Mouse ray did not intersect vertex drag plane for {handle}.");
                return;
            }

            float startAxisParameter = 0f;

            if (!constraint.IsPlane)
            {
                startAxisParameter = Vector3.Dot(startMouseHit - context.Pivot, axis);

                // Prefer the direct ray/axis solve, but keep the drag-plane
                // value as a fallback when the view is nearly parallel.
                GizmoMath.TryGetClosestRayLineParameter(mouseRay, context.Pivot, axis, out startAxisParameter);
            }

            m_VertexTransformDrag.BeginCapture(editedObject.Id, handle, context.Frame, constraint, context.Pivot, startAxisParameter, dragPlane, startMouseHit, context.Size, editedObject.Transform, m_MeshEditController.EffectiveSelectionCount);

            CaptureMeshSnapStart(editedObject);

            if (!m_MeshEditController.CaptureSelectedVertices(editedObject, m_VertexTransformDrag))
            {
                m_VertexTransformDrag.Reset();
                return;
            }

            SealMeshSnapStart(editedObject);
            m_TranslationSceneSnapWasEnabled = false;

            StartGizmoDrag(handle, GizmoTargetKind.VertexSelection);

            m_MeshEditController.SetVertexHoverSuppressed(true);
        }

        private void UpdateVertexMoveDrag(Ray mouseRay, Vector2 mousePosition, bool precision, bool incrementSnapEnabled, bool targetSnapEnabled)
        {
            if (!TryGetMeshEditObject(out ProceduralObject editedObject) || editedObject.Id != m_VertexTransformDrag.ObjectId || m_MeshEditController == null)
            {
                CancelGizmoDrag(restoreTransform: false);
                return;
            }

            if (!TryResolveTranslationDragInput(mouseRay, m_VertexTransformDrag.StartPivot, m_VertexTransformDrag.Constraint, m_VertexTransformDrag.StartAxisParameter, m_VertexTransformDrag.DragPlane, m_VertexTransformDrag.StartMouseHit, precision, incrementSnapEnabled, out Vector2 components, out bool incrementChanged))
            {
                return;
            }

            bool hadTargetSnap = m_TranslationSnapState.HasActiveSnap;
            bool snapReady = UpdateMeshSnapReadiness(editedObject, mousePosition, targetSnapEnabled);

            bool targetSnapped = TryResolveTranslationSnap(targetSnapEnabled && snapReady, mousePosition, components, out Vector2 targetComponents);

            if (targetSnapped)
            {
                components = targetComponents;
            }

            if (!incrementChanged && !targetSnapped && !hadTargetSnap)
            {
                return;
            }

            // Vertex positions live in object-local space even though the gizmo
            // axis and pointer solve are in world space.
            Vector3 worldDelta = m_VertexTransformDrag.Constraint.ComposeWorldDelta(components);

            Vector3 localDelta = m_VertexTransformDrag.StartObjectTransform.InverseTransformVector(worldDelta);

            bool moved = m_MeshEditController.ApplyVertexMove(editedObject, m_VertexTransformDrag, localDelta);
            if (moved)
            {
                m_MeshSnapPreparation.AcceptOwnGeometry(editedObject);
                if (m_MeshSnapPreparation.ValidateActive(editedObject)) m_MeshTranslationSnapTargetSession?.AcceptOwnGeometry(editedObject);
            }

            if (m_VertexTransformDrag.History?.PreviewFailed == true)
            {
                Mod.log.Warn("[PO Mesh Edit] Move cancelled because its normal update was invalid.");
                CancelGizmoDrag(restoreTransform: true);
                return;
            }
        }
    }
}