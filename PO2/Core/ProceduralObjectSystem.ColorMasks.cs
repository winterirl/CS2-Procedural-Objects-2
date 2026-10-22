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
        public void SetSelectedColorMaskSource()
        {
            SetSelectedColorMaskMode(ColorMaskMode.Source, "source");
        }

        public void SetSelectedColorMaskDisabled()
        {
            SetSelectedColorMaskMode(ColorMaskMode.Disabled, "off");
        }

        public void SetSelectedColorMaskOverride()
        {
            SetSelectedColorMaskMode(ColorMaskMode.Override, "override");
        }

        public void SetSelectedColorMask1Hex(string value)
        {
            SetSelectedColorMaskHex(0, value);
        }

        public void SetSelectedColorMask2Hex(string value)
        {
            SetSelectedColorMaskHex(1, value);
        }

        public void SetSelectedColorMask3Hex(string value)
        {
            SetSelectedColorMaskHex(2, value);
        }

        private void SetSelectedColorMaskMode(ColorMaskMode mode, string description)
        {
            if (!TryGetSelectedSurfaceArea(out ProceduralObject selected, out MeshPart _, out int partIndex, out SurfaceArea area, out int _))
            {
                return;
            }

            if (selected.IsLocked)
            {
                return;
            }

            if (mode == ColorMaskMode.Source && !area.ColorMask.HasSourceMask)
            {
                return;
            }

            using (var history = BeginAppearanceHistory(selected, partIndex, area.Id, LocalizedText.For("PO2.History.Action.ColorMask.SetMode")))
            {
                if (history == null) return;
                if (selected.SetSurfaceAreaColorMaskMode(partIndex, area.Id, mode))
                {
                    if (!history.Complete()) return;
                }
            }
        }

        private void SetSelectedColorMaskHex(int maskIndex, string value)
        {
            if (!TryGetSelectedSurfaceArea(out ProceduralObject selected, out MeshPart _, out int partIndex, out SurfaceArea area, out int _))
            {
                return;
            }

            if (selected.IsLocked)
            {
                return;
            }

            if (!TryParseColorMaskHex(value, out Color color))
            {
                return;
            }

            Color mask1 = area.ColorMask.OverrideMask1;
            Color mask2 = area.ColorMask.OverrideMask2;
            Color mask3 = area.ColorMask.OverrideMask3;

            switch (maskIndex)
            {
                case 0:
                    mask1 = color;
                    break;
                case 1:
                    mask2 = color;
                    break;
                case 2:
                    mask3 = color;
                    break;
                default:
                    return;
            }

            using (var history = BeginAppearanceHistory(selected, partIndex, area.Id, LocalizedText.For("PO2.History.Action.ColorMask.SetColor")))
            {
                if (history == null) return;
                selected.SetSurfaceAreaColorMaskOverride(partIndex, area.Id, mask1, mask2, mask3);
                history.Complete();
            }
        }

        private string GetSelectedColorMaskHex(int maskIndex)
        {
            if (!TryGetSelectedSurfaceArea(out ProceduralObject _, out MeshPart _, out int _, out SurfaceArea area, out int _))
            {
                return "#FFFFFF";
            }

            Color color;
            switch (maskIndex)
            {
                case 0:
                    color = area.ColorMask.OverrideMask1;
                    break;
                case 1:
                    color = area.ColorMask.OverrideMask2;
                    break;
                case 2:
                    color = area.ColorMask.OverrideMask3;
                    break;
                default:
                    return "#FFFFFF";
            }

            int r = Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f);
            int g = Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f);
            int b = Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f);
            int rgb = (r << 16) | (g << 8) | b;
            if (m_ColorMaskRgb[maskIndex] != rgb)
            {
                m_ColorMaskHex[maskIndex] = $"#{r:X2}{g:X2}{b:X2}";
                m_ColorMaskRgb[maskIndex] = rgb;
            }

            return m_ColorMaskHex[maskIndex];
        }

        private static bool TryParseColorMaskHex(string value, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim();
            if (normalized.StartsWith("#"))
            {
                normalized = normalized.Substring(1);
            }

            if (normalized.Length != 6 || !uint.TryParse(normalized, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint packed))
            {
                return false;
            }

            float r = ((packed >> 16) & 0xff) / 255f;
            float g = ((packed >> 8) & 0xff) / 255f;
            float b = (packed & 0xff) / 255f;
            color = new Color(r, g, b, 1f);
            return true;
        }
    }
}