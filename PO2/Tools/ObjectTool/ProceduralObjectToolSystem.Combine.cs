using Game;
using Game.Input;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry.Picking;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private int m_CombineParentId = -1;
        private ProceduralObject m_CombineParent;

        public bool IsCombineSourceActive =>
            m_Mode == ObjectToolMode.CombineSource;

        public void BeginCombineSourceTargeting()
        {
            if (m_Mode == ObjectToolMode.CombineSource)
            {
                BackOutOneLevel();
                return;
            }

            if (IsObjectEditorMode)
            {
                LeaveMeshEdit(commitActiveDrag: true);
            }

            CancelGizmoDrag(restoreTransform: true);
            ClearPrimitivePartPlacementParent();

            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (selected == null)
            {
                return;
            }

            if (selected.IsLocked)
            {
                return;
            }

            if (selected.Parts.Count >= ProceduralObject.kMaxPartCount)
            {
                Mod.log.Warn($"[PO Combine] Object {selected.Id} already contains " + $"the maximum {ProceduralObject.kMaxPartCount} parts.");
                return;
            }

            m_CombineParentId = selected.Id;
            m_CombineParent = selected;
            m_Mode = ObjectToolMode.CombineSource;
            m_ProceduralObjectSystem.ClearHoveredObject();
            ClearGizmoHover();
            m_ProceduralObjectSystem.ActivateTool();
        }

        private JobHandle UpdateCombineSourceMode(JobHandle inputDeps, bool primaryPressed)
        {
            ClearGizmoHover();

            if (!TryGetCombineParent(out ProceduralObject parent, out string failureReason))
            {
                CancelInvalidCombine(failureReason);
                return inputDeps;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                m_ProceduralObjectSystem.ClearHoveredObject();
                return inputDeps;
            }

            Ray mouseRay = camera.ScreenPointToRay(InputManager.instance.mousePosition);

            if (ResolveObjectPick(mouseRay, ref primaryPressed, out int candidateId) != ObjectPickStatus.Hit)
            {
                m_ProceduralObjectSystem.ClearHoveredObject();
                return inputDeps;
            }

            if (candidateId == parent.Id)
            {
                m_ProceduralObjectSystem.ClearHoveredObject();
                return inputDeps;
            }

            if (!m_ProceduralObjectSystem.TryGetObject(candidateId, out ProceduralObject source))
            {
                m_ProceduralObjectSystem.ClearHoveredObject();
                return inputDeps;
            }

            if (source.IsLocked)
            {
                m_ProceduralObjectSystem.ClearHoveredObject();
                return inputDeps;
            }

            if (source.Parts.Count > ProceduralObject.kMaxPartCount - parent.Parts.Count)
            {
                m_ProceduralObjectSystem.ClearHoveredObject();
                if (primaryPressed)
                {
                    Mod.log.Warn($"[PO Combine] Source object {candidateId} would " + $"exceed the {ProceduralObject.kMaxPartCount}-part limit.");
                }

                return inputDeps;
            }

            m_ProceduralObjectSystem.SetCombineHoveredObject(candidateId);

            if (primaryPressed)
            {
                TryCombineSource(parent, source);
            }

            return inputDeps;
        }

        private void TryCombineSource(ProceduralObject parent, ProceduralObject source)
        {
            int parentObjectId = parent.Id;
            int sourceObjectId = source.Id;

            if (!m_ProceduralObjectSystem.TryCombineObjects(m_CombineParentId, m_CombineParent, sourceObjectId, source, out int firstPartIndex, out int addedPartCount, out string failureReason))
            {
                Mod.log.Warn($"[PO Combine] Could not combine source object " + $"{sourceObjectId} into object {parentObjectId}: " + failureReason);

                if (!TryGetCombineParent(out ProceduralObject _, out string invalidReason))
                {
                    CancelInvalidCombine(invalidReason);
                }

                return;
            }

            ClearCombineParent();
            m_Mode = ObjectToolMode.Transform;
            SetActivePartTransformTarget();
            ClearGizmoHover();
        }

        private bool TryGetCombineParent(out ProceduralObject parent, out string failureReason)
        {
            parent = null;
            failureReason = string.Empty;

            if (m_CombineParent == null || m_CombineParentId < 0 || !m_ProceduralObjectSystem.TryGetObject(m_CombineParentId, out ProceduralObject liveParent) || !ReferenceEquals(liveParent, m_CombineParent))
            {
                failureReason = "The combine parent is no longer available.";
                return false;
            }

            if (!ReferenceEquals(m_ProceduralObjectSystem.SelectedObject, liveParent))
            {
                failureReason = "The selected combine parent changed.";
                return false;
            }

            if (liveParent.IsLocked)
            {
                failureReason = "The combine parent was locked.";
                return false;
            }

            if (liveParent.Parts.Count >= ProceduralObject.kMaxPartCount)
            {
                failureReason = $"The combine parent reached the maximum " + $"{ProceduralObject.kMaxPartCount} parts.";
                return false;
            }

            parent = liveParent;
            return true;
        }

        private void CancelInvalidCombine(string failureReason)
        {
            int parentObjectId = m_CombineParentId;

            ClearCombineParent();
            m_Mode = m_ProceduralObjectSystem.SelectedObject != null ? ObjectToolMode.Transform : ObjectToolMode.Select;
            ClearGizmoHover();

            Mod.log.Warn($"[PO Combine] Source targeting for object " + $"{parentObjectId} was cancelled: {failureReason}");
        }

        private void ClearCombineParent()
        {
            m_ProceduralObjectSystem?.ClearHoveredObject();
            m_CombineParentId = -1;
            m_CombineParent = null;
        }
    }
}