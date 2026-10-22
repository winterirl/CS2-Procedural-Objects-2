using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using PO2.Geometry;
using PO2.Materials;
using PO2.Rendering;
using PO2.Rendering.Mapping;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        private static void FinishFullMeshWrite(Mesh mesh, bool recalculateTangents)
        {
            if (recalculateTangents && mesh.vertexCount != 0) RenderVertexStreams.RecalculateTangents(mesh);
            mesh.RecalculateBounds();
            RenderVertexStreams.PublishBounds(mesh, mesh.bounds);
        }

        private static void SetDirectionStream<T>(Mesh mesh, T[] values, VertexAttribute attribute, RenderVertexStreams.Role role) where T : struct
        {
            int dimension = attribute == VertexAttribute.Normal ? 3 : 4;
            if (values != null && values.Length == mesh.vertexCount && mesh.HasVertexAttribute(attribute))
            {
                int stream = mesh.GetVertexAttributeStream(attribute);
                if (RenderVertexStreams.DynamicStream(mesh, attribute, stream, dimension))
                {
                    RenderVertexStreams.Submit(mesh, values, 0, 0, values.Length, stream, dimension * 4, role);
                    mesh.MarkModified();
                    return;
                }
            }

            // Adding/removing a direction attribute is a layout change, not a range update.
            if (attribute == VertexAttribute.Normal) mesh.normals = (Vector3[])(object)values;
            else mesh.tangents = (Vector4[])(object)values;
        }

        private static bool RefreshWholeSourceGeometry(RenderPart part, MeshPart source, bool positions, bool directions)
        {
            foreach (var area in part.Areas)
                if (area.GeneratedMapping != null)
                    return false;
            foreach (var area in part.Areas)
            {
                if (!source.TryGetSurfaceArea(area.SurfaceAreaId, out var settings)) return false;
                bool updated = directions ? RefreshAreaMeshSourceGeometry(area, source.Mesh, positions) : RefreshAreaMeshSourcePositions(area, settings, source.Mesh);
                if (!updated) return false;
            }

            foreach (var area in part.Areas)
            {
                if (area.IsolatedRenderer) continue;
                int offset = area.CombinedVertexOffset, count = area.CombinedVertexCount;
                if (offset < 0 || count != area.AreaMesh.Vertices.Length || (long)offset + count > part.CombinedVertices.Length || (long)offset + count > part.CombinedNormals.Length) return false;
                for (int i = 0; i < count; i++)
                {
                    if (positions) part.CombinedVertices[offset + i] = area.AreaMesh.Vertices[i];
                    if (directions && area.AreaMesh.Normals != null) part.CombinedNormals[offset + i] = area.AreaMesh.Normals[i];
                    if (directions && part.UsesSourceTangents && area.AreaMesh.Tangents != null && part.CombinedTangents.Length == part.CombinedVertices.Length)
                        part.CombinedTangents[offset + i] = area.AreaMesh.Tangents[i];
                }
            }

            foreach (var area in part.Areas)
            {
                if (!area.IsolatedRenderer) continue;
                source.TryGetSurfaceArea(area.SurfaceAreaId, out var settings);
                var mesh = area.AreaMesh;
                if (area.IsolatedMesh == null) return false;
                WriteSourceChanges(area.IsolatedMesh, mesh.Vertices, mesh.Normals, mesh.Tangents, positions, directions, RequiresTangentSpace(settings.Material), area.UsesSourceTangents);
            }

            if (part.HasMainRendererGeometry) WriteSourceChanges(part.Mesh, part.CombinedVertices, part.CombinedNormals, part.CombinedTangents, positions, directions, part.HasTangentStream, part.UsesSourceTangents);
            return true;
        }

        private static void WriteSourceChanges(Mesh mesh, Vector3[] vertices, Vector3[] normals, Vector4[] tangents, bool positions, bool directions, bool requiresTangents, bool sourceTangents, bool preciseFallback = false)
        {
            if (positions)
            {
                if (mesh.vertexCount == vertices.Length && RenderVertexStreams.DynamicStream(mesh, VertexAttribute.Position, 0, 3))
                    RenderVertexStreams.Submit(mesh, vertices, 0, 0, vertices.Length, 0, 12, RenderVertexStreams.Role.Position);
                else mesh.vertices = vertices;
            }

            if (directions)
            {
                if (normals != null) SetDirectionStream(mesh, normals, VertexAttribute.Normal, RenderVertexStreams.Role.Normal);
                if (requiresTangents && vertices.Length > 0)
                {
                    if (sourceTangents && tangents != null && tangents.Length == vertices.Length)
                        SetDirectionStream(mesh, tangents, VertexAttribute.Tangent, RenderVertexStreams.Role.Tangent);
                    else RenderVertexStreams.RecalculateTangents(mesh);
                }
            }

            if (preciseFallback && positions && !directions && requiresTangents && !sourceTangents && vertices.Length > 0)
                RenderVertexStreams.RecalculateTangents(mesh);
            if (positions) FinishFullMeshWrite(mesh, false);
            else mesh.MarkModified();
        }

        private static void WriteSingleAreaUnityMesh(Mesh mesh, Vector3[] vertices, Vector3[] normals, Vector4[] uvs, Vector4[] tangents, Color32[] vertexColors, Vector4[] texCoord1, Vector4[] texCoord3, int uv0Dimension, int[] triangles, bool requiresTangents, bool useSourceTangents)
        {
            mesh.Clear();
            mesh.indexFormat = vertices.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            RenderVertexStreams.WriteAll(mesh, vertices, normals, tangents, uvs, vertexColors, texCoord1, texCoord3, uv0Dimension, requiresTangents, useSourceTangents);
            mesh.triangles = triangles;
            FinishFullMeshWrite(mesh, requiresTangents && !(useSourceTangents && tangents != null && tangents.Length == vertices.Length));
        }

        private static void WriteCombinedUnityMesh(Mesh mesh, Vector3[] vertices, Vector3[] normals, Vector4[] uvs, Vector4[] tangents, Color32[] vertexColors, Vector4[] texCoord1, Vector4[] texCoord3, int uv0Dimension, IReadOnlyList<int[]> submeshTriangles, bool requiresTangents, bool useSourceTangents)
        {
            mesh.Clear();
            mesh.indexFormat = vertices.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            RenderVertexStreams.WriteAll(mesh, vertices, normals, tangents, uvs, vertexColors, texCoord1, texCoord3, uv0Dimension, requiresTangents, useSourceTangents);
            mesh.subMeshCount = submeshTriangles.Count;
            for (int i = 0; i < submeshTriangles.Count; i++) mesh.SetTriangles(submeshTriangles[i], i, false);
            FinishFullMeshWrite(mesh, requiresTangents && !(useSourceTangents && tangents != null && tangents.Length == vertices.Length));
        }

        private static void WriteIsolatedAreaStreams(RenderArea renderArea, Vector3[] vertices, Vector3[] normals, Vector4[] uvs, Vector4[] tangents, Color32[] vertexColors, Vector4[] texCoord1, Vector4[] texCoord3, bool requiresTangents, bool useSourceTangents)
        {
            Mesh mesh = renderArea.IsolatedMesh;
            if (mesh == null) return;
            RenderVertexStreams.WriteAll(mesh, vertices, normals, tangents, uvs, vertexColors, texCoord1, texCoord3, renderArea.UV0Dimension, requiresTangents, useSourceTangents);
            FinishFullMeshWrite(mesh, requiresTangents && !(useSourceTangents && tangents != null && tangents.Length == vertices.Length));
        }

        private static void WriteIncrementalUnityMeshStreams(RenderPart renderPart)
        {
            RenderVertexStreams.WriteAll(renderPart.Mesh, renderPart.CombinedVertices, renderPart.CombinedNormals, renderPart.CombinedTangents, renderPart.CombinedUVs, renderPart.CombinedColors, renderPart.CombinedTexCoord1, renderPart.CombinedTexCoord3, renderPart.CombinedUV0Dimension, renderPart.HasTangentStream, renderPart.UsesSourceTangents);
            FinishFullMeshWrite(renderPart.Mesh, renderPart.HasTangentStream && !renderPart.UsesSourceTangents);
        }

        private static bool RefreshAreaMeshSourceGeometry(RenderArea renderArea, MeshData sourceMesh, bool positionsChanged)
        {
            MeshData areaMesh = renderArea.AreaMesh;
            int[] sourceVertexIndices = renderArea.SourceVertexIndices;

            if (areaMesh == null || areaMesh.Vertices == null || sourceMesh == null || sourceMesh.Vertices == null || sourceVertexIndices == null || sourceVertexIndices.Length != areaMesh.Vertices.Length)
            {
                return false;
            }

            bool sourceHasNormals = sourceMesh.Normals != null && sourceMesh.Normals.Length == sourceMesh.Vertices.Length;
            bool sourceHasTangents = sourceMesh.Tangents != null && sourceMesh.Tangents.Length == sourceMesh.Vertices.Length;

            if ((areaMesh.Normals != null) != sourceHasNormals || (areaMesh.Tangents != null) != sourceHasTangents)
            {
                return false;
            }

            for (int i = 0; i < sourceVertexIndices.Length; i++)
            {
                int sourceVertexIndex = sourceVertexIndices[i];
                if (sourceVertexIndex < 0 || sourceVertexIndex >= sourceMesh.Vertices.Length)
                {
                    return false;
                }

                if (positionsChanged)
                {
                    areaMesh.Vertices[i] = sourceMesh.Vertices[sourceVertexIndex];
                }

                if (sourceHasNormals)
                {
                    areaMesh.Normals[i] = sourceMesh.Normals[sourceVertexIndex];
                }

                if (sourceHasTangents)
                {
                    areaMesh.Tangents[i] = sourceMesh.Tangents[sourceVertexIndex];
                }
            }

            return true;
        }

        private static bool RefreshAreaMeshSourcePositions(RenderArea renderArea, SurfaceArea sourceArea, MeshData sourceMesh)
        {
            MeshData areaMesh = renderArea.AreaMesh;
            int[] sourceVertexIndices = renderArea.SourceVertexIndices;

            if (sourceArea == null || areaMesh == null || areaMesh.Vertices == null || sourceMesh == null || sourceMesh.Vertices == null || sourceVertexIndices == null || sourceVertexIndices.Length != areaMesh.Vertices.Length)
            {
                return false;
            }

            for (int i = 0; i < sourceVertexIndices.Length; i++)
            {
                int sourceVertexIndex = sourceVertexIndices[i];
                if (sourceVertexIndex < 0 || sourceVertexIndex >= sourceMesh.Vertices.Length)
                {
                    return false;
                }

                areaMesh.Vertices[i] = sourceMesh.Vertices[sourceVertexIndex];
            }

            return true;
        }

        private static void CopyAreaStreams(RenderPart renderPart, RenderArea renderArea, Vector3[] vertices, Vector3[] normals, Vector2[] uvs)
        {
            int offset = renderArea.CombinedVertexOffset;
            int count = renderArea.CombinedVertexCount;
            bool hasNormals = normals != null && normals.Length == count;
            bool hasUVs = uvs != null && uvs.Length == count;

            for (int i = 0; i < count; i++)
            {
                renderPart.CombinedVertices[offset + i] = vertices[i];
                renderPart.CombinedNormals[offset + i] = hasNormals ? normals[i] : Vector3.up;
                renderPart.CombinedUVs[offset + i] = hasUVs ? new Vector4(uvs[i].x, uvs[i].y, 0f, 0f) : Vector4.zero;
            }
        }

        private static void CopyAreaStreams(RenderPart renderPart, RenderArea renderArea, Vector3[] vertices, Vector3[] normals, Vector4[] uvs)
        {
            int offset = renderArea.CombinedVertexOffset;
            int count = renderArea.CombinedVertexCount;
            bool hasNormals = normals != null && normals.Length == count;
            bool hasUVs = uvs != null && uvs.Length == count;

            for (int i = 0; i < count; i++)
            {
                renderPart.CombinedVertices[offset + i] = vertices[i];
                renderPart.CombinedNormals[offset + i] = hasNormals ? normals[i] : Vector3.up;
                renderPart.CombinedUVs[offset + i] = hasUVs ? uvs[i] : Vector4.zero;

                if (renderPart.UsesSourceTangents && renderPart.CombinedTangents.Length == renderPart.CombinedVertices.Length && renderArea.AreaMesh.Tangents != null && renderArea.AreaMesh.Tangents.Length == count)
                {
                    renderPart.CombinedTangents[offset + i] = renderArea.AreaMesh.Tangents[i];
                }

                bool hasColors = renderArea.AreaMesh.VertexColors != null && renderArea.AreaMesh.VertexColors.Length == count;
                bool hasTexCoord1 = renderArea.AreaMesh.TexCoord1 != null && renderArea.AreaMesh.TexCoord1.Length == count;
                bool hasTexCoord3 = renderArea.AreaMesh.TexCoord3 != null && renderArea.AreaMesh.TexCoord3.Length == count;

                if (renderPart.CombinedColors.Length == renderPart.CombinedVertices.Length)
                {
                    renderPart.CombinedColors[offset + i] = hasColors ? renderArea.AreaMesh.VertexColors[i] : new Color32(255, 255, 255, 255);
                }

                if (renderPart.CombinedTexCoord1.Length == renderPart.CombinedVertices.Length)
                {
                    renderPart.CombinedTexCoord1[offset + i] = hasTexCoord1 ? renderArea.AreaMesh.TexCoord1[i] : Vector4.zero;
                }

                if (renderPart.CombinedTexCoord3.Length == renderPart.CombinedVertices.Length)
                {
                    renderPart.CombinedTexCoord3[offset + i] = hasTexCoord3 ? renderArea.AreaMesh.TexCoord3[i] : Vector4.zero;
                }
            }
        }

        private static Vector4[] ConvertUV2ToUV4(Vector2[] source)
        {
            if (source == null)
            {
                return null;
            }

            Vector4[] result = new Vector4[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = new Vector4(source[i].x, source[i].y, 0f, 0f);
            }

            return result;
        }
    }
}