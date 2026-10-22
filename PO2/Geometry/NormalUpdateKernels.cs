using System;
using System.Runtime.InteropServices;
using Unity.Burst;

namespace PO2.Geometry
{
    // These calls finish before the pinned arrays leave their fixed scope.
    [BurstCompile]
    internal static unsafe class NormalUpdateKernels
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            internal float X, Y, Z;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Context
        {
            internal Point* Positions;
            internal Point* Normals;
            internal Point* StartPositions;
            internal Point* StartNormals;
            internal Point* Sums;
            internal int* Selected;
            internal int* Affected;
            internal int* Triangles;
            internal int* Records;
            internal int* SelectedNormals;
            internal int* SelectedCorners;
            internal byte* Moved;
            internal byte* Active;
            internal int SelectedCount, AffectedCount, RecordCount, VertexCount;
            internal int MaskChanged, MovedCount, ActiveCount, Evaluated, Error;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Kernel(ref Context context);

        private static readonly object s_Lock = new object();
        private static bool s_Initialized;
        private static Kernel s_Compare = Compare, s_Neighborhood = Neighborhood;
        private static Kernel s_Accumulate = Accumulate, s_Finish = Finish, s_Publish = Publish;
        internal static bool UsesBurst { get; private set; }
        internal static string Failure { get; private set; } = string.Empty;

        internal static void Initialize()
        {
            lock (s_Lock)
            {
                if (s_Initialized) return;
                s_Initialized = true;
                try
                {
                    if (BurstCompiler.IsEnabled)
                    {
                        var compare = BurstCompiler.CompileFunctionPointer<Kernel>(Compare).Invoke;
                        var neighborhood = BurstCompiler.CompileFunctionPointer<Kernel>(Neighborhood).Invoke;
                        var accumulate = BurstCompiler.CompileFunctionPointer<Kernel>(Accumulate).Invoke;
                        var finish = BurstCompiler.CompileFunctionPointer<Kernel>(Finish).Invoke;
                        var publish = BurstCompiler.CompileFunctionPointer<Kernel>(Publish).Invoke;
                        s_Compare = compare;
                        s_Neighborhood = neighborhood;
                        s_Accumulate = accumulate;
                        s_Finish = finish;
                        s_Publish = publish;
                        UsesBurst = true;
                    }
                    else Failure = "Burst is disabled; normal kernels use their managed entry points.";
                }
                catch (Exception error)
                {
                    Failure = "Normal Burst initialization failed: " + error.Message;
                }
            }
        }

        internal static void ComparePositions(ref Context c)
        {
            s_Compare(ref c);
        }

        internal static void BuildNeighborhood(ref Context c)
        {
            s_Neighborhood(ref c);
        }

        internal static void SumTriangles(ref Context c)
        {
            s_Accumulate(ref c);
        }

        internal static void FinishNormals(ref Context c)
        {
            s_Finish(ref c);
        }

        internal static void PublishNormals(ref Context c)
        {
            s_Publish(ref c);
        }

        internal static bool Same(Point a, Point b)
        {
            int* left = (int*)&a;
            int* right = (int*)&b;
            return left[0] == right[0] && left[1] == right[1] && left[2] == right[2];
        }

        private static bool Finite(Point p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private static void Compare(ref Context c)
        {
            for (int i = 0; i < c.SelectedCount; i++)
            {
                Point current = c.Positions[c.Selected[i]], start = c.StartPositions[i];
                if (!Finite(current) || !Finite(start))
                {
                    c.Error = 1;
                    return;
                }

                byte moved = Same(current, start) ? (byte)0 : (byte)1;
                if (moved != c.Moved[i]) c.MaskChanged = 1;
                c.Moved[i] = moved;
                c.MovedCount += moved;
            }
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private static void Neighborhood(ref Context c)
        {
            for (int i = 0; i < c.AffectedCount; i++) c.Active[i] = 0;
            for (int i = 0; i < c.SelectedCount; i++)
            {
                int slot = c.SelectedNormals[i];
                if (c.Moved[i] != 0 && slot >= 0) c.Active[slot] = 1;
            }

            for (int i = 0; i < c.RecordCount; i++)
            {
                int corner = i * 3;
                int a = c.SelectedCorners[corner], b = c.SelectedCorners[corner + 1], d = c.SelectedCorners[corner + 2];
                if ((a < 0 || c.Moved[a] == 0) && (b < 0 || c.Moved[b] == 0) && (d < 0 || c.Moved[d] == 0)) continue;
                for (int k = 1; k <= 3; k++)
                {
                    int slot = c.Records[i * 4 + k];
                    if (slot >= 0) c.Active[slot] = 1;
                }
            }

            c.ActiveCount = 0;
            for (int i = 0; i < c.AffectedCount; i++) c.ActiveCount += c.Active[i];
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private static void Accumulate(ref Context c)
        {
            for (int i = 0; i < c.AffectedCount; i++) c.Sums[i] = default;
            for (int i = 0; i < c.RecordCount; i++)
            {
                int record = i * 4;
                int sa = c.Records[record + 1], sb = c.Records[record + 2], sc = c.Records[record + 3];
                if ((sa < 0 || c.Active[sa] == 0) && (sb < 0 || c.Active[sb] == 0) && (sc < 0 || c.Active[sc] == 0)) continue;
                int offset = c.Records[record];
                int ia = c.Triangles[offset], ib = c.Triangles[offset + 1], ic = c.Triangles[offset + 2];
                if ((uint)ia >= (uint)c.VertexCount || (uint)ib >= (uint)c.VertexCount || (uint)ic >= (uint)c.VertexCount)
                {
                    c.Error = 3;
                    return;
                }

                Point a = c.Positions[ia], b = c.Positions[ib], d = c.Positions[ic];
                if (!Finite(a) || !Finite(b) || !Finite(d))
                {
                    c.Error = 1;
                    return;
                }

                float bx = b.X - a.X, by = b.Y - a.Y, bz = b.Z - a.Z;
                float dx = d.X - a.X, dy = d.Y - a.Y, dz = d.Z - a.Z;
                Point normal = new Point { X = by * dz - bz * dy, Y = bz * dx - bx * dz, Z = bx * dy - by * dx };
                if (!Finite(normal))
                {
                    c.Error = 2;
                    return;
                }

                for (int k = 1; k <= 3; k++)
                {
                    int slot = c.Records[record + k];
                    if (slot < 0 || c.Active[slot] == 0) continue;
                    Point sum = c.Sums[slot];
                    sum.X += normal.X;
                    sum.Y += normal.Y;
                    sum.Z += normal.Z;
                    c.Sums[slot] = sum;
                }

                c.Evaluated++;
            }
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private static void Finish(ref Context c)
        {
            for (int i = 0; i < c.AffectedCount; i++)
            {
                Point result;
                if (c.Active[i] == 0) result = c.StartNormals[i];
                else
                {
                    Point sum = c.Sums[i];
                    if (!Finite(sum))
                    {
                        c.Error = 2;
                        return;
                    }

                    float squared = sum.X * sum.X + sum.Y * sum.Y + sum.Z * sum.Z;
                    // Match Vector3's zero-sum fallback, then its normalization
                    // cutoff and component division. Tiny nonzero sums become zero.
                    if (squared > 0f)
                    {
                        float magnitude = (float)Math.Sqrt(squared);
                        result = magnitude > 0.00001f ? new Point { X = sum.X / magnitude, Y = sum.Y / magnitude, Z = sum.Z / magnitude } : default;
                    }
                    else result = new Point { Y = 1f };
                }

                if (!Finite(result))
                {
                    c.Error = 2;
                    return;
                }

                c.Sums[i] = result;
            }
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private static void Publish(ref Context c)
        {
            for (int i = 0; i < c.AffectedCount; i++) c.Normals[c.Affected[i]] = c.Sums[i];
        }
    }
}