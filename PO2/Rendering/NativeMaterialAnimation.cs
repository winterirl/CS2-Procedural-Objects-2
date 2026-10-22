using UnityEngine;

namespace PO2.Rendering
{
    internal static class NativeMaterialAnimation
    {
        // Colossal's wind importer enables this on vegetation surface materials.
        internal const string kWindKeyword = "_ANIM_HIERARCHY_PIVOT";

        internal static bool DisableWind(Material material)
        {
            if (material == null || !material.IsKeywordEnabled(kWindKeyword))
            {
                return false;
            }

            material.DisableKeyword(kWindKeyword);
            return true;
        }
    }
}