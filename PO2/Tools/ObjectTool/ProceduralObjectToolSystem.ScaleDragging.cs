using Game;
using Game.Input;
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
        private ScaleDragState m_ScaleDrag;

        private void BeginScaleDrag(Camera camera, Ray mouseRay, in GizmoContext context, GizmoHandle handle)
        {
            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (selected == null || selected.Id != context.ObjectId)
            {
                return;
            }

            float gizmoSize = context.Size;

            if (handle == GizmoHandle.ScaleUniform)
            {
                if (context.TargetKind == GizmoTargetKind.Part && !TryBeginPartTransform(selected, context, PartTransformKind.Scale))
                {
                    return;
                }

                if (context.TargetKind == GizmoTargetKind.VertexSelection && !TryBeginSelectionTransform(selected, context, handle, LocalizedText.For("PO2.History.Action.Vertices.Scale")))
                {
                    return;
                }

                if (context.TargetKind == GizmoTargetKind.CageCorners && !BeginCageDrag(selected, context)) return;

                m_ScaleDrag = new ScaleDragState
                {
                    ObjectId = selected.Id,
                    Handle = handle,
                    StartTransform = selected.Transform,
                    StartScale = selected.Transform.Scale,
                    Frame = context.Frame,
                    Pivot = context.Pivot,
                    AxisIndex = -1,
                    StartMousePosition = InputManager.instance.mousePosition,
                    GizmoSize = gizmoSize,
                    ActiveHandleLength = gizmoSize,
                };

                StartGizmoDrag(handle, context.TargetKind);

                return;
            }

            if (!GizmoMath.TryGetTransformAxis(handle, out TransformAxis transformAxis))
            {
                return;
            }

            Vector3 worldAxis = context.Frame.GetAxis(transformAxis);

            if (!GizmoMath.TryCreateTranslationDragPlane(camera, context.Pivot, worldAxis, out Plane dragPlane))
            {
                Mod.log.Warn($"[PO Gizmo] Could not create scale drag plane for {handle}.");
                return;
            }

            if (!GizmoMath.TryRayPlane(mouseRay, dragPlane, out Vector3 startMouseHit))
            {
                Mod.log.Warn($"[PO Gizmo] Mouse ray did not intersect scale drag plane for {handle}.");
                return;
            }

            float startAxisParameter = Vector3.Dot(startMouseHit - context.Pivot, worldAxis.normalized);

            GizmoMath.TryGetClosestRayLineParameter(mouseRay, context.Pivot, worldAxis, out startAxisParameter);

            if (context.TargetKind == GizmoTargetKind.Part && !TryBeginPartTransform(selected, context, PartTransformKind.Scale))
            {
                return;
            }

            if (context.TargetKind == GizmoTargetKind.VertexSelection && !TryBeginSelectionTransform(selected, context, handle, LocalizedText.For("PO2.History.Action.Vertices.Scale")))
            {
                return;
            }

            if (context.TargetKind == GizmoTargetKind.CageCorners && !BeginCageDrag(selected, context)) return;

            m_ScaleDrag = new ScaleDragState
            {
                ObjectId = selected.Id,
                Handle = handle,
                StartTransform = selected.Transform,
                StartScale = selected.Transform.Scale,
                Frame = context.Frame,
                Pivot = context.Pivot,
                Axis = worldAxis.normalized,
                AxisIndex = GizmoMath.GetScaleAxisIndex(handle),
                StartAxisParameter = startAxisParameter,
                DragPlane = dragPlane,
                StartMouseHit = startMouseHit,
                StartMousePosition = InputManager.instance.mousePosition,
                GizmoSize = gizmoSize,
                ActiveHandleLength = gizmoSize,
            };

            StartGizmoDrag(handle, context.TargetKind);
        }

        private void UpdateScaleDrag(Ray mouseRay, bool precision, bool snapEnabled)
        {
            const float minimumScale = 0.01f;

            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (selected == null || selected.Id != m_ScaleDrag.ObjectId || (m_ActiveGizmoTargetKind == GizmoTargetKind.Part && !m_PartTransformDrag.Matches(selected)))
            {
                CancelGizmoDrag(restoreTransform: false);
                return;
            }

            float unsnappedScaleDelta;

            if (m_ScaleDrag.Handle == GizmoHandle.ScaleUniform)
            {
                Vector2 currentMouse = InputManager.instance.mousePosition;

                float rawPixelDelta = currentMouse.y - m_ScaleDrag.StartMousePosition.y;

                float effectivePixelDelta = m_DragModifierState.UpdateEffectiveInput(rawPixelDelta, precision);

                // Exponential response keeps scaling positive and gives similar
                // pointer travel for doubling or halving at different sizes.
                unsnappedScaleDelta = Mathf.Exp(effectivePixelDelta * 0.006f) - 1f;
            }
            else
            {
                float rawAxisDistance;

                if (GizmoMath.TryGetClosestRayLineParameter(mouseRay, m_ScaleDrag.Pivot, m_ScaleDrag.Axis, out float currentAxisParameter))
                {
                    rawAxisDistance = currentAxisParameter - m_ScaleDrag.StartAxisParameter;
                }
                else
                {
                    if (!GizmoMath.TryRayPlane(mouseRay, m_ScaleDrag.DragPlane, out Vector3 currentMouseHit))
                    {
                        return;
                    }

                    Vector3 mouseDelta = currentMouseHit - m_ScaleDrag.StartMouseHit;

                    rawAxisDistance = Vector3.Dot(mouseDelta, m_ScaleDrag.Axis);
                }

                float rawScaleDelta = rawAxisDistance / Mathf.Max(0.001f, m_ScaleDrag.GizmoSize);

                unsnappedScaleDelta = m_DragModifierState.UpdateEffectiveInput(rawScaleDelta, precision);
            }

            if (!m_DragModifierState.Resolve(unsnappedScaleDelta, precision, snapEnabled, m_SnapSettings.ScaleIncrement, m_SnapSettings.PrecisionScaleIncrement, out float scaleDelta))
            {
                return;
            }

            float scaleFactor = Mathf.Max(minimumScale, 1f + scaleDelta);

            if (GizmoMath.IsAxisScaleHandle(m_ScaleDrag.Handle))
            {
                m_ScaleDrag.ActiveHandleLength = m_ScaleDrag.GizmoSize * scaleFactor;
            }

            if (m_ActiveGizmoTargetKind == GizmoTargetKind.Part || m_ActiveGizmoTargetKind == GizmoTargetKind.VertexSelection || m_ActiveGizmoTargetKind == GizmoTargetKind.CageCorners)
            {
                Vector3 scaleFactors = Vector3.one;

                if (m_ScaleDrag.Handle == GizmoHandle.ScaleUniform)
                {
                    scaleFactors = Vector3.one * scaleFactor;
                }
                else
                {
                    switch (m_ScaleDrag.AxisIndex)
                    {
                        case 0:
                            scaleFactors.x = scaleFactor;
                            break;

                        case 1:
                            scaleFactors.y = scaleFactor;
                            break;

                        case 2:
                            scaleFactors.z = scaleFactor;
                            break;

                        default:
                            return;
                    }
                }

                Matrix4x4 worldFromStart = CreateWorldScaleTransform(m_ScaleDrag.Pivot, m_ScaleDrag.Frame, scaleFactors);

                if (m_ActiveGizmoTargetKind == GizmoTargetKind.CageCorners)
                {
                    m_CageDrag.TryScale(selected, m_CageDragMask, worldFromStart, m_CageScaleDragPivot, scaleFactor == 1f, out m_DeformationStatus);
                }
                else if (m_ActiveGizmoTargetKind == GizmoTargetKind.VertexSelection)
                {
                    TryApplySelectionTransformPreview(selected, worldFromStart, scaleFactor == 1f);
                }
                else
                {
                    TryApplyPartTransformPreview(selected, worldFromStart);
                }

                return;
            }

            if (m_ScaleDrag.Handle == GizmoHandle.ScaleUniform)
            {
                Vector3 scale = m_ScaleDrag.StartScale * scaleFactor;

                scale = new Vector3(Mathf.Max(minimumScale, scale.x), Mathf.Max(minimumScale, scale.y), Mathf.Max(minimumScale, scale.z));

                if (!selected.TrySetScaleAroundPivot(scale))
                {
                    CancelGizmoDrag(restoreTransform: true);
                }

                return;
            }

            Vector3 newScale = m_ScaleDrag.StartScale;

            switch (m_ScaleDrag.AxisIndex)
            {
                case 0:
                    newScale.x = Mathf.Max(minimumScale, m_ScaleDrag.StartScale.x * scaleFactor);
                    break;

                case 1:
                    newScale.y = Mathf.Max(minimumScale, m_ScaleDrag.StartScale.y * scaleFactor);
                    break;

                case 2:
                    newScale.z = Mathf.Max(minimumScale, m_ScaleDrag.StartScale.z * scaleFactor);
                    break;

                default:
                    return;
            }

            if (!selected.TrySetScaleAroundPivot(newScale))
            {
                CancelGizmoDrag(restoreTransform: true);
            }
        }
    }
}