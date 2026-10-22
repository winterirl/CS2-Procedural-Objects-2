using UnityEngine;

namespace PO2.Tools.Gizmos
{
    internal static class SelectionTransformMath
    {
        internal static Matrix4x4 CreateWorldRotation(Vector3 pivot, Vector3 axis, float degrees, out bool neutral)
        {
            float angle = degrees % 360f;
            neutral = angle == 0f;
            return neutral ? Matrix4x4.identity : Matrix4x4.Translate(pivot) * Matrix4x4.Rotate(Quaternion.AngleAxis(angle, axis)) * Matrix4x4.Translate(-pivot);
        }

        internal static Matrix4x4 CreateWorldScale(Vector3 pivot, TransformFrame frame, Vector3 factors)
        {
            Quaternion rotation = frame.ToRotation();
            return Matrix4x4.Translate(pivot) * Matrix4x4.Rotate(rotation) * Matrix4x4.Scale(factors) * Matrix4x4.Rotate(Quaternion.Inverse(rotation)) * Matrix4x4.Translate(-pivot);
        }

        internal static bool TryGetLocalLinearTransform(Matrix4x4 worldFromLocal, Matrix4x4 worldFromStart, bool neutral, out Matrix4x4 localFromStart)
        {
            // Mesh edits already know their pivot in PO-local coordinates.
            // Keep large city coordinates out of the matrix multiplication so
            // a vertex at the pivot cannot drift just from cancellation error.
            worldFromLocal.m03 = 0f;
            worldFromLocal.m13 = 0f;
            worldFromLocal.m23 = 0f;
            worldFromStart.m03 = 0f;
            worldFromStart.m13 = 0f;
            worldFromStart.m23 = 0f;
            return TryGetLocalTransform(worldFromLocal, worldFromStart, neutral, out localFromStart);
        }

        internal static bool TryGetLocalTransform(Matrix4x4 worldFromLocal, Matrix4x4 worldFromStart, bool neutral, out Matrix4x4 localFromStart)
        {
            localFromStart = Matrix4x4.identity;
            float determinant = worldFromLocal.determinant;
            if (!IsFinite(worldFromLocal) || !IsFinite(worldFromStart) || !IsFinite(determinant) || determinant == 0f)
            {
                return false;
            }

            Matrix4x4 localFromWorld = worldFromLocal.inverse;
            if (!IsFinite(localFromWorld) || localFromWorld.determinant == 0f)
            {
                return false;
            }

            // A complete turn or neutral scale restores the captured values
            // directly, including on a PO with a scaled/rotated root.
            if (!neutral)
            {
                localFromStart = localFromWorld * worldFromStart * worldFromLocal;
            }

            return IsFinite(localFromStart);
        }

        internal static bool SameMatrix(Matrix4x4 left, Matrix4x4 right)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (left[row, column] != right[row, column])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsFinite(Matrix4x4 matrix)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (!IsFinite(matrix[row, column]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        internal static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}