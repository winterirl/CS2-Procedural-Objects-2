using UnityEngine;

namespace PO2.Tools.Gizmos
{
    // A resolved transform frame contains world-space directions only.
    // Pivot, target, and orientation source stay with the tool so other
    // modes can produce frames without changing gizmo consumers.
    public readonly struct TransformFrame
    {
        private const float kMinimumQuaternionMagnitudeSquared = 0.000000000001f;

        private const float kMinimumDirectionMagnitudeSquared = 0.00000001f;

        public Vector3 XAxis { get; }
        public Vector3 YAxis { get; }
        public Vector3 ZAxis { get; }

        private TransformFrame(Vector3 xAxis, Vector3 yAxis, Vector3 zAxis)
        {
            XAxis = xAxis;
            YAxis = yAxis;
            ZAxis = zAxis;
        }

        public static TransformFrame Global =>
            new TransformFrame(Vector3.right, Vector3.up, Vector3.forward);

        public static TransformFrame FromObjectRotation(Quaternion rotation)
        {
            if (!TryNormalize(rotation, out Quaternion normalized))
            {
                return Global;
            }

            return new TransformFrame(normalized * Vector3.right, normalized * Vector3.up, normalized * Vector3.forward);
        }

        public static TransformFrame FromObjectRelativeRotation(Quaternion objectRotation, Quaternion relativeRotation)
        {
            return FromObjectRotation(objectRotation * relativeRotation);
        }

        public static TransformFrame FromUprightHeading(Quaternion rotation)
        {
            if (!TryNormalize(rotation, out Quaternion normalized))
            {
                return Global;
            }

            // Local Move/Rotate stay upright.
            // Extract only the quaternion twist around world Y instead of reading Euler Y,
            // which can jump after compound pitch and roll rotations.
            float twistMagnitudeSquared = normalized.y * normalized.y + normalized.w * normalized.w;

            if (twistMagnitudeSquared >= kMinimumQuaternionMagnitudeSquared)
            {
                float inverseMagnitude = 1f / Mathf.Sqrt(twistMagnitudeSquared);

                Quaternion heading = new Quaternion(0f, normalized.y * inverseMagnitude, 0f, normalized.w * inverseMagnitude);

                return FromObjectRotation(heading);
            }

            // A 180-degree swing around a horizontal axis has no unique Y twist.
            // Prefer the projected forward direction because Local is defined by
            // object heading, then rebuild an upright right-handed frame.
            Vector3 projectedZ = normalized * Vector3.forward;

            projectedZ.y = 0f;

            if (projectedZ.sqrMagnitude >= kMinimumDirectionMagnitudeSquared)
            {
                Vector3 zAxis = projectedZ.normalized;
                Vector3 xAxis = Vector3.Cross(Vector3.up, zAxis).normalized;

                return new TransformFrame(xAxis, Vector3.up, zAxis);
            }

            Vector3 projectedX = normalized * Vector3.right;

            projectedX.y = 0f;

            if (projectedX.sqrMagnitude >= kMinimumDirectionMagnitudeSquared)
            {
                Vector3 xAxis = projectedX.normalized;
                Vector3 zAxis = Vector3.Cross(xAxis, Vector3.up).normalized;

                return new TransformFrame(xAxis, Vector3.up, zAxis);
            }

            return Global;
        }

        public Vector3 GetAxis(TransformAxis axis)
        {
            switch (axis)
            {
                case TransformAxis.X:
                    return XAxis;

                case TransformAxis.Y:
                    return YAxis;

                case TransformAxis.Z:
                    return ZAxis;

                default:
                    return Vector3.zero;
            }
        }

        public Quaternion ToRotation()
        {
            return Quaternion.LookRotation(ZAxis, YAxis);
        }

        private static bool TryNormalize(Quaternion rotation, out Quaternion normalized)
        {
            float magnitudeSquared = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;

            if (!IsFinite(rotation.x) || !IsFinite(rotation.y) || !IsFinite(rotation.z) || !IsFinite(rotation.w) || magnitudeSquared < kMinimumQuaternionMagnitudeSquared)
            {
                normalized = Quaternion.identity;
                return false;
            }

            float inverseMagnitude = 1f / Mathf.Sqrt(magnitudeSquared);

            normalized = new Quaternion(rotation.x * inverseMagnitude, rotation.y * inverseMagnitude, rotation.z * inverseMagnitude, rotation.w * inverseMagnitude);

            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}