using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using PO2.Localization;

namespace PO2.Tools.Arrays
{
    // A finished cache is read-only and can be shared by several placement
    // requests. Building and querying it only uses value math, never scene APIs.
    internal sealed class PartArrayCurve
    {
        internal const double kDefaultTolerance = .0001;
        private const int kMaxPieces = 8192, kMaxDepth = 24, kMaxSearchNodes = 65536;
        private readonly ArrayBezierPiece[] m_Pieces;
        private readonly double[] m_Distances;
        private readonly Vector3[] m_Forwards, m_Ups;
        internal PartArrayBezier Definition { get; }
        internal double Tolerance { get; }
        internal double Length => m_Distances[m_Distances.Length - 1];
        internal int SampleCount => m_Pieces.Length + 1;

        private PartArrayCurve(PartArrayBezier definition, ArrayBezierPiece[] pieces, Vector3 up, Vector3? referenceForward, double tolerance, CancellationToken token)
        {
            Definition = definition;
            m_Pieces = pieces;
            Tolerance = tolerance;
            m_Distances = new double[pieces.Length + 1];
            m_Forwards = new Vector3[pieces.Length];
            m_Ups = new Vector3[pieces.Length];
            for (int i = 0; i < pieces.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                m_Distances[i + 1] = m_Distances[i] + ArcLength(pieces[i], 1);
                Vector3 forward = Direction(pieces[i], 0);
                if (i == 0) up = referenceForward.HasValue ? PartArrayCurveMath.TransportUp(PartArrayCurveMath.Unit(referenceForward.Value, forward), forward, up) : PartArrayCurveMath.Up(forward, up);
                else up = PartArrayCurveMath.TransportUp(m_Forwards[i - 1], forward, m_Ups[i - 1]);
                m_Forwards[i] = forward;
                m_Ups[i] = up;
            }
        }

        internal static bool TryCreate(PartArrayBezier definition, Vector3 up, CancellationToken token, out PartArrayCurve curve, out LocalizedText reason, double tolerance = kDefaultTolerance, Vector3? referenceForward = null)
        {
            curve = null;
            reason = LocalizedText.Empty;
            token.ThrowIfCancellationRequested();
            if (!definition.IsFinite || !PartArrayLayout.Finite(up) || !PartArrayCurveMath.Finite(tolerance) || tolerance <= 0 || referenceForward.HasValue && (!PartArrayLayout.Finite(referenceForward.Value) || new ArrayPoint(referenceForward.Value).Length < 1e-12))
            {
                reason = LocalizedText.For("PO2.PartArray.Curve.Error.InvalidDefinition");
                return false;
            }

            var pieces = new List<ArrayBezierPiece>(64);
            var root = new ArrayBezierPiece(new ArrayPoint(definition.Start), new ArrayPoint(definition.Control1), new ArrayPoint(definition.Control2), new ArrayPoint(definition.End), 0, 1);
            if (!Flatten(root, tolerance, 0, pieces, token))
            {
                reason = LocalizedText.For("PO2.PartArray.Curve.Error.DetailLimit");
                return false;
            }

            var built = new PartArrayCurve(definition, pieces.ToArray(), up, referenceForward, tolerance, token);
            if (!PartArrayCurveMath.Finite(built.Length) || built.Length < .000001)
            {
                reason = LocalizedText.For("PO2.PartArray.Curve.Error.ZeroLength");
                return false;
            }

            curve = built;
            return true;
        }

        private static bool Flatten(ArrayBezierPiece p, double tolerance, int depth, List<ArrayBezierPiece> result, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            double polygon = (p.B - p.A).Length + (p.C - p.B).Length + (p.D - p.C).Length;
            double chord = (p.D - p.A).Length;
            double deviation = Math.Max(DistanceToLine(p.B, p.A, p.D), DistanceToLine(p.C, p.A, p.D));
            // The length error budget is shared across the whole curve. The
            // second check keeps long, shallow bends close to their overlay.
            if (polygon - chord <= tolerance * (p.T1 - p.T0) && deviation <= tolerance)
            {
                if (result.Count >= kMaxPieces) return false;
                result.Add(p);
                return true;
            }

            if (depth >= kMaxDepth || result.Count >= kMaxPieces) return false;
            p.Split(.5, out var left, out var right);
            return Flatten(left, tolerance, depth + 1, result, token) && Flatten(right, tolerance, depth + 1, result, token);
        }

        private static double DistanceToLine(ArrayPoint p, ArrayPoint a, ArrayPoint b)
        {
            var delta = b - a;
            if (delta.Squared < 1e-24) return (p - a).Length;
            double t = Math.Max(0, Math.Min(1, ArrayPoint.Dot(p - a, delta) / delta.Squared));
            return (p - (a + delta * t)).Length;
        }

