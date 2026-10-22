using MeshData = PO2.Geometry.MeshData;
using System;
using Game.Rendering;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using PO2.Geometry;
using PO2.Materials;
using PO2.Rendering;
using PO2.Rendering.Mapping;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        private Texture2D m_MappingDiagnosticTexture;

        private bool ActiveSurfaceAreaStructureMatches(RenderPart renderPart, MeshPart sourcePart)
        {
            // Empty Surface Areas have no renderer entry.
            // Compare only areas that currently own triangles or every empty group forces rebuilds.
            int activeSourceAreaCount = 0;

            for (int i = 0; i < sourcePart.SurfaceAreas.Count; i++)
            {
                SurfaceArea sourceArea = sourcePart.SurfaceAreas[i];
                if (sourcePart.GetSurfaceAreaTriangleCount(sourceArea.Id) == 0)
                {
                    continue;
                }

                activeSourceAreaCount++;
                RenderArea renderArea = FindRenderArea(renderPart, sourceArea.Id);
                if (renderArea == null)
                {
                    return false;
                }

                bool shouldIsolate = RequiresIsolatedRenderer(sourceArea.Material);
                if (renderArea.IsolatedRenderer != shouldIsolate)
                {
                    return false;
                }
            }

            return activeSourceAreaCount == renderPart.Areas.Count;
        }

        private bool HasMappingStructureTransition(RenderPart renderPart, MeshPart sourcePart)
        {
            for (int i = 0; i < sourcePart.SurfaceAreas.Count; i++)
            {
                SurfaceArea sourceArea = sourcePart.SurfaceAreas[i];
                if (sourcePart.GetSurfaceAreaTriangleCount(sourceArea.Id) == 0)
                {
                    continue;
                }

                RenderArea renderArea = FindRenderArea(renderPart, sourceArea.Id);
                if (renderArea == null)
                {
                    return true;
                }

                bool sourceGenerated = IsGeneratedMappingMode(sourceArea.Material.Mapping.Mode);
                bool renderGenerated = renderArea.GeneratedMapping != null;
                if (sourceGenerated != renderGenerated)
                {
                    return true;
                }
            }

            return false;
        }

        private bool RefreshDerivedGeometry(RenderPart renderPart, MeshPart sourcePart, Vector3 objectScale, bool geometryChanged, out bool meshStreamsChanged)
        {
            meshStreamsChanged = false;

            // Area meshes contain compact copies of their source vertices.
            // Refresh those first, then regenerate UV charts or copy streams
            // into the combined/isolated Unity mesh.
            for (int i = 0; i < renderPart.Areas.Count; i++)
            {
                RenderArea renderArea = renderPart.Areas[i];
                if (!sourcePart.TryGetSurfaceArea(renderArea.SurfaceAreaId, out SurfaceArea sourceArea))
                {
                    return false;
                }

                if (!RefreshAreaMeshSourceStreams(renderArea, sourceArea, sourcePart.Mesh))
                {
                    return false;
                }

                if (renderArea.GeneratedMapping != null)
                {
                    bool structureChanged = renderArea.GeneratedMapping.Update(renderArea.AreaMesh, renderArea.AreaTopology, objectScale, sourceArea.Material.Mapping, sourceArea.Material.GetEffectiveTileSizeMeters(), false);

                    if (structureChanged || renderArea.GeneratedMapping.Vertices.Length != renderArea.CombinedVertexCount)
                    {
                        return false;
                    }

                    if (renderArea.IsolatedRenderer)
                    {
                        WriteIsolatedAreaStreams(renderArea, renderArea.GeneratedMapping.Vertices, renderArea.GeneratedMapping.Normals, ConvertUV2ToUV4(renderArea.GeneratedMapping.UVs), null, null, null, null, RequiresTangentSpace(sourceArea.Material), false);
                    }
                    else
                    {
                        CopyAreaStreams(renderPart, renderArea, renderArea.GeneratedMapping.Vertices, renderArea.GeneratedMapping.Normals, renderArea.GeneratedMapping.UVs);
                        meshStreamsChanged = true;
                    }
                }
                else if (geometryChanged)
                {
                    if (renderArea.AreaMesh.Vertices.Length != renderArea.CombinedVertexCount)
                    {
                        return false;
                    }

                    if (renderArea.IsolatedRenderer)
                    {
                        WriteIsolatedAreaStreams(renderArea, renderArea.AreaMesh.Vertices, renderArea.AreaMesh.Normals, renderArea.AreaMesh.UV0, renderArea.AreaMesh.Tangents, renderArea.AreaMesh.VertexColors, renderArea.AreaMesh.TexCoord1, renderArea.AreaMesh.TexCoord3, RequiresTangentSpace(sourceArea.Material), renderArea.UsesSourceTangents);
                    }
                    else
                    {
                        CopyAreaStreams(renderPart, renderArea, renderArea.AreaMesh.Vertices, renderArea.AreaMesh.Normals, renderArea.AreaMesh.UV0);
                        meshStreamsChanged = true;
                    }
                }
            }

            return true;
        }

        private static bool TryRefreshPositionOnlyGeometry(RenderPart renderPart, MeshPart sourcePart)
        {
            return RefreshWholeSourceGeometry(renderPart, sourcePart, true, false);
        }

        private static bool TryRefreshSourceGeometry(RenderPart renderPart, MeshPart sourcePart, bool positionsChanged)
        {
            return RefreshWholeSourceGeometry(renderPart, sourcePart, positionsChanged, true);
        }

        private bool RefreshAreaState(RenderPart renderPart, MeshPart sourcePart, Vector3 objectScale, bool hovered, ref bool meshStreamsChanged)
        {
            // Source, scalar properties, mapping, and color masks have separate
            // versions so editing one slider does not rebuild mesh structure.
            bool materialArrayChanged = false;
            bool propertyBlocksChanged = false;
            bool tangentRefreshNeeded = false;

            for (int i = 0; i < sourcePart.SurfaceAreas.Count; i++)
            {
                SurfaceArea sourceArea = sourcePart.SurfaceAreas[i];
                if (sourcePart.GetSurfaceAreaTriangleCount(sourceArea.Id) == 0)
                {
                    continue;
                }

                RenderArea renderArea = FindRenderArea(renderPart, sourceArea.Id);
                if (renderArea == null)
                {
                    return false;
                }

                if (renderArea.MappingVersion != sourceArea.MappingVersion)
                {
                    if (!RefreshMappingState(renderPart, renderArea, sourceArea, objectScale, out bool mappingStreamsChanged))
                    {
                        return false;
                    }

                    meshStreamsChanged |= mappingStreamsChanged;
                }

                if (renderArea.MaterialSourceVersion != sourceArea.MaterialSourceVersion)
                {
                    bool sourceRequiresTangents = RequiresTangentSpace(sourceArea.Material);

                    ReleaseMaterial(renderArea);
                    renderArea.MaterialSourceKind = sourceArea.Material.SourceKind;
                    renderArea.DefinitionId = sourceArea.Material.DefinitionId;
                    renderArea.SurfaceGuid = sourceArea.Material.SurfaceGuid;
                    renderArea.VanillaSpecialKind = sourceArea.Material.VanillaSpecialKind;
                    renderArea.ResolvedSurfaceGuid = ResolveNativeSurfaceGuid(sourceArea.Material);
                    renderArea.Material = AcquireMaterial(sourceArea.Material, renderPart.ReceivesDecals);
                    renderArea.ReceivesDecals = renderPart.ReceivesDecals;
                    renderArea.MaterialSourceVersion = sourceArea.MaterialSourceVersion;
                    renderArea.MaterialPropertiesVersion = sourceArea.MaterialPropertiesVersion;
                    renderArea.Metallic = sourceArea.Material.Metallic;
                    renderArea.Smoothness = sourceArea.Material.Smoothness;
                    renderArea.CoatStrength = sourceArea.Material.CoatStrength;

                    if (renderArea.GeneratedMapping != null)
                    {
                        renderArea.GeneratedMapping.UpdateMappingParameters(renderArea.AreaMesh, objectScale, sourceArea.Material.Mapping, sourceArea.Material.GetEffectiveTileSizeMeters());

                        if (renderArea.GeneratedMapping.Vertices.Length != renderArea.CombinedVertexCount)
                        {
                            return false;
                        }

                        if (renderArea.IsolatedRenderer)
                        {
                            WriteIsolatedAreaStreams(renderArea, renderArea.GeneratedMapping.Vertices, renderArea.GeneratedMapping.Normals, ConvertUV2ToUV4(renderArea.GeneratedMapping.UVs), null, null, null, null, sourceRequiresTangents, false);
                        }
                        else
                        {
                            CopyAreaStreams(renderPart, renderArea, renderArea.GeneratedMapping.Vertices, renderArea.GeneratedMapping.Normals, renderArea.GeneratedMapping.UVs);
                            meshStreamsChanged = true;
                        }
                    }

                    if (renderArea.IsolatedRenderer)
                    {
                        if (renderArea.IsolatedRendererComponent != null)
                        {
                            renderArea.IsolatedRendererComponent.sharedMaterial = renderArea.Material;
                        }
                    }
                    else
                    {
                        materialArrayChanged = true;
                        tangentRefreshNeeded |= sourceRequiresTangents && !renderPart.HasTangentStream;
                    }

                    propertyBlocksChanged = true;
                }
                else if (renderArea.MaterialPropertiesVersion != sourceArea.MaterialPropertiesVersion)
                {
                    renderArea.MaterialPropertiesVersion = sourceArea.MaterialPropertiesVersion;
                    renderArea.Metallic = sourceArea.Material.Metallic;
                    renderArea.Smoothness = sourceArea.Material.Smoothness;
                    renderArea.CoatStrength = sourceArea.Material.CoatStrength;
                    propertyBlocksChanged = true;
                }

                if (renderArea.ColorMaskVersion != sourceArea.ColorMaskVersion)
                {
                    CopyColorMaskState(renderArea, sourceArea.ColorMask);
                    renderArea.ColorMaskVersion = sourceArea.ColorMaskVersion;
                    propertyBlocksChanged = true;
                }
            }

            if (materialArrayChanged)
            {
                ApplyMainRendererMaterials(renderPart);
            }

            if (tangentRefreshNeeded)
            {
                renderPart.HasTangentStream = true;
                if (!meshStreamsChanged && renderPart.CombinedVertices.Length > 0)
                {
                    if (renderPart.UsesSourceTangents && renderPart.CombinedTangents.Length == renderPart.CombinedVertices.Length)
                    {
                        renderPart.Mesh.tangents = renderPart.CombinedTangents;
                    }
                    else
                    {
                        RenderVertexStreams.RecalculateTangents(renderPart.Mesh);
                        renderPart.Mesh.MarkModified();
                    }
                }
            }

            if (propertyBlocksChanged || materialArrayChanged)
            {
                ApplyPartPropertyBlocks(renderPart, hovered);
            }

            return true;
        }

        private static bool RefreshMappingState(RenderPart renderPart, RenderArea renderArea, SurfaceArea sourceArea, Vector3 objectScale, out bool meshStreamsChanged)
        {
            meshStreamsChanged = false;
            TextureMappingMode sourceMode = sourceArea.Material.Mapping.Mode;

            // Crossing Original <--> generated mapping changes vertex structure
            // and is rejected here so Sync falls back to RebuildRenderPart.
            if (renderArea.GeneratedMapping == null)
            {
                renderArea.MappingVersion = sourceArea.MappingVersion;
                renderArea.MappingMode = sourceMode;
                return true;
            }

            if (!IsGeneratedMappingMode(sourceMode))
            {
                return false;
            }

            renderArea.GeneratedMapping.UpdateMappingParameters(renderArea.AreaMesh, objectScale, sourceArea.Material.Mapping, sourceArea.Material.GetEffectiveTileSizeMeters());

            if (renderArea.GeneratedMapping.Vertices.Length != renderArea.CombinedVertexCount)
            {
                return false;
            }

            if (renderArea.IsolatedRenderer)
            {
                WriteIsolatedAreaStreams(renderArea, renderArea.GeneratedMapping.Vertices, renderArea.GeneratedMapping.Normals, ConvertUV2ToUV4(renderArea.GeneratedMapping.UVs), null, null, null, null, RequiresTangentSpace(sourceArea.Material), false);
            }
            else
            {
                CopyAreaStreams(renderPart, renderArea, renderArea.GeneratedMapping.Vertices, renderArea.GeneratedMapping.Normals, renderArea.GeneratedMapping.UVs);
                meshStreamsChanged = true;
            }

            renderArea.MappingVersion = sourceArea.MappingVersion;
            renderArea.MappingMode = sourceMode;
            return true;
        }

        private static bool RefreshAreaMeshSourceStreams(RenderArea renderArea, SurfaceArea sourceArea, MeshData sourceMesh)
        {
            MeshData areaMesh = renderArea.AreaMesh;
            int[] sourceVertexIndices = renderArea.SourceVertexIndices;

            if (areaMesh == null || sourceVertexIndices == null || sourceVertexIndices.Length != areaMesh.Vertices.Length)
            {
                return false;
            }

            bool sourceHasNormals = sourceMesh.Normals != null && sourceMesh.Normals.Length == sourceMesh.Vertices.Length;
            bool sourceHasTangents = sourceMesh.Tangents != null && sourceMesh.Tangents.Length == sourceMesh.Vertices.Length;
            bool sourceHasUV0 = sourceMesh.UV0 != null && sourceMesh.UV0.Length == sourceMesh.Vertices.Length;
            bool sourceHasVertexColors = (sourceArea.SourceVertexAttributes & VertexAttributeFlags.VertexColor) != 0 && sourceMesh.VertexColors != null && sourceMesh.VertexColors.Length == sourceMesh.Vertices.Length;
            bool sourceHasTexCoord1 = (sourceArea.SourceVertexAttributes & VertexAttributeFlags.TexCoord1) != 0 && sourceMesh.TexCoord1 != null && sourceMesh.TexCoord1.Length == sourceMesh.Vertices.Length;
            bool sourceHasTexCoord3 = (sourceArea.SourceVertexAttributes & VertexAttributeFlags.TexCoord3) != 0 && sourceMesh.TexCoord3 != null && sourceMesh.TexCoord3.Length == sourceMesh.Vertices.Length;

            // Stream appearance/disappearance is structural for this cache.
            // Returning false lets the caller rebuild arrays with the new shape.
            if ((areaMesh.Normals != null) != sourceHasNormals || (areaMesh.Tangents != null) != sourceHasTangents || (areaMesh.UV0 != null) != sourceHasUV0 || (areaMesh.VertexColors != null) != sourceHasVertexColors || (areaMesh.TexCoord1 != null) != sourceHasTexCoord1 || (areaMesh.TexCoord3 != null) != sourceHasTexCoord3)
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

                if (sourceHasNormals)
                {
                    areaMesh.Normals[i] = sourceMesh.Normals[sourceVertexIndex];
                }

                if (sourceHasTangents)
                {
                    areaMesh.Tangents[i] = sourceMesh.Tangents[sourceVertexIndex];
                }

                if (sourceHasUV0)
                {
                    areaMesh.UV0[i] = sourceMesh.UV0[sourceVertexIndex];
                }

                if (sourceHasVertexColors)
                {
                    areaMesh.VertexColors[i] = sourceMesh.VertexColors[sourceVertexIndex];
                }

                if (sourceHasTexCoord1)
                {
                    areaMesh.TexCoord1[i] = sourceMesh.TexCoord1[sourceVertexIndex];
                }

                if (sourceHasTexCoord3)
                {
                    areaMesh.TexCoord3[i] = sourceMesh.TexCoord3[sourceVertexIndex];
                }
            }

            return true;
        }

        private static RenderArea FindRenderArea(RenderPart renderPart, int surfaceAreaId)
        {
            for (int i = 0; i < renderPart.Areas.Count; i++)
            {
                if (renderPart.Areas[i].SurfaceAreaId == surfaceAreaId)
                {
                    return renderPart.Areas[i];
                }
            }

            return null;
        }

        private void ReleaseRenderAreas(RenderPart renderPart)
        {
            ReleaseGeometryUpdates(renderPart);
            for (int i = 0; i < renderPart.Areas.Count; i++)
            {
                RenderArea area = renderPart.Areas[i];
                ReleaseMaterial(area);

                if (area.IsolatedMesh != null)
                {
                    UnityEngine.Object.Destroy(area.IsolatedMesh);
                    area.IsolatedMesh = null;
                }

                if (area.IsolatedGameObject != null)
                {
                    UnityEngine.Object.Destroy(area.IsolatedGameObject);
                    area.IsolatedGameObject = null;
                }
            }

            renderPart.Areas.Clear();
            if (renderPart.Renderer != null)
            {
                renderPart.Renderer.sharedMaterials = Array.Empty<Material>();
            }

            renderPart.HasMainRendererGeometry = false;
        }

        private Texture2D GetMappingDiagnosticTexture()
        {
            if (m_MappingDiagnosticTexture != null)
            {
                return m_MappingDiagnosticTexture;
            }

            const int size = 64;
            const int grid = 16;

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true);

            texture.name = "PO2 Alignment Grid";
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear;

            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool gridLine = x % grid <= 1 || y % grid <= 1;
                    bool majorU = x >= 4 && x <= 7;
                    bool directionMark = y > size / 2 && x > size / 2 && x - size / 2 > y - size / 2 - 4 && x - size / 2 < y - size / 2 + 4;

                    float value = gridLine ? 0.20f : majorU ? 0.38f : directionMark ? 0.52f : 0.88f;

                    pixels[y * size + x] = new Color(value, value, value, 1f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true, false);

            m_MappingDiagnosticTexture = texture;
            return texture;
        }
    }
}