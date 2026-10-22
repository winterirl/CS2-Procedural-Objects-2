using Colossal.IO.AssetDatabase;
using Game.Prefabs;
using Game.Rendering;
using PO2.Materials;
using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using MaterialData = PO2.Materials.MaterialData;

namespace PO2.Conversion
{
    internal sealed partial class AssetMeshExtractor
    {
        private readonly struct SourceMeshTransform
        {
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public Vector3 Scale { get; }
            private readonly ConversionTransform m_Transform;

            public SourceMeshTransform(Vector3 position, Quaternion rotation, Vector3 scale)
            {
                Position = position;
                Rotation = rotation;
                Scale = scale;
                m_Transform = new ConversionTransform(ToFloat3(position), new quaternion(rotation.x, rotation.y, rotation.z, rotation.w), ToFloat3(scale));
            }

            public Vector3 TransformPoint(Vector3 point) =>
                ToVector3(m_Transform.TransformPoint(ToFloat3(point)));

            public Vector3 TransformNormal(Vector3 normal) =>
                ToVector3(m_Transform.TransformNormal(ToFloat3(normal)));

            public Vector3 TransformTangent(Vector3 tangent) =>
                ToVector3(m_Transform.TransformTangent(ToFloat3(tangent)));

            public Quaternion TransformRotation(Quaternion rotation) =>
                ToQuaternion(math.mul(m_Transform.Rotation, new quaternion(rotation.x, rotation.y, rotation.z, rotation.w)));

            private static float3 ToFloat3(Vector3 value) => new float3(value.x, value.y, value.z);
        }

        private readonly struct ResolvedRenderEntry
        {
            public RenderPrefab RenderPrefab { get; }
            public SubMesh SourceSubMesh { get; }
            public bool HasSubMeshTransform { get; }
            public int RenderEntryIndex { get; }
            public int ColorIndex { get; }

            public ResolvedRenderEntry(RenderPrefab renderPrefab, SubMesh sourceSubMesh, bool hasSubMeshTransform, int renderEntryIndex, int colorIndex = -1)
            {
                RenderPrefab = renderPrefab;
                SourceSubMesh = sourceSubMesh;
                HasSubMeshTransform = hasSubMeshTransform;
                RenderEntryIndex = renderEntryIndex;
                ColorIndex = colorIndex >= 0 ? colorIndex : renderEntryIndex;
            }
        }

        private bool TryCapturePart(Entity entity, string prefabName, ResolvedRenderEntry renderEntry, Vector3 sourceMeshScale, int renderEntryCount, string partName, ConversionPartProfile conversionProfile, out AssetConversionPartWorkItem part)
        {
            part = null;

            RenderPrefab renderPrefab = renderEntry.RenderPrefab;
            GeometryAsset geometryAsset = renderPrefab.geometryAsset;
            if (geometryAsset == null)
            {
                Mod.log.Warn($"[PO Conversion] RenderPrefab '{renderPrefab.name}' has no GeometryAsset.");
                return false;
            }

            // Load and decompress the GeometryAsset here
            // then pass its buffers to the worker for stream decoding once the ECS
            //  and material metadata has been captured.
            GeometryAsset.Data data = default(GeometryAsset.Data);
            GeometryAsset.Loading loading = default(GeometryAsset.Loading);

            try
            {
                GeometryAsset.LoadSync(geometryAsset.GetAsyncReadDescriptor(), GeometryAsset.Attribute.Position | GeometryAsset.Attribute.Normal | GeometryAsset.Attribute.Tangent | GeometryAsset.Attribute.Color | GeometryAsset.Attribute.TexCoord0 | GeometryAsset.Attribute.TexCoord1 | GeometryAsset.Attribute.TexCoord3, ref data, ref loading);

                if (!data.IsValid || data.meshCount <= 0)
                {
                    Mod.log.Warn($"[PO Conversion] GeometryAsset '{geometryAsset.name}' loaded no meshes.");
                    return false;
                }

                int totalVertexCount = GeometryAsset.GetAllVertexCount(ref data);
                int totalIndexCount = GeometryAsset.GetAllIndicesCount(ref data);

                if (totalVertexCount <= 0 || totalIndexCount <= 0 || totalIndexCount % 3 != 0)
                {
                    Mod.log.Warn($"[PO Conversion] GeometryAsset '{geometryAsset.name}' has invalid " + $"geometry counts: vertices={totalVertexCount}, " + $"indices={totalIndexCount}.");
                    return false;
                }

                if (renderPrefab.materialCount != data.meshCount)
                {
                    Mod.log.Warn($"[PO Conversion] RenderPrefab '{renderPrefab.name}' has " + $"meshCount={data.meshCount} but " + $"materialCount={renderPrefab.materialCount}. " + "Geometry regions without a matching native material " + "will use the fallback material.");
                }

                ColorMaskData sourceColorMask = CaptureNativeColorMask(entity, renderEntry.ColorIndex);
                MaterialData[] materials = new MaterialData[data.meshCount];
                NativeWindowKind[] windowKinds = new NativeWindowKind[data.meshCount];
                bool[] isolationHints = new bool[data.meshCount];

                for (int meshIndex = 0; meshIndex < data.meshCount; meshIndex++)
                {
                    materials[meshIndex] = CreateSourceMaterial(renderPrefab, meshIndex, out windowKinds[meshIndex], out isolationHints[meshIndex]);
                }

                SourceMeshTransform sourceMeshTransform = renderEntry.HasSubMeshTransform ? new SourceMeshTransform(ToVector3(renderEntry.SourceSubMesh.m_Position), ToQuaternion(renderEntry.SourceSubMesh.m_Rotation), sourceMeshScale) : new SourceMeshTransform(Vector3.zero, Quaternion.identity, sourceMeshScale);

                // Constructor steals data and clears the local.
                // The finally block releases the buffers only if the work item did not take them.
                part = new AssetConversionPartWorkItem(partName, ref data, sourceMeshTransform.Position, sourceMeshTransform.Rotation, sourceMeshTransform.Scale, materials, windowKinds, isolationHints, sourceColorMask, conversionProfile);

                return true;
            }
            catch (Exception exception)
            {
                Mod.log.Error($"[PO Conversion] Geometry capture failed for '{prefabName}' " + $"render entry {renderEntry.RenderEntryIndex + 1}/" + $"{renderEntryCount} ('{renderPrefab.name}'): {exception}");
                part = null;
                return false;
            }
            finally
            {
                data.Dispose();
                loading.Dispose();
            }
        }

