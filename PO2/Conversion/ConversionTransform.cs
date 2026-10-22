using System;
using Unity.Mathematics;

namespace PO2.Conversion
{
    // Both decoders bake the same source transform before building PO geometry.
    internal readonly struct ConversionTransform
    {
        internal readonly float3 Position;
        internal readonly quaternion Rotation;
        internal readonly float3 Scale;
        private readonly float3 m_InverseScale;
        private readonly int m_HasScale;

        internal ConversionTransform(float3 position, quaternion rotation, float3 scale)
        {
            if (!IsValidScale(scale))
            {
                throw new ArgumentOutOfRangeException(nameof(scale), "Conversion scale must be finite and positive.");
            }

            Position = position;
            Rotation = rotation;
            Scale = scale;
            m_InverseScale = new float3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            m_HasScale = scale.x != 1f || scale.y != 1f || scale.z != 1f ? 1 : 0;
        }

        internal static bool IsValidScale(float3 scale)
        {
            return ValidAxis(scale.x) && ValidAxis(scale.y) && ValidAxis(scale.z);
        }

        private static bool ValidAxis(float value)
        {
            return value > 0f && !float.IsInfinity(value) && !float.IsInfinity(1f / value);
        }

        internal float3 TransformPoint(float3 point)
        {
            return math.mul(Rotation, point * Scale) + Position;
        }

        internal float3 TransformNormal(float3 normal)
        {
            return math.normalizesafe(math.mul(Rotation, normal * m_InverseScale));
        }

        internal float3 TransformTangent(float3 tangent)
        {
            float3 direction = math.mul(Rotation, tangent * Scale);
            return m_HasScale != 0 ? math.normalizesafe(direction) : direction;
        }
    }
}