using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using UnityEngine;

namespace PO2.Tools.Snapping
{
    // Calls only read detached arrays pinned by the worker. Returning every
    // bounded chunk lets cancellation stop a large partition too.
    [BurstCompile]
    internal static unsafe class TranslationSnapBuildKernels
    {
        internal struct BuildPoint
        {
            internal Vector3 Minimum, Maximum, Center;
        }

        internal struct Context
        {
            internal BuildPoint* Points;
            internal int* Order;
            internal int Start, Count, Cursor, Stage, Axis, Median;
            internal int Left, Right, Pivot, Scan, Store, PartitionActive;
            internal int HeapStart, HeapSize, HeapNext, HeapPhase;
            internal float PivotValue;
            internal Vector3 Minimum, Maximum, CenterMinimum, CenterMaximum;
            internal long BoundsVisits, PartitionVisits, Partitions, Fallbacks, SelectionVisits;
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
                if (BurstCompiler.IsEnabled)
                {
                    s_Run = BurstCompiler.CompileFunctionPointer<Kernel>(Run).Invoke;
                    UsesBurst = true;
                }
                else Failure = "Burst is disabled; snapping uses the managed kernel.";
            }
            catch (Exception error)
            {
                Failure = error.Message;
            }
        }

        internal static void Advance(ref Context c) => s_Run(ref c);

        private static float Axis(Vector3 point, int axis) => axis == 0 ? point.x : axis == 1 ? point.y : point.z;

        private static bool Less(ref Context c, int a, int b)
        {
            float left = Axis(c.Points[a].Center, c.Axis), right = Axis(c.Points[b].Center, c.Axis);
            return left < right || (left == right && a < b);
        }

        private static void Swap(ref Context c, int a, int b)
        {
            int value = c.Order[a];
            c.Order[a] = c.Order[b];
            c.Order[b] = value;
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private static void Run(ref Context c)
        {
            int budget = 65536;
            if (c.Stage == 0)
            {
                while (c.Cursor < c.Count && budget-- > 0)
                {
                    BuildPoint point = c.Points[c.Order[c.Start + c.Cursor++]];
                    c.Minimum = Vector3.Min(c.Minimum, point.Minimum);
                    c.Maximum = Vector3.Max(c.Maximum, point.Maximum);
                    c.CenterMinimum = Vector3.Min(c.CenterMinimum, point.Center);
                    c.CenterMaximum = Vector3.Max(c.CenterMaximum, point.Center);
                    c.BoundsVisits++;
                }

                if (c.Cursor < c.Count) return;
                if (c.Count <= 8)
                {
                    c.Stage = 4;
                    return;
                }

                Vector3 size = c.CenterMaximum - c.CenterMinimum;
                c.Axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                c.Left = c.Start;
                c.Right = c.Start + c.Count - 1;
                c.Median = c.Start + c.Count / 2;
                c.Stage = 1;
            }

            while (c.Stage == 1 && budget > 0)
            {
                if (c.Left >= c.Right)
                {
                    c.Stage = 4;
                    return;
                }

                if (c.PartitionActive == 0)
                {
                    if (c.SelectionVisits > (long)c.Count * 8)
                    {
                        // Sort just the unresolved range when quickselect stops
                        // making useful progress. Heap sort needs no scratch.
                        c.HeapStart = c.Left;
                        c.HeapSize = c.Right - c.Left + 1;
                        c.HeapNext = c.HeapSize / 2 - 1;
                        c.HeapPhase = 0;
                        c.Stage = 2;
                        c.Fallbacks++;
                        break;
                    }

                    int a = c.Left, b = c.Left + (c.Right - c.Left) / 2, d = c.Right;
                    if (Less(ref c, c.Order[b], c.Order[a]))
                    {
                        int t = a;
                        a = b;
                        b = t;
                    }

                    if (Less(ref c, c.Order[d], c.Order[b]))
                    {
                        int t = b;
                        b = d;
                        d = t;
                    }

                    if (Less(ref c, c.Order[b], c.Order[a]))
                    {
                        int t = a;
                        a = b;
                        b = t;
                    }

                    c.Pivot = c.Order[b];
                    c.PivotValue = Axis(c.Points[c.Pivot].Center, c.Axis);
                    Swap(ref c, b, c.Right);
                    c.Store = c.Scan = c.Left;
                    c.PartitionActive = 1;
                    c.Partitions++;
                }

                while (c.Scan < c.Right && budget-- > 0)
                {
                    int id = c.Order[c.Scan];
                    float value = Axis(c.Points[id].Center, c.Axis);
                    if (value < c.PivotValue || (value == c.PivotValue && id < c.Pivot)) Swap(ref c, c.Scan, c.Store++);
                    c.Scan++;
                    c.PartitionVisits++;
                    c.SelectionVisits++;
                }

                if (c.Scan < c.Right) return;
                Swap(ref c, c.Store, c.Right);
                c.PartitionActive = 0;
                if (c.Store == c.Median)
                {
                    c.Stage = 4;
                    return;
                }

                if (c.Median < c.Store) c.Right = c.Store - 1;
                else c.Left = c.Store + 1;
            }

            while (c.Stage == 2 && budget > 0)
            {
                int root;
                if (c.HeapPhase == 0)
                {
                    if (c.HeapNext < 0)
                    {
                        c.HeapPhase = 1;
                        continue;
                    }

                    root = c.HeapNext--;
                }
                else
                {
                    if (c.HeapSize <= 1)
                    {
                        c.Stage = 4;
                        return;
                    }

                    Swap(ref c, c.HeapStart, c.HeapStart + --c.HeapSize);
                    root = 0;
                }

                // One sift is at most the tree depth, so a chunk can exceed
                // its visit budget only by a few comparisons.
                while (root * 2 + 1 < c.HeapSize)
                {
                    int child = root * 2 + 1;
                    if (child + 1 < c.HeapSize && Less(ref c, c.Order[c.HeapStart + child], c.Order[c.HeapStart + child + 1])) child++;
                    c.PartitionVisits++;
                    budget--;
                    if (!Less(ref c, c.Order[c.HeapStart + root], c.Order[c.HeapStart + child])) break;
                    Swap(ref c, c.HeapStart + root, c.HeapStart + child);
                    root = child;
                }
            }
        }
    }
}