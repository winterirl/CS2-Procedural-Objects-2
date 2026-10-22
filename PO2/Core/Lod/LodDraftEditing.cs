using System;

namespace PO2.Core.Lod
{
    internal enum LodDraftField
    {
        Inherit = 0,
        Lod1Enabled = 1,
        Lod1Method = 2,
        Lod1Ratio = 3,
        Lod1Cap = 4,
        Lod1Error = 5,
        Lod1Prune = 6,
        Lod2Enabled = 11,
        Lod2Method = 12,
        Lod2Ratio = 13,
        Lod2Cap = 14,
        Lod2Error = 15,
        Lod2Prune = 16,
        InheritDistances = 20,
        Lod1Distance = 21,
        Lod2Distance = 22,
    }

    internal static class LodDraftEditing
    {
        internal static bool TryChange(LodProfile profile, LodDraftField field, double value, float globalLod1, float globalLod2, out LodProfile changed)
        {
            changed = profile;
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return false;
            }

            if (field == LodDraftField.Inherit)
            {
                if (value != 0 && value != 1)
                {
                    return false;
                }

                changed = value == 1 ? null : LodProfile.Resolve(profile);
                return true;
            }

            if (profile == null)
            {
                return false;
            }

            try
            {
                if (field >= LodDraftField.InheritDistances && field <= LodDraftField.Lod2Distance)
                {
                    LodTransitionSettings distances = LodProfile.ResolveTransitions(profile, globalLod1, globalLod2);
                    if (field == LodDraftField.InheritDistances)
                    {
                        if (value != 0 && value != 1)
                        {
                            return false;
                        }

                        distances = value == 1 ? null : distances;
                    }
                    else
                    {
                        if (profile.Transitions == null)
                        {
                            return false;
                        }

                        distances = new LodTransitionSettings(field == LodDraftField.Lod1Distance ? (float)value : distances.Lod1Distance, field == LodDraftField.Lod2Distance ? (float)value : distances.Lod2Distance);
                    }

                    changed = new LodProfile(profile.Lod1, profile.Lod2, distances);
                    return true;
                }

                bool second = (int)field >= 11;
                int property = (int)field - (second ? 10 : 0);
                if (property < 1 || property > 6)
                {
                    return false;
                }

                LodLevelSettings level = second ? profile.Lod2 : profile.Lod1;
                if ((!level.Enabled && property != 1) || (property == 6 && level.Method != LodMethod.Aggressive))
                {
                    return false;
                }

                if ((property == 1 || property == 2) && value != 0 && value != 1)
                {
                    return false;
                }

                if (property == 4 && (value < 0 || value > LodLevelSettings.kMaximumTriangleCap || value != Math.Truncate(value)))
                {
                    return false;
                }

                level = new LodLevelSettings(property == 1 ? value == 1 : level.Enabled, property == 2 ? (LodMethod)value : level.Method, property == 3 ? (float)value : level.TriangleRatio, property == 4 ? (value == 0 ? (int?)null : (int)value) : level.TriangleCap, property == 5 ? (float)value : level.MaximumRelativeError, property == 6 ? (float)value : level.PruneRelativeError);
                changed = new LodProfile(second ? profile.Lod1 : level, second ? level : profile.Lod2, profile.Transitions);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }
    }
}