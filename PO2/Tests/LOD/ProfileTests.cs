using System;
using PO2.Core.Lod;
using PO2.Rendering.Lod;

internal static partial class Program {
    private static void ValidateLodProfiles() {
        var defaults = LodProfile.Resolve(null);
        foreach (float invalid in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity, -0.01f, 0f, 1f, 1.01f }) {
            RejectLodValue(() => new LodLevelSettings(true, LodMethod.EdgeCollapse, invalid, null, 0f, 0f));
        }
        foreach (float invalid in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity, -0.01f, 1.01f }) {
            RejectLodValue(() => new LodLevelSettings(false, LodMethod.Aggressive, 0.5f, null, invalid, 0f));
            RejectLodValue(() => new LodLevelSettings(false, LodMethod.Aggressive, 0.5f, null, 0f, invalid));
        }
        foreach (int cap in new[] { 0, -1, int.MinValue, int.MaxValue, LodLevelSettings.kMaximumTriangleCap + 1 }) {
            RejectLodValue(() => new LodLevelSettings(true, LodMethod.Aggressive, 0.5f, cap, 0.01f, 0f));
        }
        foreach (LodMethod method in new[] { (LodMethod)2, (LodMethod)255 }) {
            RejectLodValue(() => new LodLevelSettings(true, method, 0.5f, null, 0f, 0f));
        }
        foreach (float invalid in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity, 0.49f, 2.01f }) {
            RejectLodValue(() => new LodTransitionSettings(invalid, 1f));
            RejectLodValue(() => new LodTransitionSettings(1f, invalid));
        }
        RejectLodValue(() => new LodProfile(null, defaults.Lod2));
        RejectLodValue(() => new LodProfile(defaults.Lod1, null));

        var boundary = new LodLevelSettings(true, LodMethod.Aggressive, float.Epsilon,
            LodLevelSettings.kMaximumTriangleCap, 1f, 0f);
        Check(checked(boundary.TriangleCap.Value * 3) <= int.MaxValue, "Triangle cap cannot overflow index counts");
        Check(boundary.MaximumRelativeError == 1f && boundary.PruneRelativeError == 0f, "Inclusive error endpoints are valid");
        var global = LodProfile.ResolveTransitions(null, float.NaN, 10f);
        Check(global.Lod1Distance == 1f && global.Lod2Distance == 2f, "Invalid global settings use the existing fallback/clamp");
        global = LodProfile.ResolveTransitions(defaults, -1f, float.NegativeInfinity);
        Check(global.Lod1Distance == 0.5f && global.Lod2Distance == 1f, "Custom generation can inherit global distances");
        var transitions = new LodTransitionSettings(2f, 0.5f);
        var custom = new LodProfile(defaults.Lod1, defaults.Lod2, transitions);
        Check(ReferenceEquals(custom, LodProfile.Resolve(custom)), "Resolving custom settings retains the immutable value");
        Check(ReferenceEquals(transitions, LodProfile.ResolveTransitions(custom, 0.5f, 2f)),
            "Custom distances replace global values");
        Check(LodProfile.HasSameGeneration(defaults, custom), "Distance-only changes do not require generation");
        Check(!defaults.Equals(custom), "Distance inheritance remains part of saved equality");
        Check(!LodProfile.HasSameGeneration(null, defaults), "Explicit defaults and inheritance use different publication policies");
        Check(LodProfile.HasSameGeneration(null, null), "Two inherited profiles share generation");

        var edge = new LodLevelSettings(true, LodMethod.EdgeCollapse, 0.4f, null, 0.005f, 0.25f);
        Check(defaults.Lod1.HasSameGeneration(edge) && !defaults.Lod1.Equals(edge),
            "An unused pruning value is saved without requiring edge-collapse regeneration");
        var disabled1 = new LodLevelSettings(false, LodMethod.EdgeCollapse, 0.4f, null, 0f, 0f);
        var disabled2 = new LodLevelSettings(false, LodMethod.Aggressive, 0.1f, 10, 1f, 1f);
        Check(disabled1.HasSameGeneration(disabled2) && !disabled1.Equals(disabled2),
            "Disabled level settings remain distinct without generating geometry");
        Check(!disabled1.HasSameGeneration(defaults.Lod1), "Enabling a level requires generation");

        var equal = new LodProfile(new LodLevelSettings(true, LodMethod.EdgeCollapse, 0.4f, null, 0.005f, 0f),
            new LodLevelSettings(true, LodMethod.Aggressive, 0.1f, 1500, 0.01f, 0.001f));
        Check(defaults.Equals(equal) && defaults.GetHashCode() == equal.GetHashCode(), "Profiles use value equality");
        var zero = new LodLevelSettings(true, LodMethod.EdgeCollapse, 0.5f, null, 0f, 0f);
        var negativeZero = new LodLevelSettings(true, LodMethod.EdgeCollapse, 0.5f, null, -0f, -0f);
        Check(zero.Equals(negativeZero) && zero.GetHashCode() == negativeZero.GetHashCode() &&
            BitConverter.SingleToInt32Bits(negativeZero.MaximumRelativeError) == 0,
            "Signed zero is consistent for equality, serialization and history");
    }

    private static void RejectLodValue(Action action) {
        try { action(); }
        catch (ArgumentException) { Check(true, "Invalid LOD input rejected"); return; }
        throw new Exception("Invalid LOD input was accepted.");
    }
}
