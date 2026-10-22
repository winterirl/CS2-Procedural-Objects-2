using Game;
using Game.Input;
using Game.Prefabs;
using Game.Tools;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.Localization;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        public bool IsMeshEditSurfaceSelected(SurfaceRef surface)
        {
            return m_MeshEditController != null && m_MeshEditController.IsSurfaceSelected(surface);
        }

        public bool IsMeshEditSurfaceHovered(SurfaceRef surface)
        {
            return m_MeshEditController != null && m_MeshEditController.IsSurfaceHovered(surface);
        }

        public void NewSurfaceAreaFromSelection()
        {
            if (!TryGetTexturingObject(out ProceduralObject editedObject) || editedObject.IsLocked || m_MeshEditController.SelectionMode != MeshSelectionMode.SurfaceArea)
            {
                return;
            }

            if (!TryGetSelectedSurfaceTriangles(editedObject, out int partIndex, out int[] triangleIds))
            {
                Mod.log.Warn("[PO Surface] New Surface Area requires a non-empty selection on the active part.");
                return;
            }

            int sourceAreaId = m_ProceduralObjectSystem.SelectedSurfaceAreaId;
            using (var history = m_ProceduralObjectSystem.BeginSurfaceAreaHistory(editedObject, partIndex, LocalizedText.For("PO2.History.Action.SurfaceArea.Create")))
            {
                if (history == null) return;
                if (editedObject.TryCreateSurfaceAreaFromTriangles(partIndex, triangleIds, sourceAreaId, out int newAreaId, out string failureReason))
                {
                    if (!history.Complete()) return;
                    m_ProceduralObjectSystem.SetSelectedSurfaceArea(newAreaId);
                }
                else
                {
                    Mod.log.Warn("[PO Surface] Could not create Surface Area from the " + $"current selection: {failureReason}");
                }
            }
        }

        public void AssignSelectionToActiveSurfaceArea()
        {
            if (!TryGetTexturingObject(out ProceduralObject editedObject) || editedObject.IsLocked || m_MeshEditController.SelectionMode != MeshSelectionMode.SurfaceArea)
            {
                return;
            }

            if (!TryGetSelectedSurfaceTriangles(editedObject, out int partIndex, out int[] triangleIds))
            {
                Mod.log.Warn("[PO Surface] Assign requires a non-empty selection on the active part.");
                return;
            }

            int areaId = m_ProceduralObjectSystem.SelectedSurfaceAreaId;
            using (var history = m_ProceduralObjectSystem.BeginSurfaceAreaHistory(editedObject, partIndex, LocalizedText.For("PO2.History.Action.SurfaceArea.Assign")))
            {
                if (history == null) return;
                if (editedObject.AssignTrianglesToSurfaceArea(partIndex, triangleIds, areaId, out string failureReason))
                {
                    if (!history.Complete()) return;
                }
                else
                {
                    Mod.log.Warn("[PO Surface] Could not assign the current selection to " + $"the active Surface Area: {failureReason}");
                }
            }
        }

        public void DeleteActiveSurfaceArea()
        {
            if (!TryGetTexturingObject(out ProceduralObject editedObject) || editedObject.IsLocked || m_MeshEditController.SelectionMode != MeshSelectionMode.SurfaceArea)
            {
                return;
            }

            int partIndex = m_ProceduralObjectSystem.SelectedPartIndex;
            int areaId = m_ProceduralObjectSystem.SelectedSurfaceAreaId;
            if (areaId <= MeshPart.kDefaultSurfaceAreaId)
            {
                Mod.log.Warn("[PO Surface] The default Surface Area cannot be deleted.");
                return;
            }

            using (var history = m_ProceduralObjectSystem.BeginSurfaceAreaHistory(editedObject, partIndex, LocalizedText.For("PO2.History.Action.SurfaceArea.Delete")))
            {
                if (history == null) return;
                if (editedObject.TryDeleteSurfaceArea(partIndex, areaId, out int reassignedTriangleCount, out string failureReason))
                {
                    if (!history.Complete()) return;
                    m_MeshEditController.ClearSelection();
                    m_MeshEditController.ClearHover();
                    ClearGizmoHover();
                    m_ProceduralObjectSystem.SetSelectedSurfaceArea(MeshPart.kDefaultSurfaceAreaId);
                }
                else
                {
                    Mod.log.Warn($"[PO Surface] Could not delete Surface Area {areaId}: " + failureReason);
                }
            }
        }

        private bool TryGetSelectedSurfaceTriangles(ProceduralObject editedObject, out int partIndex, out int[] triangleIds)
        {
            partIndex = m_ProceduralObjectSystem.SelectedPartIndex;
            triangleIds = System.Array.Empty<int>();
            if (partIndex < 0 || partIndex >= editedObject.Parts.Count)
            {
                return false;
            }

            SurfaceRef[] selection = m_MeshEditController.GetSurfaceSelectionSnapshot();
            System.Collections.Generic.List<int> ids = new System.Collections.Generic.List<int>(selection.Length);
            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i].PartIndex == partIndex)
                {
                    ids.Add(selection[i].TriangleId);
                }
            }

            if (ids.Count == 0)
            {
                return false;
            }

            triangleIds = ids.ToArray();
            return true;
        }

        private int GetSelectedSurfaceUnitCount()
        {
            if (!TryGetTexturingObject(out ProceduralObject editedObject) || m_MeshEditController == null || m_MeshEditController.SelectionMode != MeshSelectionMode.SurfaceArea)
            {
                return 0;
            }

            return m_MeshEditController.CountSelectedSurfaceUnits(editedObject);
        }

        private void UpdateMeshSurfaceSelectionInput(ProceduralObject editedObject, Camera camera, Vector2 mousePosition, bool primaryPressed, bool shiftPressed, bool controlPressed, bool altPressed)
        {
            m_MeshEditController.SetVertexHoverSuppressed(true);
            ClearGizmoHover();

            int activePartIndex = m_ProceduralObjectSystem.SelectedPartIndex;
            if (TryPickSurface(editedObject, activePartIndex, camera, mousePosition, out SurfaceRef hoveredSurface))
            {
                m_MeshEditController.SetHoveredSurface(hoveredSurface);

                if (primaryPressed)
                {
                    SurfaceRef[] hoveredUnit = BuildSurfaceSelectionUnit(editedObject, hoveredSurface, altPressed);
                    m_MeshEditController.ApplySurfaceSelectionSet(editedObject, hoveredUnit, shiftPressed, controlPressed);

                    if (!shiftPressed && !controlPressed && activePartIndex >= 0 && activePartIndex < editedObject.Parts.Count)
                    {
                        int areaId = editedObject.Parts[activePartIndex].GetTriangleSurfaceAreaId(hoveredSurface.TriangleId);
                        m_ProceduralObjectSystem.SetSelectedSurfaceArea(areaId);
                    }
                }
            }
            else
            {
                m_MeshEditController.SetHoveredSurface(null);
                if (primaryPressed && !shiftPressed && !controlPressed)
                {
                    m_MeshEditController.ApplySurfaceSelectionSet(editedObject, null, false, false);
                }
            }
        }
    }
}