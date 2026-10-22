using System;
using System.Collections.Generic;
using System.IO;
using Colossal.IO.AssetDatabase;
using Colossal.IO.AssetDatabase.VirtualTexturing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using PO2.Conversion;
using PO2.Core;
using PO2.Geometry;
using PO2.Rendering;

namespace PO2.Rendering.Objects
{
    // Builds Unity rendering objects from PO data. Cached resources can be discarded;
    // Sync uses the PO's versions to decide what needs rebuilding.
    public sealed partial class ProceduralObjectRenderer : IDisposable
    {
        private readonly Dictionary<int, RenderInstance> m_Instances = new Dictionary<int, RenderInstance>();

        public ProceduralObjectRenderer(TextureStreamingSystem textureStreamingSystem, string modRootPath)
        {
            m_TextureStreamingSystem = textureStreamingSystem;
            m_ModRootPath = modRootPath;
        }

        public void Create(ProceduralObject proceduralObject)
        {
            Create(proceduralObject, null);
        }

        internal void Create(ProceduralObject proceduralObject, ConvertedRenderPreparation conversionPreparation)
        {
            if (m_Instances.ContainsKey(proceduralObject.Id))
            {
                Destroy(proceduralObject.Id);
            }

            RenderInstance instance = new RenderInstance
            {
                ObjectId = proceduralObject.Id,
                Source = proceduralObject,
                Root = new GameObject($"PO2 Object {proceduralObject.Id}"),
            };

            for (int i = 0; i < proceduralObject.Parts.Count; i++)
            {
                MeshPart part = proceduralObject.Parts[i];

                GameObject partObject = new GameObject($"Part {i}: {part.Name}");
                partObject.transform.SetParent(instance.Root.transform, false);

                MeshFilter meshFilter = partObject.AddComponent<MeshFilter>();
                MeshRenderer meshRenderer = partObject.AddComponent<MeshRenderer>();
                Mesh mesh = new Mesh
                {
                    name = $"PO2 Mesh {proceduralObject.Id}:{i}",
                };
                mesh.MarkDynamic();
                meshFilter.sharedMesh = mesh;

                RenderPart renderPart = new RenderPart
                {
                    GameObject = partObject,
                    Mesh = mesh,
                    PickSource = part,
                    Renderer = meshRenderer,
                    ReceivesDecals = proceduralObject.ReceivesDecals,
                };

                instance.Parts.Add(renderPart);

                ConvertedRenderPartPreparation preparedPart = conversionPreparation != null && conversionPreparation.Parts != null && i < conversionPreparation.Parts.Length ? conversionPreparation.Parts[i] : null;

                if (preparedPart == null || !TryRebuildPreparedConversionPart(renderPart, part, proceduralObject.Transform.Scale, false, preparedPart))
                {
                    RebuildRenderPart(renderPart, part, proceduralObject.Transform.Scale, false);
                }
            }

            m_Instances.Add(proceduralObject.Id, instance);
            m_PickSceneVersion++;
            ApplyTransform(instance, proceduralObject.Transform);
            proceduralObject.ClearDirty(ProceduralObjectDirtyFlags.Transform | ProceduralObjectDirtyFlags.Geometry | ProceduralObjectDirtyFlags.Topology | ProceduralObjectDirtyFlags.Material);
        }

        public void Sync(ProceduralObject proceduralObject)
        {
            SyncCore(proceduralObject);
        }

