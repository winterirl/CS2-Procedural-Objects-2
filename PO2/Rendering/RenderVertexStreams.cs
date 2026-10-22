using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace PO2.Rendering
{
    // Position and direction buffers can change without touching the material payload.
    internal static class RenderVertexStreams
    {
        internal const MeshUpdateFlags kBatchFlags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers;

        internal enum Role
        {
            Position,
            Normal,
            Tangent,
            Auxiliary
        }

        internal sealed class Layout
        {
            internal readonly VertexAttributeDescriptor[] Attributes;
            internal readonly int Normal = -1, Tangent = -1, Auxiliary = -1;
            internal readonly int AuxiliaryStride, UV0Dimension;
            internal readonly bool Colors, UVs, TexCoord1, TexCoord3;

            internal Layout(bool normals, bool tangents, bool colors, bool uvs, bool tc1, bool tc3, int uvDimension)
            {
                if (uvDimension != 2 && uvDimension != 4) throw new ArgumentOutOfRangeException(nameof(uvDimension));
                UV0Dimension = uvDimension;
                Colors = colors;
                UVs = uvs;
                TexCoord1 = tc1;
                TexCoord3 = tc3;
                var attributes = new List<VertexAttributeDescriptor>(7)
                {
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0)
                };
                int stream = 1;
                if (normals)
                {
                    Normal = stream++;
                    attributes.Add(new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, Normal));
                }

                if (tangents)
                {
                    Tangent = stream++;
                    attributes.Add(new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, Tangent));
                }

                if (colors || uvs || tc1 || tc3)
                {
                    Auxiliary = stream;
                    if (colors) attributes.Add(new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, stream));
                    if (uvs) attributes.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, uvDimension, stream));
                    if (tc1) attributes.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4, stream));
                    if (tc3) attributes.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4, stream));
                }

                AuxiliaryStride = (colors ? 4 : 0) + (uvs ? uvDimension * 4 : 0) + (tc1 ? 16 : 0) + (tc3 ? 16 : 0);
                Attributes = attributes.ToArray();
            }

            internal bool Matches(Mesh mesh, int count)
            {
                if (mesh == null || mesh.vertexCount != count || mesh.vertexAttributeCount != Attributes.Length) return false;
                foreach (var attribute in Attributes)
                {
                    if (!mesh.HasVertexAttribute(attribute.attribute) || mesh.GetVertexAttributeFormat(attribute.attribute) != attribute.format || mesh.GetVertexAttributeDimension(attribute.attribute) != attribute.dimension || mesh.GetVertexAttributeStream(attribute.attribute) != attribute.stream) return false;
                }

                return DynamicStream(mesh, VertexAttribute.Position, 0, 3) && (Normal < 0 || DynamicStream(mesh, VertexAttribute.Normal, Normal, 3)) && (Tangent < 0 || DynamicStream(mesh, VertexAttribute.Tangent, Tangent, 4)) && (Auxiliary < 0 || mesh.GetVertexBufferStride(Auxiliary) == AuxiliaryStride);
            }
        }

        internal static bool DynamicStream(Mesh mesh, VertexAttribute attribute, int stream, int dimension) =>
            mesh.HasVertexAttribute(attribute) && mesh.GetVertexAttributeFormat(attribute) == VertexAttributeFormat.Float32 && mesh.GetVertexAttributeDimension(attribute) == dimension && mesh.GetVertexAttributeStream(attribute) == stream && mesh.GetVertexAttributeOffset(attribute) == 0 && mesh.GetVertexBufferStride(stream) == dimension * 4;

        internal static Layout Describe(Vector3[] positions, Vector3[] normals, Vector4[] tangents, Vector4[] uv, Color32[] colors, Vector4[] tc1, Vector4[] tc3, int dimension, bool requiresTangents)
        {
            int count = positions.Length;
            Validate(normals, count);
            Validate(tangents, count);
            Validate(uv, count);
            Validate(colors, count);
            Validate(tc1, count);
            Validate(tc3, count);
            return new Layout(Present(normals, count), requiresTangents, Present(colors, count), Present(uv, count), Present(tc1, count), Present(tc3, count), dimension <= 2 ? 2 : 4);
        }

        private static bool Present<T>(T[] values, int count) => values != null && values.Length == count && count != 0;

        private static void Validate<T>(T[] values, int count)
        {
            if (values != null && values.Length != 0 && values.Length != count)
                throw new InvalidOperationException("Renderer vertex stream length changed.");
        }

        internal static unsafe byte[] PackAuxiliary(Layout layout, int count, Vector4[] uv, Color32[] colors, Vector4[] tc1, Vector4[] tc3, CancellationToken token = default)
        {
            var bytes = new byte[checked(count * layout.AuxiliaryStride)];
            if (bytes.Length == 0) return bytes;
            fixed (byte* start = bytes)
            {
                for (int i = 0; i < count; i++)
                {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    byte* target = start + i * layout.AuxiliaryStride;
                    if (layout.Colors)
                    {
                        *(Color32*)target = colors[i];
                        target += 4;
                    }

                    if (layout.UVs)
                    {
                        if (layout.UV0Dimension == 4) *(Vector4*)target = uv[i];
                        else *(Vector2*)target = new Vector2(uv[i].x, uv[i].y);
                        target += layout.UV0Dimension * 4;
                    }

                    if (layout.TexCoord1)
                    {
                        *(Vector4*)target = tc1[i];
                        target += 16;
                    }

                    if (layout.TexCoord3) *(Vector4*)target = tc3[i];
                }
            }

            return bytes;
        }

        internal static void WriteAll(Mesh mesh, Vector3[] positions, Vector3[] normals, Vector4[] tangents, Vector4[] uv, Color32[] colors, Vector4[] tc1, Vector4[] tc3, int dimension, bool requiresTangents, bool useSourceTangents)
        {
            var layout = Describe(positions, normals, tangents, uv, colors, tc1, tc3, dimension, requiresTangents);
            bool reset = !layout.Matches(mesh, positions.Length);
            if (reset) mesh.SetVertexBufferParams(positions.Length, layout.Attributes);
            Submit(mesh, positions, 0, 0, positions.Length, 0, 12, Role.Position);
            if (layout.Normal >= 0) Submit(mesh, normals, 0, 0, positions.Length, layout.Normal, 12, Role.Normal);
            if (layout.Tangent >= 0 && useSourceTangents && Present(tangents, positions.Length))
                Submit(mesh, tangents, 0, 0, positions.Length, layout.Tangent, 16, Role.Tangent);
            if (layout.Auxiliary >= 0)
            {
                byte[] auxiliary = PackAuxiliary(layout, positions.Length, uv, colors, tc1, tc3);
                Submit(mesh, auxiliary, 0, 0, auxiliary.Length, layout.Auxiliary, 1, Role.Auxiliary);
            }
        }

        internal static void Submit<T>(Mesh mesh, T[] values, int source, int destination, int count, int stream, int elementBytes, Role role) where T : struct
        {
            if (count == 0) return;
            mesh.SetVertexBufferData(values, source, destination, count, stream, kBatchFlags);
        }

        internal static void RecalculateTangents(Mesh mesh)
        {
            mesh.RecalculateTangents(kBatchFlags);
        }

        internal static void Notify(Mesh mesh)
        {
            mesh.MarkModified();
        }

        internal static void PublishBounds(Mesh mesh, Bounds bounds)
        {
            // The existing indices and base vertices stay intact. Conservative
            // submesh bounds are enough; the mesh bound itself is exact.
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                var submesh = mesh.GetSubMesh(i);
                submesh.bounds = bounds;
                mesh.SetSubMesh(i, submesh, kBatchFlags);
            }

            mesh.bounds = bounds;
            mesh.MarkModified();
        }
    }
}