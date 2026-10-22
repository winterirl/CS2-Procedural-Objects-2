using UnityEngine;

namespace PO2.Materials
{
    // Keeps the converted asset's color variations as well as an optional PO override.
    // Retaining both makes Source mode and restore original reversible.
    public sealed class ColorMaskData
    {
        public ColorMaskMode Mode { get; private set; }
        public bool HasSourceMask { get; }

        public Color SourceMask1 { get; }
        public Color SourceMask2 { get; }
        public Color SourceMask3 { get; }

        public Color OverrideMask1 { get; private set; }
        public Color OverrideMask2 { get; private set; }
        public Color OverrideMask3 { get; private set; }

        public ColorMaskData() : this(ColorMaskMode.Disabled, false, Color.white, Color.white, Color.white, Color.white, Color.white, Color.white)
        {
        }

        private ColorMaskData(ColorMaskMode mode, bool hasSourceMask, Color sourceMask1, Color sourceMask2, Color sourceMask3, Color overrideMask1, Color overrideMask2, Color overrideMask3)
        {
            HasSourceMask = hasSourceMask;
            SourceMask1 = sourceMask1;
            SourceMask2 = sourceMask2;
            SourceMask3 = sourceMask3;
            OverrideMask1 = overrideMask1;
            OverrideMask2 = overrideMask2;
            OverrideMask3 = overrideMask3;
            Mode = NormalizeMode(mode, hasSourceMask);
        }


        internal static ColorMaskData Restore(ColorMaskMode mode, bool hasSourceMask, Color sourceMask1, Color sourceMask2, Color sourceMask3, Color overrideMask1, Color overrideMask2, Color overrideMask3)
        {
            return new ColorMaskData(mode, hasSourceMask, sourceMask1, sourceMask2, sourceMask3, overrideMask1, overrideMask2, overrideMask3);
        }

        public static ColorMaskData FromSource(Color sourceMask1, Color sourceMask2, Color sourceMask3)
        {
            return new ColorMaskData(ColorMaskMode.Source, true, sourceMask1, sourceMask2, sourceMask3, sourceMask1, sourceMask2, sourceMask3);
        }

        public bool SetMode(ColorMaskMode mode)
        {
            // Source has no meaning for primitives/imports that never had vanilla
            // color variations ~ normalize that request back to Disabled.
            ColorMaskMode normalized = NormalizeMode(mode, HasSourceMask);
            if (Mode == normalized)
            {
                return false;
            }

            Mode = normalized;
            return true;
        }

        public bool SetOverride(Color mask1, Color mask2, Color mask3)
        {
            bool changed = !ColorsEqual(OverrideMask1, mask1) || !ColorsEqual(OverrideMask2, mask2) || !ColorsEqual(OverrideMask3, mask3) || Mode != ColorMaskMode.Override;

            if (!changed)
            {
                return false;
            }

            // Supplying override colors also selects Override mode. Callers do
            // not need a second mutation/version bump for the mode change.
            OverrideMask1 = mask1;
            OverrideMask2 = mask2;
            OverrideMask3 = mask3;
            Mode = ColorMaskMode.Override;
            return true;
        }

        public ColorMaskData Clone()
        {
            return new ColorMaskData(Mode, HasSourceMask, SourceMask1, SourceMask2, SourceMask3, OverrideMask1, OverrideMask2, OverrideMask3);
        }

        private static ColorMaskMode NormalizeMode(ColorMaskMode mode, bool hasSourceMask)
        {
            if (mode == ColorMaskMode.Source && !hasSourceMask)
            {
                return ColorMaskMode.Disabled;
            }

            return mode;
        }

        private static bool ColorsEqual(Color a, Color b)
        {
            return Mathf.Approximately(a.r, b.r) && Mathf.Approximately(a.g, b.g) && Mathf.Approximately(a.b, b.b) && Mathf.Approximately(a.a, b.a);
        }
    }
}