using UnityEngine;

namespace PO2.Core
{
    // The saved PO transform is separate from Unity objects. Mesh coordinates are PO-local.
    // Keep position and rotation at the root; bake shape scaling into the geometry.
    // Root scale remains supported for existing data.
    public struct ObjectTransform
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;

        public ObjectTransform(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public static ObjectTransform IdentityAt(Vector3 position)
        {
            return new ObjectTransform(position, Quaternion.identity, Vector3.one);
        }

        public Vector3 TransformPoint(Vector3 localPoint)
        {
            // Match Unity's local-to-world TRS order: scale, then rotate, then translate.
            // Keeping this explicit avoids depending on a runtime  transform component for coordinate conversion.
            Vector3 scaledPoint = Vector3.Scale(localPoint, Scale);

            return Position + Rotation * scaledPoint;
        }

        public Vector3 InverseTransformPoint(Vector3 worldPoint)
        {
            Vector3 unrotatedPoint = Quaternion.Inverse(Rotation) * (worldPoint - Position);

            return DivideByScale(unrotatedPoint);
        }

        public Vector3 TransformVector(Vector3 localVector)
        {
            return Rotation * Vector3.Scale(localVector, Scale);
        }

        public Vector3 InverseTransformVector(Vector3 worldVector)
        {
            Vector3 unrotatedVector = Quaternion.Inverse(Rotation) * worldVector;

            return DivideByScale(unrotatedVector);
        }

        private Vector3 DivideByScale(Vector3 value)
        {
            return new Vector3(SafeDivide(value.x, Scale.x), SafeDivide(value.y, Scale.y), SafeDivide(value.z, Scale.z));
        }

        private static float SafeDivide(float value, float scaleComponent)
        {
            const float minimumMagnitude = 0.000001f;

            // A 0 or near-0 scale can still be encountered in drag states or old data.
            // Clamp only the divisor so inverse-space math remains finite without silently changing stored scale.
            // aka - even being able to try to divide by 0 is bad
            if (Mathf.Abs(scaleComponent) < minimumMagnitude)
            {
                scaleComponent = scaleComponent < 0f ? -minimumMagnitude : minimumMagnitude;
            }

            return value / scaleComponent;
        }
    }
}