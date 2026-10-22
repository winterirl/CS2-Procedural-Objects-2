using System;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Burst;
using UnityEngine;
using PO2.Geometry;

namespace PO2.Core.Operations
{
    [BurstCompile]
    internal static unsafe class ArrayKernels
    {
        private struct Context
        {
            internal Vector3* Source;
            internal Vector3* Output;
            internal Vector3 Offset;
            internal int Start, End, Invalid;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Kernel(ref Context context);

        private static Kernel s_Translate = Translate;

        private struct TransformContext
        {
            internal Vector3* Positions;
            internal Vector3* Normals;
            internal Vector4* Tangents;
            internal Matrix4x4 Matrix, NormalMatrix;
            internal int WriteNormals;
            internal int Start, End, Invalid;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void TransformKernel(ref TransformContext context);

        private static TransformKernel s_Transform = Transform;
        private static bool s_Initialized;

        internal static void Initialize()
        {
            if (s_Initialized) return;
            s_Initialized = true;
            try
            {
                if (BurstCompiler.IsEnabled)
                {
                    s_Translate = BurstCompiler.CompileFunctionPointer<Kernel>(Translate).Invoke;
                    s_Transform = BurstCompiler.CompileFunctionPointer<TransformKernel>(Transform).Invoke;
                }
            }
            catch (Exception e)
            {
                Mod.log.Warn("[PO Array] Using managed translation: " + e.Message);
            }
        }

        internal static Vector3[] CopyTranslated(Vector3[] source, Vector3 offset, CancellationToken token)
        {
            var output = new Vector3[source.Length];
            fixed (Vector3* input = source, result = output)
            {
                var context = new Context { Source = input, Output = result, Offset = offset };
                for (int i = 0; i < source.Length; i += 8192)
                {
                    token.ThrowIfCancellationRequested();
                    context.Start = i;
                    context.End = Math.Min(source.Length, i + 8192);
                    s_Translate(ref context);
                    if (context.Invalid != 0) throw new InvalidOperationException("The array would create an invalid vertex position.");
                }
            }

            return output;
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private static void Translate(ref Context c)
        {
            for (int i = c.Start; i < c.End; i++)
            {
                Vector3 value = c.Source[i];
                value.x += c.Offset.x;
                value.y += c.Offset.y;
                value.z += c.Offset.z;
                if (!Finite(value.x) || !Finite(value.y) || !Finite(value.z))
                {
                    c.Invalid = 1;
                    return;
                }

                c.Output[i] = value;
            }
        }

        internal static MeshData CopyTransformed(MeshData mesh, Matrix4x4 matrix, CancellationToken token, bool copyPayloads = false)
        {
            var positions = (Vector3[])mesh.Vertices.Clone();
            var normals = mesh.Normals == null ? null : (Vector3[])mesh.Normals.Clone();
            var tangents = mesh.Tangents == null ? null : (Vector4[])mesh.Tangents.Clone();
            fixed (Vector3* p = positions, n = normals)
            fixed (Vector4* t = tangents)
            {
                var context = new TransformContext { Positions = p, Normals = n, Tangents = t, Matrix = matrix, NormalMatrix = matrix.inverse.transpose, WriteNormals = 1 };
                for (int i = 0; i < positions.Length; i += 8192)
                {
                    token.ThrowIfCancellationRequested();
                    context.Start = i;
                    context.End = Math.Min(positions.Length, i + 8192);
                    s_Transform(ref context);
                    if (context.Invalid != 0) throw new InvalidOperationException("The array rotation produced invalid geometry.");
                }
            }

            // Line templates can share read-only payloads before translation
            // Curve copies go straight to publication and need their own arrays
            return new MeshData(positions, normals, tangents, copyPayloads ? Copy(mesh.UV0) : mesh.UV0, copyPayloads ? Copy(mesh.VertexColors) : mesh.VertexColors, copyPayloads ? Copy(mesh.TexCoord1) : mesh.TexCoord1, copyPayloads ? Copy(mesh.TexCoord3) : mesh.TexCoord3, copyPayloads ? Copy(mesh.Triangles) : mesh.Triangles);
        }

        private static T[] Copy<T>(T[] values) => values == null ? null : (T[])values.Clone();

        internal static Vector4[] CopyTangents(MeshData mesh, Matrix4x4 matrix, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var tangents = Copy(mesh.Tangents);
            if (tangents == null || matrix.Equals(Matrix4x4.identity)) return tangents;
            fixed (Vector3* normals = mesh.Normals)
            fixed (Vector4* output = tangents)
            {
                var context = new TransformContext { Normals = normals, Tangents = output, Matrix = matrix, NormalMatrix = matrix.inverse.transpose };
                for (int i = 0; i < tangents.Length; i += 8192)
                {
                    token.ThrowIfCancellationRequested();
                    context.Start = i;
                    context.End = Math.Min(tangents.Length, i + 8192);
                    s_Transform(ref context);
                    if (context.Invalid != 0) throw new InvalidOperationException("The array rotation produced invalid tangents.");
                }
            }

            return tangents;
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
        private static void Transform(ref TransformContext c)
        {
            for (int i = c.Start; i < c.End; i++)
            {
                Vector3 n = Vector3.zero;
                if (c.Positions != null)
                {
                    Vector3 p = c.Matrix.MultiplyPoint3x4(c.Positions[i]);
                    if (!Finite(p))
                    {
                        c.Invalid = 1;
                        return;
                    }

                    c.Positions[i] = p;
                }

                if (c.Normals != null)
                {
                    n = c.NormalMatrix.MultiplyVector(c.Normals[i]);
                    if (!Normalize(ref n))
                    {
                        c.Invalid = 1;
                        return;
                    }

                    if (c.WriteNormals != 0) c.Normals[i] = n;
                }

                if (c.Tangents != null)
                {
                    Vector4 source = c.Tangents[i];
                    Vector3 tangent = c.Matrix.MultiplyVector(new Vector3(source.x, source.y, source.z));
                    if (n.sqrMagnitude > 1e-12f && tangent.sqrMagnitude > 1e-12f)
                    {
                        tangent -= n * Vector3.Dot(n, tangent);
                        if (tangent.sqrMagnitude < 1e-12f) tangent = Vector3.Cross(n, Mathf.Abs(n.y) < .999f ? Vector3.up : Vector3.right);
                    }

                    if (!Normalize(ref tangent) || !Finite(source.w))
                    {
                        c.Invalid = 1;
                        return;
                    }

                    c.Tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, source.w);
                }
            }
        }

        private static bool Normalize(ref Vector3 value)
        {
            if (!Finite(value)) return false;
            float squared = value.sqrMagnitude;
            if (!Finite(squared)) return false;
            value = squared < 1e-12f ? Vector3.zero : value / Mathf.Sqrt(squared);
            return Finite(value);
        }

        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}