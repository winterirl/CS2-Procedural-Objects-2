using System;

namespace PO2.Core.Lod
{
    internal sealed class LodTransitionSettings : IEquatable<LodTransitionSettings>
    {
        internal const float kMinimumDistanceMultiplier = 0.5f;
        internal const float kMaximumDistanceMultiplier = 2f;

        internal float Lod1Distance { get; }
        internal float Lod2Distance { get; }

        internal LodTransitionSettings(float lod1Distance, float lod2Distance)
        {
            Validate(lod1Distance, nameof(lod1Distance));
            Validate(lod2Distance, nameof(lod2Distance));
            Lod1Distance = lod1Distance;
            Lod2Distance = lod2Distance;
        }

        internal static LodTransitionSettings FromGlobal(float lod1Distance, float lod2Distance)
        {
            return new LodTransitionSettings(Sanitize(lod1Distance), Sanitize(lod2Distance));
        }

        public bool Equals(LodTransitionSettings other)
        {
            return other != null && Lod1Distance == other.Lod1Distance && Lod2Distance == other.Lod2Distance;
        }

        public override bool Equals(object obj) => Equals(obj as LodTransitionSettings);

        public override int GetHashCode()
        {
            unchecked
            {
                return Lod1Distance.GetHashCode() * 31 + Lod2Distance.GetHashCode();
            }
        }

        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 1f;
            return Math.Max(kMinimumDistanceMultiplier, Math.Min(kMaximumDistanceMultiplier, value));
        }

        private static void Validate(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < kMinimumDistanceMultiplier || value > kMaximumDistanceMultiplier)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }
}