        private bool TryGetSourceState(Entity entity, out PrefabRef prefabRef, out Game.Objects.Transform sourceTransform, out string failureReason)
        {
            prefabRef = default(PrefabRef);
            sourceTransform = default(Game.Objects.Transform);

            if (entity == Entity.Null || !m_EntityManager.Exists(entity))
            {
                failureReason = "Cannot convert a null or missing entity.";
                return false;
            }

            if (m_EntityManager.HasComponent<Game.Common.Deleted>(entity))
            {
                failureReason = $"Entity {entity} is already marked Deleted.";
                return false;
            }

            if (!m_EntityManager.HasComponent<PrefabRef>(entity))
            {
                failureReason = $"Entity {entity} has no PrefabRef.";
                return false;
            }

            if (!m_EntityManager.HasComponent<Game.Objects.Transform>(entity))
            {
                failureReason = $"Entity {entity} has no object Transform.";
                return false;
            }

            prefabRef = m_EntityManager.GetComponentData<PrefabRef>(entity);
            sourceTransform = m_EntityManager.GetComponentData<Game.Objects.Transform>(entity);
            failureReason = string.Empty;
            return true;
        }

        private bool HasSupportedRenderEntry(Entity prefabEntity)
        {
            if (m_EntityManager.HasBuffer<SubMesh>(prefabEntity))
            {
                DynamicBuffer<SubMesh> subMeshes = m_EntityManager.GetBuffer<SubMesh>(prefabEntity, true);

                for (int i = 0; i < subMeshes.Length; i++)
                {
                    if (m_PrefabSystem.TryGetPrefab<RenderPrefab>(subMeshes[i].m_SubMesh, out RenderPrefab renderPrefab) && renderPrefab != null && renderPrefab.geometryAsset != null)
                    {
                        return true;
                    }
                }

                return false;
            }

            return m_PrefabSystem.TryGetPrefab<RenderPrefab>(prefabEntity, out RenderPrefab directRenderPrefab) && directRenderPrefab != null && directRenderPrefab.geometryAsset != null;
        }

