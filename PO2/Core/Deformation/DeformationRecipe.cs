using System;
using UnityEngine;
using PO2.Localization;

namespace PO2.Core.Deformation
{
    internal readonly struct DeformationRecipe
    {
        private readonly Matrix4x4 m_ReferenceFromLocal;
        private readonly Vector3 m_A, m_X, m_Y, m_Z, m_XY, m_XZ, m_YZ, m_XYZ;
        internal readonly bool Identity;

        private DeformationRecipe(Matrix4x4 inverse, Vector3[] d, bool identity)
        {
            m_ReferenceFromLocal = inverse;
            Identity = identity;
            m_A = d[0];
            m_X = d[1] - d[0];
            m_Y = d[2] - d[0];
            m_Z = d[4] - d[0];
            m_XY = d[3] - d[1] - d[2] + d[0];
            m_XZ = d[5] - d[1] - d[4] + d[0];
            m_YZ = d[6] - d[2] - d[4] + d[0];
            m_XYZ = d[7] - d[3] - d[5] - d[6] + d[1] + d[2] + d[4] - d[0];
        }

        internal static bool TryCreate(Matrix4x4 reference, Vector3[] corners, out DeformationRecipe recipe, out LocalizedText reason)
        {
            recipe = default;
            reason = LocalizedText.For("PO2.Object.Deformation.Error.UnsafeGeometry");
            if (corners == null || corners.Length != 8 || !Finite(reference) || !Finite(reference.inverse) || reference.determinant == 0f) return false;
            var d = new Vector3[8];
            bool identity = true;
            for (int i = 0; i < 8; i++)
            {
                if (!Finite(corners[i])) return false;
                d[i] = corners[i] - reference.MultiplyPoint3x4(DeformationCage.UnitCorner(i));
                identity &= d[i].Equals(Vector3.zero);
            }

            var candidate = new DeformationRecipe(reference.inverse, d, identity);
            if (!identity && !candidate.ValidCage()) return false;
            recipe = candidate;
            reason = LocalizedText.Empty;
            return true;
        }

        internal Vector3 Position(Vector3 source)
        {
            if (Identity) return source;
            Vector3 q = m_ReferenceFromLocal.MultiplyPoint3x4(source);
            return source + m_A + m_X * q.x + m_Y * q.y + m_Z * q.z + m_XY * (q.x * q.y) + m_XZ * (q.x * q.z) + m_YZ * (q.y * q.z) + m_XYZ * (q.x * q.y * q.z);
        }

        private Matrix4x4 DisplacementDerivative(Vector3 q)
        {
            Matrix4x4 d = default(Matrix4x4);
            Column(ref d, 0, m_X + m_XY * q.y + m_XZ * q.z + m_XYZ * (q.y * q.z));
            Column(ref d, 1, m_Y + m_XY * q.x + m_YZ * q.z + m_XYZ * (q.x * q.z));
            Column(ref d, 2, m_Z + m_XZ * q.x + m_YZ * q.y + m_XYZ * (q.x * q.y));
            return d;
        }

        internal Matrix4x4 Jacobian(Vector3 source)
        {
            Matrix4x4 j = DisplacementDerivative(m_ReferenceFromLocal.MultiplyPoint3x4(source)) * m_ReferenceFromLocal;
            j.m00 += 1f;
            j.m11 += 1f;
            j.m22 += 1f;
            j.m03 = j.m13 = j.m23 = 0f;
            j.m33 = 1f;
            return j;
        }

        private double NormalizedDeterminant(double x, double y, double z)
        {
            // Use double arithmetic for the small validity calculation, especially
            // when a far corner makes large terms cancel each other out.
            double dxX = m_X.x + m_XY.x * y + m_XZ.x * z + m_XYZ.x * y * z;
            double dxY = m_X.y + m_XY.y * y + m_XZ.y * z + m_XYZ.y * y * z;
            double dxZ = m_X.z + m_XY.z * y + m_XZ.z * z + m_XYZ.z * y * z;
            double dyX = m_Y.x + m_XY.x * x + m_YZ.x * z + m_XYZ.x * x * z;
            double dyY = m_Y.y + m_XY.y * x + m_YZ.y * z + m_XYZ.y * x * z;
            double dyZ = m_Y.z + m_XY.z * x + m_YZ.z * z + m_XYZ.z * x * z;
            double dzX = m_Z.x + m_XZ.x * x + m_YZ.x * y + m_XYZ.x * x * y;
            double dzY = m_Z.y + m_XZ.y * x + m_YZ.y * y + m_XYZ.y * x * y;
            double dzZ = m_Z.z + m_XZ.z * x + m_YZ.z * y + m_XYZ.z * x * y;
            var r = m_ReferenceFromLocal;
            double a = r.m00 * dxX + r.m01 * dxY + r.m02 * dxZ + 1d;
            double b = r.m00 * dyX + r.m01 * dyY + r.m02 * dyZ;
            double c = r.m00 * dzX + r.m01 * dzY + r.m02 * dzZ;
            double d = r.m10 * dxX + r.m11 * dxY + r.m12 * dxZ;
            double e = r.m10 * dyX + r.m11 * dyY + r.m12 * dyZ + 1d;
            double f = r.m10 * dzX + r.m11 * dzY + r.m12 * dzZ;
            double g = r.m20 * dxX + r.m21 * dxY + r.m22 * dxZ;
            double h = r.m20 * dyX + r.m21 * dyY + r.m22 * dyZ;
            double i = r.m20 * dzX + r.m21 * dzY + r.m22 * dzZ + 1d;
            return a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        }

        private const int kCageValidationDepth = 12;
        private const int kCageValidationCells = 255;

        private bool ValidCage()
        {
            // The determinant is quadratic in each reference coordinate. Its
            // 27 Bernstein coefficients bound the whole cage, not just samples.
            var coefficients = new double[27];
            for (int z = 0; z < 3; z++)
            for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
            {
                double value = NormalizedDeterminant(x * 0.5d, y * 0.5d, z * 0.5d);
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0d) return false;
                coefficients[x + 3 * y + 9 * z] = value;
            }

            for (int stride = 1; stride <= 9; stride *= 3)
            {
                for (int i = 0; i < 27; i++)
                {
                    if ((i / stride) % 3 != 0) continue;
                    coefficients[i + stride] = 2d * coefficients[i + stride] - 0.5d * (coefficients[i] + coefficients[i + 2 * stride]);
                }
            }

            if (PositiveCageCell(coefficients, 0)) return true;

            // Ambiguous bounds get a bounded refinement. Reuse one small work
            // buffer across both branches; this never visits mesh vertices.
            var work = new double[27 + 54 * kCageValidationDepth];
            Array.Copy(coefficients, work, 27);
            int remaining = kCageValidationCells;
            return ValidateCageCell(work, 0, 0, ref remaining);
        }

