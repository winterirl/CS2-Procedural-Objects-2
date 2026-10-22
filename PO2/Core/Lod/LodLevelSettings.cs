using System;

namespace PO2.Core.Lod
{
    internal sealed class LodLevelSettings : IEquatable<LodLevelSettings>
    {
        internal const int kMaximumTriangleCap = int.MaxValue / 3;

        internal bool Enabled { get; }
        internal LodMethod Method { get; }
        internal float TriangleRatio { get; }
        internal int? TriangleCap { get; }
        internal float MaximumRelativeError { get; }
        internal float PruneRelativeError { get; }

        internal LodLevelSettings(bool enabled, LodMethod method, float triangleRatio, int? triangleCap, float maximumRelativeError, float pruneRelativeError)
        {
            if (method != LodMethod.EdgeCollapse && method != LodMethod.Aggressive)
            {
                throw new ArgumentOutOfRangeException(nameof(method));
            }

            if (!IsFinite(triangleRatio) || triangleRatio <= 0f || triangleRatio >= 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(triangleRatio));
            }

            if (triangleCap.HasValue && (triangleCap.Value < 1 || triangleCap.Value > kMaximumTriangleCap))
            {
                throw new ArgumentOutOfRangeException(nameof(triangleCap));
            }

            ValidateUnitInterval(maximumRelativeError, nameof(maximumRelativeError));
            ValidateUnitInterval(pruneRelativeError, nameof(pruneRelativeError));

            Enabled = enabled;
            Method = method;
            TriangleRatio = triangleRatio;
            TriangleCap = triangleCap;
            // Signed zero should compare and hash the same way
            MaximumRelativeError = maximumRelativeError == 0f ? 0f : maximumRelativeError;
            PruneRelativeError = pruneRelativeError == 0f ? 0f : pruneRelativeError;
        }

        public bool Equals(LodLevelSettings other)
        {
            return other != null && Enabled == other.Enabled && Method == other.Method && TriangleRatio == other.TriangleRatio && TriangleCap == other.TriangleCap && MaximumRelativeError == other.MaximumRelativeError && PruneRelativeError == other.PruneRelativeError;
        }

        internal bool HasSameGeneration(LodLevelSettings other)
        {
            if (other == null || Enabled != other.Enabled) return false;
            if (!Enabled) return true;
            return Method == other.Method && TriangleRatio == other.TriangleRatio && TriangleCap == other.TriangleCap && MaximumRelativeError == other.MaximumRelativeError && (Method != LodMethod.Aggressive || PruneRelativeError == other.PruneRelativeError);
        }

        public override bool Equals(object obj) => Equals(obj as LodLevelSettings);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Enabled.GetHashCode();
                hash = hash * 31 + (int)Method;
                hash = hash * 31 + TriangleRatio.GetHashCode();
                hash = hash * 31 + TriangleCap.GetHashCode();
                hash = hash * 31 + MaximumRelativeError.GetHashCode();
                return hash * 31 + PruneRelativeError.GetHashCode();
            }
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void ValidateUnitInterval(float value, string name)
        {
            if (!IsFinite(value) || value < 0f || value > 1f) throw new ArgumentOutOfRangeException(name);
        }
    }
}