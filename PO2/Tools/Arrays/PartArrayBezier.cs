using UnityEngine;

namespace PO2.Tools.Arrays
{
    internal enum PartArrayPathKind
    {
        Line,
        Curve
    }

    internal enum PartArrayAlignment
    {
        EndToEnd,
        FixedOrientation
    }

    internal readonly struct PartArrayBezier
    {
        internal readonly Vector3 Start, Control1, Control2, End;

        internal PartArrayBezier(Vector3 start, Vector3 control1, Vector3 control2, Vector3 end)
        {
            Start = start;
            Control1 = control1;
            Control2 = control2;
            End = end;
        }

        internal static PartArrayBezier Straight(Vector3 start, Vector3 end) =>
            new PartArrayBezier(start, start + (end - start) / 3, start + (end - start) * (2f / 3), end);

        internal bool IsFinite => PartArrayLayout.Finite(Start) && PartArrayLayout.Finite(Control1) && PartArrayLayout.Finite(Control2) && PartArrayLayout.Finite(End);
    }

    internal readonly struct ArrayBezierPiece
    {
        internal readonly ArrayPoint A, B, C, D;
        internal readonly double T0, T1;

        internal ArrayBezierPiece(ArrayPoint a, ArrayPoint b, ArrayPoint c, ArrayPoint d, double t0, double t1)
        {
            A = a;
            B = b;
            C = c;
            D = d;
            T0 = t0;
            T1 = t1;
        }

        internal ArrayPoint Point(double t)
        {
            double u = 1 - t;
            return A * (u * u * u) + B * (3 * u * u * t) + C * (3 * u * t * t) + D * (t * t * t);
        }

        internal ArrayPoint Derivative(double t)
        {
            double u = 1 - t;
            return (B - A) * (3 * u * u) + (C - B) * (6 * u * t) + (D - C) * (3 * t * t);
        }

        internal void Split(double t, out ArrayBezierPiece left, out ArrayBezierPiece right)
        {
            var ab = ArrayPoint.Lerp(A, B, t);
            var bc = ArrayPoint.Lerp(B, C, t);
            var cd = ArrayPoint.Lerp(C, D, t);
            var abc = ArrayPoint.Lerp(ab, bc, t);
            var bcd = ArrayPoint.Lerp(bc, cd, t);
            var p = ArrayPoint.Lerp(abc, bcd, t);
            double middle = T0 + (T1 - T0) * t;
            left = new ArrayBezierPiece(A, ab, abc, p, T0, middle);
            right = new ArrayBezierPiece(p, bcd, cd, D, middle, T1);
        }
    }
}