using System;
using UnityEngine;
using Point = PO2.Geometry.Editing.DissolvePolygon.Point;

namespace PO2.Geometry.Editing
{
    internal readonly struct SubdivisionBasis
    {
        private readonly Vector3 m_Origin;
        private readonly Point m_U, m_V, m_Normal;
        private readonly double m_UU, m_UV, m_VV, m_Denominator;
        internal readonly double ExtentSquared;

        internal SubdivisionBasis(Vector3 a, Vector3 b, Vector3 c)
        {
            m_Origin = a;
            m_U = Point.Local(b, a);
            m_V = Point.Local(c, a);
            m_UU = Dot(m_U, m_U);
            m_UV = Dot(m_U, m_V);
            m_VV = Dot(m_V, m_V);
            m_Normal = Point.Cross(m_U, m_V);
            m_Denominator = m_Normal.LengthSquared;
            ExtentSquared = Math.Max(m_UU, m_VV);
        }

        internal bool Valid => !double.IsNaN(m_Denominator) && !double.IsInfinity(m_Denominator) && ExtentSquared > 0 && m_Denominator > ExtentSquared * ExtentSquared * 1e-20;

        internal bool Weights(Vector3 p, out double b, out double c)
        {
            var q = Point.Local(p, m_Origin);
            double qu = Dot(q, m_U), qv = Dot(q, m_V);
            b = (qu * m_VV - qv * m_UV) / m_Denominator;
            c = (qv * m_UU - qu * m_UV) / m_Denominator;
            double height = Dot(q, m_Normal);
            return Valid && height * height <= m_Denominator * ExtentSquared * 1e-12 && !double.IsNaN(b) && !double.IsInfinity(b) && !double.IsNaN(c) && !double.IsInfinity(c);
        }

        internal Vector3 UnitNormal
        {
            get
            {
                double length = Math.Sqrt(m_Denominator);
                return new Vector3((float)(m_Normal.X / length), (float)(m_Normal.Y / length), (float)(m_Normal.Z / length));
            }
        }

        internal static double Dot(Point a, Point b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        internal static double Value(double a, double b, double c, double u, double v) => a + (b - a) * u + (c - a) * v;

        internal static bool Same(double a, double b, double c, double value, double u, double v, double minimum = 1e-6) =>
            Math.Abs(Value(a, b, c, u, v) - value) <= Math.Max(minimum, Math.Max(Math.Abs(b - a), Math.Abs(c - a)) * 1e-5);

        internal static bool Same(Vector3 a, Vector3 b, Vector3 c, Vector3 p, double u, double v) =>
            Same(a.x, b.x, c.x, p.x, u, v) && Same(a.y, b.y, c.y, p.y, u, v) && Same(a.z, b.z, c.z, p.z, u, v);

        internal static bool Same(Vector4 a, Vector4 b, Vector4 c, Vector4 p, double u, double v) =>
            Same(a.x, b.x, c.x, p.x, u, v) && Same(a.y, b.y, c.y, p.y, u, v) && Same(a.z, b.z, c.z, p.z, u, v) && Same(a.w, b.w, c.w, p.w, u, v);

        internal static unsafe bool SameBits(Vector4 a, Vector4 b)
        {
            uint* left = (uint*)&a;
            uint* right = (uint*)&b;
            return left[0] == right[0] && left[1] == right[1] && left[2] == right[2] && left[3] == right[3];
        }
    }
}