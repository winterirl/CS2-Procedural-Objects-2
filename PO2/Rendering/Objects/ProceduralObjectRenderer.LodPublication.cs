using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using PO2.Rendering.Lod;
using PO2.Core.Lod;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        // Transition heights are fractions of the camera view, not world distances.
        private const float kLod0TransitionHeight = 0.18f;
        private const float kLod1TransitionHeight = 0.045f;
        private const float kLod2TransitionHeight = 0.0f;

        // Publish at most one Unity operation per rendering update. Large meshes
        // spread stream copies, indices, and the final upload across separate updates.
        private const int kMaximumLodPublicationStepsPerUpdate = 1;

        private readonly Dictionary<int, LodObjectState> m_LodObjects = new Dictionary<int, LodObjectState>();
        private readonly List<LodPublicationWork> m_LodPublicationQueue = new List<LodPublicationWork>();
        private readonly List<int> m_LodTransitionUpdateQueue = new List<int>();
        private int m_LodPublicationQueueIndex;
        private int m_LodTransitionUpdateQueueIndex;
        private int m_LodPendingResultCount;
        private int? m_SelectedLodObjectId;
        private float m_Lod0TransitionHeight = kLod0TransitionHeight;
        private float m_Lod1TransitionHeight = kLod1TransitionHeight;

        private long m_LodPublishedResultCount;
        private long m_LodDiscardedResultCount;
        private long m_LodFailedPublicationCount;
        private double m_LodMaximumPublicationStepMilliseconds;
        private double m_LodLastPublicationStepMilliseconds;
        private string m_LodLastPublicationFailure = string.Empty;

        internal bool QueueLodResult(LodGenerationResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            int objectId = result.Key.ObjectId;
            if (!m_Instances.ContainsKey(objectId) || !IsLodRequestCurrent(result.Key))
            {
                m_LodDiscardedResultCount++;
                return false;
            }

            if (!m_LodObjects.TryGetValue(objectId, out LodObjectState objectState))
            {
                objectState = new LodObjectState();
                m_LodObjects.Add(objectId, objectState);
            }

            if (result.Key.IsPreview && objectState.PreviewSessionId != result.Key.PreviewSessionId)
            {
                m_LodDiscardedResultCount++;
                return false;
            }

            LodPublicationWork work = new LodPublicationWork(result);
            bool replacingPending = objectState.Pending != null;
            if (replacingPending)
            {
                DestroyPublicationWork(objectState.Pending);
                objectState.Pending = null;
                m_LodDiscardedResultCount++;
            }

            if (!result.Key.IsPreview)
            {
                objectState.AcceptedProfile = result.Profile;
            }

            objectState.Pending = work;
            if (!replacingPending)
            {
                m_LodPendingResultCount++;
            }

            m_LodPublicationQueue.Add(work);
            return true;
        }

        internal bool CanAdvanceLodGeneration()
        {
            return m_LodPendingResultCount == 0;
        }

        internal void SetLodDistanceMultipliers(float lod1Distance, float lod2Distance)
        {
            lod1Distance = SanitizeLodDistanceMultiplier(lod1Distance);
            m_GlobalLod1Distance = lod1Distance;
            lod2Distance = SanitizeLodDistanceMultiplier(lod2Distance);
            m_GlobalLod2Distance = lod2Distance;

            float lod0TransitionHeight = kLod0TransitionHeight / lod1Distance;
            float lod1TransitionHeight = kLod1TransitionHeight / lod2Distance;

            // Unity needs descending transition heights. Keep a small gap at overlapping
            // slider limits without changing the saved settings.
            if (lod1TransitionHeight >= lod0TransitionHeight)
            {
                lod1TransitionHeight = lod0TransitionHeight * 0.99f;
            }

            if (m_Lod0TransitionHeight == lod0TransitionHeight && m_Lod1TransitionHeight == lod1TransitionHeight)
            {
                return;
            }

            m_Lod0TransitionHeight = lod0TransitionHeight;
            m_Lod1TransitionHeight = lod1TransitionHeight;
            m_LodTransitionUpdateQueue.Clear();
            m_LodTransitionUpdateQueueIndex = 0;

            foreach (KeyValuePair<int, LodObjectState> pair in m_LodObjects)
            {
                if (pair.Value.Published != null || pair.Value.Preview != null)
                {
                    m_LodTransitionUpdateQueue.Add(pair.Key);
                }
            }
        }

        internal void UpdateLodPublication()
        {
            AdvanceLodRetirement();
            bool performedUnityOperation = false;
            for (int stepIndex = 0; stepIndex < kMaximumLodPublicationStepsPerUpdate; stepIndex++)
            {
                LodPublicationWork work = DequeueLodPublicationWork();
                if (work == null)
                {
                    AdvanceLodTransitionUpdate();
                    return;
                }

                if (!m_LodObjects.TryGetValue(work.ObjectId, out LodObjectState objectState) || !ReferenceEquals(objectState.Pending, work))
                {
                    continue;
                }

                if (!IsLodRequestCurrent(work.Key) || (work.Key.IsPreview && objectState.PreviewSessionId != work.Key.PreviewSessionId))
                {
                    DiscardPendingLod(objectState);
                    continue;
                }

                try
                {
                    performedUnityOperation = true;
                    long uploadStart = Stopwatch.GetTimestamp();
                    bool publishGroup = work.IsComplete;
                    if (publishGroup)
                    {
                        PublishCompletedLod(objectState, work);
                    }
                    else
                    {
                        AdvanceLodMeshPublication(work);
                        m_LodUploadProgress?.Invoke(work.Key, work.NextMeshIndex, work.TotalMeshCount);
                    }

                    double uploadMilliseconds = ElapsedMilliseconds(uploadStart);
                    m_LodLastPublicationStepMilliseconds = uploadMilliseconds;
                    m_LodMaximumPublicationStepMilliseconds = Math.Max(m_LodMaximumPublicationStepMilliseconds, uploadMilliseconds);

                    if (!publishGroup)
                    {
                        m_LodPublicationQueue.Add(work);
                    }
                }
                catch (Exception exception)
                {
                    DestroyPublicationWork(work);
                    objectState.Pending = null;
                    m_LodPendingResultCount = Math.Max(0, m_LodPendingResultCount - 1);
                    RemoveEmptyLodObjectState(work.ObjectId, objectState);
                    m_LodFailedPublicationCount++;
                    m_LodLastPublicationFailure = exception.GetType().Name + ": " + exception.Message;
                    m_LodPublicationFailed?.Invoke(work.Key, m_LodLastPublicationFailure);
                    Mod.log.Warn("[PO LOD] Unity mesh publication failed for object " + work.ObjectId + ": " + exception);
                }
            }

            CompactLodPublicationQueue();
            if (!performedUnityOperation)
            {
                AdvanceLodTransitionUpdate();
            }
        }

        internal void InvalidateLod(int objectId)
        {
            if (!m_LodObjects.TryGetValue(objectId, out LodObjectState objectState))
            {
                return;
            }

            if (objectState.Pending != null)
            {
                DestroyPublicationWork(objectState.Pending);
                objectState.Pending = null;
                m_LodPendingResultCount = Math.Max(0, m_LodPendingResultCount - 1);
                m_LodDiscardedResultCount++;
            }

            DisableLodGroup(objectId);
            if (objectState.Preview != null)
            {
                DestroyPublishedLod(objectState.Preview);
                objectState.Preview = null;
            }

            if (objectState.Published != null)
            {
                DestroyPublishedLod(objectState.Published);
                objectState.Published = null;
            }

            m_LodObjects.Remove(objectId);
        }

        internal bool HasLodMetricScaleChanged(int objectId, Vector3 objectScale)
        {
            if (!m_Instances.TryGetValue(objectId, out RenderInstance instance))
            {
                return false;
            }

            for (int i = 0; i < instance.Parts.Count; i++)
            {
                Vector3 previous = instance.Parts[i].MappingScale;
                if (previous.x != objectScale.x || previous.y != objectScale.y || previous.z != objectScale.z)
                {
                    return true;
                }
            }

            return false;
        }

        internal void SetSelectedLodObject(int? objectId)
        {
            if (m_SelectedLodObjectId == objectId)
            {
                ForceSelectedLod(objectId, true);
                return;
            }

            ForceSelectedLod(m_SelectedLodObjectId, false);
            m_SelectedLodObjectId = objectId;
            ForceSelectedLod(m_SelectedLodObjectId, true);
        }

        internal LodPublicationDiagnostics CaptureLodPublicationDiagnostics()
        {
            int pendingObjects = 0;
            int pendingMeshUploads = 0;
            int publishedObjects = 0;
            int publishedMeshes = 0;

            foreach (LodObjectState objectState in m_LodObjects.Values)
            {
                if (objectState.Pending != null)
                {
                    pendingObjects++;
                    pendingMeshUploads += objectState.Pending.RemainingMeshCount;
                }

                if (objectState.Published != null)
                {
                    publishedObjects++;
                    publishedMeshes += objectState.Published.MeshCount;
                }
            }

            return new LodPublicationDiagnostics(pendingObjects, pendingMeshUploads, publishedObjects, publishedMeshes, m_LodPublishedResultCount, m_LodDiscardedResultCount, m_LodFailedPublicationCount, m_LodLastPublicationStepMilliseconds, m_LodMaximumPublicationStepMilliseconds, m_LodLastPublicationFailure);
        }

        private void ResetLodPublicationAfterClear()
        {
            AdvanceLodRetirement(true);
            m_LodObjects.Clear();
            m_LodPublicationQueue.Clear();
            m_LodTransitionUpdateQueue.Clear();
            m_LodPublicationQueueIndex = 0;
            m_LodTransitionUpdateQueueIndex = 0;
            m_LodPendingResultCount = 0;
            m_SelectedLodObjectId = null;
        }

        private LodPublicationWork DequeueLodPublicationWork()
        {
            while (m_LodPublicationQueueIndex < m_LodPublicationQueue.Count)
            {
                int preview = m_LodPublicationQueue.FindIndex(m_LodPublicationQueueIndex, x => x != null && x.Key.IsPreview && x.Result != null);
                if (preview > m_LodPublicationQueueIndex)
                {
                    LodPublicationWork next = m_LodPublicationQueue[preview];
                    m_LodPublicationQueue[preview] = m_LodPublicationQueue[m_LodPublicationQueueIndex];
                    m_LodPublicationQueue[m_LodPublicationQueueIndex] = next;
                }

                LodPublicationWork work = m_LodPublicationQueue[m_LodPublicationQueueIndex++];
                if (work != null)
                {
                    return work;
                }
            }

            CompactLodPublicationQueue(true);
            return null;
        }

        private void AdvanceLodMeshPublication(LodPublicationWork work)
        {
            LodGenerationResult result = work.Result;
            if (result == null || work.IsComplete)
            {
                throw new InvalidOperationException("LOD publication work has no remaining mesh.");
            }

            if (work.CurrentUpload == null)
            {
                work.CurrentUpload = BeginLodMeshPublication(work);
                return;
            }

            LodMeshUpload upload = work.CurrentUpload;
            LodMeshData meshData = upload.MeshData;
            while (true)
            {
                switch (upload.Stage)
                {
                    case LodMeshUploadStage.Vertices:
                        upload.Mesh.vertices = meshData.Vertices;
                        upload.Stage = LodMeshUploadStage.Normals;
                        return;
                    case LodMeshUploadStage.Normals:
                        upload.Stage = LodMeshUploadStage.UV0;
                        if (meshData.Normals != null)
                        {
                            upload.Mesh.normals = meshData.Normals;
                            return;
                        }

                        break;
                    case LodMeshUploadStage.UV0:
                        upload.Stage = LodMeshUploadStage.VertexColors;
                        if (meshData.UV0 != null)
                        {
                            SetUnityUV0(upload.Mesh, meshData.UV0, meshData.UV0Dimension);
                            return;
                        }

                        break;
                    case LodMeshUploadStage.VertexColors:
                        upload.Stage = LodMeshUploadStage.TexCoord1;
                        if (meshData.VertexColors != null)
                        {
                            upload.Mesh.colors32 = meshData.VertexColors;
                            return;
                        }

                        break;
                    case LodMeshUploadStage.TexCoord1:
                        upload.Stage = LodMeshUploadStage.TexCoord3;
                        if (meshData.TexCoord1 != null)
                        {
                            SetUnityTexCoord1(upload.Mesh, meshData.TexCoord1);
                            return;
                        }

                        break;
                    case LodMeshUploadStage.TexCoord3:
                        upload.Stage = LodMeshUploadStage.Indices;
                        if (meshData.TexCoord3 != null)
                        {
                            SetUnityTexCoord3(upload.Mesh, meshData.TexCoord3);
                            return;
                        }

                        break;
                    case LodMeshUploadStage.Indices:
                        upload.Mesh.subMeshCount = 1;
                        upload.Mesh.SetTriangles(meshData.Triangles, 0, false);
                        upload.Stage = LodMeshUploadStage.Tangents;
                        return;
                    case LodMeshUploadStage.Tangents:
                        upload.Stage = LodMeshUploadStage.Finalize;
                        if (upload.RequiresTangents)
                        {
                            if (meshData.Tangents != null && meshData.Tangents.Length == meshData.VertexCount)
                            {
                                upload.Mesh.tangents = meshData.Tangents;
                            }
                            else
                            {
                                upload.Mesh.RecalculateTangents();
                            }

                            return;
                        }

                        break;
                    case LodMeshUploadStage.Finalize:
                        Vector3 boundsSize = meshData.BoundsMax - meshData.BoundsMin;
                        upload.Mesh.bounds = new Bounds(meshData.BoundsMin + boundsSize * 0.5f, boundsSize);
                        upload.Mesh.UploadMeshData(true);
                        work.CurrentUpload = null;
                        work.NextMeshIndex++;
                        return;
                    default:
                        throw new InvalidOperationException("Unknown LOD mesh publication stage.");
                }
            }
        }

        private LodMeshUpload BeginLodMeshPublication(LodPublicationWork work)
        {
            LodGenerationResult result = work.Result;
            if (!m_Instances.TryGetValue(work.ObjectId, out RenderInstance instance))
            {
                throw new InvalidOperationException("The rendered object no longer exists.");
            }

            int areaCount = result.Areas.Length;
            int level = work.UploadLevels[work.NextMeshIndex / areaCount];
            int areaIndex = work.NextMeshIndex % areaCount;
            LodAreaResult areaResult = result.Areas[areaIndex];
            LodAreaSource source = areaResult.Source;

            if (source.PartIndex < 0 || source.PartIndex >= instance.Parts.Count)
            {
                throw new InvalidOperationException("The generated LOD references a missing mesh part.");
            }

            RenderPart part = instance.Parts[source.PartIndex];
            RenderArea area = FindRenderArea(part, source.SurfaceAreaId);
            if (area == null || area.IsolatedRenderer != source.IsolatedRenderer)
            {
                throw new InvalidOperationException("The generated LOD Surface Area no longer matches " + "the live renderer.");
            }

            LodMeshData meshData = level == 1 ? areaResult.Lod1.Mesh : areaResult.Lod2.Mesh;
            GameObject areaObject = null;
            Mesh mesh = null;
            try
            {
                areaObject = new GameObject("LOD" + level + " Surface " + source.SurfaceAreaId + ": Part " + source.PartIndex);
                areaObject.SetActive(false);
                areaObject.transform.SetParent(part.GameObject.transform, false);

                MeshFilter meshFilter = areaObject.AddComponent<MeshFilter>();
                MeshRenderer meshRenderer = areaObject.AddComponent<MeshRenderer>();
                mesh = new Mesh
                {
                    name = "PO2 LOD" + level + " Mesh " + work.ObjectId + ":" + source.PartIndex + ":" + source.SurfaceAreaId,
                };
                mesh.indexFormat = meshData.VertexCount > ushort.MaxValue ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                meshFilter.sharedMesh = mesh;
                meshRenderer.sharedMaterial = area.Material;
                Renderer sourceRenderer = area.IsolatedRenderer ? area.IsolatedRendererComponent : part.Renderer;
                CopyLodRendererSettings(sourceRenderer, meshRenderer);
                if (area.PropertyBlock != null)
                {
                    meshRenderer.SetPropertyBlock(area.PropertyBlock);
                }

                work.AddMesh(level, areaObject, mesh, meshRenderer, source.PartIndex, source.SurfaceAreaId);
                return new LodMeshUpload(mesh, meshData, source.RequiresTangents);
            }
            catch
            {
                UnityEngine.Object.Destroy(mesh);
                UnityEngine.Object.Destroy(areaObject);
                throw;
            }
        }

        private void PublishCompletedLod(LodObjectState objectState, LodPublicationWork work)
        {
            if (!IsLodRequestCurrent(work.Key))
            {
                DiscardPendingLod(objectState);
                return;
            }

            PublishedLodState candidate = work.CreatePublishedState();
            PublishedLodState previous = work.Key.IsPreview ? objectState.Preview : objectState.Published;
            ApplyLodPresentation(work.ObjectId, objectState, candidate);
            if (work.Key.IsPreview)
            {
                objectState.Preview = candidate;
            }
            else
            {
                objectState.Published = candidate;
            }

            work.ReleasePublishedState();
            objectState.Pending = null;
            m_LodPendingResultCount = Math.Max(0, m_LodPendingResultCount - 1);
            ++m_LodPublishedResultCount;
            DestroyPublishedLod(previous);
            m_LodPublicationReady?.Invoke(work.Key, candidate.MeshCount);
        }

        private static List<Renderer> CollectLod0Renderers(RenderInstance instance)
        {
            List<Renderer> renderers = new List<Renderer>();
            for (int partIndex = 0; partIndex < instance.Parts.Count; partIndex++)
            {
                RenderPart part = instance.Parts[partIndex];
                if (part.HasMainRendererGeometry && part.Renderer != null)
                {
                    renderers.Add(part.Renderer);
                }

                for (int areaIndex = 0; areaIndex < part.Areas.Count; areaIndex++)
                {
                    MeshRenderer isolated = part.Areas[areaIndex].IsolatedRendererComponent;
                    if (isolated != null)
                    {
                        renderers.Add(isolated);
                    }
                }
            }

            return renderers;
        }

        private static void CopyLodRendererSettings(Renderer source, MeshRenderer destination)
        {
            if (destination == null)
            {
                return;
            }

            if (source != null)
            {
                destination.shadowCastingMode = source.shadowCastingMode;
                destination.receiveShadows = source.receiveShadows;
                destination.renderingLayerMask = source.renderingLayerMask;
            }
        }

        private void AdvanceLodTransitionUpdate()
        {
            while (m_LodTransitionUpdateQueueIndex < m_LodTransitionUpdateQueue.Count)
            {
                int objectId = m_LodTransitionUpdateQueue[m_LodTransitionUpdateQueueIndex++];
                if (!m_LodObjects.TryGetValue(objectId, out LodObjectState objectState) || (objectState.Published == null && objectState.Preview == null))
                {
                    continue;
                }

                try
                {
                    ApplyPublishedLodTransitions(objectId, objectState.Published);
                }
                catch (Exception exception)
                {
                    Mod.log.Warn("[PO LOD] Could not refresh distance settings for " + "object " + objectId + ": " + exception);
                }

                break;
            }

            if (m_LodTransitionUpdateQueueIndex >= m_LodTransitionUpdateQueue.Count)
            {
                m_LodTransitionUpdateQueue.Clear();
                m_LodTransitionUpdateQueueIndex = 0;
            }
        }

        private void ApplyPublishedLodTransitions(int objectId, PublishedLodState published)
        {
            if (m_LodObjects.TryGetValue(objectId, out LodObjectState state))
            {
                ApplyLodPresentation(objectId, state, ActiveLodSet(state));
            }
        }

        private static float SanitizeLodDistanceMultiplier(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 1.0f;
            }

            return Math.Max(Setting.kMinimumLodDistanceMultiplier, Math.Min(Setting.kMaximumLodDistanceMultiplier, value));
        }

        private void ForceSelectedLod(int? objectId, bool selected)
        {
            if (!objectId.HasValue || !m_Instances.TryGetValue(objectId.Value, out RenderInstance instance) || instance.Root == null)
            {
                return;
            }

            LODGroup lodGroup = instance.Root.GetComponent<LODGroup>();
            if (lodGroup != null && lodGroup.enabled)
            {
                int force = selected ? 0 : -1;
                if (m_LodObjects.TryGetValue(objectId.Value, out LodObjectState state) && state.PreviewSessionId != 0)
                {
                    force = state.ForcedIndex;
                }

                lodGroup.ForceLOD(force);
            }
        }

        private void DisableLodGroup(int objectId)
        {
            if (!m_Instances.TryGetValue(objectId, out RenderInstance instance) || instance.Root == null)
            {
                return;
            }

            LODGroup group = instance.Root.GetComponent<LODGroup>();
            if (group != null)
            {
                group.enabled = false;
                group.SetLODs(Array.Empty<UnityEngine.LOD>());
            }

            foreach (Renderer renderer in CollectLod0Renderers(instance))
            {
                renderer.enabled = true;
            }
        }

        private void ApplyPublishedLodPropertyBlocks(RenderInstance instance)
        {
            if (instance == null || !m_LodObjects.TryGetValue(instance.ObjectId, out LodObjectState objectState))
            {
                return;
            }

            ApplyLodPropertyBlocks(instance, objectState.Published);
            ApplyLodPropertyBlocks(instance, objectState.Preview);
            ApplyLodPropertyBlocks(instance, objectState.Pending);
        }

        private static void ApplyLodPropertyBlocks(RenderInstance instance, LodRendererCollection collection)
        {
            if (instance == null || collection == null)
            {
                return;
            }

            for (int i = 0; i < collection.RendererEntries.Count; i++)
            {
                LodRendererEntry entry = collection.RendererEntries[i];
                if (entry.Renderer == null || entry.PartIndex < 0 || entry.PartIndex >= instance.Parts.Count)
                {
                    continue;
                }

                RenderArea area = FindRenderArea(instance.Parts[entry.PartIndex], entry.SurfaceAreaId);
                if (area != null)
                {
                    entry.Renderer.sharedMaterial = area.Material;
                    RenderPart part = instance.Parts[entry.PartIndex];
                    CopyLodRendererSettings(area.IsolatedRenderer ? area.IsolatedRendererComponent : part.Renderer, entry.Renderer);
                    if (area.PropertyBlock != null)
                    {
                        entry.Renderer.SetPropertyBlock(area.PropertyBlock);
                    }
                }
            }
        }

        private void RemoveEmptyLodObjectState(int objectId, LodObjectState objectState)
        {
            if (objectState != null && objectState.Pending == null && objectState.Published == null && objectState.Preview == null && objectState.PreviewSessionId == 0)
            {
                m_LodObjects.Remove(objectId);
            }
        }

        private void CompactLodPublicationQueue(bool force = false)
        {
            if (m_LodPublicationQueueIndex == 0)
            {
                return;
            }

            if (!force && (m_LodPublicationQueueIndex < 256 || m_LodPublicationQueueIndex * 2 < m_LodPublicationQueue.Count))
            {
                return;
            }

            m_LodPublicationQueue.RemoveRange(0, m_LodPublicationQueueIndex);
            m_LodPublicationQueueIndex = 0;
        }

        private void DestroyPublicationWork(LodPublicationWork work)
        {
            if (work == null)
            {
                return;
            }

            work.Result = null;
            work.CurrentUpload = null;
            RetireLodResources(work);
            work.Lod1Renderers.Clear();
            work.Lod2Renderers.Clear();
        }

        private void DestroyPublishedLod(PublishedLodState published)
        {
            if (published != null)
            {
                RetireLodResources(published);
            }
        }

        private static double ElapsedMilliseconds(long startTimestamp)
        {
            long elapsed = Stopwatch.GetTimestamp() - startTimestamp;
            return elapsed * 1000.0 / Stopwatch.Frequency;
        }

        private sealed class LodObjectState
        {
            internal LodPublicationWork Pending;
            internal PublishedLodState Published;
            internal PublishedLodState Preview;
            internal ulong PreviewSessionId;
            internal LodProfile DraftProfile;
            internal LodProfile AcceptedProfile;
            internal LodPreviewDisplay Display;
            internal int ForcedIndex = -1;
        }

        private abstract class LodRendererCollection
        {
            internal readonly List<GameObject> GameObjects = new List<GameObject>();
            internal readonly List<Mesh> Meshes = new List<Mesh>();
            internal readonly List<LodRendererEntry> RendererEntries = new List<LodRendererEntry>();

            internal int MeshCount => Meshes.Count;

            internal void ClearUnityObjects()
            {
                GameObjects.Clear();
                Meshes.Clear();
                RendererEntries.Clear();
            }
        }

        private sealed class LodPublicationWork : LodRendererCollection
        {
            internal LodPublicationWork(LodGenerationResult result)
            {
                Result = result ?? throw new ArgumentNullException(nameof(result));
                ObjectId = result.Key.ObjectId;
                Key = result.Key;
                LodProfile settings = LodProfile.Resolve(result.Profile);
                bool build = result.Key.IsPreview || result.Profile != null || result.WorthPublishing;
                UploadLevels = !build ? Array.Empty<int>() : settings.Lod1.Enabled && settings.Lod2.Enabled ? new[] { 1, 2 } : settings.Lod1.Enabled ? new[] { 1 } : settings.Lod2.Enabled ? new[] { 2 } : Array.Empty<int>();
                TotalMeshCount = checked(result.Areas.Length * UploadLevels.Length);
                if (UploadLevels.Length != 0 && result.Areas.Length == 0)
                {
                    throw new InvalidOperationException("Generated levels have no render areas.");
                }

                foreach (LodAreaResult area in result.Areas)
                {
                    foreach (int level in UploadLevels)
                    {
                        if (!(level == 1 ? area.Lod1 : area.Lod2).Enabled)
                        {
                            throw new InvalidOperationException("A generated level is missing a render area.");
                        }
                    }
                }
            }

            internal LodGenerationKey Key { get; }
            internal int[] UploadLevels { get; }
            internal int ObjectId { get; }
            internal int TotalMeshCount { get; }
            internal LodGenerationResult Result;
            internal int NextMeshIndex;
            internal LodMeshUpload CurrentUpload;
            internal readonly List<Renderer> Lod1Renderers = new List<Renderer>();
            internal readonly List<Renderer> Lod2Renderers = new List<Renderer>();

            internal bool IsComplete =>
                NextMeshIndex >= TotalMeshCount;

            internal int RemainingMeshCount =>
                Math.Max(0, TotalMeshCount - NextMeshIndex);

            internal void AddMesh(int level, GameObject gameObject, Mesh mesh, MeshRenderer renderer, int partIndex, int surfaceAreaId)
            {
                GameObjects.Add(gameObject);
                Meshes.Add(mesh);
                RendererEntries.Add(new LodRendererEntry(renderer, level, partIndex, surfaceAreaId));
                if (level == 1)
                {
                    Lod1Renderers.Add(renderer);
                }
                else
                {
                    Lod2Renderers.Add(renderer);
                }
            }

            internal PublishedLodState CreatePublishedState()
            {
                PublishedLodState published = new PublishedLodState
                {
                    Key = Key, Profile = Result.Profile, Chain = Result.AutomaticChain,
                    Diagnostics = new LodObjectDiagnostics(Result),
                };
                published.GameObjects.AddRange(GameObjects);
                published.Meshes.AddRange(Meshes);
                published.RendererEntries.AddRange(RendererEntries);
                return published;
            }

            internal void ReleasePublishedState()
            {
                Result = null;
                ClearUnityObjects();
                Lod1Renderers.Clear();
                Lod2Renderers.Clear();
            }
        }

        private sealed class PublishedLodState : LodRendererCollection
        {
            internal LodGenerationKey Key;
            internal LodProfile Profile;
            internal LodAutomaticChain Chain;
            internal LodObjectDiagnostics Diagnostics;
        }

        private sealed class LodMeshUpload
        {
            internal LodMeshUpload(Mesh mesh, LodMeshData meshData, bool requiresTangents)
            {
                Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
                MeshData = meshData ?? throw new ArgumentNullException(nameof(meshData));
                RequiresTangents = requiresTangents;
                Stage = LodMeshUploadStage.Vertices;
            }

            internal Mesh Mesh { get; }
            internal LodMeshData MeshData { get; }
            internal bool RequiresTangents { get; }
            internal LodMeshUploadStage Stage;
        }

        private enum LodMeshUploadStage
        {
            Vertices = 0,
            Normals = 1,
            UV0 = 2,
            VertexColors = 3,
            TexCoord1 = 4,
            TexCoord3 = 5,
            Indices = 6,
            Tangents = 7,
            Finalize = 8,
        }

        private readonly struct LodRendererEntry
        {
            internal LodRendererEntry(MeshRenderer renderer, int level, int partIndex, int surfaceAreaId)
            {
                Renderer = renderer;
                Level = level;
                PartIndex = partIndex;
                SurfaceAreaId = surfaceAreaId;
            }

            internal MeshRenderer Renderer { get; }
            internal int Level { get; }
            internal int PartIndex { get; }
            internal int SurfaceAreaId { get; }
        }
    }

    internal readonly struct LodPublicationDiagnostics
    {
        internal LodPublicationDiagnostics(int pendingObjects, int pendingMeshUploads, int publishedObjects, int publishedMeshes, long publishedResults, long discardedResults, long failedPublications, double lastPublicationStepMilliseconds, double maximumPublicationStepMilliseconds, string lastFailureReason)
        {
            PendingObjects = pendingObjects;
            PendingMeshUploads = pendingMeshUploads;
            PublishedObjects = publishedObjects;
            PublishedMeshes = publishedMeshes;
            PublishedResults = publishedResults;
            DiscardedResults = discardedResults;
            FailedPublications = failedPublications;
            LastPublicationStepMilliseconds = lastPublicationStepMilliseconds;
            MaximumPublicationStepMilliseconds = maximumPublicationStepMilliseconds;
            LastFailureReason = lastFailureReason ?? string.Empty;
        }

        internal int PendingObjects { get; }
        internal int PendingMeshUploads { get; }
        internal int PublishedObjects { get; }
        internal int PublishedMeshes { get; }
        internal long PublishedResults { get; }
        internal long DiscardedResults { get; }
        internal long FailedPublications { get; }
        internal double LastPublicationStepMilliseconds { get; }
        internal double MaximumPublicationStepMilliseconds { get; }
        internal string LastFailureReason { get; }
    }
}