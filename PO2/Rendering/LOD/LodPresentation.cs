using System;
using PO2.Core.Lod;

namespace PO2.Rendering.Lod
{
    internal sealed class LodPresentation
    {
        private LodPresentation(int[] levels, float[] heights, int forcedIndex)
        {
            Levels = levels;
            Heights = heights;
            ForcedIndex = forcedIndex;
        }

        internal int[] Levels { get; }
        internal float[] Heights { get; }
        internal int ForcedIndex { get; }

        internal static LodPresentation Create(LodAutomaticChain chain, bool hasLod1, bool hasLod2, LodPreviewDisplay display, bool ordinarySelection, LodProfile profile, float globalLod1, float globalLod2)
        {
            if (display < LodPreviewDisplay.Auto || display > LodPreviewDisplay.Lod2)
            {
                throw new ArgumentOutOfRangeException(nameof(display));
            }

            bool first = chain.IncludeLod1 && hasLod1;
            bool second = chain.IncludeLod2 && hasLod2;
            if (display == LodPreviewDisplay.Lod1)
            {
                first = hasLod1;
            }

            if (display == LodPreviewDisplay.Lod2)
            {
                second = hasLod2;
            }

            int[] levels = first && second ? new[] { 0, 1, 2 } : first ? new[] { 0, 1 } : second ? new[] { 0, 2 } : new[] { 0 };
            LodTransitionSettings distances = LodProfile.ResolveTransitions(profile, globalLod1, globalLod2);
            GetTransitionHeights(distances, out float firstHeight, out float secondHeight);
            var heights = new float[levels.Length];
            for (int i = 0; i < levels.Length - 1; ++i)
            {
                heights[i] = levels[i + 1] == 1 ? firstHeight : secondHeight;
            }

            int force = ordinarySelection || display == LodPreviewDisplay.Original ? 0 : -1;
            if (display == LodPreviewDisplay.Lod1 || display == LodPreviewDisplay.Lod2)
            {
                int logical = display == LodPreviewDisplay.Lod1 ? 1 : 2;
                force = Array.IndexOf(levels, logical);
                if (force < 0)
                {
                    force = 0;
                }
            }

            return new LodPresentation(levels, heights, force);
        }

        internal static void GetTransitionHeights(LodTransitionSettings distances, out float first, out float second)
        {
            first = 0.18f / distances.Lod1Distance;
            second = Math.Min(0.045f / distances.Lod2Distance, first * 0.99f);
        }
    }
}