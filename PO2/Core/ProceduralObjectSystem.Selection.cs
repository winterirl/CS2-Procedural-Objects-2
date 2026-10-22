using System;
using Game;
using UnityEngine;
using PO2.Geometry;
using PO2.Geometry.Picking;
using PO2.Materials;

namespace PO2.Core
{
    public partial class ProceduralObjectSystem
    {
        private int? m_SelectedObjectId;
        private int? m_HoveredObjectId;
        private bool m_DeleteHover;
        private int m_SelectedPartIndex;
        private int m_SelectedSurfaceAreaId = MeshPart.kDefaultSurfaceAreaId;
        public int? SelectedObjectId => m_SelectedObjectId;
        public int? HoveredObjectId => m_HoveredObjectId;

        public string SelectedObjectName =>
            TryGetSelected(out ProceduralObject selected) ? selected.Name : string.Empty;

        public bool SelectedObjectLocked =>
            TryGetSelected(out ProceduralObject selected) && selected.IsLocked;

        public bool SelectedObjectReceivesDecals =>
            TryGetSelected(out ProceduralObject selected) && selected.ReceivesDecals;

        public int SelectedPartIndex
        {
            get { return TryGetSelectedPart(out ProceduralObject _, out MeshPart _, out int partIndex) ? partIndex : -1; }
        }

        public int SelectedPartCount
        {
            get { return TryGetSelected(out ProceduralObject selected) ? selected.Parts.Count : 0; }
        }

        public string SelectedPartName
        {
            get { return TryGetSelectedPart(out ProceduralObject _, out MeshPart part, out int _) ? part.Name : string.Empty; }
        }

        public int SelectedSurfaceAreaId
        {
            get { return TryGetSelectedSurfaceArea(out ProceduralObject _, out MeshPart _, out int _, out SurfaceArea area, out int _) ? area.Id : -1; }
        }

        public int SelectedSurfaceAreaCount
        {
            get { return TryGetSelectedPart(out ProceduralObject _, out MeshPart part, out int _) ? part.SurfaceAreas.Count : 0; }
        }

        public ProceduralObject SelectedObject
        {
            get
            {
                TryGetSelected(out ProceduralObject selected);
                return selected;
            }
        }

        internal uint PickSceneVersion => m_Renderer.PickSceneVersion;

        internal ObjectPickStatus FindClosestObject(Ray ray, out int objectId) => m_Renderer.FindClosestObject(ray, out objectId);

        internal ObjectPickStatus FindClosestMesh(Ray ray, out int objectId, out float distance)
        {
            return m_Renderer.FindClosestMesh(ray, out objectId, out distance);
        }

        public bool TryFindClosest(Ray ray, out int objectId, out float distance)
        {
            return FindClosestMesh(ray, out objectId, out distance) == ObjectPickStatus.Hit;
        }

        public bool SelectClosest(Ray ray)
        {
            ObjectPickStatus status = FindClosestObject(ray, out int objectId);
            if (status == ObjectPickStatus.Pending) return false;
            if (status == ObjectPickStatus.Miss)
            {
                Deselect();
                return false;
            }

            Select(objectId);
            return true;
        }

        public bool SelectObject(int objectId)
        {
            if (!m_Objects.ContainsKey(objectId))
            {
                return false;
            }

            Select(objectId);
            return true;
        }

        public void SetHoveredObject(int? objectId)
        {
            // Selected objects use their own presentation and suppress ordinary hover.
            // this prevents two competing visual states on the same PO
            if (m_SelectedObjectId.HasValue)
            {
                objectId = null;
            }

            ApplyHoveredObject(objectId);
        }

        internal void SetCombineHoveredObject(int? objectId)
        {
            if (objectId.HasValue && m_SelectedObjectId == objectId)
            {
                objectId = null;
            }

            ApplyHoveredObject(objectId);
        }

        internal void SetDeleteHoveredObject(int objectId)
        {
            ApplyHoveredObject(objectId, delete: true);
        }

        private void ApplyHoveredObject(int? objectId, bool delete = false)
        {
            if (objectId.HasValue && !m_Objects.ContainsKey(objectId.Value))
            {
                objectId = null;
            }

            delete &= objectId.HasValue;
            if (m_HoveredObjectId == objectId && m_DeleteHover == delete)
            {
                return;
            }

            if (m_HoveredObjectId.HasValue)
            {
                m_Renderer.SetHovered(m_HoveredObjectId.Value, false);
            }

            m_HoveredObjectId = objectId;
            m_DeleteHover = delete;

            if (m_HoveredObjectId.HasValue)
            {
                m_Renderer.SetHovered(m_HoveredObjectId.Value, true, delete);
            }
        }

        public void ClearHoveredObject()
        {
            ApplyHoveredObject(null);
        }

        public void Deselect()
        {
            ClearHoveredObject();

            m_SelectedObjectId = null;
            m_SelectedPartIndex = 0;
            m_SelectedSurfaceAreaId = MeshPart.kDefaultSurfaceAreaId;
            NotifyLodSelectionChanged();
        }

        public void PreviousSelectedPart()
        {
            StepSelectedPart(-1);
        }

        public void NextSelectedPart()
        {
            StepSelectedPart(1);
        }

        public bool SetSelectedPart(int partIndex)
        {
            if (!TryGetSelected(out ProceduralObject selected) || partIndex < 0 || partIndex >= selected.Parts.Count)
            {
                return false;
            }

            if (m_SelectedPartIndex == partIndex)
            {
                return false;
            }

            ApplySelectedPart(selected, partIndex);
            return true;
        }

