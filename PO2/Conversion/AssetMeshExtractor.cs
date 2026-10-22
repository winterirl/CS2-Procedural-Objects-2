using System;
using System.Collections.Generic;
using Colossal.IO.AssetDatabase;
using Game.Prefabs;
using Game.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using PO2.Core;
using PO2.Geometry;

namespace PO2.Conversion
{
    // Copies prefab geometry and materials into independent PO parts.
    // The PO keeps working after the source entity is removed.
    internal sealed partial class AssetMeshExtractor
    {
        private readonly EntityManager m_EntityManager;
        private readonly PrefabSystem m_PrefabSystem;

        public AssetMeshExtractor(EntityManager entityManager, PrefabSystem prefabSystem)
        {
            m_EntityManager = entityManager;
            m_PrefabSystem = prefabSystem;
        }

        public bool IsSupportedTarget(Entity entity)
        {
            if (!TryGetSourceState(entity, out PrefabRef prefabRef, out Game.Objects.Transform _, out string _))
            {
                return false;
            }

            return HasSupportedRenderEntry(prefabRef.m_Prefab);
        }

        public bool TryCapture(Entity entity, out AssetConversionWorkItem workItem)
        {
            workItem = null;
            ConversionProfile profile = new ConversionProfile();

            if (!TryGetSourceState(entity, out PrefabRef prefabRef, out Game.Objects.Transform sourceTransform, out string sourceFailureReason))
            {
                Mod.log.Warn($"[PO Conversion] {sourceFailureReason}");
                return false;
            }

            // Capture ECS, prefab, and material state here. The worker decodes the loaded buffers.
            Entity prefabEntity = prefabRef.m_Prefab;
            string prefabName = m_PrefabSystem.GetPrefabName(prefabEntity);
            profile.PrefabName = prefabName;

            // Trees need to be handled a little differently to avoid converting all life-stages at once
            bool isTree = m_EntityManager.HasComponent<Game.Objects.Tree>(entity);
            Game.Objects.Tree tree = default(Game.Objects.Tree);
            SubMeshFlags treeStage = 0;
            float3 treeScale = new float3(1f);
            bool hasGrowthScale = false;
            if (isTree)
            {
                tree = m_EntityManager.GetComponentData<Game.Objects.Tree>(entity);
                hasGrowthScale = m_EntityManager.HasComponent<GrowthScaleData>(prefabEntity);
                // Match BatchInstanceSystem's adult fallback when growth data is absent.
                treeStage = hasGrowthScale ? BatchDataHelpers.CalculateTreeSubMeshData(tree, m_EntityManager.GetComponentData<GrowthScaleData>(prefabEntity), out treeScale) : SubMeshFlags.RequireAdult;
                if (!ConversionTransform.IsValidScale(treeScale))
                {
                    Mod.log.Warn($"[PO Conversion] Tree '{prefabName}' has invalid growth scale; conversion cancelled.");
                    return false;
                }
            }

            List<ResolvedRenderEntry> renderEntries = new List<ResolvedRenderEntry>();
            bool resolvedRenderEntries = TryResolveRenderEntries(entity, prefabEntity, isTree, treeStage, renderEntries, out int sourceRenderEntryCount);

            if (!resolvedRenderEntries)
            {
                Mod.log.Warn($"[PO Conversion] Prefab '{prefabName}' has no usable render entries for conversion.");
                return false;
            }

            List<AssetConversionPartWorkItem> parts = new List<AssetConversionPartWorkItem>(renderEntries.Count);
            HashSet<string> usedPartNames = new HashSet<string>(StringComparer.Ordinal);
            bool captureSucceeded = false;

            try
            {
                for (int i = 0; i < renderEntries.Count; i++)
                {
                    ResolvedRenderEntry renderEntry = renderEntries[i];
                    string partName = GetUniquePartName(renderEntry.RenderPrefab.name, usedPartNames);

                    GeometryAsset geometryAsset = renderEntry.RenderPrefab.geometryAsset;
                    ConversionPartProfile partProfile = profile.AddPart(partName, renderEntry.RenderPrefab.name, geometryAsset != null ? geometryAsset.name : string.Empty);

                    if (!TryCapturePart(entity, prefabName, renderEntry, ToVector3(treeScale), sourceRenderEntryCount, partName, partProfile, out AssetConversionPartWorkItem part))
                    {
                        return false;
                    }

                    parts.Add(part);
                }

                if (parts.Count == 0)
                {
                    Mod.log.Warn($"[PO Conversion] Prefab '{prefabName}' produced no convertible mesh parts.");
                    return false;
                }

                ObjectTransform poTransform = new ObjectTransform(ToVector3(sourceTransform.m_Position), ToQuaternion(sourceTransform.m_Rotation), Vector3.one);
                workItem = new AssetConversionWorkItem(entity, prefabEntity, prefabName, poTransform, parts.ToArray(), profile);
                captureSucceeded = true;

                return true;
            }
            finally
            {
                if (!captureSucceeded)
                {
                    for (int i = 0; i < parts.Count; i++)
                    {
                        parts[i]?.Dispose();
                    }
                }
            }
        }

        // The worker only accesses the detached GeometryAsset buffers and their unpack helpers.
        // ECS, prefabs, and Unity rendering objects stay on the main thread.
        internal static AssetConversionBuildResult BuildCaptured(AssetConversionWorkItem workItem)
        {
            if (workItem == null || workItem.Parts == null || workItem.Parts.Length == 0)
            {
                workItem?.Dispose();
                return AssetConversionBuildResult.Failed("The captured conversion contains no mesh parts.");
            }

            ConversionProfile profile = workItem.Profile;

            try
            {
                MeshPart[] parts = new MeshPart[workItem.Parts.Length];
                ConvertedRenderPartPreparation[] renderParts = new ConvertedRenderPartPreparation[workItem.Parts.Length];
                ConversionBoundsSnapshot localBounds = default(ConversionBoundsSnapshot);

                for (int i = 0; i < workItem.Parts.Length; i++)
                {
                    if (!TryBuildCapturedPart(workItem.Parts[i], out MeshPart part, out ConvertedRenderPartPreparation renderPart, out ConversionBoundsSnapshot partBounds, out string failureReason))
                    {
                        return AssetConversionBuildResult.Failed(failureReason);
                    }

                    parts[i] = part;
                    renderParts[i] = renderPart;
                    localBounds.Encapsulate(partBounds);
                }

                AssetConversionResult result = new AssetConversionResult(workItem.RequestedName, workItem.InitialTransform, parts, localBounds, new ConvertedRenderPreparation(renderParts), profile);

                return AssetConversionBuildResult.Completed(result);
            }
            catch (Exception exception)
            {
                return AssetConversionBuildResult.Failed($"Background conversion failed: {exception}");
            }
            finally
            {
                // Native GeometryAsset payloads are worker-owned after capture.
                // Release them here ~ even failed/abandoned paths. 
                workItem.Dispose();
            }
        }
    }
}