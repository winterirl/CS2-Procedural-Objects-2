using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using UnityEngine;
using PO2.Geometry;

namespace PO2.History
{
    // History still checks the actual values. These calls only combine the
    // repeated walks, and finish before any pinned array leaves its scope.
    [BurstCompile]
    internal static unsafe class VertexHistoryKernels
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Context
        {
            internal Vector3* Positions, Normals, OldPositions, OldNormals, CapturePositions, CaptureNormals;
            internal int* PositionIndices, NormalIndices, Triangles, Snapshot;
            internal int PositionCount, NormalCount, VertexCount, NormalLength, TangentLength, TriangleCount;
            internal int Validate, HashValues, SamePositions, SameNormals, Error;
            internal ulong Hash;
        }

        internal struct Result
        {
            internal ulong Hash;
            internal bool SamePositions, SameNormals;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Kernel(ref Context context);

        private static Kernel s_Values = Values, s_Layout = LayoutKernel;
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
                    Failure = "Burst is disabled; history uses managed kernels.";
                    return;
                }

                var values = BurstCompiler.CompileFunctionPointer<Kernel>(Values).Invoke;
                var layout = BurstCompiler.CompileFunctionPointer<Kernel>(LayoutKernel).Invoke;
                s_Values = values;
                s_Layout = layout;
                UsesBurst = true;
            }
            catch (Exception error)
            {
                Failure = "History Burst initialization failed: " + error.Message;
            }
        }

        internal static Result Capture(MeshData mesh, int[] positions, int[] normals, out Vector3[] capturedPositions, out Vector3[] capturedNormals, bool validate = false)
        {
            if (positions == null || normals == null) throw new ArgumentNullException(nameof(positions));
            capturedPositions = positions.Length == 0 ? Array.Empty<Vector3>() : new Vector3[positions.Length];
            capturedNormals = normals.Length == 0 ? Array.Empty<Vector3>() : new Vector3[normals.Length];
            return Scan(mesh, positions, normals, null, null, capturedPositions, capturedNormals, validate, true);
        }

        internal static Result Scan(MeshData mesh, int[] positions, int[] normals, Vector3[] oldPositions = null, Vector3[] oldNormals = null, Vector3[] capturePositions = null, Vector3[] captureNormals = null, bool validate = true, bool hash = true)
        {
            if (mesh == null || mesh.Vertices == null || positions == null || normals == null || (oldPositions != null && oldPositions.Length != positions.Length) || (oldNormals != null && oldNormals.Length != normals.Length) || (capturePositions != null && capturePositions.Length != positions.Length) || (captureNormals != null && captureNormals.Length != normals.Length))
                throw new ArgumentException("Invalid vertex history buffers.");
            var c = new Context
            {
                VertexCount = mesh.Vertices.Length, NormalLength = mesh.Normals?.Length ?? -1,
                PositionCount = positions.Length, NormalCount = normals.Length,
                Validate = validate ? 1 : 0, HashValues = hash ? 1 : 0,
                SamePositions = oldPositions != null ? 1 : 0, SameNormals = oldNormals != null ? 1 : 0,
                Hash = 14695981039346656037UL
            };
            fixed (Vector3* p = mesh.Vertices, n = mesh.Normals, op = oldPositions, on = oldNormals, cp = capturePositions, cn = captureNormals)
            fixed (int* pi = positions, ni = normals)
            {
                c.Positions = p;
                c.Normals = n;
                c.OldPositions = op;
                c.OldNormals = on;
                c.CapturePositions = cp;
                c.CaptureNormals = cn;
                c.PositionIndices = pi;
                c.NormalIndices = ni;
                s_Values(ref c);
            }

            if (c.Error != 0) throw new InvalidOperationException(c.Error == 1 ? "Vertex history index is outside its source stream." : "Vertex edit produced a non-finite value.");
            return new Result { Hash = c.Hash, SamePositions = c.SamePositions != 0, SameNormals = c.SameNormals != 0 };
        }

        internal static ulong Layout(MeshData mesh, int[] snapshot = null)
        {
            if (mesh == null || mesh.Vertices == null || mesh.Triangles == null)
                throw new ArgumentException("Missing vertex history layout.");
            if (snapshot != null && (snapshot.Length != mesh.Triangles.Length || ReferenceEquals(snapshot, mesh.Triangles)))
                throw new ArgumentException("Layout snapshot must be an independent matching buffer.");
            var c = new Context
            {
                VertexCount = mesh.Vertices.Length, NormalLength = mesh.Normals?.Length ?? -1,
                TangentLength = mesh.Tangents?.Length ?? -1, TriangleCount = mesh.Triangles.Length,
                Hash = 14695981039346656037UL
            };
            fixed (int* triangles = mesh.Triangles, copy = snapshot)
            {
                c.Triangles = triangles;
                c.Snapshot = copy;
                s_Layout(ref c);
            }

            return c.Hash;
        }

        private static ulong Add(ulong hash, int value)
        {
            unchecked
            {
                uint bits = (uint)value;
                hash = (hash ^ (byte)bits) * 1099511628211UL;
                hash = (hash ^ (byte)(bits >> 8)) * 1099511628211UL;
                hash = (hash ^ (byte)(bits >> 16)) * 1099511628211UL;
                return (hash ^ (byte)(bits >> 24)) * 1099511628211UL;
            }
        }

        private static ulong Add(ulong hash, Vector3 value)
        {
            int* bits = (int*)&value;
            return Add(Add(Add(hash, bits[0]), bits[1]), bits[2]);
        }

        private static bool Same(Vector3 a, Vector3 b)
        {
            int* x = (int*)&a;
            int* y = (int*)&b;
            return x[0] == y[0] && x[1] == y[1] && x[2] == y[2];
        }

        private static bool Finite(Vector3 p) =>
            !float.IsNaN(p.x) && !float.IsInfinity(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.z);

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private static void Values(ref Context c)
        {
            for (int i = 0; i < c.PositionCount; i++)
            {
                int index = c.PositionIndices[i];
                if ((uint)index >= (uint)c.VertexCount)
                {
                    c.Error = 1;
                    return;
                }

                Vector3 value = c.Positions[index];
                if (c.Validate != 0 && !Finite(value))
                {
                    c.Error = 2;
                    return;
                }

                if (c.OldPositions != null && !Same(value, c.OldPositions[i])) c.SamePositions = 0;
                if (c.CapturePositions != null) c.CapturePositions[i] = value;
                if (c.HashValues != 0) c.Hash = Add(c.Hash, value);
            }

            for (int i = 0; i < c.NormalCount; i++)
            {
                int index = c.NormalIndices[i];
                if (c.Normals == null || (uint)index >= (uint)c.NormalLength)
                {
                    c.Error = 1;
                    return;
                }

                Vector3 value = c.Normals[index];
                if (c.Validate != 0 && !Finite(value))
                {
                    c.Error = 2;
                    return;
                }

                if (c.OldNormals != null && !Same(value, c.OldNormals[i])) c.SameNormals = 0;
                if (c.CaptureNormals != null) c.CaptureNormals[i] = value;
                if (c.HashValues != 0) c.Hash = Add(c.Hash, value);
            }
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private static void LayoutKernel(ref Context c)
        {
            c.Hash = Add(c.Hash, c.VertexCount);
            c.Hash = Add(c.Hash, c.NormalLength);
            c.Hash = Add(c.Hash, c.TangentLength);
            c.Hash = Add(c.Hash, c.TriangleCount);
            for (int i = 0; i < c.TriangleCount; i++)
            {
                int value = c.Triangles[i];
                c.Hash = Add(c.Hash, value);
                if (c.Snapshot != null) c.Snapshot[i] = value;
            }
        }
    }
}