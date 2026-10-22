using System;
using System.Runtime.InteropServices;
using Unity.Burst;

namespace PO2.Tools.MeshEdit
{
    // Integer-only work. Arrays are pinned only for the synchronous call.
    [BurstCompile]
    internal static unsafe class MeshDependencyKernels
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Context
        {
            internal int* Input;
            internal int* Output;
            internal int* Offsets;
            internal int* Values;
            internal byte* Marked;
            internal int Operation, InputCount, OutputCount, Limit, Offset, Capacity, Error;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Kernel(ref Context c);

        private static Kernel s_Run = Execute;
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
                    s_Run = BurstCompiler.CompileFunctionPointer<Kernel>(Execute).Invoke;
                    UsesBurst = true;
                }
                else Failure = "Burst is disabled; dependency kernels use the managed entry point.";
            }
            catch (Exception error)
            {
                Failure = error.Message;
            }
        }

        internal static void Run(ref Context c)
        {
            s_Run(ref c);
        }

        private static bool Add(ref Context c, int index)
        {
            if ((uint)index >= (uint)c.Capacity)
            {
                c.Error = 1;
                return false;
            }

            if (c.Marked[index] != 0) return false;
            c.Marked[index] = 1;
            c.Output[c.OutputCount++] = index;
            return true;
        }

        [BurstCompile(CompileSynchronously = true)]
        private static void Execute(ref Context c)
        {
            if (c.Operation == 5)
            {
                for (int i = 0; i < c.Capacity; i++)
                {
                    c.Output[i] = i;
                    c.Marked[i] = 1;
                }

                c.OutputCount = c.Capacity;
                return;
            }

            for (int i = 0; i < c.InputCount; i++)
            {
                int index = c.Input[i];
                if (c.Operation == 0)
                {
                    if ((uint)index >= (uint)c.Limit)
                    {
                        c.Error = 1;
                        continue;
                    }

                    Add(ref c, index + c.Offset);
                }
                else if (c.Operation == 1)
                {
                    for (int j = c.Offsets[index]; j < c.Offsets[index + 1]; j++) Add(ref c, c.Values[j]);
                }
                else if (c.Operation == 2)
                {
                    for (int node = c.Offsets[index]; node >= 0; node = c.Values[node])
                        if (!Add(ref c, node))
                            break;
                }
                else if (c.Operation == 3) c.Marked[index] = 0;
                else if (index != c.Output[i])
                {
                    c.Error = 1;
                    return;
                }
            }
        }
    }
}