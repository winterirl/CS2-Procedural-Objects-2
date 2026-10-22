using System;
using System.Runtime.InteropServices;
using Unity.Burst;

namespace PO2.Geometry
{
    [BurstCompile]
    internal static unsafe class VertexEditPreparationKernels
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Context
        {
            internal int* Selected;
            internal int* Affected;
            internal int* Triangles;
            internal int* SelectedMap;
            internal int* AffectedMap;
            internal int* Records;
            internal int* SelectedNormals;
            internal int* SelectedCorners;
            internal int* Words;
            internal int VertexCount, SelectedCount, AffectedCount, TriangleCount, RecordCount, Error;
            internal int Operation, WordCount, VisitedTriangles;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Kernel(ref Context context);

        private static Kernel s_Run = Run;
        private static bool s_Initialized;
        internal static bool UsesBurst { get; private set; }
        internal static string Failure { get; private set; } = string.Empty;

        internal static void Initialize()
        {
            if (s_Initialized) return;
            s_Initialized = true;
            try
            {
                if (!BurstCompiler.IsEnabled)
                {
                    Failure = "Burst is disabled; edit preparation uses managed kernels.";
                    return;
                }

                s_Run = BurstCompiler.CompileFunctionPointer<Kernel>(Run).Invoke;
                UsesBurst = true;
            }
            catch (Exception error)
            {
                Failure = "Edit preparation Burst initialization failed: " + error.Message;
            }
        }

        internal static void Execute(ref Context c)
        {
            s_Run(ref c);
            if (c.Error != 0) throw new InvalidOperationException("Invalid vertex edit membership or triangle stream.");
        }

        // The small path needs no source-sized scratch. Larger selections use
        // a bitset so sorting does not dominate a dense mouse-down capture.
        internal static int SortUnique(int[] values, int vertexCount)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.Length < 128 || vertexCount > 32 * 1024 * 1024)
            {
                Array.Sort(values);
                int unique = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    if ((uint)values[i] >= (uint)vertexCount) throw new ArgumentOutOfRangeException(nameof(values));
                    if (unique == 0 || values[i] != values[unique - 1]) values[unique++] = values[i];
                }