        private void StepSelectedPart(int direction)
        {
            if (!TryGetSelected(out ProceduralObject selected) || selected.Parts.Count == 0)
            {
                return;
            }

            if (selected.Parts.Count == 1)
            {
                m_SelectedPartIndex = 0;
                m_SelectedSurfaceAreaId = GetInitialSurfaceAreaId(selected.Parts[0]);
                return;
            }

            int count = selected.Parts.Count;
            int next = (m_SelectedPartIndex + direction) % count;
            if (next < 0)
            {
                next += count;
            }

            SetSelectedPart(next);
        }

        private void ApplySelectedPart(ProceduralObject selected, int partIndex)
        {
            partIndex = Mathf.Clamp(partIndex, 0, selected.Parts.Count - 1);
            m_SelectedPartIndex = partIndex;
            m_SelectedSurfaceAreaId = GetInitialSurfaceAreaId(selected.Parts[partIndex]);
            m_ProceduralObjectToolSystem?.OnActiveMeshPartChanged();
        }

        public void PreviousSelectedSurfaceArea()
        {
            StepSelectedSurfaceArea(-1);
        }

        public void NextSelectedSurfaceArea()
        {
            StepSelectedSurfaceArea(1);
        }

        public bool SetSelectedSurfaceArea(int surfaceAreaId)
        {
            if (!TryGetSelectedPart(out ProceduralObject selected, out MeshPart part, out int partIndex) || !part.TryGetSurfaceArea(surfaceAreaId, out SurfaceArea _))
            {
                return false;
            }

            if (m_SelectedSurfaceAreaId == surfaceAreaId)
            {
                return false;
            }

            m_SelectedSurfaceAreaId = surfaceAreaId;
            return true;
        }

        private void StepSelectedSurfaceArea(int direction)
        {
            if (!TryGetSelectedPart(out ProceduralObject selected, out MeshPart part, out int partIndex) || part.SurfaceAreas.Count == 0)
            {
                return;
            }

            int currentIndex = 0;
            for (int i = 0; i < part.SurfaceAreas.Count; i++)
            {
                if (part.SurfaceAreas[i].Id == m_SelectedSurfaceAreaId)
                {
                    currentIndex = i;
                    break;
                }
            }

            int next = (currentIndex + direction) % part.SurfaceAreas.Count;
            if (next < 0)
            {
                next += part.SurfaceAreas.Count;
            }

            m_SelectedSurfaceAreaId = part.SurfaceAreas[next].Id;
        }

        private void Select(int id)
        {
            ClearHoveredObject();

            if (m_SelectedObjectId == id)
            {
                NotifyLodSelectionChanged();
                return;
            }

            if (!m_Objects.ContainsKey(id))
            {
                m_SelectedObjectId = null;
                m_SelectedPartIndex = 0;
                m_SelectedSurfaceAreaId = MeshPart.kDefaultSurfaceAreaId;
                NotifyLodSelectionChanged();
                return;
            }

            m_SelectedObjectId = id;
            m_SelectedPartIndex = 0;
            m_SelectedSurfaceAreaId = MeshPart.kDefaultSurfaceAreaId;
            NotifyLodSelectionChanged();
        }

        private bool TryGetSelected(out ProceduralObject proceduralObject)
        {
            if (m_SelectedObjectId.HasValue && m_Objects.TryGetValue(m_SelectedObjectId.Value, out proceduralObject))
            {
                return true;
            }

            proceduralObject = null;
            return false;
        }

        private bool TryGetSelectedPart(out ProceduralObject selected, out MeshPart part, out int partIndex)
        {
            if (!TryGetSelected(out selected) || selected.Parts.Count == 0)
            {
                part = null;
                partIndex = -1;
                return false;
            }

            bool partIndexChanged = false;
            if (m_SelectedPartIndex < 0)
            {
                m_SelectedPartIndex = 0;
                partIndexChanged = true;
            }
            else if (m_SelectedPartIndex >= selected.Parts.Count)
            {
                m_SelectedPartIndex = selected.Parts.Count - 1;
                partIndexChanged = true;
            }

            partIndex = m_SelectedPartIndex;
            part = selected.Parts[partIndex];
            if (partIndexChanged)
            {
                m_SelectedSurfaceAreaId = GetInitialSurfaceAreaId(part);
                m_ProceduralObjectToolSystem?.OnActiveMeshPartChanged();
            }

            return true;
        }

        private static int GetInitialSurfaceAreaId(MeshPart part)
        {
            for (int i = 0; i < part.SurfaceAreas.Count; i++)
            {
                if (part.SurfaceAreas[i].Id == MeshPart.kDefaultSurfaceAreaId)
                {
                    return MeshPart.kDefaultSurfaceAreaId;
                }
            }

            return part.SurfaceAreas.Count > 0 ? part.SurfaceAreas[0].Id : -1;
        }

        private bool TryGetSelectedSurfaceArea(out ProceduralObject selected, out MeshPart part, out int partIndex, out SurfaceArea area, out int areaIndex)
        {
            area = null;
            areaIndex = -1;
            if (!TryGetSelectedPart(out selected, out part, out partIndex) || part.SurfaceAreas.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < part.SurfaceAreas.Count; i++)
            {
                if (part.SurfaceAreas[i].Id == m_SelectedSurfaceAreaId)
                {
                    area = part.SurfaceAreas[i];
                    areaIndex = i;
                    return true;
                }
            }

            // Surface Areas may be deleted or replaced by topology operations.
            // Repair stale selection to the part's first valid area selection itself is never persisted
            m_SelectedSurfaceAreaId = part.SurfaceAreas[0].Id;
            area = part.SurfaceAreas[0];
            areaIndex = 0;
            return true;
        }
    }
}