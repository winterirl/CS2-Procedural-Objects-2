using System;
using UnityEngine;

namespace PO2.Rendering.Lod
{
    internal static class LodMeshCompactor
    {
        internal static LodMeshData Compact(LodMeshData source, int[] simplifiedIndices, int indexCount, bool recalculateNormals, bool recalculateTangents)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (simplifiedIndices == null)
            {
                throw new ArgumentNullException(nameof(simplifiedIndices));
            }

            if (indexCount <= 0 || indexCount > simplifiedIndices.Length || indexCount % 3 != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(indexCount));
            }

            int[] sourceToCompacted = new int[source.VertexCount];
            for (int i = 0; i < sourceToCompacted.Length; ++i)
            {
                sourceToCompacted[i] = -1;
            }

            int maximumVertexCount = Math.Min(source.VertexCount, indexCount);
            Vector3[] vertices = new Vector3[maximumVertexCount];
            Vector3[] normals = source.Normals != null ? new Vector3[maximumVertexCount] : null;
            Vector4[] tangents = source.Tangents != null ? new Vector4[maximumVertexCount] : null;
            Vector4[] uv0 = source.UV0 != null ? new Vector4[maximumVertexCount] : null;
            Color32[] colors = source.VertexColors != null ? new Color32[maximumVertexCount] : null;
            Vector4[] texCoord1 = source.TexCoord1 != null ? new Vector4[maximumVertexCount] : null;
            Vector4[] texCoord3 = source.TexCoord3 != null ? new Vector4[maximumVertexCount] : null;
            int[] triangles = new int[indexCount];

            int compactedVertexCount = 0;
            for (int indexOffset = 0; indexOffset < indexCount; ++indexOffset)
            {
                int sourceVertexIndex = simplifiedIndices[indexOffset];
                if (sourceVertexIndex < 0 || sourceVertexIndex >= source.VertexCount)
                {
                    throw new ArgumentException("Simplified indices reference an invalid vertex.", nameof(simplifiedIndices));
                }

                int compactedVertexIndex = sourceToCompacted[sourceVertexIndex];
                if (compactedVertexIndex < 0)
                {
                    compactedVertexIndex = compactedVertexCount++;
                    sourceToCompacted[sourceVertexIndex] = compactedVertexIndex;
                    vertices[compactedVertexIndex] = source.Vertices[sourceVertexIndex];
                    if (normals != null)
                    {
                        normals[compactedVertexIndex] = source.Normals[sourceVertexIndex];
                    }

                    if (tangents != null)
                    {
                        tangents[compactedVertexIndex] = source.Tangents[sourceVertexIndex];
                    }

                    if (uv0 != null)
                    {
                        uv0[compactedVertexIndex] = source.UV0[sourceVertexIndex];
                    }

                    if (colors != null)
                    {
                        colors[compactedVertexIndex] = source.VertexColors[sourceVertexIndex];
                    }

                    if (texCoord1 != null)
                    {
                        texCoord1[compactedVertexIndex] = source.TexCoord1[sourceVertexIndex];
                    }

                    if (texCoord3 != null)
                    {
                        texCoord3[compactedVertexIndex] = source.TexCoord3[sourceVertexIndex];
                    }
                }

                triangles[indexOffset] = compactedVertexIndex;
            }

            Resize(ref vertices, compactedVertexCount);
            Resize(ref normals, compactedVertexCount);
            Resize(ref tangents, compactedVertexCount);
            Resize(ref uv0, compactedVertexCount);
            Resize(ref colors, compactedVertexCount);
            Resize(ref texCoord1, compactedVertexCount);
            Resize(ref texCoord3, compactedVertexCount);

            LodMeshData compacted = new LodMeshData(vertices, normals, tangents, uv0, colors, texCoord1, texCoord3, source.UV0Dimension, triangles);
            if (recalculateNormals && compacted.Normals != null)
            {
                compacted = compacted.WithNormals(LodMeshMath.CalculateNormals(compacted));
            }

            if (recalculateTangents)
            {
                compacted = compacted.WithTangents(LodMeshMath.CalculateTangents(compacted));
            }

            return compacted;
        }

        private static void Resize<T>(ref T[] values, int count)
        {
            if (values != null && values.Length != count)
            {
                Array.Resize(ref values, count);
            }
        }
    }
}