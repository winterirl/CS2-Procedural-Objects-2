using System;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Burst;

namespace PO2.History
{
    // Read each stream once to make/apply the exact delta and check both states.
    // The pointers only refer to detached arrays, and never outlive a batch call.
    [BurstCompile]
    internal static unsafe class MirrorHistoryKernels
    {
        internal const ulong kHashStart = 14695981039346656037UL;

        [StructLayout(LayoutKind.Sequential)]
        private struct Context
        {
            internal byte* Source, Other, Output;
            internal int Start, End, Mode;
            internal ulong Before, After;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Kernel(ref Context context);

        private static Kernel s_Run = Process;
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
                    Failure = "Burst is disabled; mirror history uses its managed kernel.";
                    return;
                }

                s_Run = BurstCompiler.CompileFunctionPointer<Kernel>(Process).Invoke;
                UsesBurst = true;
            }
            catch (Exception error)
            {
                Failure = "Mirror history Burst initialization failed: " + error.Message;
            }
        }

        private static ulong Length(ulong hash, int count)
        {
            unchecked
            {
                for (int i = 0; i < 4; i++)
                {
                    hash = (hash ^ (byte)count) * 1099511628211UL;
                    count >>= 8;
                }

                return hash;
            }
        }

        private static void Run(ref Context c, int count, CancellationToken token)
        {
            for (int at = 0; at < count; at += 32768)
            {
                token.ThrowIfCancellationRequested();
                c.Start = at;
                c.End = Math.Min(count, at + 32768);
                s_Run(ref c);
            }

            token.ThrowIfCancellationRequested();
        }

        internal static ulong Hash<T>(T[] values, ulong seed, CancellationToken token) where T : unmanaged
        {
            fixed (T* source = values)
            {
                var c = new Context { Source = (byte*)source, Before = Length(seed, values?.Length ?? -1) };
                Run(ref c, checked((values?.Length ?? 0) * sizeof(T)), token);
                return c.Before;
            }
        }

        internal static void Create<T>(T[] before, T[] after, byte[] delta, ref int offset, ref ulong beforeHash, ref ulong afterHash, CancellationToken token) where T : unmanaged
        {
            if ((before?.Length ?? -1) != (after?.Length ?? -1)) throw new InvalidOperationException("Mirror history stream lengths changed.");
            int bytes = checked((before?.Length ?? 0) * sizeof(T));
            if (offset < 0 || offset > delta.Length - bytes) throw new InvalidOperationException("Mirror delta is too small.");
            fixed (T* a = before, b = after)
            fixed (byte* output = delta)
            {
                var c = new Context
                {
                    Source = (byte*)a, Other = (byte*)b, Output = output + offset, Mode = 1,
                    Before = Length(beforeHash, before?.Length ?? -1), After = Length(afterHash, after?.Length ?? -1)
                };
                Run(ref c, bytes, token);
                beforeHash = c.Before;
                afterHash = c.After;
            }

            offset += bytes;
        }

        internal static T[] Apply<T>(T[] current, byte[] delta, ref int offset, ref ulong currentHash, ref ulong resultHash, CancellationToken token) where T : unmanaged
        {
            int bytes = checked((current?.Length ?? 0) * sizeof(T));
            if (offset < 0 || offset > delta.Length - bytes) throw new InvalidOperationException("Mirror delta is incomplete.");
            var result = current == null ? null : new T[current.Length];
            fixed (T* source = current, output = result)
            fixed (byte* raw = delta)
            {
                var c = new Context
                {
                    Source = (byte*)source, Other = raw + offset, Output = (byte*)output, Mode = 2,
                    Before = Length(currentHash, current?.Length ?? -1), After = Length(resultHash, current?.Length ?? -1)
                };
                Run(ref c, bytes, token);
                currentHash = c.Before;
                resultHash = c.After;
            }

            offset += bytes;
            return result;
        }

        [BurstCompile(CompileSynchronously = true)]
        private static void Process(ref Context c)
        {
            ulong before = c.Before, after = c.After;
            unchecked
            {
                for (int i = c.Start; i < c.End; i++)
                {
                    byte a = c.Source[i];
                    before = (before ^ a) * 1099511628211UL;
                    if (c.Mode == 0) continue;
                    byte b = c.Mode == 1 ? c.Other[i] : (byte)(a ^ c.Other[i]);
                    c.Output[i] = c.Mode == 1 ? (byte)(a ^ b) : b;
                    after = (after ^ b) * 1099511628211UL;
                }
            }

            c.Before = before;
            c.After = after;
        }
    }
}