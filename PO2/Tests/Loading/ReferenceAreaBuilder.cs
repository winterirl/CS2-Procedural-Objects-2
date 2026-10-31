// Frozen v1.0.2 extraction and combination used by the reference renderer.
using System;
using System.Collections.Generic;
using UnityEngine;
using PO2.Geometry;
using PO2.Materials;
namespace PO2.Conversion
{
    internal static class ConvertedRenderPreparationBuilder
    {
        internal static void BuildAreaSourceMesh(MeshPart part, SurfaceArea surfaceArea, int[] sourceTriangleIds, out MeshData areaMesh, out int[] sourceVertexIndices, System.Threading.CancellationToken cancellation = default)
        {
            BuildAreaSourceMesh(part.Mesh, surfaceArea, sourceTriangleIds, out areaMesh, out sourceVertexIndices, cancellation);
        }

        internal static void BuildAreaSourceMesh(MeshData source, SurfaceArea surfaceArea, int[] sourceTriangleIds, out MeshData areaMesh, out int[] sourceVertexIndices, System.Threading.CancellationToken cancellation = default)
        {
            bool hasNormals = source.Normals != null && source.Normals.Length == source.Vertices.Length;
            bool hasTangents = source.Tangents != null && source.Tangents.Length == source.Vertices.Length;
            bool hasUV0 = source.UV0 != null && source.UV0.Length == source.Vertices.Length;
            bool hasVertexColors = (surfaceArea.SourceVertexAttributes & VertexAttributeFlags.VertexColor) != 0 && source.VertexColors != null && source.VertexColors.Length == source.Vertices.Length;
            bool hasTexCoord1 = (surfaceArea.SourceVertexAttributes & VertexAttributeFlags.TexCoord1) != 0 && source.TexCoord1 != null && source.TexCoord1.Length == source.Vertices.Length;
            bool hasTexCoord3 = (surfaceArea.SourceVertexAttributes & VertexAttributeFlags.TexCoord3) != 0 && source.TexCoord3 != null && source.TexCoord3.Length == source.Vertices.Length;

            Dictionary<int, int> sourceToArea = new Dictionary<int, int>();
            List<int> sourceIndices = new List<int>();
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = hasNormals ? new List<Vector3>() : null;
            List<Vector4> tangents = hasTangents ? new List<Vector4>() : null;
            List<Vector4> uv0 = hasUV0 ? new List<Vector4>() : null;
            List<Color32> colors = hasVertexColors ? new List<Color32>() : null;
            List<Vector4> texCoord1 = hasTexCoord1 ? new List<Vector4>() : null;
            List<Vector4> texCoord3 = hasTexCoord3 ? new List<Vector4>() : null;
            int[] triangles = new int[sourceTriangleIds.Length * 3];

            for (int i = 0; i < sourceTriangleIds.Length; i++)
            {
                if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                int sourceOffset = sourceTriangleIds[i] * 3;
                int targetOffset = i * 3;

                for (int corner = 0; corner < 3; corner++)
                {
                    int sourceVertexIndex = source.Triangles[sourceOffset + corner];
                    if (!sourceToArea.TryGetValue(sourceVertexIndex, out int areaVertexIndex))
                    {
                        areaVertexIndex = vertices.Count;
                        sourceToArea.Add(sourceVertexIndex, areaVertexIndex);
                        sourceIndices.Add(sourceVertexIndex);
                        vertices.Add(source.Vertices[sourceVertexIndex]);

                        if (hasNormals)
                        {
                            normals.Add(source.Normals[sourceVertexIndex]);
                        }

                        if (hasTangents)
                        {
                            tangents.Add(source.Tangents[sourceVertexIndex]);
                        }

                        if (hasUV0)
                        {
                            uv0.Add(source.UV0[sourceVertexIndex]);
                        }

                        if (hasVertexColors)
                        {
                            colors.Add(source.VertexColors[sourceVertexIndex]);
                        }

                        if (hasTexCoord1)
                        {
                            texCoord1.Add(source.TexCoord1[sourceVertexIndex]);
                        }

                        if (hasTexCoord3)
                        {
                            texCoord3.Add(source.TexCoord3[sourceVertexIndex]);
                        }
                    }

                    triangles[targetOffset + corner] = areaVertexIndex;
                }
            }

            areaMesh = new MeshData(vertices.ToArray(), hasNormals ? normals.ToArray() : null, hasTangents ? tangents.ToArray() : null, hasUV0 ? uv0.ToArray() : null, hasVertexColors ? colors.ToArray() : null, hasTexCoord1 ? texCoord1.ToArray() : null, hasTexCoord3 ? texCoord3.ToArray() : null, triangles);
            sourceVertexIndices = sourceIndices.ToArray();
        }

        internal static void AppendAreaVertices(List<Vector3> combinedVertices, List<Vector3> combinedNormals, List<Vector4> combinedUVs, List<Vector4> combinedTangents, List<Color32> combinedColors, List<Vector4> combinedTexCoord1, List<Vector4> combinedTexCoord3, Vector3[] vertices, Vector3[] normals, Vector4[] uvs, Vector4[] tangents, Color32[] vertexColors, Vector4[] texCoord1, Vector4[] texCoord3)
        {
            bool hasNormals = normals != null && normals.Length == vertices.Length;
            bool hasUVs = uvs != null && uvs.Length == vertices.Length;
            bool hasTangents = tangents != null && tangents.Length == vertices.Length;
            bool hasColors = vertexColors != null && vertexColors.Length == vertices.Length;
            bool hasTexCoord1 = texCoord1 != null && texCoord1.Length == vertices.Length;
            bool hasTexCoord3 = texCoord3 != null && texCoord3.Length == vertices.Length;
            Color32 defaultColor = new Color32(255, 255, 255, 255);

            if (hasTexCoord1 && combinedTexCoord1.Count == 0)
            {
                for (int i = 0; i < combinedVertices.Count; i++) combinedTexCoord1.Add(Vector4.zero);
            }

            bool appendTexCoord1 = hasTexCoord1 || combinedTexCoord1.Count != 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                combinedVertices.Add(vertices[i]);
                combinedNormals.Add(hasNormals ? normals[i] : Vector3.up);
                combinedUVs.Add(hasUVs ? uvs[i] : Vector4.zero);
                combinedTangents.Add(hasTangents ? tangents[i] : new Vector4(1f, 0f, 0f, 1f));
                combinedColors.Add(hasColors ? vertexColors[i] : defaultColor);
                if (appendTexCoord1) combinedTexCoord1.Add(hasTexCoord1 ? texCoord1[i] : Vector4.zero);
                combinedTexCoord3.Add(hasTexCoord3 ? texCoord3[i] : Vector4.zero);
            }
        }
    }
}
