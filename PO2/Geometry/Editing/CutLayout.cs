using System;

namespace PO2.Geometry.Editing
{
    // Preview and commit use the same fractions so the placed cuts match the lines.
    public static class CutLayout
    {
        public const int kMaxCuts = 128;
        public const float kEndpointEpsilon = 0.001f;

        public static float GetFraction(int count, int index, float center)
        {
            if (count == 1) return center;
            return (index + 1f) / (count + 1f) + (center - 0.5f);
        }

        public static float ClampCenter(int count, float center)
        {
            // Leave a little room beyond the builder's endpoint rejection margin.
            float travel = 1f / (count + 1f) - 2f * kEndpointEpsilon;
            return Math.Max(0.5f - travel, Math.Min(0.5f + travel, center));
        }

        public static bool IsValid(int count, float center)
        {
            return count >= 1 && count <= kMaxCuts && !float.IsNaN(center) && !float.IsInfinity(center) && GetFraction(count, 0, center) > kEndpointEpsilon && GetFraction(count, count - 1, center) < 1f - kEndpointEpsilon;
        }
    }
}