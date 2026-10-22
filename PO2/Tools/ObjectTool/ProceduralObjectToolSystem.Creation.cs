using Game;
using Game.Common;
using Game.Input;
using Game.Tools;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Core.Primitives;
using PO2.Geometry;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private PrimitiveKind m_SelectedCreationPrimitive = PrimitiveKind.Box;

        private int m_PrimitivePartPlacementParentId = -1;
        private ProceduralObject m_PrimitivePartPlacementParent;

        public int ActivePlacementPrimitiveType =>
            IsPrimitivePlacementMode ? (int)m_SelectedCreationPrimitive : -1;

        public int SelectedCreationPrimitiveType =>
            (int)m_SelectedCreationPrimitive;

        private bool IsPrimitivePlacementMode =>
            m_Mode == ObjectToolMode.PlacePrimitive || m_Mode == ObjectToolMode.PlacePrimitivePart;

        public void BeginPrimitivePlacement(PrimitiveKind primitiveType)
        {
            CaptureCreateReturnTarget();

            if (IsObjectEditorMode)
            {
                LeaveMeshEdit(commitActiveDrag: true);
            }

            CancelGizmoDrag(restoreTransform: true);
            ClearCombineParent();

            if (!PrimitiveDefinitions.TryGet(primitiveType, out PrimitiveDefinition _))
            {
                Mod.log.Warn($"[PO Tool] Unsupported primitive type {primitiveType}.");
                return;
            }

            bool placeAsPart = m_Mode == ObjectToolMode.PlacePrimitivePart;

            if (placeAsPart && !TryGetPrimitivePartPlacementParent(out ProceduralObject _, out string failureReason))
            {
                CancelInvalidPrimitivePartPlacement(failureReason);
                return;
            }

            if (!placeAsPart)
            {
                ClearPrimitivePartPlacementParent();
            }

            m_SelectedCreationPrimitive = primitiveType;
            m_Mode = placeAsPart ? ObjectToolMode.PlacePrimitivePart : ObjectToolMode.PlacePrimitive;
            ClearGizmoHover();

            m_ProceduralObjectSystem.ActivateTool();
        }

        public void BeginPrimitivePartPlacement()
        {
            m_WorkspaceReturnTarget = Workspace.Transform;

            if (IsObjectEditorMode)
            {
                LeaveMeshEdit(commitActiveDrag: true);
            }

            CancelGizmoDrag(restoreTransform: true);
            ClearCombineParent();

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
                Mod.log.Warn($"[PO Part] Object {selected.Id} already contains the " + $"maximum {ProceduralObject.kMaxPartCount} parts.");
                return;
            }

            m_PrimitivePartPlacementParentId = selected.Id;
            m_PrimitivePartPlacementParent = selected;
            m_Mode = ObjectToolMode.PlacePrimitivePart;
            ClearGizmoHover();
            m_ProceduralObjectSystem.ActivateTool();
        }

        private JobHandle UpdatePlacementMode(JobHandle inputDeps, bool primaryPressed)
        {
            ClearGizmoHover();

            if (m_Mode == ObjectToolMode.PlacePrimitivePart && !TryGetPrimitivePartPlacementParent(out ProceduralObject _, out string failureReason))
            {
                CancelInvalidPrimitivePartPlacement(failureReason);
                return inputDeps;
            }

            if (primaryPressed)
            {
                TryPlacePrimitiveAtCursor();
            }

            return inputDeps;
        }

        private void TryPlacePrimitiveAtCursor()
        {
            if (!GetRaycastResult(out Unity.Entities.Entity _, out Game.Common.RaycastHit hit))
            {
                return;
            }

            Vector3 position = new Vector3(hit.m_HitPosition.x, hit.m_HitPosition.y, hit.m_HitPosition.z);

            PrimitiveParameters parameters = CreatePlacementParametersSnapshot(m_SelectedCreationPrimitive);

            if (parameters == null)
            {
                Mod.log.Warn($"[PO Tool] Could not snapshot creation parameters for " + $"{m_SelectedCreationPrimitive}.");
                return;
            }

            if (m_Mode == ObjectToolMode.PlacePrimitivePart)
            {
                TryPlacePrimitivePart(parameters, position);
                return;
            }

            ProceduralObject proceduralObject = m_ProceduralObjectSystem.CreatePrimitive(parameters, position);

            if (proceduralObject == null)
            {
                return;
            }

            m_Mode = ObjectToolMode.Transform;
        }

        private void TryPlacePrimitivePart(PrimitiveParameters parameters, Vector3 worldPosition)
        {
            if (!TryGetPrimitivePartPlacementParent(out ProceduralObject parent, out string failureReason))
            {
                CancelInvalidPrimitivePartPlacement(failureReason);
                return;
            }

            ObjectTransform parentTransform = parent.Transform;

            Matrix4x4 parentLocalToWorld = Matrix4x4.TRS(parentTransform.Position, parentTransform.Rotation, parentTransform.Scale);

            // The primitive starts world-aligned at the click.
            // Baking the parent's inverse into its vertices keeps that world appearance
            // even when the parent is rotated or nonuniformly scaled.
            Matrix4x4 parentFromPrimitive = parentLocalToWorld.inverse * Matrix4x4.Translate(worldPosition);

            if (!m_ProceduralObjectSystem.TryAddPrimitivePart(m_PrimitivePartPlacementParentId, m_PrimitivePartPlacementParent, parameters, parentFromPrimitive, out int addedPartIndex, out MeshPart addedPart, out failureReason))
            {
                Mod.log.Warn($"[PO Part] Could not add primitive part to object " + $"{m_PrimitivePartPlacementParentId}: " + failureReason);

                if (!TryGetPrimitivePartPlacementParent(out ProceduralObject _, out string invalidReason))
                {
                    CancelInvalidPrimitivePartPlacement(invalidReason);
                }

                return;
            }

            ClearPrimitivePartPlacementParent();
            m_Mode = ObjectToolMode.Transform;
            SetActivePartTransformTarget();
            ClearGizmoHover();
        }

        private bool TryGetPrimitivePartPlacementParent(out ProceduralObject parent, out string failureReason)
        {
            parent = null;
            failureReason = string.Empty;

            if (m_PrimitivePartPlacementParent == null || m_PrimitivePartPlacementParentId < 0 || !m_ProceduralObjectSystem.TryGetObject(m_PrimitivePartPlacementParentId, out ProceduralObject liveParent) || !ReferenceEquals(liveParent, m_PrimitivePartPlacementParent))
            {
                failureReason = "The parent PO is no longer available.";
                return false;
            }

            if (!ReferenceEquals(m_ProceduralObjectSystem.SelectedObject, liveParent))
            {
                failureReason = "The selected parent PO changed during placement.";
                return false;
            }

            if (liveParent.IsLocked)
            {
                failureReason = "The parent PO was locked.";
                return false;
            }

            if (liveParent.Parts.Count >= ProceduralObject.kMaxPartCount)
            {
                failureReason = $"The parent PO reached the maximum " + $"{ProceduralObject.kMaxPartCount} parts.";
                return false;
            }

            parent = liveParent;
            return true;
        }

        private void CancelInvalidPrimitivePartPlacement(string failureReason)
        {
            int parentObjectId = m_PrimitivePartPlacementParentId;

            ClearPrimitivePartPlacementParent();
            m_Mode = m_ProceduralObjectSystem.SelectedObject != null ? ObjectToolMode.Transform : ObjectToolMode.Select;
            ClearGizmoHover();

            Mod.log.Warn($"[PO Part] Primitive part placement for object " + $"{parentObjectId} was cancelled: {failureReason}");
        }

        private void ClearPrimitivePartPlacementParent()
        {
            m_PrimitivePartPlacementParentId = -1;
            m_PrimitivePartPlacementParent = null;
        }
    }
}