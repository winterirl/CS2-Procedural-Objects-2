using System;

namespace PO2.Materials
{
    // Classifies native surfaces by asset-pipeline suffix.
    // The shader alone cannot distinguish every source family.
    public static class NativeSurfaceClassifier
    {
        // Keep _Wim distinct from _Win even though both currently use SG_WinShader.
        public static NativeWindowKind ClassifyWindowKind(string surfaceName)
        {
            if (string.IsNullOrEmpty(surfaceName))
            {
                return NativeWindowKind.None;
            }

            if (surfaceName.IndexOf("_Wim_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return NativeWindowKind.FrostedWindow;
            }

            if (surfaceName.IndexOf("_Win_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return NativeWindowKind.Window;
            }

            return NativeWindowKind.None;
        }

        public static string GetWindowFamilyLabel(NativeWindowKind kind)
        {
            switch (kind)
            {
                case NativeWindowKind.Window:
                    return "Window (_Win)";
                case NativeWindowKind.FrostedWindow:
                    return "FrostedWindow (_Wim)";
                default:
                    return null;
            }
        }
    }
}