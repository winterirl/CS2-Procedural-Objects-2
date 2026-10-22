using System;

namespace PO2.Core.Lod
{
    internal sealed class LodProfile : IEquatable<LodProfile>
    {
        internal static LodProfile Default { get; } = new LodProfile(new LodLevelSettings(true, LodMethod.EdgeCollapse, 0.40f, null, 0.005f, 0f), new LodLevelSettings(true, LodMethod.Aggressive, 0.10f, 1500, 0.01f, 0.001f));

        internal LodLevelSettings Lod1 { get; }

        internal LodLevelSettings Lod2 { get; }

        // Null keeps following the global distance settings
        internal LodTransitionSettings Transitions { get; }

        internal LodProfile(LodLevelSettings lod1, LodLevelSettings lod2, LodTransitionSettings transitions = null)
        {
            Lod1 = lod1 ?? throw new ArgumentNullException(nameof(lod1));
            Lod2 = lod2 ?? throw new ArgumentNullException(nameof(lod2));
            Transitions = transitions;
        }

        internal static LodProfile Resolve(LodProfile profile) => profile ?? Default;

        internal static LodTransitionSettings ResolveTransitions(LodProfile profile, float globalLod1, float globalLod2)
        {
            return profile?.Transitions ?? LodTransitionSettings.FromGlobal(globalLod1, globalLod2);
        }

        internal static bool HasSameGeneration(LodProfile left, LodProfile right)
        {
            if (ReferenceEquals(left, right)) return true;
            // Inheritance also selects the existing complete-chain publication rule.
            if (left == null || right == null) return false;
            return left.Lod1.HasSameGeneration(right.Lod1) && left.Lod2.HasSameGeneration(right.Lod2);
        }

        internal static bool CanReuseGeneration(LodProfile generated, LodProfile requested)
        {
            if (HasSameGeneration(generated, requested))
            {
                return true;
            }

            if (generated == null || requested == null)
            {
                return false;
            }

            return (!requested.Lod1.Enabled || generated.Lod1.HasSameGeneration(requested.Lod1)) && (!requested.Lod2.Enabled || generated.Lod2.HasSameGeneration(requested.Lod2));
        }

        public bool Equals(LodProfile other)
        {
            return other != null && Lod1.Equals(other.Lod1) && Lod2.Equals(other.Lod2) && Equals(Transitions, other.Transitions);
        }

        public override bool Equals(object obj) => Equals(obj as LodProfile);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Lod1.GetHashCode() * 31 + Lod2.GetHashCode();
                return hash * 31 + (Transitions?.GetHashCode() ?? 0);
            }
        }
    }
}