        private bool TryResolveRenderEntries(Entity sourceEntity, Entity prefabEntity, bool isTree, SubMeshFlags treeStage, List<ResolvedRenderEntry> results, out int sourceRenderEntryCount)
        {
            sourceRenderEntryCount = 0;
            results.Clear();

            // Most placeable prefabs point at RenderPrefabs through SubMesh.
            // Some assets are RenderPrefabs directly which is why there's the fallback below.
            if (m_EntityManager.HasBuffer<SubMesh>(prefabEntity))
            {
                DynamicBuffer<SubMesh> subMeshes = m_EntityManager.GetBuffer<SubMesh>(prefabEntity, true);

                sourceRenderEntryCount = subMeshes.Length;
                if (isTree)
                {
                    return TryResolveTreeRenderEntries(sourceEntity, prefabEntity, subMeshes, treeStage, results);
                }

                for (int i = 0; i < subMeshes.Length; i++)
                {
                    SubMesh candidate = subMeshes[i];
                    if (!m_PrefabSystem.TryGetPrefab<RenderPrefab>(candidate.m_SubMesh, out RenderPrefab candidateRenderPrefab) || candidateRenderPrefab == null || candidateRenderPrefab.geometryAsset == null)
                    {
                        Mod.log.Warn($"[PO Conversion] Skipping unsupported render entry " + $"{i + 1}/{subMeshes.Length}.");
                        continue;
                    }

                    results.Add(new ResolvedRenderEntry(candidateRenderPrefab, candidate, (candidate.m_Flags & SubMeshFlags.HasTransform) != 0, i));
                }

                return results.Count > 0;
            }

            sourceRenderEntryCount = 1;
            if (m_PrefabSystem.TryGetPrefab<RenderPrefab>(prefabEntity, out RenderPrefab directRenderPrefab) && directRenderPrefab != null && directRenderPrefab.geometryAsset != null)
            {
                results.Add(new ResolvedRenderEntry(directRenderPrefab, default(SubMesh), false, 0));
                return true;
            }

            return false;
        }

        private bool TryResolveTreeRenderEntries(Entity sourceEntity, Entity prefabEntity, DynamicBuffer<SubMesh> subMeshes, SubMeshFlags stage, List<ResolvedRenderEntry> results)
        {
            try
            {
                bool hasGroups = m_EntityManager.HasBuffer<SubMeshGroup>(prefabEntity);
                bool hasMeshGroups = m_EntityManager.HasBuffer<MeshGroup>(sourceEntity);
                DynamicBuffer<MeshGroup> meshGroups = hasMeshGroups ? m_EntityManager.GetBuffer<MeshGroup>(sourceEntity, true) : default(DynamicBuffer<MeshGroup>);
                DynamicBuffer<SubMeshGroup> groups = hasGroups ? m_EntityManager.GetBuffer<SubMeshGroup>(prefabEntity, true) : default(DynamicBuffer<SubMeshGroup>);

                int groupCount = hasGroups && hasMeshGroups ? meshGroups.Length : 1;
                var ranges = new ConversionMeshRange[groupCount];
                for (int i = 0; i < groupCount; i++)
                {
                    MeshGroup group = hasMeshGroups && i < meshGroups.Length ? meshGroups[i] : default(MeshGroup);
                    int start = 0;
                    int end = subMeshes.Length;
                    if (hasGroups)
                    {
                        if (group.m_SubMeshGroup >= groups.Length)
                        {
                            throw new InvalidOperationException("The tree's selected mesh group is missing.");
                        }

                        int2 range = groups[group.m_SubMeshGroup].m_SubMeshRange;
                        start = range.x;
                        end = range.y;
                    }

                    ranges[i] = new ConversionMeshRange(start, end, group.m_ColorOffset);
                }

                var flags = new SubMeshFlags[subMeshes.Length];
                for (int i = 0; i < flags.Length; i++) flags[i] = subMeshes[i].m_Flags;
                List<ConversionMeshSelection> selected = ConvertedTreeSelection.Select(flags, stage, ranges);
                foreach (ConversionMeshSelection entry in selected)
                {
                    SubMesh candidate = subMeshes[entry.MeshIndex];
                    if (!m_PrefabSystem.TryGetPrefab<RenderPrefab>(candidate.m_SubMesh, out RenderPrefab renderPrefab) || renderPrefab == null || renderPrefab.geometryAsset == null)
                    {
                        // Don't replace a tree with only some of its visible geometry.
                        throw new InvalidOperationException($"Tree render entry {entry.MeshIndex} has no supported geometry.");
                    }

                    results.Add(new ResolvedRenderEntry(renderPrefab, candidate, (candidate.m_Flags & SubMeshFlags.HasTransform) != 0, entry.MeshIndex, entry.ColorIndex));
                }

                return results.Count > 0;
            }
            catch (Exception exception)
            {
                results.Clear();
                Mod.log.Warn($"[PO Conversion] Could not select the tree's current geometry: {exception.Message}");
                return false;
            }
        }

        private static Vector3 ToVector3(float3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private static Quaternion ToQuaternion(quaternion value)
        {
            return new Quaternion(value.value.x, value.value.y, value.value.z, value.value.w);
        }
    }
}