        private void SyncCore(ProceduralObject proceduralObject)
        {
            if (!m_Instances.TryGetValue(proceduralObject.Id, out RenderInstance instance))
            {
                Create(proceduralObject);
                return;
            }

            // Part count changes are rare and invalidate basically every index
            // in the render cache, so recreating the instance is safer/cheaper.
            if (!ReferenceEquals(instance.Source, proceduralObject) || instance.Parts.Count != proceduralObject.Parts.Count)
            {
                bool hovered = instance.Hovered;
                Destroy(proceduralObject.Id);
                Create(proceduralObject);
                SetHovered(proceduralObject.Id, hovered, instance.DeleteHovered);
                return;
            }

            instance.Source = proceduralObject;
            m_PickSceneVersion++;
            ProceduralObjectDirtyFlags dirty = proceduralObject.DirtyFlags;
            bool transformChanged = (dirty & ProceduralObjectDirtyFlags.Transform) != 0;

            if (transformChanged)
            {
                ApplyTransform(instance, proceduralObject.Transform);
            }

            for (int i = 0; i < proceduralObject.Parts.Count; i++)
            {
                MeshPart sourcePart = proceduralObject.Parts[i];
                RenderPart renderPart = instance.Parts[i];
                renderPart.DeleteHovered = instance.DeleteHovered;
                bool sourcePartChanged = !ReferenceEquals(renderPart.PickSource, sourcePart);
                renderPart.PickSource = sourcePart;
                Vector3 objectScale = proceduralObject.Transform.Scale;
                bool decalReceptionChanged = renderPart.ReceivesDecals != proceduralObject.ReceivesDecals;
                if (decalReceptionChanged)
                {
                    renderPart.ReceivesDecals = proceduralObject.ReceivesDecals;
                }

                // Structural changes can alter submeshes, renderer isolation,
                // or generated-UV vertex splits. Those need a full part rebuild.
                bool structureChanged = sourcePartChanged || renderPart.TopologyVersion != sourcePart.TopologyVersion || renderPart.SurfaceAreaMembershipVersion != sourcePart.SurfaceAreaMembershipVersion || !ActiveSurfaceAreaStructureMatches(renderPart, sourcePart) || HasMappingStructureTransition(renderPart, sourcePart);

                if (structureChanged)
                {
                    RebuildRenderPart(renderPart, sourcePart, objectScale, instance.Hovered);
                    continue;
                }

                bool geometryChanged = renderPart.GeometryVersion != sourcePart.GeometryVersion;
                bool positionsChanged = renderPart.PositionVersion != sourcePart.PositionVersion;
                bool directionsChanged = renderPart.DirectionVersion != sourcePart.DirectionVersion;
                bool broadGeometryChanged = renderPart.FullGeometryVersion != sourcePart.FullGeometryVersion;
                bool positionOnlyGeometryChanged = geometryChanged && positionsChanged && !directionsChanged && !broadGeometryChanged;
                bool sourceGeometryChanged = geometryChanged && directionsChanged && !broadGeometryChanged;
                bool scaleChanged = transformChanged && !Approximately(renderPart.MappingScale, objectScale);

                // Geometry/material parameter changes usually stay on the fast
                // path and only rewrite the streams that actually changed.
                bool meshStreamsChanged = false;
                bool preciseGeometry = false;

                if (geometryChanged || scaleChanged)
                {
                    bool usedTargetedRefresh = false;
                    if (geometryChanged && !scaleChanged && !broadGeometryChanged)
                    {
                        preciseGeometry = TryRefreshPreciseGeometry(renderPart, sourcePart, objectScale);
                        usedTargetedRefresh = preciseGeometry;
                    }

                    if (!usedTargetedRefresh)
                    {
                        // A broad fallback must not publish invalid values that precise preflight rejected.
                        RenderGeometryKernels.Validate(sourcePart.Mesh.Vertices);
                        RenderGeometryKernels.Validate(sourcePart.Mesh.Normals);
                    }

                    if (!usedTargetedRefresh && !scaleChanged && sourceGeometryChanged)
                    {
                        usedTargetedRefresh = TryRefreshSourceGeometry(renderPart, sourcePart, positionsChanged);
                    }

                    if (!usedTargetedRefresh && positionOnlyGeometryChanged && !scaleChanged)
                    {
                        usedTargetedRefresh = TryRefreshPositionOnlyGeometry(renderPart, sourcePart);
                    }

                    if (!usedTargetedRefresh)
                    {
                        if (!RefreshDerivedGeometry(renderPart, sourcePart, objectScale, geometryChanged, out bool derivedStreamsChanged))
                        {
                            RebuildRenderPart(renderPart, sourcePart, objectScale, instance.Hovered);
                            continue;
                        }

                        meshStreamsChanged |= derivedStreamsChanged;
                    }
                }

                if (!RefreshAreaState(renderPart, sourcePart, objectScale, instance.Hovered, ref meshStreamsChanged))
                {
                    RebuildRenderPart(renderPart, sourcePart, objectScale, instance.Hovered);
                    continue;
                }

                if (decalReceptionChanged && !RefreshDecalReceptionMaterials(renderPart, sourcePart, instance.Hovered))
                {
                    RebuildRenderPart(renderPart, sourcePart, objectScale, instance.Hovered);
                    continue;
                }

                if (meshStreamsChanged)
                {
                    WriteIncrementalUnityMeshStreams(renderPart);
                }

                if (geometryChanged || scaleChanged || meshStreamsChanged)
                    CompleteGeometryUpdate(renderPart, sourcePart, preciseGeometry && !meshStreamsChanged);
                renderPart.GeometryVersion = sourcePart.GeometryVersion;
                renderPart.PositionVersion = sourcePart.PositionVersion;
                renderPart.FullGeometryVersion = sourcePart.FullGeometryVersion;
                renderPart.DirectionVersion = sourcePart.DirectionVersion;
                renderPart.TopologyVersion = sourcePart.TopologyVersion;
                renderPart.SurfaceAreaMembershipVersion = sourcePart.SurfaceAreaMembershipVersion;
                renderPart.MappingScale = objectScale;
            }

            proceduralObject.ClearDirty(ProceduralObjectDirtyFlags.Transform | ProceduralObjectDirtyFlags.Geometry | ProceduralObjectDirtyFlags.Topology | ProceduralObjectDirtyFlags.Material);
        }

