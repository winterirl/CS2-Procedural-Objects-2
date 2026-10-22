using Game;
using Game.Input;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Tools.Gizmos;
using PO2.Tools.Snapping;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private TranslateDragState m_TranslateDrag;
        private readonly TranslationSnapState m_TranslationSnapState = new TranslationSnapState();

        private void BeginTranslateDrag(Camera camera, Ray mouseRay, in GizmoContext context, GizmoHandle handle)
        {
            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (selected == null || selected.Id != context.ObjectId)
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
                    Mod.log.Warn($"[PO Gizmo] Could not create drag plane for {handle}.");
                    return;
                }
            }

            Vector3 startMouseHit;

            bool hasStartMouseHit = constraint.IsPlane ? GizmoMath.TryRayPlane(mouseRay, dragPlane, GizmoMath.kMinimumPlaneRayIncidence, out startMouseHit) : GizmoMath.TryRayPlane(mouseRay, dragPlane, out startMouseHit);

            if (!hasStartMouseHit)
            {
                Mod.log.Warn($"[PO Gizmo] Mouse ray did not intersect drag plane for {handle}.");
                return;
            }

            float startAxisParameter = 0f;

            if (!constraint.IsPlane)
            {
                startAxisParameter = Vector3.Dot(startMouseHit - context.Pivot, axis);

                GizmoMath.TryGetClosestRayLineParameter(mouseRay, context.Pivot, axis, out startAxisParameter);
            }

            if (context.TargetKind == GizmoTargetKind.Part && !TryBeginPartTransform(selected, context, PartTransformKind.Translation))
            {
                return;
            }

            if (context.TargetKind == GizmoTargetKind.CageCorners && !BeginCageDrag(selected, context)) return;

            m_TranslateDrag = new TranslateDragState
            {
                ObjectId = selected.Id,
                Handle = handle,
                StartTransform = selected.Transform,
                StartPosition = context.Pivot,
                Frame = context.Frame,
                Constraint = constraint,
                StartAxisParameter = startAxisParameter,
                DragPlane = dragPlane,
                StartMouseHit = startMouseHit,
                GizmoSize = context.Size,
            };

            m_ObjectTranslationSnapCaptureReady = false;
            m_ObjectTranslationSnapTargetCache.Clear();
            ClearMeshTranslationSnapTargetSession();
            m_TranslationSnapState.Begin(null, constraint);
            m_TranslationSceneSnapWasEnabled = false;

            if (context.TargetKind == GizmoTargetKind.Object && IsTargetSnappingActiveForCurrentModifiers())
            {
                PrepareObjectTranslationSnap(selected, -1);
            }
            else if (context.TargetKind == GizmoTargetKind.Part && IsTargetSnappingActiveForCurrentModifiers())
            {
                PrepareObjectTranslationSnap(selected, context.PartIndex);
            }

            StartGizmoDrag(handle, context.TargetKind);
        }

        private void UpdateTranslateDrag(Ray mouseRay, Vector2 mousePosition, bool precision, bool incrementSnapEnabled, bool targetSnapEnabled)
        {
            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (selected == null || selected.Id != m_TranslateDrag.ObjectId || (m_ActiveGizmoTargetKind == GizmoTargetKind.Part && !m_PartTransformDrag.Matches(selected)))
            {
                CancelGizmoDrag(restoreTransform: false);
                return;
            }

            if (!TryResolveTranslationDragInput(mouseRay, m_TranslateDrag.StartPosition, m_TranslateDrag.Constraint, m_TranslateDrag.StartAxisParameter, m_TranslateDrag.DragPlane, m_TranslateDrag.StartMouseHit, precision, incrementSnapEnabled, out Vector2 components, out bool incrementChanged))
            {
                return;
            }

            if (m_ActiveGizmoTargetKind == GizmoTargetKind.CageCorners)
            {
                if (incrementChanged) m_CageDrag.TryMove(selected, m_CageDragMask, m_TranslateDrag.Constraint.ComposeWorldDelta(components), out m_DeformationStatus);
                return;
            }

            bool hadTargetSnap = m_TranslationSnapState.HasActiveSnap;

            if (targetSnapEnabled && !m_ObjectTranslationSnapCaptureReady)
            {
                PrepareObjectTranslationSnap(selected, m_ActiveGizmoTargetKind == GizmoTargetKind.Part ? m_PartTransformDrag.PartIndex : -1);
            }

            bool targetSnapped = TryResolveTranslationSnap(targetSnapEnabled, mousePosition, components, out Vector2 targetComponents);

            if (targetSnapped)
            {
                components = targetComponents;
            }

            if (!incrementChanged && !targetSnapped && !hadTargetSnap)
            {
                return;
            }

            Vector3 worldDelta = m_TranslateDrag.Constraint.ComposeWorldDelta(components);

            if (m_ActiveGizmoTargetKind == GizmoTargetKind.Part)
            {
                Matrix4x4 worldFromStart = Matrix4x4.Translate(worldDelta);

                TryApplyPartTransformPreview(selected, worldFromStart);

                return;
            }

            Vector3 newRootPosition = m_TranslateDrag.StartTransform.Position + worldDelta;
            if (!selected.Transform.Position.Equals(newRootPosition))
            {
                selected.SetPosition(newRootPosition);
            }
        }

        private bool TryResolveTranslationDragInput(Ray mouseRay, Vector3 pivot, TranslationConstraint constraint, float startAxisParameter, Plane dragPlane, Vector3 startMouseHit, bool precision, bool incrementSnapEnabled, out Vector2 components, out bool changed)
        {
            components = Vector2.zero;
            changed = false;

            Vector2 rawComponents;

            if (constraint.IsPlane)
            {
                if (!GizmoMath.TryRayPlane(mouseRay, dragPlane, GizmoMath.kMinimumPlaneRayIncidence, out Vector3 currentMouseHit))
                {
                    return false;
                }

                rawComponents = constraint.ProjectWorldDelta(currentMouseHit - startMouseHit);
            }
            else
            {
                float rawAxisDistance;

                // Keep the established direct axis solve and its stable
                // camera-plane fallback for the original X/Y/Z handles.
                if (GizmoMath.TryGetClosestRayLineParameter(mouseRay, pivot, constraint.UAxis, out float currentAxisParameter))
                {
                    rawAxisDistance = currentAxisParameter - startAxisParameter;
                }
                else
                {
                    if (!GizmoMath.TryRayPlane(mouseRay, dragPlane, out Vector3 currentMouseHit))
                    {
                        return false;
                    }

                    rawAxisDistance = Vector3.Dot(currentMouseHit - startMouseHit, constraint.UAxis);
                }

                rawComponents = new Vector2(rawAxisDistance, 0f);
            }

            bool uChanged = m_DragModifierState.ResolveLinear(rawComponents.x, precision, incrementSnapEnabled, m_SnapSettings.MoveIncrement, m_SnapSettings.PrecisionMoveIncrement, out float uDistance);

            float vDistance = 0f;
            bool vChanged = false;

            if (constraint.IsPlane)
            {
                vChanged = m_SecondaryDragModifierState.ResolveLinear(rawComponents.y, precision, incrementSnapEnabled, m_SnapSettings.MoveIncrement, m_SnapSettings.PrecisionMoveIncrement, out vDistance);
            }

            components = new Vector2(uDistance, vDistance);

            changed = uChanged || vChanged;
            return true;
        }
    }
}