        private static bool PositiveCageCell(double[] coefficients, int offset)
        {
            for (int i = 0; i < 27; i++)
            {
                if (coefficients[offset + i] <= 0d) return false;
            }

            return true;
        }

        private static bool ValidateCageCell(double[] work, int offset, int depth, ref int remaining)
        {
            if (--remaining < 0) return false;
            if (PositiveCageCell(work, offset)) return true;
            // End coefficients are actual corner values of this smaller box.
            for (int z = 0; z <= 2; z += 2)
            for (int y = 0; y <= 2; y += 2)
            for (int x = 0; x <= 2; x += 2)
            {
                if (work[offset + x + 3 * y + 9 * z] <= 0d) return false;
            }

            if (depth == kCageValidationDepth) return false;
            int stride = depth % 3 == 0 ? 1 : depth % 3 == 1 ? 3 : 9;
            int left = 27 + depth * 54, right = left + 27;
            for (int i = 0; i < 27; i++)
            {
                if ((i / stride) % 3 != 0) continue;
                double a = work[offset + i], b = work[offset + i + stride], c = work[offset + i + 2 * stride];
                double middle = (a + 2d * b + c) * 0.25d;
                work[left + i] = a;
                work[left + i + stride] = (a + b) * 0.5d;
                work[left + i + 2 * stride] = middle;
                work[right + i] = middle;
                work[right + i + stride] = (b + c) * 0.5d;
                work[right + i + 2 * stride] = c;
            }

            return ValidateCageCell(work, left, depth + 1, ref remaining) && ValidateCageCell(work, right, depth + 1, ref remaining);
        }