        public bool TryGetWorldBounds(int objectId, out Bounds bounds)
        {
            bounds = default;

            if (!m_Instances.TryGetValue(objectId, out RenderInstance instance))
            {
                return false;
            }

            bool hasBounds = false;

            foreach (RenderPart part in instance.Parts)
            {
                if (part.Renderer != null && part.HasMainRendererGeometry)
                {
                    if (!hasBounds)
                    {
                        bounds = part.Renderer.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(part.Renderer.bounds);
                    }
                }

                for (int i = 0; i < part.Areas.Count; i++)
                {
                    MeshRenderer isolatedRenderer = part.Areas[i].IsolatedRendererComponent;
                    if (isolatedRenderer == null)
                    {
                        continue;
                    }

                    if (!hasBounds)
                    {
                        bounds = isolatedRenderer.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(isolatedRenderer.bounds);
                    }
                }
            }

            return hasBounds;
        }

        public void Destroy(int objectId)
        {
            if (!m_Instances.TryGetValue(objectId, out RenderInstance instance))
            {
                return;
            }

            InvalidateLod(objectId);

            foreach (RenderPart part in instance.Parts)
            {
                part.PickCache.Release();
                ReleaseRenderAreas(part);

                if (part.Mesh != null)
                {
                    UnityEngine.Object.Destroy(part.Mesh);
                }
            }

            if (instance.Root != null)
            {
                UnityEngine.Object.Destroy(instance.Root);
            }

            m_Instances.Remove(objectId);
            m_PickSceneVersion++;
        }

        public void Clear()
        {
            m_PickWorker.Detach();
            m_PickCapture = null;
            m_PickCandidates.Clear();
            m_UnreadyPickCandidates.Clear();
            m_PendingPickObjects.Clear();
            int[] objectIds = new int[m_Instances.Count];
            m_Instances.Keys.CopyTo(objectIds, 0);

            foreach (int objectId in objectIds)
            {
                Destroy(objectId);
            }

            ResetLodPublicationAfterClear();
            PollTopologyCleanup(true);
        }

        public void Dispose()
        {
            Clear();

            foreach (SpecialMaterialEntry entry in m_SpecialMaterials.Values)
            {
                DestroyMaterialVariants(entry);
            }

            m_SpecialMaterials.Clear();

            foreach (SurfaceMaterialEntry entry in m_SurfaceMaterials.Values)
            {
                DestroyMaterialVariants(entry);
                entry.Surface?.Unload();
            }

            m_SurfaceMaterials.Clear();

            foreach (LibraryMaterialEntry entry in m_LibraryMaterials.Values)
            {
                DestroyLibraryMaterialEntry(entry);
            }

            m_LibraryMaterials.Clear();
            m_FailedLibraryMaterials.Clear();
            m_GlassSurfaceClassification.Clear();
            m_WindowSurfaceClassification.Clear();

            foreach (TextureResourceEntry entry in m_LibraryTextures.Values)
            {
                DestroyTexture(entry.Texture);
            }

            m_LibraryTextures.Clear();

            if (m_MappingDiagnosticTexture != null)
            {
                UnityEngine.Object.Destroy(m_MappingDiagnosticTexture);
                m_MappingDiagnosticTexture = null;
            }
        }

        private static bool Approximately(Vector3 a, Vector3 b)
        {
            return Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y) && Mathf.Approximately(a.z, b.z);
        }
    }
}