using System;
using Game;
using Game.Tools;
using UnityEngine;
using PO2.Geometry;
using PO2.Localization;
using PO2.Materials;

namespace PO2.Core
{
    public partial class ProceduralObjectSystem
    {
        public int SelectedMappingMode
        {
            get { return TryGetSelectedSurfaceArea(out ProceduralObject _, out MeshPart _, out int _, out SurfaceArea area, out int _) ? (int)area.Material.Mapping.Mode : (int)TextureMappingMode.Original; }
        }

        public float SelectedMappingTextureWidthMeters =>
            GetSelectedMappingValue(area => area.Material.GetEffectiveTileSizeMeters().x, 1f);

        public float SelectedMappingTextureHeightMeters =>
            GetSelectedMappingValue(area => area.Material.GetEffectiveTileSizeMeters().y, 1f);

        public float SelectedMappingOffsetU =>
            GetSelectedMappingValue(area => area.Material.Mapping.Offset.x, 0f);

        public float SelectedMappingOffsetV =>
            GetSelectedMappingValue(area => area.Material.Mapping.Offset.y, 0f);

        public float SelectedMappingRotation =>
            GetSelectedMappingValue(area => area.Material.Mapping.Rotation, 0f);

        public void CycleSelectedMappingMode()
        {
            if (!TryGetSelectedSurfaceArea(out ProceduralObject selected, out MeshPart _, out int partIndex, out SurfaceArea area, out int _))
            {
                return;
            }

            if (selected.IsLocked)
            {
                return;
            }

            TextureMappingMode current = area.Material.Mapping.Mode;
            TextureMappingMode next;
            switch (current)
            {
                case TextureMappingMode.Original:
                    next = TextureMappingMode.Repeat;
                    break;
                case TextureMappingMode.Repeat:
                    next = TextureMappingMode.Stretch;
                    break;
                default:
                    next = TextureMappingMode.Original;
                    break;
            }

            using (var history = BeginAppearanceHistory(selected, partIndex, area.Id, LocalizedText.For("PO2.History.Action.Mapping.SetMode")))
            {
                if (history == null) return;
                if (selected.SetSurfaceAreaMappingMode(partIndex, area.Id, next))
                {
                    if (!history.Complete()) return;
                }
            }
        }

        public void SetSelectedMappingTextureWidth(float widthMeters)
        {
            SetSelectedMappingTextureSizeComponent(width: true, sizeMeters: widthMeters);
        }

        public void SetSelectedMappingTextureHeight(float heightMeters)
        {
            SetSelectedMappingTextureSizeComponent(width: false, sizeMeters: heightMeters);
        }

        public void SetSelectedMappingOffsetU(float offsetMeters)
        {
            SetSelectedMappingOffsetComponent(uAxis: true, offsetMeters: offsetMeters);
        }

        public void SetSelectedMappingOffsetV(float offsetMeters)
        {
            SetSelectedMappingOffsetComponent(uAxis: false, offsetMeters: offsetMeters);
        }

        public void SetSelectedMappingRotation(float degrees)
        {
            if (!IsFinite(degrees) || !TryGetEditableSelectedSurfaceArea("change mapping rotation", out ProceduralObject selected, out int partIndex, out SurfaceArea area))
            {
                return;
            }

            using (var history = BeginAppearanceHistory(selected, partIndex, area.Id, LocalizedText.For("PO2.History.Action.Mapping.Rotate")))
            {
                if (history == null) return;
                if (selected.SetSurfaceAreaTextureRotation(partIndex, area.Id, degrees))
                {
                    if (!history.Complete()) return;
                }
            }
        }

        public void ResetSelectedMappingTransform()
        {
            if (!TryGetEditableSelectedSurfaceArea("reset mapping transform", out ProceduralObject selected, out int partIndex, out SurfaceArea area))
            {
                return;
            }

            using (var history = BeginAppearanceHistory(selected, partIndex, area.Id, LocalizedText.For("PO2.History.Action.Mapping.Reset")))
            {
                if (history == null) return;
                bool changed = false;
                changed |= selected.SetSurfaceAreaTextureScale(partIndex, area.Id, Vector2.one);
                changed |= selected.SetSurfaceAreaTextureRotation(partIndex, area.Id, 0f);
                changed |= selected.SetSurfaceAreaTextureOffset(partIndex, area.Id, Vector2.zero);

                if (!history.Complete()) return;
            }
        }

        private void SetSelectedMappingTextureSizeComponent(bool width, float sizeMeters)
        {
            if (!IsFinite(sizeMeters) || sizeMeters <= 0f)
            {
                Mod.log.Warn("[PO Mapping] Physical texture size must be greater than zero.");
                return;
            }

            if (!TryGetEditableSelectedSurfaceArea("change physical texture size", out ProceduralObject selected, out int partIndex, out SurfaceArea area))
            {
                return;
            }

            Vector2 baseTileSize = area.Material.GetBaseTileSizeMeters();
            Vector2 scale = area.Material.Mapping.TextureScale;
            if (width)
            {
                scale.x = sizeMeters / Mathf.Max(0.001f, baseTileSize.x);
            }
            else
            {
                scale.y = sizeMeters / Mathf.Max(0.001f, baseTileSize.y);
            }

            if (!IsFinite(scale.x) || !IsFinite(scale.y))
            {
                Mod.log.Warn("[PO Mapping] Physical texture size is outside the supported range.");
                return;
            }

            using (var history = BeginAppearanceHistory(selected, partIndex, area.Id, LocalizedText.For("PO2.History.Action.Mapping.Resize")))
            {
                if (history == null) return;
                if (selected.SetSurfaceAreaTextureScale(partIndex, area.Id, scale))
                {
                    if (!history.Complete()) return;
                }
            }
        }

        private void SetSelectedMappingOffsetComponent(bool uAxis, float offsetMeters)
        {
            if (!IsFinite(offsetMeters) || !TryGetEditableSelectedSurfaceArea("change mapping offset", out ProceduralObject selected, out int partIndex, out SurfaceArea area))
            {
                return;
            }

            Vector2 offset = area.Material.Mapping.Offset;
            if (uAxis)
            {
                offset.x = offsetMeters;
            }
            else
            {
                offset.y = offsetMeters;
            }

            using (var history = BeginAppearanceHistory(selected, partIndex, area.Id, LocalizedText.For("PO2.History.Action.Mapping.Offset")))
            {
                if (history == null) return;
                if (selected.SetSurfaceAreaTextureOffset(partIndex, area.Id, offset))
                {
                    if (!history.Complete()) return;
                }
            }
        }

        private float GetSelectedMappingValue(Func<SurfaceArea, float> selector, float fallback)
        {
            if (!TryGetSelectedSurfaceArea(out ProceduralObject _, out MeshPart _, out int _, out SurfaceArea area, out int _))
            {
                return fallback;
            }

            return selector(area);
        }
    }
}