using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using UnityEngine;
using PO2.Geometry;

namespace PO2.History
{
    [BurstCompile]
    internal static unsafe class HistoryBoundaryKernels
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Context
        {
            internal int* Left, Right, PositionIndices, NormalIndices;
            internal Vector3* Positions, Normals, OppositePositions, OppositeNormals;
            internal int Length, Equal, PositionCount, NormalCount, VertexCount, NormalLength, Error;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Kernel(ref Context context);

        private static Kernel s_Compare = Compare, s_Swap = Swap;
        private static bool s_Initialized;
        internal static bool CompareUsesBurst { get; private set; }
        internal static bool SwapUsesBurst { get; private set; }
        internal static string Failure { get; private set; } = string.Empty;

        internal static void Initialize()
        {
            if (s_Initialized) return;
            s_Initialized = true;
            if (!BurstCompiler.IsEnabled)
            {
                Failure = "Burst is disabled; history boundaries use managed kernels.";
                return;
            }

            try
            {
                s_Compare = BurstCompiler.CompileFunctionPointer<Kernel>(Compare).Invoke;
                CompareUsesBurst = true;
            }
            catch (Exception error)
            {
                Failure = "Layout comparison: " + error.Message;
            }

            try
            {
                s_Swap = BurstCompiler.CompileFunctionPointer<Kernel>(Swap).Invoke;
                SwapUsesBurst = true;
            }
            catch (Exception error)
            {
                Failure += " Replay swap: " + error.Message;
            }
        }

        internal static bool EqualTriangles(int[] left, int[] right)
        {
            if (left == null || right == null) throw new ArgumentNullException(nameof(left));
            if (left.Length != right.Length) return false;
            var c = new Context { Length = left.Length, Equal = 1 };
            fixed (int* a = left, b = right)
            {
                c.Left = a;
                c.Right = b;
                s_Compare(ref c);
            }

            return c.Equal != 0;
        }

        internal static bool CanSwap(MeshData mesh, Vector3[] positions, Vector3[] normals) =>
            mesh != null && positions != null && normals != null && !Aliases(mesh.Vertices, mesh.Normals) && !Aliases(positions, normals) && !Aliases(positions, mesh.Vertices) && !Aliases(positions, mesh.Normals) && !Aliases(normals, mesh.Vertices) && !Aliases(normals, mesh.Normals);

        private static bool Aliases(Vector3[] a, Vector3[] b) => a != null && a.Length != 0 && ReferenceEquals(a, b);

        // The action checks finite values and the expected hash first. Indices
        // come from its immutable layout. Check every bound before swapping any value.
        internal static void SwapValidated(MeshData mesh, int[] positions, int[] normals, Vector3[] oppositePositions, Vector3[] oppositeNormals)
        {
            if (!CanSwap(mesh, oppositePositions, oppositeNormals) || mesh.Vertices == null || positions == null || normals == null || oppositePositions.Length != positions.Length || oppositeNormals.Length != normals.Length)
                throw new ArgumentException("Invalid vertex history swap buffers.");
            var c = new Context
            {
                PositionCount = positions.Length, NormalCount = normals.Length,
                VertexCount = mesh.Vertices.Length, NormalLength = mesh.Normals?.Length ?? -1
            };
            fixed (Vector3* p = mesh.Vertices, n = mesh.Normals, op = oppositePositions, on = oppositeNormals)
            fixed (int* pi = positions, ni = normals)
            {
                c.Positions = p;
                c.Normals = n;
                c.OppositePositions = op;
                c.OppositeNormals = on;
                c.PositionIndices = pi;
                c.NormalIndices = ni;
                s_Swap(ref c);
            }

            if (c.Error != 0) throw new InvalidOperationException("Vertex history swap index is outside its source stream.");
        }

        [BurstCompile(CompileSynchronously = true)]
        private static void Compare(ref Context c)
        {
            for (int i = 0; i < c.Length; i++)
            {
                if (c.Left[i] != c.Right[i])
                {
                    c.Equal = 0;
                    return;
                }
            }
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private static void Swap(ref Context c)
        {
            for (int i = 0; i < c.PositionCount; i++)
                if ((uint)c.PositionIndices[i] >= (uint)c.VertexCount)
                {
                    c.Error = 1;
                    return;
                }

            for (int i = 0; i < c.NormalCount; i++)
                if (c.Normals == null || (uint)c.NormalIndices[i] >= (uint)c.NormalLength)
                {
                    c.Error = 1;
                    return;
                }

            for (int i = 0; i < c.PositionCount; i++)
            {
                int index = c.PositionIndices[i];
                Vector3 value = c.Positions[index];
                c.Positions[index] = c.OppositePositions[i];
                c.OppositePositions[i] = value;
            }

            for (int i = 0; i < c.NormalCount; i++)
            {
                int index = c.NormalIndices[i];
                Vector3 value = c.Normals[index];
                c.Normals[index] = c.OppositeNormals[i];
                c.OppositeNormals[i] = value;
            }
        }
    }
}