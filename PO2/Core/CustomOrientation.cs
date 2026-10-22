using System.Text;
using UnityEngine;

namespace PO2.Core
{
    // A named transform frame captured relative to its owning PO.
    // The stored rotation is detached from the geometry that created it.
    public sealed class CustomOrientation
    {
        internal const int kMaxNameUtf8Bytes = 64;

        private const float kMinimumQuaternionMagnitudeSquared = 0.000000000001f;

        public string Name { get; private set; }
        public Quaternion LocalRotation { get; }

        private CustomOrientation(string name, Quaternion localRotation)
        {
            Name = name;
            LocalRotation = localRotation;
        }

        internal static bool TryCreate(string name, Quaternion localRotation, out CustomOrientation orientation)
        {
            orientation = null;

            if (!TryNormalizeName(name, out string normalizedName) || !TryNormalizeRotation(localRotation, out Quaternion normalizedRotation))
            {
                return false;
            }

            orientation = new CustomOrientation(normalizedName, normalizedRotation);
            return true;
        }

        internal bool TryRename(string name)
        {
            if (!TryNormalizeName(name, out string normalizedName) || normalizedName == Name)
            {
                return false;
            }

            Name = normalizedName;
            return true;
        }

        internal static CustomOrientation RestoreHistory(string name, Quaternion rotation)
        {
            if (!TryNormalizeName(name, out string normalized) || normalized != name || !TryNormalizeRotation(rotation, out _)) throw new System.ArgumentException("Invalid history orientation.");
            return new CustomOrientation(name, rotation);
        }

        internal CustomOrientation Clone()
        {
            return new CustomOrientation(Name, LocalRotation);
        }

        private static bool TryNormalizeName(string name, out string normalized)
        {
            normalized = name == null ? string.Empty : name.Trim();

            return normalized.Length > 0 && Encoding.UTF8.GetByteCount(normalized) <= kMaxNameUtf8Bytes;
        }

        internal static bool TryNormalizeRotation(Quaternion rotation, out Quaternion normalized)
        {
            float magnitudeSquared = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;

            if (!IsFinite(rotation.x) || !IsFinite(rotation.y) || !IsFinite(rotation.z) || !IsFinite(rotation.w) || !IsFinite(magnitudeSquared) || magnitudeSquared < kMinimumQuaternionMagnitudeSquared)
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