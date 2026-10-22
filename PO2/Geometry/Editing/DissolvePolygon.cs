using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace PO2.Geometry.Editing
{
    internal static class DissolvePolygon
    {
        internal const int kMaximumCorners = 256;

        internal sealed class Work
        {
            private int m_Remaining = 4000000;
            private readonly CancellationToken m_Token;

            internal Work(CancellationToken token)
            {
                m_Token = token;
            }

            internal void Step()
            {
                if ((m_Remaining & 255) == 0) m_Token.ThrowIfCancellationRequested();
                if (--m_Remaining < 0) throw new InvalidOperationException("This selection needs too much triangulation work. Dissolve fewer vertices at once.");
            }
        }

        internal readonly struct Point
        {
            internal readonly double X, Y, Z;

            internal Point(double x, double y, double z = 0)
            {
                X = x;
                Y = y;
                Z = z;
            }

            internal static Point Local(Vector3 p, Vector3 origin) => new Point((double)p.x - origin.x, (double)p.y - origin.y, (double)p.z - origin.z);
            public static Point operator -(Point a, Point b) => new Point(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Point operator /(Point a, double b) => new Point(a.X / b, a.Y / b, a.Z / b);
            internal double LengthSquared => Dot(this, this);
            internal static double Dot(Point a, Point b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            internal static Point Cross(Point a, Point b) => new Point(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
            internal bool Finite => !double.IsNaN(X + Y + Z) && !double.IsInfinity(X + Y + Z);
        }

        internal static double Orient(Point a, Point b, Point c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        private static bool OnSegment(Point a, Point b, Point p, double distance, double area) =>
            Math.Abs(Orient(a, b, p)) <= area && p.X >= Math.Min(a.X, b.X) - distance && p.X <= Math.Max(a.X, b.X) + distance && p.Y >= Math.Min(a.Y, b.Y) - distance && p.Y <= Math.Max(a.Y, b.Y) + distance;

        private static bool Intersects(Point a, Point b, Point c, Point d, double distance, double area)
        {
            double abC = Orient(a, b, c), abD = Orient(a, b, d), cdA = Orient(c, d, a), cdB = Orient(c, d, b);
            return ((abC > area && abD < -area || abC < -area && abD > area) && (cdA > area && cdB < -area || cdA < -area && cdB > area)) || OnSegment(a, b, c, distance, area) || OnSegment(a, b, d, distance, area) || OnSegment(c, d, a, distance, area) || OnSegment(c, d, b, distance, area);
        }

        private static bool InsideTriangle(Point a, Point b, Point c, Point p, double area) =>
            Orient(a, b, p) >= -area && Orient(b, c, p) >= -area && Orient(c, a, p) >= -area;

        internal static bool ContainsStrict(Point[] polygon, Point p, double distance, double area, Work work)
        {
            bool inside = false;
            for (int i = 0; i < polygon.Length; i++)
            {
                work.Step();
                Point a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                if (OnSegment(a, b, p, distance, area)) return false;
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y)) inside = !inside;
            }

            return inside;
        }

        internal static int[] Triangulate(Point[] points, double distance, double area, Work work, out bool quad)
        {
            int count = points.Length;
            if (count < 3 || count > kMaximumCorners) throw new InvalidOperationException("Dissolve requires a boundary with 3 to 256 corners.");
            for (int i = 0; i < count; i++)
            {
                if ((points[i] - points[(i + 1) % count]).LengthSquared <= distance * distance)
                    throw new InvalidOperationException("The dissolve boundary has coincident corners.");
                for (int j = i + 1; j < count; j++)
                {
                    work.Step();
                    if (j == i + 1 || i == 0 && j == count - 1) continue;
                    if (Intersects(points[i], points[(i + 1) % count], points[j], points[(j + 1) % count], distance, area))
                        throw new InvalidOperationException("The dissolve boundary crosses or touches itself.");
                }
            }

            quad = count == 4;
            for (int i = 0; i < count && quad; i++) quad &= Orient(points[i], points[(i + 1) % count], points[(i + 2) % count]) > area;
            if (quad) return new[] { 0, 1, 2, 0, 2, 3 };
            var active = new bool[count];
            var queued = new bool[count];
            var convex = new bool[count];
            var previous = new int[count];
            var next = new int[count];
            var blockers = new int[count];
            var ears = new List<int>(count * 2);
            int head = 0, remaining = count;
            for (int i = 0; i < count; i++)
            {
                active[i] = true;
                previous[i] = (i + count - 1) % count;
                next[i] = (i + 1) % count;
            }

            Action<int> refresh = b =>
            {
                int a = previous[b], c = next[b];
                blockers[b] = 0;
                convex[b] = Orient(points[a], points[b], points[c]) > area;
                if (!convex[b]) return;
                for (int p = 0; p < count; p++)
                {
                    work.Step();
                    if (active[p] && p != a && p != b && p != c && InsideTriangle(points[a], points[b], points[c], points[p], area)) blockers[b]++;
                }

                if (blockers[b] == 0 && !queued[b])
                {
                    ears.Add(b);
                    queued[b] = true;
                }
            };
            for (int i = 0; i < count; i++) refresh(i);
            var triangles = new int[(count - 2) * 3];
            int emitted = 0;
            while (remaining > 3)
            {
                int b = -1;
                while (head < ears.Count)
                {
                    int candidate = ears[head++];
                    queued[candidate] = false;
                    if (active[candidate] && convex[candidate] && blockers[candidate] == 0)
                    {
                        b = candidate;
                        break;
                    }
                }

                if (b < 0) throw new InvalidOperationException("The dissolve boundary cannot be triangulated safely.");
                int a = previous[b], c = next[b];
                triangles[emitted++] = a;
                triangles[emitted++] = b;
                triangles[emitted++] = c;
                active[b] = false;
                remaining--;
                // Only the two neighboring triangles change shape. Other ear
                // candidates can only lose this point as a blocker. Maintaining
                // their counts keeps the entire clipping pass quadratic.
                for (int i = 0; i < count; i++)
                {
                    work.Step();
                    if (!active[i] || i == a || i == c || !convex[i] || blockers[i] == 0) continue;
                    if (InsideTriangle(points[previous[i]], points[i], points[next[i]], points[b], area) && --blockers[i] == 0 && !queued[i])
                    {
                        ears.Add(i);
                        queued[i] = true;
                    }
                }

                next[a] = c;
                previous[c] = a;
                refresh(a);
                refresh(c);
            }

            int first = 0;
            while (!active[first]) first++;
            int second = next[first], third = next[second];
            if (Orient(points[first], points[second], points[third]) <= area)
                throw new InvalidOperationException("Dissolve would create a degenerate triangle.");
            triangles[emitted++] = first;
            triangles[emitted++] = second;
            triangles[emitted] = third;
            return triangles;
        }
    }
}