using Game;
using Game.Input;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Core.Deformation;
using PO2.Geometry.Editing;
using PO2.History;
using PO2.Localization;
using PO2.Tools.Gizmos;
using PO2.Tools.MeshEdit;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private readonly CageDeformationDragState m_CageDrag = new CageDeformationDragState();
        private bool m_DeformMode;
        private byte m_CageSelection, m_CageDragMask;
        private int m_CageHovered = -1, m_CagePartIndex = -2;
        private ProceduralObject m_CageObject;
        private uint m_CageGeometryVersion;
        private ObjectTransform m_CageRoot;
        private TransformFrame m_CageFrame;
        private Matrix4x4 m_CageReference;
        private Vector3[] m_CageReferenceCorners;
        private Vector3 m_CageScaleDragPivot;
        private SelectionTransformPivot m_CageScalePivot;
        private LocalizedText m_DeformationStatus = LocalizedText.Empty;

        public bool DeformMode => m_DeformMode && m_Mode == ObjectToolMode.Transform;
        public int DeformationPivot => m_GizmoMode == GizmoMode.Scale ? (int)m_CageScalePivot : (int)SelectionTransformPivot.SelectionCenter;
        public LocalizedText DeformationStatus => m_DeformationStatus;

        public int DeformationSelectionCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < 8; i++)
                    if ((m_CageSelection & (1 << i)) != 0)
                        n++;
                return n;
            }
        }

        private int DeformationPartIndex => m_TransformTarget == TransformTarget.ActivePart ? m_ProceduralObjectSystem.SelectedPartIndex : -1;

        public bool CanBakeDeformation => !DeformationSnapshotBusy && m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObject.IsLocked && m_ProceduralObjectSystem.SelectedObject.GetDeformation(DeformationPartIndex) != null;

        // Queue<T> conflicts between the game toolchain assemblies; use List instead.
        private System.Collections.Generic.List<int> m_MeshDeformationScopes;
        private int m_MeshDeformationObjectId = -1;
        private bool m_MeshDeformationBake;
        private int MeshDeformationPartIndex => MeshEditVertexScopeMode == MeshEditVertexScope.CurrentPart ? m_ProceduralObjectSystem.SelectedPartIndex : -1;
        public bool CanBakeMeshDeformation => MeshEditRequiresBake && !DeformationSnapshotBusy && !m_ProceduralObjectSystem.SelectedObject.IsLocked && !m_ProceduralObjectSystem.HistoryBusy && !m_IsGizmoDragging && !m_IsVertexSlideDragging && !m_BoxSelection.IsGestureActive && !m_MeshEditController.IsAlignmentTargeting && !m_MeshEditController.IsVertexSlideTargeting && !IsMeshTopologyInteractionActive;

        public void BakeMeshDeformation() => FinishMeshDeformation(true);
        public void ResetMeshDeformation() => FinishMeshDeformation(false);

        private void FinishMeshDeformation(bool bake)
        {
            if (!CanBakeMeshDeformation) return;
            m_DeformationStatus = LocalizedText.Empty;
            m_MeshDeformationBake = bake;
            var obj = m_ProceduralObjectSystem.SelectedObject;
            int scope = MeshDeformationPartIndex;
            m_MeshDeformationScopes = new System.Collections.Generic.List<int>();
            foreach (var cage in obj.Deformations)
            {
                if (scope < 0 || cage.PartIndex < 0 || cage.PartIndex == scope)
                    m_MeshDeformationScopes.Add(cage.PartIndex);
            }

            m_MeshDeformationObjectId = obj.Id;
            ContinueMeshDeformation(obj);
        }

        private void ContinueMeshDeformation(ProceduralObject obj)
        {
            if (m_MeshDeformationScopes == null) return;
            if (m_MeshDeformationObjectId != obj.Id || !IsObjectEditorMode || m_ProceduralObjectSystem.SelectedObject != obj || m_MeshDeformationScopes.Count == 0)
            {
                m_MeshDeformationScopes = null;
                m_MeshDeformationObjectId = -1;
                return;
            }

            int partIndex = m_MeshDeformationScopes[0];
            m_MeshDeformationScopes.RemoveAt(0);
            var cage = obj.GetDeformation(partIndex);
            if (cage == null)
            {
                ResetCageDrag();
                return;
            }

            // Apply and reset use the same worker/history path as Object Deform.
            // Process each affected cage without leaving Edit or changing scope.
            FinishDeformation(m_MeshDeformationBake, obj, cage);
        }

        private void ResetCageDrag()
        {
            m_MeshDeformationScopes = null;
            m_MeshDeformationObjectId = -1;
            m_CageDrag.Reset();
        }

        public void EditExistingDeformation()
        {
            if (DeformationSnapshotBusy) return;
            var obj = m_ProceduralObjectSystem.SelectedObject;
            if (obj == null || !obj.HasDeformation) return;
            var cage = obj.GetDeformation(DeformationPartIndex) ?? obj.Deformations[0];
            if (IsObjectEditorMode) ExitMeshEdit();
            if (cage.PartIndex >= 0) m_ProceduralObjectSystem.SetSelectedPart(cage.PartIndex);
            m_TransformTarget = cage.PartIndex < 0 ? TransformTarget.Object : TransformTarget.ActivePart;
            SetDeformMode();
        }

        public bool TransformRequiresBake => m_Mode == ObjectToolMode.Transform && m_TransformTarget == TransformTarget.ActivePart && m_ProceduralObjectSystem.SelectedObject != null && !m_ProceduralObjectSystem.SelectedObject.CanEditGeometry(m_ProceduralObjectSystem.SelectedPartIndex, out _);

        public bool SelectedHasDeformation => m_ProceduralObjectSystem.SelectedObject?.HasDeformation ?? false;

        public void SetDeformMode()
        {
            if (IsObjectEditorMode || DeformationCommitBusy || m_ProceduralObjectSystem.SelectedObject == null) return;
            CancelGizmoDrag(true);
            CancelBoxSelection();
            ClearGizmoHover();
            m_MirrorMode = false;
            m_DeformMode = true;
            m_GizmoMode = GizmoMode.Translate;
            m_Mode = ObjectToolMode.Transform;
            m_DeformationStatus = LocalizedText.Empty;
        }

        public void SetDeformationMoveMode()
        {
            SetDeformationOperation(GizmoMode.Translate);
        }

        public void SetDeformationScaleMode()
        {
            SetDeformationOperation(GizmoMode.Scale);
        }

        private void SetDeformationOperation(GizmoMode mode)
        {
            if (!DeformMode || DeformationCommitBusy) return;
            CancelGizmoDrag(true);
            CancelBoxSelection();
            ClearGizmoHover();
            m_GizmoMode = mode;
            ApplyCageSelection(m_CageSelection, BoxSelectionMode.Replace);
        }

        public void SetDeformationPivot(int value)
        {
            if (!DeformMode || m_GizmoMode != GizmoMode.Scale || value < 0 || value > 1 || DeformationCommitBusy) return;
            CancelGizmoDrag(true);
            m_CageScalePivot = (SelectionTransformPivot)value;
            ApplyCageSelection(m_CageSelection, BoxSelectionMode.Replace);
        }

        public void ClearDeformationSelection()
        {
            if (!DeformMode || DeformationSnapshotBusy) return;
            ApplyCageSelection(0, BoxSelectionMode.Replace);
            ClearGizmoHover();
        }

        public void BakeDeformation()
        {
            FinishDeformation(true);
        }

        public void ResetDeformation()
        {
            FinishDeformation(false);
        }

        private void FinishDeformation(bool bake)
        {
            if (!CanBakeDeformation) return;
            var obj = m_ProceduralObjectSystem.SelectedObject;
            FinishDeformation(bake, obj, obj.GetDeformation(DeformationPartIndex));
        }

        private void FinishDeformation(bool bake, ProceduralObject obj, DeformationCage cage)
        {
            if (!m_CageDrag.TryBegin(obj, cage.PartIndex, cage.LocalFromReference, m_ProceduralObjectSystem, bake ? LocalizedText.For("PO2.History.Action.Deformation.Bake") : LocalizedText.For("PO2.History.Action.Deformation.Reset"), true, bake, out m_DeformationStatus))
            {
                ResetCageDrag();
                return;
            }

            if (!bake && !m_CageDrag.TryApply(obj, DeformationCage.ReferenceCorners(cage.LocalFromReference), out m_DeformationStatus))
            {
                ResetCageDrag();
                return;
            }

            if (!m_CageDrag.TryBeginCompletion(obj, out bool pending, out m_DeformationStatus) || !pending) ResetCageDrag();
            m_CageReferenceCorners = null;
        }

        private void ApplyNumericCageTransform(TransformAxis axis, float value, bool scale)
        {
            if (scale != (m_GizmoMode == GizmoMode.Scale) || !IsFinite(value) || value == 0f || DeformationSnapshotBusy || m_CageSelection == 0 || m_ProceduralObjectSystem.ActionHistory.IsBusy) return;
            var obj = m_ProceduralObjectSystem.SelectedObject;
            if (!TryGetCage(obj, out var reference, out var corners)) return;
            var frame = ResolveActiveTransformFrame(obj);
            Vector3 pivot = scale ? CageScaleLocalPivot(obj, corners) : Vector3.zero;
            if (!m_CageDrag.TryBegin(obj, DeformationPartIndex, reference, m_ProceduralObjectSystem, scale ? LocalizedText.For("PO2.History.Action.Deformation.ScaleCorners") : LocalizedText.For("PO2.History.Action.Deformation.MoveCorners"), false, false, out m_DeformationStatus)) return;
            bool applied;
            if (scale)
            {
                Vector3 factors = Vector3.one;
                float factor = Mathf.Max(0.01f, 1f + value);
                if (axis == TransformAxis.X) factors.x = factor;
                else if (axis == TransformAxis.Y) factors.y = factor;
                else factors.z = factor;
                applied = m_CageDrag.TryScale(obj, m_CageSelection, SelectionTransformMath.CreateWorldScale(Vector3.zero, frame, factors), pivot, factor == 1f, out m_DeformationStatus);
            }
            else applied = m_CageDrag.TryMove(obj, m_CageSelection, frame.GetAxis(axis) * value, out m_DeformationStatus);

            if (!applied || !m_CageDrag.TryBeginCompletion(obj, out bool pending, out m_DeformationStatus) || !pending)
                ResetCageDrag();
        }

        private bool TryGetCage(ProceduralObject obj, out Matrix4x4 reference, out Vector3[] corners)
        {
            reference = default;
            corners = null;
            if (obj == null || obj.IsLocked) return false;
            int scope = DeformationPartIndex;
            if (!ReferenceEquals(obj, m_CageObject) || scope != m_CagePartIndex)
            {
                m_CageObject = obj;
                m_CagePartIndex = scope;
                m_CageSelection = 0;
                m_CageHovered = -1;
                m_CageReferenceCorners = null;
                CancelBoxSelection();
            }

            var cage = m_CageDrag.IsCaptured && m_CageDrag.ObjectId == obj.Id ? m_CageDrag.Cage : obj.GetDeformation(scope);
            if (cage != null)
            {
                reference = cage.LocalFromReference;
                corners = cage.Corners;
                return true;
            }

            if (!obj.CanEditDeformationScope(scope, out m_DeformationStatus)) return false;
            var frame = ResolveActiveTransformFrame(obj);
            if (m_CageReferenceCorners == null || m_CageGeometryVersion != obj.GeometryVersion || !m_CageRoot.Equals(obj.Transform) || !m_CageFrame.XAxis.Equals(frame.XAxis) || !m_CageFrame.YAxis.Equals(frame.YAxis) || !m_CageFrame.ZAxis.Equals(frame.ZAxis))
            {
                if (!CageBounds.TryFit(obj, scope, frame, out m_CageReference))
                {
                    m_DeformationStatus = LocalizedText.For("PO2.Object.Deformation.Error.EmptyTarget");
                    return false;
                }

                m_CageReferenceCorners = DeformationCage.ReferenceCorners(m_CageReference);
                m_CageGeometryVersion = obj.GeometryVersion;
                m_CageRoot = obj.Transform;
                m_CageFrame = frame;
            }

            reference = m_CageReference;
            corners = m_CageReferenceCorners;
            return true;
        }

        private Vector3 CageScaleLocalPivot(ProceduralObject obj, Vector3[] corners)
        {
            return m_CageScalePivot == SelectionTransformPivot.SavedPivot ? DeformationPartIndex < 0 ? obj.LocalPivot : obj.Parts[DeformationPartIndex].LocalPivot : CageSelectionCenter(obj, corners);
        }

        private Vector3 CageSelectionCenter(ProceduralObject obj, Vector3[] corners)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < 8; i++)
                if ((m_CageSelection & (1 << i)) != 0)
                {
                    sum += corners[i];
                    n++;
                }

            return n == 0 ? obj.LocalPivot : sum / n;
        }

        private bool TryGetCageGizmoContext(Camera camera, ProceduralObject obj, out GizmoContext context)
        {
            context = default;
            if (!TryGetCage(obj, out _, out var corners)) return false;
            Vector3 localPivot = m_GizmoMode == GizmoMode.Scale ? m_CageDrag.IsCaptured ? m_CageScaleDragPivot : CageScaleLocalPivot(obj, corners) : CageSelectionCenter(obj, corners);
            Vector3 pivot = obj.Transform.TransformPoint(localPivot);
            TransformFrame frame = ResolveActiveTransformFrame(obj);
            float size = m_CageSelection == 0 ? 0f : GizmoMath.GetScreenSpaceGizmoSize(camera, pivot);
            float activeLength = 0f;
            if (m_IsGizmoDragging)
            {
                if (m_GizmoMode == GizmoMode.Scale)
                {
                    frame = m_ScaleDrag.Frame;
                    size = m_ScaleDrag.GizmoSize;
                    activeLength = m_ScaleDrag.ActiveHandleLength;
                }
                else
                {
                    frame = m_TranslateDrag.Frame;
                    size = m_TranslateDrag.GizmoSize;
                }
            }

            context = new GizmoContext(GizmoTargetKind.CageCorners, obj.Id, DeformationPartIndex, m_GizmoMode, pivot, frame, size, activeLength);
            return true;
        }

        internal bool TryGetCageOverlay(out CageOverlay overlay)
        {
            overlay = default;
            var obj = m_ProceduralObjectSystem.SelectedObject;
            if (AxisPickingActive || !m_ProceduralObjectSystem.IsToolActive || !DeformMode || obj == null || !TryGetCage(obj, out _, out var corners)) return false;
            overlay.Visible = true;
            overlay.Selected = m_CageSelection;
            overlay.Hovered = m_CageHovered;
            for (int i = 0; i < 8; i++) overlay.Set(i, obj.Transform.TransformPoint(corners[i]));
            return true;
        }

        private bool BeginCageDrag(ProceduralObject obj, in GizmoContext context)
        {
            if (m_CageSelection == 0 || !TryGetCage(obj, out var reference, out var corners)) return false;
            m_CageDragMask = m_CageSelection;
            m_CageScaleDragPivot = context.Mode == GizmoMode.Scale ? CageScaleLocalPivot(obj, corners) : Vector3.zero;
            return m_CageDrag.TryBegin(obj, context.PartIndex, reference, m_ProceduralObjectSystem, context.Mode == GizmoMode.Scale ? LocalizedText.For("PO2.History.Action.Deformation.ScaleCorners") : LocalizedText.For("PO2.History.Action.Deformation.MoveCorners"), false, false, out m_DeformationStatus);
        }

        private void UpdateCagePointer(Camera camera, Ray ray, Vector2 mouse, bool pressed, bool released, bool shift, bool control)
        {
            m_ProceduralObjectSystem.ClearHoveredObject();
            var obj = m_ProceduralObjectSystem.SelectedObject;
            if (!TryGetCage(obj, out _, out var corners))
            {
                ClearGizmoHover();
                return;
            }

            m_CageHovered = -1;
            float best = 100f, depth = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = camera.WorldToScreenPoint(obj.Transform.TransformPoint(corners[i]));
                float distance = (new Vector2(p.x, p.y) - mouse).sqrMagnitude;
                if (p.z > 0 && (distance < best || (distance == best && p.z < depth)))
                {
                    best = distance;
                    depth = p.z;
                    m_CageHovered = i;
                }
            }

            if (m_BoxSelection.IsGestureActive)
            {
                m_BoxSelection.Update(mouse);
                UpdateBoxSelectionVisualRect(camera);
                if (released)
                {
                    byte matched = 0;
                    if (m_BoxSelection.IsActive)
                    {
                        Rect rect = m_BoxSelection.GetScreenRect();
                        for (int i = 0; i < 8; i++)
                        {
                            Vector3 p = camera.WorldToScreenPoint(obj.Transform.TransformPoint(corners[i]));
                            if (p.z > 0 && rect.Contains(new Vector2(p.x, p.y))) matched |= (byte)(1 << i);
                        }
                    }

                    ApplyCageSelection(matched, m_BoxSelection.Mode);
                    CancelBoxSelection();
                }

                return;
            }

            if (m_CageSelection != 0 && TryGetCageGizmoContext(camera, obj, out var context))
            {
                UpdateGizmoHover(ray, context);
                if (pressed && !(m_CageHovered >= 0 && (shift || control)) && m_HoveredGizmoHandle != GizmoHandle.None)
                {
                    BeginGizmoDrag(camera, ray, context, m_HoveredGizmoHandle);
                    return;
                }
            }
            else ClearGizmoHover();

            if (!pressed) return;
            BoxSelectionMode mode = control ? BoxSelectionMode.Remove : shift ? BoxSelectionMode.Add : BoxSelectionMode.Replace;
            if (m_CageHovered >= 0) ApplyCageSelection((byte)(1 << m_CageHovered), mode);
            else m_BoxSelection.Begin(mouse, mode);
        }

        private void ApplyCageSelection(byte matched, BoxSelectionMode mode)
        {
            m_CageSelection = mode == BoxSelectionMode.Add ? (byte)(m_CageSelection | matched) : mode == BoxSelectionMode.Remove ? (byte)(m_CageSelection & ~matched) : matched;
            m_DeformationStatus = LocalizedText.Empty;
        }

        public bool DeformationCommitBusy => m_CageDrag.IsCompletionPending;
        public bool DeformationSnapshotBusy => m_CageDrag.IsCaptured;
        public LocalizedText DeformationCommitStatus => DeformationCommitBusy ? m_CageDrag.CompletionStatus : LocalizedText.Empty;

        private bool ProcessPendingCageDeformation()
        {
            if (!m_CageDrag.IsCompletionPending) return false;
            int objectId = m_CageDrag.ObjectId;
            if (!m_ProceduralObjectSystem.TryGetObject(objectId, out var obj))
            {
                ResetCageDrag();
                return true;
            }

            bool success = m_CageDrag.TryFinishCompletion(obj, out bool completed, out LocalizedText reason);
            if (!completed) return false;
            var remainingDeformations = m_MeshDeformationScopes;
            int deformationObjectId = m_MeshDeformationObjectId;
            ResetCageDrag();
            if (!success)
            {
                m_DeformationStatus = reason;
                Mod.log.Error("[PO Deformation] " + reason);
            }

            NotifyHistoryGeometryChanged(obj);
            if (success && remainingDeformations != null)
            {
                m_MeshDeformationScopes = remainingDeformations;
                m_MeshDeformationObjectId = deformationObjectId;
                ContinueMeshDeformation(obj);
            }

            return true;
        }
    }
}