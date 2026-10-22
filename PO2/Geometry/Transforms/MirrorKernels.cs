using System;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Burst;
using UnityEngine;
using PO2.Persistence.Model;

namespace PO2.Geometry.Transforms
{
    // These calls run on detached worker buffers. Pins last for the call only;
    // the kernel never holds live mesh data or calls Unity's object APIs.
    [BurstCompile]
    internal static unsafe class MirrorKernels
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Context
        {
            internal Vector3* Positions;
            internal Vector3* Normals;
            internal Vector4* Tangents;
            internal Vector3* OutputPositions;
            internal Vector3* OutputNormals;
            internal Vector4* OutputTangents;
            internal int* Triangles;
            internal int* OutputTriangles;
            internal Matrix4x4 PositionMatrix, NormalMatrix;
            internal int Start, End, Error, ErrorVertex;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Kernel(ref Context context);

        private static Kernel s_Transform = TransformVertices, s_Winding = ReverseTriangles;
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
                    Failure = "Burst is disabled; mirror uses managed kernels.";
                    return;
                }

                var transform = BurstCompiler.CompileFunctionPointer<Kernel>(TransformVertices).Invoke;
                var winding = BurstCompiler.CompileFunctionPointer<Kernel>(ReverseTriangles).Invoke;
                s_Transform = transform;
                s_Winding = winding;
                UsesBurst = true;
            }
            catch (Exception error)
            {
                Failure = "Mirror Burst initialization failed: " + error.Message;
            }
        }

        internal static void Transform(PersistencePartData source, Matrix4x4 matrix, Vector3[] positions, Vector3[] normals, Vector4[] tangents, int[] triangles, CancellationToken token)
        {
            if (!PartGeometryTransform.TryGetMirrorNormalMatrix(matrix, out var normalMatrix, out var reason))
                throw new InvalidOperationException(reason);
            int count = source.Vertices.Length;
            if (positions.Length != count || triangles.Length != source.Triangles.Length || triangles.Length % 3 != 0 || (source.Normals != null && source.Normals.Length != count) || (source.Tangents != null && source.Tangents.Length != count) || (normals == null ? source.Normals != null : source.Normals == null || normals.Length != count) || (tangents == null ? source.Tangents != null : source.Tangents == null || tangents.Length != count))
                throw new InvalidOperationException("Mirror vertex streams have mismatched lengths.");
            fixed (Vector3* p = source.Vertices, n = source.Normals, op = positions, on = normals)
            fixed (Vector4* t = source.Tangents, ot = tangents)
            fixed (int* indices = source.Triangles, output = triangles)
            {
                var c = new Context
                {
                    Positions = p, Normals = n, Tangents = t,
                    OutputPositions = op, OutputNormals = on, OutputTangents = ot,
                    Triangles = indices, OutputTriangles = output, PositionMatrix = matrix, NormalMatrix = normalMatrix
                };
                for (int start = 0; start < count; start += 8192)
                {
                    token.ThrowIfCancellationRequested();
                    c.Start = start;
                    c.End = Math.Min(count, start + 8192);
                    s_Transform(ref c);
                    if (c.Error != 0) throw new InvalidOperationException($"Invalid mirror geometry at render vertex {c.ErrorVertex} (stream {c.Error}).");
                }

                for (int start = 0; start < triangles.Length; start += 12288)
                {
                    token.ThrowIfCancellationRequested();
                    c.Start = start;
                    c.End = Math.Min(triangles.Length, start + 12288);
                    s_Winding(ref c);
                }
            }
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static float LengthSquared(Vector3 v) => v.x * v.x + v.y * v.y + v.z * v.z;
        private static Vector3 Direction(Matrix4x4 m, Vector3 v) => new Vector3(m.m00 * v.x + m.m01 * v.y + m.m02 * v.z, m.m10 * v.x + m.m11 * v.y + m.m12 * v.z, m.m20 * v.x + m.m21 * v.y + m.m22 * v.z);

        private static Vector3 Normalize(Vector3 v)
        {
            float length = (float)Math.Sqrt(LengthSquared(v));
            return length > 0.00001f ? new Vector3(v.x / length, v.y / length, v.z / length) : default;
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private static void TransformVertices(ref Context c)
        {
            for (int i = c.Start; i < c.End; i++)
            {
                Vector3 input = c.Positions[i];
                Vector3 p = Direction(c.PositionMatrix, input);
                p.x += c.PositionMatrix.m03;
                p.y += c.PositionMatrix.m13;
                p.z += c.PositionMatrix.m23;
                if (!Finite(input) || !Finite(p))
                {
                    c.Error = 1;
                    c.ErrorVertex = i;
                    return;
                }

                c.OutputPositions[i] = p;
                Vector3 n = default;
                if (c.Normals != null)
                {
                    input = c.Normals[i];
                    if (!Finite(input))
                    {
                        c.Error = 2;
                        c.ErrorVertex = i;
                        return;
                    }

                    if (LengthSquared(input) >= 1e-12f)
                    {
                        n = Direction(c.NormalMatrix, input);
                        if (!Finite(n) || LengthSquared(n) < 1e-12f)
                        {
                            c.Error = 2;
                            c.ErrorVertex = i;
                            return;
                        }

                        n = Normalize(n);
                    }

                    c.OutputNormals[i] = n;
                }

                if (c.Tangents == null) continue;
                Vector4 inputTangent = c.Tangents[i];
                input = new Vector3(inputTangent.x, inputTangent.y, inputTangent.z);
                Vector3 t = Direction(c.PositionMatrix, input);
                if (!Finite(input) || !Finite(inputTangent.w) || !Finite(t))
                {
                    c.Error = 3;
                    c.ErrorVertex = i;
                    return;
                }

                if (LengthSquared(t) < 1e-12f) t = default;
                else
                {
                    if (LengthSquared(n) >= 1e-12f)
                    {
                        float dot = n.x * t.x + n.y * t.y + n.z * t.z;
                        t.x -= n.x * dot;
                        t.y -= n.y * dot;
                        t.z -= n.z * dot;
                        if (LengthSquared(t) < 1e-12f)
                        {
                            // Match the existing tangent fallback for parallel directions.
                            t = Math.Abs(n.y) < 0.999f ? new Vector3(-n.z, 0, n.x) : new Vector3(0, n.z, -n.y);
                        }
                    }

                    if (!Finite(t) || LengthSquared(t) < 1e-12f)
                    {
                        c.Error = 3;
                        c.ErrorVertex = i;
                        return;
                    }

                    t = Normalize(t);
                }

                c.OutputTangents[i] = new Vector4(t.x, t.y, t.z, -inputTangent.w);
            }
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private static void ReverseTriangles(ref Context c)
        {
            for (int i = c.Start; i < c.End; i += 3)
            {
                c.OutputTriangles[i] = c.Triangles[i];
                c.OutputTriangles[i + 1] = c.Triangles[i + 2];
                c.OutputTriangles[i + 2] = c.Triangles[i + 1];
            }
        }
    }
}