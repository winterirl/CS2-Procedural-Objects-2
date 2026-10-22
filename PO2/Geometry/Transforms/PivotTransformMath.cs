using UnityEngine;
using PO2.Core;

namespace PO2.Geometry.Transforms
{
    // Object vertices stay in their existing local coordinates when the pivot
    // changes. Rotation and scale therefore move the root just enough to keep
    // the chosen world pivot in place.
    internal static class PivotTransformMath
    {
        internal static bool TryCreateTransformKeepingPivot(ObjectTransform startTransform, Vector3 localPivot, Quaternion rotation, Vector3 scale, out ObjectTransform result)
        {
            result = startTransform;

            Vector3 worldPivot = startTransform.TransformPoint(localPivot);
            Vector3 scaledPivot = Vector3.Scale(localPivot, scale);
            Vector3 position = worldPivot - rotation * scaledPivot;

            if (!IsFinite(position) || !IsFinite(rotation) || !IsFinite(scale))
            {
                return false;
            }

            result = new ObjectTransform(position, rotation, scale);
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}