        internal bool Positions(DeformationBaseline baseline, Vector3[] output, out LocalizedText reason)
        {
            reason = LocalizedText.Empty;
            if (output == null || output.Length != baseline.Positions.Length)
            {
                reason = LocalizedText.For("PO2.Object.Deformation.Error.PositionStreamMismatch");
                return false;
            }

            for (int i = 0; i < output.Length; i++)
            {
                output[i] = Position(baseline.Positions[i]);
                if (!Finite(output[i]))
                {
                    reason = LocalizedText.For("PO2.Object.Deformation.Error.NonFinitePosition");
                    return false;
                }
            }

            return true;
        }

        internal bool Directions(DeformationBaseline baseline, Vector3[] normals, Vector4[] tangents, out LocalizedText reason)
        {
            reason = LocalizedText.Empty;
            if ((baseline.Normals?.Length ?? -1) != (normals?.Length ?? -1) || (baseline.Tangents?.Length ?? -1) != (tangents?.Length ?? -1))
            {
                reason = LocalizedText.For("PO2.Object.Deformation.Error.DirectionStreamMismatch");
                return false;
            }

            if (Identity)
            {
                if (normals != null) Array.Copy(baseline.Normals, normals, normals.Length);
                if (tangents != null) Array.Copy(baseline.Tangents, tangents, tangents.Length);
                return true;
            }

            if (normals == null && tangents == null) return true;
            for (int i = 0; i < baseline.Positions.Length; i++)
            {
                Matrix4x4 j = Jacobian(baseline.Positions[i]);
                Vector3 normal = Vector3.zero;
                if (normals != null)
                {
                    Vector3 x = new Vector3(j.m00, j.m10, j.m20);
                    Vector3 y = new Vector3(j.m01, j.m11, j.m21);
                    Vector3 z = new Vector3(j.m02, j.m12, j.m22);
                    Vector3 n = baseline.Normals[i];
                    normal = Vector3.Cross(y, z) * n.x + Vector3.Cross(z, x) * n.y + Vector3.Cross(x, y) * n.z;
                    if (!Finite(normal))
                    {
                        reason = LocalizedText.For("PO2.Object.Deformation.Error.InvalidNormal");
                        return false;
                    }

                    normal = NormalizeDirection(normal);
                    normals[i] = normal;
                }

                if (tangents != null)
                {
                    Vector4 source = baseline.Tangents[i];
                    Vector3 tangent = j.MultiplyVector(new Vector3(source.x, source.y, source.z));
                    tangent -= normal * Vector3.Dot(normal, tangent);
                    if (!Finite(tangent))
                    {
                        reason = LocalizedText.For("PO2.Object.Deformation.Error.InvalidTangent");
                        return false;
                    }

                    tangent = NormalizeDirection(tangent);
                    tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, source.w);
                }
            }

            return true;
        }

        private static Vector3 NormalizeDirection(Vector3 value)
        {
            float largest = Mathf.Max(Mathf.Abs(value.x), Mathf.Max(Mathf.Abs(value.y), Mathf.Abs(value.z)));
            return largest == 0f ? Vector3.zero : new Vector3(value.x / largest, value.y / largest, value.z / largest).normalized;
        }

        internal static void Column(ref Matrix4x4 matrix, int column, Vector3 value)
        {
            matrix[0, column] = value.x;
            matrix[1, column] = value.y;
            matrix[2, column] = value.z;
        }

        internal static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        internal static bool Finite(Matrix4x4 value)
        {
            for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                if (!Finite(value[r, c]))
                    return false;
            return true;
        }
    }
}