                return unique;
            }

            var words = new int[(vertexCount + 31) / 32];
            var c = new Context { Operation = 4, VertexCount = vertexCount, SelectedCount = values.Length, WordCount = words.Length };
            fixed (int* v = values, w = words)
            {
                c.Selected = v;
                c.Words = w;
                Execute(ref c);
            }

            return c.AffectedCount;
        }

        internal static long CoverageBytes(int vertices) => checked(8L * ((vertices + 31L) / 32));

        internal static bool Covers(MeshData mesh, int[] selected, int[] affected, int[] records)
        {
            if (mesh == null || selected == null || affected == null || records == null || mesh.Triangles.Length % 3 != 0 || records.Length % 4 != 0) throw new ArgumentException("Invalid sparse coverage streams.");
            int words = checked((int)((mesh.Vertices.Length + 31L) / 32));
            var selectedWords = new int[words];
            var affectedWords = new int[words];
            var c = new Context
            {
                Operation = 5, VertexCount = mesh.Vertices.Length, SelectedCount = selected.Length,
                AffectedCount = affected.Length, TriangleCount = mesh.Triangles.Length, RecordCount = records.Length / 4
            };
            fixed (int* s = selected, a = affected, t = mesh.Triangles, r = records, sw = selectedWords, aw = affectedWords)
            {
                c.Selected = s;
                c.Affected = a;
                c.Triangles = t;
                c.Records = r;
                c.SelectedMap = sw;
                c.AffectedMap = aw;
                try
                {
                    Execute(ref c);
                }
                finally
                {
                }
            }

            return c.RecordCount >= 0;
        }

        private static bool Contains(int* words, int value) => (words[value >> 5] & (1 << (value & 31))) != 0;

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private static void Run(ref Context c)
        {
            if (c.Operation == 4)
            {
                for (int i = 0; i < c.SelectedCount; i++)
                {
                    int v = c.Selected[i];
                    if ((uint)v >= (uint)c.VertexCount)
                    {
                        c.Error = 1;
                        return;
                    }

                    c.Words[v >> 5] |= 1 << (v & 31);
                }

                int next = 0;
                for (int i = 0; i < c.WordCount; i++)
                {
                    uint word = (uint)c.Words[i];
                    for (int bit = 0; word != 0; bit++, word >>= 1)
                        if ((word & 1) != 0)
                            c.Selected[next++] = i * 32 + bit;
                }

                c.AffectedCount = next;
                return;
            }

            if (c.Operation == 5)
            {
                // Incidence only finds candidates. Keep checking the entire stream,
                // but use bit tests instead of searching tiny lists for every corner.
                for (int i = 0; i < c.SelectedCount; i++)
                {
                    int v = c.Selected[i];
                    if ((uint)v >= (uint)c.VertexCount)
                    {
                        c.Error = 1;
                        return;
                    }

                    c.SelectedMap[v >> 5] |= 1 << (v & 31);
                }

                for (int i = 0; i < c.AffectedCount; i++)
                {
                    int v = c.Affected[i];
                    if ((uint)v >= (uint)c.VertexCount)
                    {
                        c.Error = 1;
                        return;
                    }

                    c.AffectedMap[v >> 5] |= 1 << (v & 31);
                }

                int next = 0;
                for (int i = 0; i < c.TriangleCount; i += 3)
                {
                    int va = c.Triangles[i], vb = c.Triangles[i + 1], vc = c.Triangles[i + 2];
                    if ((uint)va >= (uint)c.VertexCount || (uint)vb >= (uint)c.VertexCount || (uint)vc >= (uint)c.VertexCount)
                    {
                        c.Error = 1;
                        return;
                    }

                    bool a = Contains(c.AffectedMap, va), b = Contains(c.AffectedMap, vb), d = Contains(c.AffectedMap, vc);
                    if (Contains(c.SelectedMap, va) || Contains(c.SelectedMap, vb) || Contains(c.SelectedMap, vc))
                    {
                        if (!a || !b || !d)
                        {
                            c.RecordCount = -1;
                            return;
                        }
                    }

                    if (!a && !b && !d) continue;
                    if (next >= c.RecordCount || c.Records[next * 4] != i)
                    {
                        c.RecordCount = -1;
                        return;
                    }

                    next++;
                }

                if (next != c.RecordCount) c.RecordCount = -1;
                return;
            }

            if (c.Operation == 0)
            {
                for (int i = 0; i < c.SelectedCount; i++)
                {
                    int v = c.Selected[i];
                    if ((uint)v >= (uint)c.VertexCount || c.SelectedMap[v] != 0)
                    {
                        c.Error = 1;
                        return;
                    }

                    c.SelectedMap[v] = i + 1;
                    c.AffectedMap[v] = 1;
                }

                for (int i = 0; i < c.TriangleCount; i += 3)
                {
                    int a = c.Triangles[i], b = c.Triangles[i + 1], d = c.Triangles[i + 2];
                    if ((uint)a >= (uint)c.VertexCount || (uint)b >= (uint)c.VertexCount || (uint)d >= (uint)c.VertexCount)
                    {
                        c.Error = 1;
                        return;
                    }

                    if (c.SelectedMap[a] != 0 || c.SelectedMap[b] != 0 || c.SelectedMap[d] != 0)
                        c.AffectedMap[a] = c.AffectedMap[b] = c.AffectedMap[d] = 1;
                }

                int count = 0;
                for (int v = 0; v < c.VertexCount; v++)
                    if (c.AffectedMap[v] != 0)
                        c.AffectedMap[v] = ++count;
                c.AffectedCount = count;
                return;
            }

            if (c.Operation == 1)
            {
                for (int v = 0; v < c.VertexCount; v++)
                    if (c.AffectedMap[v] != 0)
                        c.Affected[c.AffectedMap[v] - 1] = v;
                c.RecordCount = 0;
                for (int i = 0; i < c.TriangleCount; i += 3)
                    if (c.AffectedMap[c.Triangles[i]] != 0 || c.AffectedMap[c.Triangles[i + 1]] != 0 || c.AffectedMap[c.Triangles[i + 2]] != 0)
                        c.RecordCount++;
                return;
            }

            if (c.Operation == 2)
            {
                for (int i = 0; i < c.SelectedCount; i++) c.SelectedNormals[i] = c.AffectedMap[c.Selected[i]] - 1;
                int next = 0;
                for (int i = 0; i < c.TriangleCount; i += 3)
                {
                    int a = c.Triangles[i], b = c.Triangles[i + 1], d = c.Triangles[i + 2];
                    int sa = c.AffectedMap[a] - 1, sb = c.AffectedMap[b] - 1, sc = c.AffectedMap[d] - 1;
                    if (sa < 0 && sb < 0 && sc < 0) continue;
                    c.Records[next * 4] = i;
                    c.Records[next * 4 + 1] = sa;
                    c.Records[next * 4 + 2] = sb;
                    c.Records[next * 4 + 3] = sc;
                    c.SelectedCorners[next * 3] = c.SelectedMap[a] - 1;
                    c.SelectedCorners[next * 3 + 1] = c.SelectedMap[b] - 1;
                    c.SelectedCorners[next * 3 + 2] = c.SelectedMap[d] - 1;
                    next++;
                }

                return;
            }

            c.Error = 1;
        }
    }
}