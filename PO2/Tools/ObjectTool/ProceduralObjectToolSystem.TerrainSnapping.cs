using Game;
using Game.Input;
using Game.Simulation;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Tools.Gizmos;
using PO2.Tools.Snapping;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        public bool CanSnapToTerrain
        {
            get
            {
                if (m_Mode == ObjectToolMode.MeshEdit)
                {
                    return CanApplyMeshNumericMove;
                }

                ProceduralObject selected = m_ProceduralObjectSystem?.SelectedObject;

                return m_Mode == ObjectToolMode.Transform && selected != null && !selected.IsLocked && !m_IsGizmoDragging;
            }
        }

        public void SnapToTerrain()
        {
            if (!CanSnapToTerrain)
            {
                Mod.log.Warn("[PO Snap] Cannot snap to terrain without an " + "editable object or vertex/edge selection.");
                return;
            }

            TerrainHeightData terrainData = m_TerrainSystem.GetHeightData(waitForPending: true);

            if (!terrainData.isCreated)
            {
                Mod.log.Warn("[PO Snap] Terrain CPU height data is not available.");
                return;
            }

            if (m_Mode == ObjectToolMode.MeshEdit)
            {
                SnapMeshEditSelectionToTerrain(ref terrainData);
                return;
            }

            if (m_TransformTarget == TransformTarget.ActivePart)
            {
                SnapSelectedPartToTerrain(ref terrainData);
                return;
            }

            SnapSelectedObjectToTerrain(ref terrainData);
        }

        private void SnapSelectedObjectToTerrain(ref TerrainHeightData terrainData)
        {
            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (selected == null || selected.IsLocked)
            {
                return;
            }

            if (!TerrainSnapCalculator.TryCalculateObjectWorldYOffset(selected, ref terrainData, out float worldYOffset, out int sampleCount))
            {
                Mod.log.Warn($"[PO Snap] Object {selected.Id} has no valid " + "geometry samples for terrain placement.");
                return;
            }

            if (Mathf.Approximately(worldYOffset, 0f))
            {
                return;
            }

            using (var history = m_ProceduralObjectSystem.BeginObjectHistory(selected, ObjectValueKind.Transform, LocalizedText.For("PO2.History.Action.Object.AlignToTerrain")))
            {
                if (history == null) return;
                selected.SetPosition(selected.Transform.Position + Vector3.up * worldYOffset);
                if (!history.Complete()) return;
            }
        }

        private void SnapSelectedPartToTerrain(ref TerrainHeightData terrainData)
        {
            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;

            if (!TryGetNumericPartTarget(selected, out int partIndex, out Vector3 pivot))
            {
                return;
            }

            if (!TerrainSnapCalculator.TryCalculatePartWorldYOffset(selected, partIndex, ref terrainData, out float worldYOffset, out int sampleCount))
            {
                Mod.log.Warn($"[PO Snap] Part {partIndex} on object " + $"{selected.Id} has no valid geometry samples " + "for terrain placement.");
                return;
            }

            if (Mathf.Approximately(worldYOffset, 0f))
            {
                return;
            }

            if (!TryApplyNumericPartTransform(selected, partIndex, pivot, Matrix4x4.Translate(Vector3.up * worldYOffset), PartTransformKind.Translation, $"move {worldYOffset:0.###} m on world Y " + "to terrain"))
            {
                return;
            }
        }

        private void SnapMeshEditSelectionToTerrain(ref TerrainHeightData terrainData)
        {
            if (!TryGetMeshEditObject(out ProceduralObject editedObject))
            {
                return;
            }

            VertexRef[] selectedVertices = m_MeshEditController.GetEffectiveSelectionSnapshot();

            StructuralEdgeRef[] selectedEdges = m_MeshEditController.SelectionMode == MeshSelectionMode.Edge ? m_MeshEditController.GetEdgeSelectionSnapshot() : null;

            if (!TerrainSnapCalculator.TryCalculateSelectionWorldYOffset(editedObject, selectedVertices, selectedEdges, ref terrainData, out float worldYOffset, out int sampleCount))
            {
                Mod.log.Warn($"[PO Snap] The selection on object " + $"{editedObject.Id} has no valid geometry " + "samples for terrain placement.");
                return;
            }

            if (Mathf.Approximately(worldYOffset, 0f))
            {
                return;
            }

            Vector3 localDelta = editedObject.Transform.InverseTransformVector(Vector3.up * worldYOffset);

            if (!m_MeshEditController.ApplySelectedMoveDelta(editedObject, localDelta, out int movedLogicalVertexCount, out int movedRenderVertexCount, LocalizedText.For("PO2.History.Action.Vertices.SnapToTerrain")))
            {
                Mod.log.Warn($"[PO Snap] Could not move the selection on " + $"object {editedObject.Id} to terrain.");
                return;
            }
        }
    }
}