        // Five-point integration is used only inside the already-flat pieces.
        // It also handles straight curves with unevenly spaced control handles.
        private static double ArcLength(ArrayBezierPiece p, double end)
        {
            double half = end * .5, center = half;
            return half * (.5688888888888889 * p.Derivative(center).Length + .4786286704993665 * (p.Derivative(center - half * .5384693101056831).Length + p.Derivative(center + half * .5384693101056831).Length) + .2369268850561891 * (p.Derivative(center - half * .9061798459386640).Length + p.Derivative(center + half * .9061798459386640).Length));
        }

        internal ArrayPoint Point(double parameter)
        {
            parameter = ClampParameter(parameter);
            int index = PieceAt(parameter);
            var p = m_Pieces[index];
            return p.Point((parameter - p.T0) / (p.T1 - p.T0));
        }

        internal Quaternion Frame(double parameter)
        {
            parameter = ClampParameter(parameter);
            int index = PieceAt(parameter);
            var p = m_Pieces[index];
            Vector3 forward = Direction(p, (parameter - p.T0) / (p.T1 - p.T0));
            Vector3 up = PartArrayCurveMath.TransportUp(m_Forwards[index], forward, m_Ups[index]);
            return PartArrayCurveMath.Frame(forward, up);
        }

        internal Quaternion ChordFrame(double parameter, Vector3 direction)
        {
            Quaternion frame = Frame(parameter);
            Vector3 up = PartArrayCurveMath.TransportUp(frame * Vector3.forward, direction, frame * Vector3.up);
            return PartArrayCurveMath.Frame(direction, up);
        }

        internal ArrayPoint AtDistance(double distance, ref int cursor, out double parameter)
        {
            if (!PartArrayCurveMath.Finite(distance)) throw new ArgumentOutOfRangeException(nameof(distance));
            distance = Math.Max(0, Math.Min(Length, distance));
            cursor = Math.Max(0, Math.Min(m_Pieces.Length - 1, cursor));
            if (distance < m_Distances[cursor]) cursor = 0;
            while (cursor + 1 < m_Pieces.Length && distance > m_Distances[cursor + 1]) cursor++;
            var piece = m_Pieces[cursor];
            double length = m_Distances[cursor + 1] - m_Distances[cursor];
            double wanted = distance - m_Distances[cursor], low = 0, high = 1;
            double u = length > 0 ? wanted / length : 0;
            for (int i = 0; i < 40; i++)
            {
                double error = ArcLength(piece, u) - wanted;
                if (Math.Abs(error) <= Math.Min(Tolerance * .01, 1e-8)) break;
                if (error > 0) high = u;
                else low = u;
                double speed = piece.Derivative(u).Length;
                double next = speed > 1e-12 ? u - error / speed : double.NaN;
                u = next > low && next < high ? next : (low + high) * .5;
            }

            parameter = piece.T0 + (piece.T1 - piece.T0) * u;
            return piece.Point(u);
        }

        internal Vector3 SamplePosition(int index)
        {
            if (index < 0 || index > m_Pieces.Length) throw new ArgumentOutOfRangeException(nameof(index));
            return (index == m_Pieces.Length ? m_Pieces[index - 1].D : m_Pieces[index].A).Vector;
        }

        internal double MinimumGapToCurve(ArrayPoint origin, CancellationToken token)
        {
            // Each cached piece stays within Tolerance of its chord. This
            // distance plus a small margin gives a sphere containing a point
            // of the actual curve, even when the source front sits off it.
            double distance = double.MaxValue;
            for (int i = 0; i < m_Pieces.Length; i++)
            {
                if ((i & 63) == 0) token.ThrowIfCancellationRequested();
                distance = Math.Min(distance, DistanceToLine(origin, m_Pieces[i].A, m_Pieces[i].D));
            }

            return distance + Tolerance * 4;
        }

        // Search for the first outward sphere crossing, in path order. Looking
        // at control bounds also finds an exit and re-entry inside one interval.
        // We deliberately skip inward crossings behind the source's front end.
        internal bool TryChord(double after, ArrayPoint origin, double radius, CancellationToken token, out double parameter, out ArrayPoint point)
        {
            parameter = 0;
            point = default;
            if (!PartArrayCurveMath.Finite(radius) || radius <= 0 || !PartArrayCurveMath.Finite(origin.Squared))
                throw new ArgumentOutOfRangeException(nameof(radius));
            after = ClampParameter(after);
            double tolerance = Math.Min(Tolerance * .1, radius * .000001);
            int visited = 0;
            for (int i = PieceAt(after); i < m_Pieces.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var piece = m_Pieces[i];
                if (piece.T1 <= after) continue;
                if (piece.T0 < after) piece.Split((after - piece.T0) / (piece.T1 - piece.T0), out _, out piece);
                if (FindExit(piece, origin, radius, tolerance, 0, ref visited, token, out parameter))
                {
                    point = Point(parameter);
                    return true;
                }
            }

            return false;
        }

