using PO2.Core.Lod;

namespace PO2.Rendering.Lod
{
    internal enum LodLevelStatus
    {
        Disabled,
        Limited,
        Usable,
    }

    internal readonly struct LodAutomaticChain
    {
        private LodAutomaticChain(LodLevelStatus lod1Status, LodLevelStatus lod2Status, bool includeLod1, bool includeLod2, bool lod2NotCheaper, bool rejectedDefaultChain)
        {
            Lod1Status = lod1Status;
            Lod2Status = lod2Status;
            IncludeLod1 = includeLod1;
            IncludeLod2 = includeLod2;
            Lod2NotCheaper = lod2NotCheaper;
            RejectedDefaultChain = rejectedDefaultChain;
        }

        internal LodLevelStatus Lod1Status { get; }
        internal LodLevelStatus Lod2Status { get; }
        internal bool IncludeLod1 { get; }
        internal bool IncludeLod2 { get; }
        internal bool Lod2NotCheaper { get; }
        internal bool RejectedDefaultChain { get; }
        internal bool HasGeneratedLevels => IncludeLod1 || IncludeLod2;

        internal static LodAutomaticChain Resolve(LodProfile profile, int sourceTriangles, int lod1Triangles, int lod2Triangles, float minimumReduction)
        {
            LodProfile effective = LodProfile.Resolve(profile);
            LodLevelStatus lod1 = GetStatus(effective.Lod1.Enabled, sourceTriangles, lod1Triangles, minimumReduction);
            LodLevelStatus lod2 = GetStatus(effective.Lod2.Enabled, sourceTriangles, lod2Triangles, minimumReduction);
            bool includeLod1 = lod1 == LodLevelStatus.Usable;
            bool includeLod2 = lod2 == LodLevelStatus.Usable;
            bool lod2NotCheaper = includeLod1 && includeLod2 && lod2Triangles >= lod1Triangles;
            if (lod2NotCheaper)
            {
                includeLod2 = false;
            }

            // Inherited defaults still require the complete, progressively cheaper chain.
            bool rejectedDefaultChain = profile == null && !(includeLod1 && includeLod2);
            if (rejectedDefaultChain)
            {
                includeLod1 = false;
                includeLod2 = false;
            }

            return new LodAutomaticChain(lod1, lod2, includeLod1, includeLod2, lod2NotCheaper, rejectedDefaultChain);
        }

        private static LodLevelStatus GetStatus(bool enabled, int sourceTriangles, int resultTriangles, float minimumReduction)
        {
            if (!enabled)
            {
                return LodLevelStatus.Disabled;
            }

            if (sourceTriangles <= 0 || resultTriangles <= 0 || resultTriangles >= sourceTriangles)
            {
                return LodLevelStatus.Limited;
            }

            float reduction = (sourceTriangles - resultTriangles) / (float)sourceTriangles;
            return reduction >= minimumReduction ? LodLevelStatus.Usable : LodLevelStatus.Limited;
        }
    }
}