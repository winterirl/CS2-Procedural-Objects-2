using MeshData = PO2.Geometry.MeshData;
using MaterialLibrary = PO2.Materials.MaterialLibrary;
using MaterialData = PO2.Materials.MaterialData;
using System;
using System.Collections.Generic;
using Colossal.IO.AssetDatabase;
using Game.Rendering;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using PO2.Conversion;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Materials;
using PO2.Rendering.Mapping;
using AssetHash128 = Colossal.Hash128;
using MeshTopology = PO2.Geometry.Topology.MeshTopology;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        private bool TryRebuildPreparedConversionPart(RenderPart renderPart, MeshPart sourcePart, Vector3 objectScale, bool hovered, ConvertedRenderPartPreparation preparation)
        {
            if (renderPart == null || sourcePart == null || preparation == null || preparation.Areas == null || preparation.SubmeshTriangles == null)
            {
                return false;
            }

            ReleaseRenderAreas(renderPart);

            List<Material> materials = new List<Material>(preparation.SubmeshTriangles.Length);
            bool requiresTangents = false;
            bool canUseCombinedSourceTangents = true;

            try
            {
                if (preparation.Areas.Length > sourcePart.SurfaceAreas.Count)
                {
                    return PreparedConversionFallback(renderPart, sourcePart, "prepared Surface Area count is invalid");
                }

                for (int areaIndex = 0; areaIndex < preparation.Areas.Length; areaIndex++)
                {
                    ConvertedRenderAreaPreparation preparedArea = preparation.Areas[areaIndex];

                    if (preparedArea == null || !sourcePart.TryGetSurfaceArea(preparedArea.SurfaceAreaId, out SurfaceArea sourceArea) || sourceArea.Material.Mapping.Mode != TextureMappingMode.Original || preparedArea.AreaMesh == null || preparedArea.SourceVertexIndices == null)
                    {
                        return PreparedConversionFallback(renderPart, sourcePart, "prepared Surface Area state no longer matches");
                    }

                    AssetHash128 resolvedSurfaceGuid = ResolveNativeSurfaceGuid(sourceArea.Material);
                    Material material = AcquireMaterial(sourceArea.Material, renderPart.ReceivesDecals);
                    bool actualIsolation = RequiresIsolatedRenderer(sourceArea.Material);

                    MeshData areaMesh = preparedArea.AreaMesh;
                    bool areaRequiresTangents = RequiresTangentSpace(sourceArea.Material);
                    bool areaUsesSourceTangents = areaMesh.Tangents != null && areaMesh.Tangents.Length == areaMesh.Vertices.Length;

                    RenderArea renderArea = new RenderArea
                    {
                        SurfaceAreaId = sourceArea.Id,
                        MaterialIndex = preparedArea.MaterialIndex,
                        Material = material,
                        ReceivesDecals = renderPart.ReceivesDecals,
                        MaterialSourceVersion = sourceArea.MaterialSourceVersion,
                        MaterialPropertiesVersion = sourceArea.MaterialPropertiesVersion,
                        MappingVersion = sourceArea.MappingVersion,
                        ColorMaskVersion = sourceArea.ColorMaskVersion,
                        MaterialSourceKind = sourceArea.Material.SourceKind,
                        DefinitionId = sourceArea.Material.DefinitionId,
                        SurfaceGuid = sourceArea.Material.SurfaceGuid,
                        VanillaSpecialKind = sourceArea.Material.VanillaSpecialKind,
                        ResolvedSurfaceGuid = resolvedSurfaceGuid,
                        Metallic = sourceArea.Material.Metallic,
                        Smoothness = sourceArea.Material.Smoothness,
                        CoatStrength = sourceArea.Material.CoatStrength,
                        MappingMode = sourceArea.Material.Mapping.Mode,
                        AreaMesh = areaMesh,
                        SourceVertexIndices = preparedArea.SourceVertexIndices,
                        AreaTopology = null,
                        GeneratedMapping = null,
                        CombinedVertexOffset = preparedArea.CombinedVertexOffset,
                        CombinedVertexCount = preparedArea.CombinedVertexCount,
                        IsolatedRenderer = actualIsolation,
                        UsesSourceTangents = areaUsesSourceTangents,
                        UV0Dimension = preparedArea.UV0Dimension,
                    };
                    CopyColorMaskState(renderArea, sourceArea.ColorMask);
                    renderPart.Areas.Add(renderArea);

                    // Validate the worker's layout hint against the acquired shader.
                    // A mismatch rebuilds through the normal renderer path.
                    if (actualIsolation != preparedArea.ExpectedIsolatedRenderer)
                    {
                        return PreparedConversionFallback(renderPart, sourcePart, $"isolation classification changed for Surface Area {sourceArea.Id}");
                    }

                    if (actualIsolation)
                    {
                        CreateIsolatedAreaRenderer(renderPart, renderArea, sourcePart.Name, areaMesh.Vertices, areaMesh.Normals, areaMesh.UV0, areaMesh.Tangents, areaMesh.VertexColors, areaMesh.TexCoord1, areaMesh.TexCoord3, renderArea.UV0Dimension, areaMesh.Triangles, areaRequiresTangents, areaUsesSourceTangents);
                    }
                    else
                    {
                        if (preparedArea.MaterialIndex != materials.Count)
                        {
                            return PreparedConversionFallback(renderPart, sourcePart, "prepared material/submesh ordering changed");
                        }

                        materials.Add(material);
                        requiresTangents |= areaRequiresTangents;
                        if (areaRequiresTangents && !areaUsesSourceTangents)
                        {
                            canUseCombinedSourceTangents = false;
                        }
                    }
                }

                if (materials.Count != preparation.SubmeshTriangles.Length)
                {
                    return PreparedConversionFallback(renderPart, sourcePart, "prepared submesh count changed");
                }

                renderPart.CombinedVertices = preparation.CombinedVertices;
                renderPart.CombinedNormals = preparation.CombinedNormals;
                renderPart.CombinedUVs = preparation.CombinedUVs;
                renderPart.CombinedTangents = preparation.CombinedTangents;
                renderPart.CombinedColors = preparation.CombinedColors;
                renderPart.CombinedTexCoord1 = preparation.CombinedTexCoord1;
                renderPart.CombinedTexCoord3 = preparation.CombinedTexCoord3;
                renderPart.CombinedUV0Dimension = preparation.CombinedUV0Dimension;
                renderPart.HasTangentStream = requiresTangents;
                renderPart.UsesSourceTangents = requiresTangents && canUseCombinedSourceTangents && renderPart.CombinedTangents.Length == renderPart.CombinedVertices.Length;
                WriteCombinedUnityMesh(renderPart.Mesh, renderPart.CombinedVertices, renderPart.CombinedNormals, renderPart.CombinedUVs, renderPart.CombinedTangents, renderPart.CombinedColors, renderPart.CombinedTexCoord1, renderPart.CombinedTexCoord3, renderPart.CombinedUV0Dimension, preparation.SubmeshTriangles, requiresTangents, renderPart.UsesSourceTangents);
                renderPart.Renderer.sharedMaterials = materials.ToArray();

                renderPart.HasMainRendererGeometry = materials.Count > 0;
                renderPart.GeometryVersion = sourcePart.GeometryVersion;
                renderPart.PositionVersion = sourcePart.PositionVersion;
                renderPart.FullGeometryVersion = sourcePart.FullGeometryVersion;
                renderPart.DirectionVersion = sourcePart.DirectionVersion;
                renderPart.TopologyVersion = sourcePart.TopologyVersion;
                renderPart.SurfaceAreaMembershipVersion = sourcePart.SurfaceAreaMembershipVersion;
                renderPart.MappingScale = objectScale;
                PrepareGeometryUpdates(renderPart, sourcePart, preparation.GeometryUpdates);
                preparation.GeometryUpdates = null;

                ApplyPartPropertyBlocks(renderPart, hovered);

                return true;
            }
            catch
            {
                ReleaseRenderAreas(renderPart);
                throw;
            }
        }

        private bool PreparedConversionFallback(RenderPart renderPart, MeshPart sourcePart, string reason)
        {
            ReleaseRenderAreas(renderPart);
            Mod.log.Warn($"[PO Renderer] Prepared conversion renderer for " + $"'{sourcePart?.Name ?? "<unknown>"}' was not reusable " + $"({reason}); falling back to the normal rebuild path.");
            return false;
        }

        private void RebuildRenderPart(RenderPart renderPart, MeshPart sourcePart, Vector3 objectScale, bool hovered)
        {
            // Rebuilding also drops material references and any child renderers
            // owned by the previous Surface Area layout.
            ReleaseRenderAreas(renderPart);

            List<Vector3> combinedVertices = new List<Vector3>();
            List<Vector3> combinedNormals = new List<Vector3>();
            List<Vector4> combinedUVs = new List<Vector4>();
            List<Vector4> combinedTangents = new List<Vector4>();
            List<Color32> combinedColors = new List<Color32>();
            List<Vector4> combinedTexCoord1 = new List<Vector4>();
            List<Vector4> combinedTexCoord3 = new List<Vector4>();
            List<int[]> submeshTriangles = new List<int[]>();
            List<Material> materials = new List<Material>();

            bool requiresTangents = false;
            bool canUseCombinedSourceTangents = true;
            bool hasCombinedVertexColors = false;
            bool hasCombinedTexCoord1 = false;
            bool hasCombinedTexCoord3 = false;
            int combinedUV0Dimension = 2;

            var areaGroups = TopologyRendererWork.GroupTriangles(sourcePart.TriangleSurfaceAreaIds, default);
            var geometry = new SurfaceAreaGeometryBuilder(sourcePart.Mesh);
            for (int areaIndex = 0; areaIndex < sourcePart.SurfaceAreas.Count; areaIndex++)
            {
                SurfaceArea sourceArea = sourcePart.SurfaceAreas[areaIndex];

                int[] sourceTriangleIds = areaGroups.TryGetValue(sourceArea.Id, out var group) ? group : Array.Empty<int>();

                if (sourceTriangleIds.Length == 0)
                {
                    continue;
                }

                // Glass needs a separate renderer to avoid camera-dependent water artifacts.
                // Windows also need isolated vertex streams. Surface Area data stays unchanged.
                bool isolateArea = RequiresIsolatedRenderer(sourceArea.Material);

                geometry.Build(sourceArea, sourceTriangleIds, out var preparedMesh, out var sourceIndices);
                var areaBuild = new SurfaceAreaMeshBuildResult { Mesh = preparedMesh, SourceVertexIndices = sourceIndices };
                MeshData areaMesh = areaBuild.Mesh;

                MeshTopology areaTopology = null;
                Vector3[] areaVertices;
                Vector3[] areaNormals;
                Vector4[] areaUVs;
                Vector4[] areaTangents;
                Color32[] areaColors;
                Vector4[] areaTexCoord1;
                Vector4[] areaTexCoord3;
                int[] areaTriangles;
                GeneratedMapping generatedMapping = null;

                // Generated mapping may split vertices by UV chart.
                // Original mapping preserves all authored streams exactly as imported.
                if (IsGeneratedMappingMode(sourceArea.Material.Mapping.Mode))
                {
                    generatedMapping = GeneratedMapping.Create(areaMesh, sourceArea.SourceVertexAttributes, sourceArea.MappingSeed, areaBuild.SourceVertexIndices, out areaTopology);

                    generatedMapping.Update(areaMesh, areaTopology, objectScale, sourceArea.Material.Mapping, sourceArea.Material.GetEffectiveTileSizeMeters(), true);

                    areaVertices = generatedMapping.Vertices;
                    areaNormals = generatedMapping.Normals;
                    areaUVs = ConvertUV2ToUV4(generatedMapping.UVs);
                    areaTangents = null;
                    areaColors = null;
                    areaTexCoord1 = null;
                    areaTexCoord3 = null;
                    areaTriangles = generatedMapping.Triangles;
                }
                else
                {
                    areaVertices = areaMesh.Vertices;
                    areaNormals = areaMesh.Normals;
                    areaUVs = areaMesh.UV0;
                    areaTangents = areaMesh.Tangents;
                    areaColors = areaMesh.VertexColors;
                    areaTexCoord1 = areaMesh.TexCoord1;
                    areaTexCoord3 = areaMesh.TexCoord3;
                    areaTriangles = areaMesh.Triangles;
                }

                AssetHash128 resolvedSurfaceGuid = ResolveNativeSurfaceGuid(sourceArea.Material);
                Material material = AcquireMaterial(sourceArea.Material, renderPart.ReceivesDecals);
                bool areaRequiresTangents = RequiresTangentSpace(sourceArea.Material);
                bool areaUsesSourceTangents = !IsGeneratedMappingMode(sourceArea.Material.Mapping.Mode) && areaTangents != null && areaTangents.Length == areaVertices.Length;

                RenderArea renderArea = new RenderArea
                {
                    SurfaceAreaId = sourceArea.Id,
                    MaterialIndex = -1,
                    Material = material,
                    ReceivesDecals = renderPart.ReceivesDecals,
                    MaterialSourceVersion = sourceArea.MaterialSourceVersion,
                    MaterialPropertiesVersion = sourceArea.MaterialPropertiesVersion,
                    MappingVersion = sourceArea.MappingVersion,
                    ColorMaskVersion = sourceArea.ColorMaskVersion,
                    MaterialSourceKind = sourceArea.Material.SourceKind,
                    DefinitionId = sourceArea.Material.DefinitionId,
                    SurfaceGuid = sourceArea.Material.SurfaceGuid,
                    VanillaSpecialKind = sourceArea.Material.VanillaSpecialKind,
                    ResolvedSurfaceGuid = resolvedSurfaceGuid,
                    Metallic = sourceArea.Material.Metallic,
                    Smoothness = sourceArea.Material.Smoothness,
                    CoatStrength = sourceArea.Material.CoatStrength,
                    MappingMode = sourceArea.Material.Mapping.Mode,
                    AreaMesh = areaMesh,
                    SourceVertexIndices = areaBuild.SourceVertexIndices,
                    AreaTopology = areaTopology,
                    GeneratedMapping = generatedMapping,
                    CombinedVertexOffset = -1,
                    CombinedVertexCount = areaVertices.Length,
                    IsolatedRenderer = isolateArea,
                    UsesSourceTangents = areaUsesSourceTangents,
                    UV0Dimension = generatedMapping != null ? 2 : GetSurfaceAreaUV0Dimension(sourceArea),
                };
                CopyColorMaskState(renderArea, sourceArea.ColorMask);

                if (isolateArea)
                {
                    CreateIsolatedAreaRenderer(renderPart, renderArea, sourcePart.Name, areaVertices, areaNormals, areaUVs, areaTangents, areaColors, areaTexCoord1, areaTexCoord3, renderArea.UV0Dimension, areaTriangles, areaRequiresTangents, areaUsesSourceTangents);
                }
                else
                {
                    int vertexOffset = combinedVertices.Count;
                    // Ordinary areas share one MeshRenderer as submeshes.
                    // This avoids a GameObject/renderer per material in the common case.
                    AppendAreaVertices(combinedVertices, combinedNormals, combinedUVs, combinedTangents, combinedColors, combinedTexCoord1, combinedTexCoord3, areaVertices, areaNormals, areaUVs, areaTangents, areaColors, areaTexCoord1, areaTexCoord3);

                    int[] adjustedTriangles = new int[areaTriangles.Length];
                    for (int i = 0; i < areaTriangles.Length; i++)
                    {
                        adjustedTriangles[i] = areaTriangles[i] + vertexOffset;
                    }

                    int materialIndex = materials.Count;
                    materials.Add(material);
                    submeshTriangles.Add(adjustedTriangles);
                    renderArea.MaterialIndex = materialIndex;
                    renderArea.CombinedVertexOffset = vertexOffset;
                    requiresTangents |= areaRequiresTangents;
                    if (areaRequiresTangents && !areaUsesSourceTangents)
                    {
                        canUseCombinedSourceTangents = false;
                    }

                    hasCombinedVertexColors |= areaColors != null && areaColors.Length == areaVertices.Length;
                    hasCombinedTexCoord1 |= areaTexCoord1 != null && areaTexCoord1.Length == areaVertices.Length;
                    hasCombinedTexCoord3 |= areaTexCoord3 != null && areaTexCoord3.Length == areaVertices.Length;
                    if (renderArea.UV0Dimension > combinedUV0Dimension)
                    {
                        combinedUV0Dimension = renderArea.UV0Dimension;
                    }
                }

                renderPart.Areas.Add(renderArea);
            }

            renderPart.CombinedVertices = combinedVertices.ToArray();
            renderPart.CombinedNormals = combinedNormals.ToArray();
            renderPart.CombinedUVs = combinedUVs.ToArray();
            renderPart.CombinedTangents = combinedTangents.ToArray();
            renderPart.CombinedColors = hasCombinedVertexColors ? combinedColors.ToArray() : Array.Empty<Color32>();
            renderPart.CombinedTexCoord1 = hasCombinedTexCoord1 ? combinedTexCoord1.ToArray() : Array.Empty<Vector4>();
            renderPart.CombinedTexCoord3 = hasCombinedTexCoord3 ? combinedTexCoord3.ToArray() : Array.Empty<Vector4>();
            renderPart.HasTangentStream = requiresTangents;
            renderPart.UsesSourceTangents = requiresTangents && canUseCombinedSourceTangents && renderPart.CombinedTangents.Length == renderPart.CombinedVertices.Length;
            renderPart.CombinedUV0Dimension = combinedUV0Dimension;
            WriteCombinedUnityMesh(renderPart.Mesh, renderPart.CombinedVertices, renderPart.CombinedNormals, renderPart.CombinedUVs, renderPart.CombinedTangents, renderPart.CombinedColors, renderPart.CombinedTexCoord1, renderPart.CombinedTexCoord3, renderPart.CombinedUV0Dimension, submeshTriangles, requiresTangents, renderPart.UsesSourceTangents);

            renderPart.Renderer.sharedMaterials = materials.ToArray();
            renderPart.HasMainRendererGeometry = materials.Count > 0;
            renderPart.GeometryVersion = sourcePart.GeometryVersion;
            renderPart.PositionVersion = sourcePart.PositionVersion;
            renderPart.FullGeometryVersion = sourcePart.FullGeometryVersion;
            renderPart.DirectionVersion = sourcePart.DirectionVersion;
            renderPart.TopologyVersion = sourcePart.TopologyVersion;
            renderPart.SurfaceAreaMembershipVersion = sourcePart.SurfaceAreaMembershipVersion;
            renderPart.MappingScale = objectScale;
            PrepareGeometryUpdates(renderPart, sourcePart);

            ApplyPartPropertyBlocks(renderPart, hovered);
        }

        private static int[] GetSurfaceAreaTriangleIds(MeshPart part, int surfaceAreaId)
        {
            List<int> triangleIds = new List<int>();

            for (int triangleId = 0; triangleId < part.TriangleSurfaceAreaIds.Count; triangleId++)
            {
                if (part.TriangleSurfaceAreaIds[triangleId] == surfaceAreaId)
                {
                    triangleIds.Add(triangleId);
                }
            }

            return triangleIds.ToArray();
        }

        private static void AppendAreaVertices(List<Vector3> combinedVertices, List<Vector3> combinedNormals, List<Vector4> combinedUVs, List<Vector4> combinedTangents, List<Color32> combinedColors, List<Vector4> combinedTexCoord1, List<Vector4> combinedTexCoord3, Vector3[] vertices, Vector3[] normals, Vector4[] uvs, Vector4[] tangents, Color32[] vertexColors, Vector4[] texCoord1, Vector4[] texCoord3)
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

        private static void CreateIsolatedAreaRenderer(RenderPart renderPart, RenderArea renderArea, string sourcePartName, Vector3[] vertices, Vector3[] normals, Vector4[] uvs, Vector4[] tangents, Color32[] vertexColors, Vector4[] texCoord1, Vector4[] texCoord3, int uv0Dimension, int[] triangles, bool requiresTangents, bool useSourceTangents)
        {
            GameObject areaObject = new GameObject($"Isolated Surface {renderArea.SurfaceAreaId}: {sourcePartName}");
            areaObject.transform.SetParent(renderPart.GameObject.transform, false);

            MeshFilter meshFilter = areaObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = areaObject.AddComponent<MeshRenderer>();
            Mesh mesh = new Mesh
            {
                name = $"PO2 Isolated Surface {renderArea.SurfaceAreaId}",
            };
            mesh.MarkDynamic();
            meshFilter.sharedMesh = mesh;

            WriteSingleAreaUnityMesh(mesh, vertices, normals, uvs, tangents, vertexColors, texCoord1, texCoord3, uv0Dimension, triangles, requiresTangents, useSourceTangents);

            meshRenderer.sharedMaterial = renderArea.Material;
            renderArea.IsolatedGameObject = areaObject;
            renderArea.IsolatedMesh = mesh;
            renderArea.IsolatedRendererComponent = meshRenderer;
        }

        private static int GetSurfaceAreaUV0Dimension(SurfaceArea surfaceArea)
        {
            return (surfaceArea.SourceVertexAttributes & VertexAttributeFlags.UV0FourComponent) != 0 ? 4 : 2;
        }

        private bool RequiresIsolatedRenderer(MaterialData materialData)
        {
            // Give glass and windows separate child renderers for their shader and stream requirements.
            if (materialData != null && materialData.SourceKind == MaterialSourceKind.VanillaSpecial && VanillaSpecialMaterialLibrary.TryGet(materialData.VanillaSpecialKind, out VanillaSpecialMaterialDefinition definition))
            {
                return definition.RequiresIsolatedRenderer;
            }

            return IsGlassSurface(materialData) || IsWindowSurface(materialData);
        }

        private static bool RequiresTangentSpace(MaterialData material)
        {
            if (material.SourceKind == MaterialSourceKind.SurfaceAsset || material.SourceKind == MaterialSourceKind.VanillaSpecial)
            {
                return true;
            }

            return MaterialLibrary.TryGet(material.DefinitionId, out MaterialDefinition definition) && definition.NormalMode == MaterialNormalMode.TangentSpace;
        }

        private static bool IsGeneratedMappingMode(TextureMappingMode mode)
        {
            return mode == TextureMappingMode.Repeat || mode == TextureMappingMode.Stretch;
        }
    }
}