        private static bool FindExit(ArrayBezierPiece p, ArrayPoint origin, double radius, double tolerance, int depth, ref int visited, CancellationToken token, out double parameter)
        {
            parameter = 0;
            if ((++visited & 63) == 0) token.ThrowIfCancellationRequested();
            if (visited > kMaxSearchNodes) throw new LocalizedTextException(LocalizedText.For("PO2.PartArray.Curve.Error.TightFold"));
            var a = p.A - origin;
            var b = p.B - origin;
            var c = p.C - origin;
            var d = p.D - origin;
            double r2 = radius * radius;
            if (Math.Max(Math.Max(a.Squared, b.Squared), Math.Max(c.Squared, d.Squared)) < (radius - tolerance) * (radius - tolerance)) return false;
            double x = BoxDistance(a.X, b.X, c.X, d.X), y = BoxDistance(a.Y, b.Y, c.Y, d.Y), z = BoxDistance(a.Z, b.Z, c.Z, d.Z);
            if (x * x + y * y + z * z > (radius + tolerance) * (radius + tolerance)) return false;
            if (a.Squared <= r2 && d.Squared >= r2 && Outward(b - a, a, b, c, d) && Outward(c - b, a, b, c, d) && Outward(d - c, a, b, c, d))
            {
                // All derivative controls point away from all position controls.
                // Distance is increasing on this piece, so a bracketed solve is
                // safe here. Folded intervals still take the subdivision path.
                double low = 0, high = 1, u = (radius - a.Length) / Math.Max(1e-30, d.Length - a.Length);
                for (int i = 0; i < 40; i++)
                {
                    var value = p.Point(u) - origin;
                    double error = value.Squared - r2;
                    if (Math.Abs(value.Length - radius) <= tolerance * .1)
                    {
                        parameter = p.T0 + (p.T1 - p.T0) * u;
                        return true;
                    }

                    if (error > 0) high = u;
                    else low = u;
                    double derivative = 2 * ArrayPoint.Dot(value, p.Derivative(u));
                    double next = derivative > 1e-30 ? u - error / derivative : double.NaN;
                    u = next > low && next < high ? next : (low + high) * .5;
                }
                // Extremely uneven handles can defeat the fast solve. Keep the
                // original bracket and let subdivision finish it instead.
            }

            double size = (p.B - p.A).Length + (p.C - p.B).Length + (p.D - p.C).Length;
            if (size <= tolerance || depth >= 40 || p.T1 - p.T0 < 1e-14)
            {
                var delta = d - a;
                double aa = delta.Squared, bb = 2 * ArrayPoint.Dot(a, delta), cc = a.Squared - r2;
                double discriminant = bb * bb - 4 * aa * cc;
                double u = aa > 1e-30 && discriminant >= 0 ? (-bb + Math.Sqrt(discriminant)) / (2 * aa) : .5;
                u = Math.Max(0, Math.Min(1, u));
                var value = p.Point(u) - origin;
                if (Math.Abs(value.Length - radius) <= tolerance * 2 && ArrayPoint.Dot(value, p.Derivative(u)) >= 0)
                {
                    parameter = p.T0 + (p.T1 - p.T0) * u;
                    return true;
                }

                if (depth >= 40 && size > tolerance) throw new LocalizedTextException(LocalizedText.For("PO2.PartArray.Curve.Error.NoConvergence"));
                return false;
            }

            p.Split(.5, out var left, out var right);
            return FindExit(left, origin, radius, tolerance, depth + 1, ref visited, token, out parameter) || FindExit(right, origin, radius, tolerance, depth + 1, ref visited, token, out parameter);
        }

        private static double BoxDistance(double a, double b, double c, double d)
        {
            double min = Math.Min(Math.Min(a, b), Math.Min(c, d)), max = Math.Max(Math.Max(a, b), Math.Max(c, d));
            return min > 0 ? min : max < 0 ? -max : 0;
        }

        private static bool Outward(ArrayPoint derivative, ArrayPoint a, ArrayPoint b, ArrayPoint c, ArrayPoint d) =>
            ArrayPoint.Dot(derivative, a) >= 0 && ArrayPoint.Dot(derivative, b) >= 0 && ArrayPoint.Dot(derivative, c) >= 0 && ArrayPoint.Dot(derivative, d) >= 0;

        private static Vector3 Direction(ArrayBezierPiece p, double u)
        {
            var derivative = p.Derivative(u);
            if (derivative.Length < 1e-14)
            {
                derivative = u < .5 ? p.C - p.A : p.D - p.B;
                if (derivative.Length < 1e-14) derivative = p.D - p.A;
            }

            return PartArrayCurveMath.Unit(derivative.Vector, Vector3.forward);
        }

        private static double ClampParameter(double parameter)
        {
            if (!PartArrayCurveMath.Finite(parameter)) throw new ArgumentOutOfRangeException(nameof(parameter));
            return Math.Max(0, Math.Min(1, parameter));
        }

        private int PieceAt(double parameter)
        {
            int low = 0, high = m_Pieces.Length - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (m_Pieces[middle].T1 < parameter) low = middle + 1;
                else high = middle;
            }

            return low;
        }
    }
}