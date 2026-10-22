using System;
using System.Collections.Generic;
using System.Diagnostics;
using PO2.Core.Lod;
using PO2.Rendering.Lod;
using UnityEngine;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        private Func<LodGenerationKey, bool> m_IsCurrentLodRequest;
        private Action<LodGenerationKey, int> m_LodPublicationReady;
        private Action<LodGenerationKey, string> m_LodPublicationFailed;
        private Action<LodGenerationKey, int, int> m_LodUploadProgress;
        private Action<LodGenerationKey> m_LodPublicationDiscarded;
        private float m_GlobalLod1Distance = 1f;
        private float m_GlobalLod2Distance = 1f;
        private readonly List<LodRendererCollection> m_RetiredLodSets = new List<LodRendererCollection>();

        internal void ConfigureLodRequests(Func<LodGenerationKey, bool> isCurrent, Action<LodGenerationKey, int> ready, Action<LodGenerationKey, string> failed, Action<LodGenerationKey, int, int> progress, Action<LodGenerationKey> discarded = null)
        {
            m_IsCurrentLodRequest = isCurrent;
            m_LodPublicationReady = ready;
            m_LodPublicationFailed = failed;
            m_LodUploadProgress = progress;
            m_LodPublicationDiscarded = discarded;
        }

        internal void SyncAcceptedLodProfile(int objectId, LodProfile profile)
        {
            if (!m_LodObjects.TryGetValue(objectId, out LodObjectState state))
            {
                return;
            }

            if (Equals(state.AcceptedProfile, profile))
            {
                return;
            }

            state.AcceptedProfile = profile;
            if (state.PreviewSessionId == 0)
            {
                ApplyLodPresentation(objectId, state, state.Published);
            }
        }

        internal bool BeginLodPreview(int objectId, ulong sessionId, LodProfile profile)
        {
            if (sessionId == 0 || !m_Instances.ContainsKey(objectId))
            {
                return false;
            }

            if (!m_LodObjects.TryGetValue(objectId, out LodObjectState state))
            {
                state = new LodObjectState();
                m_LodObjects.Add(objectId, state);
            }

            if (state.PreviewSessionId == sessionId)
            {
                return true;
            }

            DiscardPendingLod(state);
            DestroyPublishedLod(state.Preview);
            state.Preview = null;
            state.PreviewSessionId = sessionId;
            state.AcceptedProfile = state.DraftProfile = profile;
            state.Display = LodPreviewDisplay.Original;
            ApplyLodPresentation(objectId, state, state.Published);
            return true;
        }

        internal void SetLodPreviewDraft(int objectId, ulong sessionId, LodProfile profile, LodPreviewDisplay display)
        {
            if (!TryGetLodSession(objectId, sessionId, out LodObjectState state))
            {
                return;
            }

            LodProfile previous = state.DraftProfile;
            LodPreviewDisplay previousDisplay = state.Display;
            try
            {
                state.DraftProfile = profile;
                state.Display = display;
                ApplyLodPresentation(objectId, state, ActiveLodSet(state));
            }
            catch
            {
                state.DraftProfile = previous;
                state.Display = previousDisplay;
                throw;
            }
        }

        internal void CancelLodPreview(int objectId, ulong sessionId, LodProfile acceptedProfile)
        {
            if (!TryGetLodSession(objectId, sessionId, out LodObjectState state))
            {
                return;
            }

            DiscardPendingLod(state);
            PublishedLodState previous = state.Preview;
            state.Preview = null;
            state.AcceptedProfile = state.DraftProfile = acceptedProfile;
            state.Display = LodPreviewDisplay.Original;
            RestoreAcceptedLodPresentation(objectId, state, previous);
        }

        internal void EndLodPreview(int objectId, ulong sessionId)
        {
            if (!TryGetLodSession(objectId, sessionId, out LodObjectState state))
            {
                return;
            }

            DiscardPendingLod(state);
            PublishedLodState previous = state.Preview;
            state.Preview = null;
            state.PreviewSessionId = 0;
            state.DraftProfile = null;
            state.Display = LodPreviewDisplay.Auto;
            RestoreAcceptedLodPresentation(objectId, state, previous);
        }

        private void RestoreAcceptedLodPresentation(int objectId, LodObjectState state, PublishedLodState previous)
        {
            try
            {
                ApplyLodPresentation(objectId, state, state.Published);
            }
            catch (Exception exception)
            {
                DisableLodGroup(objectId);
                SetLodSetActive(state.Published, null);
                Mod.log.Warn("[PO LOD] Restored original rendering after preview cancellation: " + exception.Message);
            }
            finally
            {
                DestroyPublishedLod(previous);
            }
        }

        internal void DiscardPendingLodPreview(int objectId, ulong sessionId)
        {
            if (TryGetLodSession(objectId, sessionId, out LodObjectState state))
            {
                DiscardPendingLod(state);
            }
        }

        internal bool TryBorrowAcceptedLod(LodGenerationKey key, LodProfile profile, out LodObjectDiagnostics diagnostics, out LodAutomaticChain chain)
        {
            diagnostics = default;
            chain = default;
            if (!TryGetLodSession(key.ObjectId, key.PreviewSessionId, out LodObjectState state))
            {
                return false;
            }

            PublishedLodState candidate = MatchesLodSource(state.Preview, key, profile) ? state.Preview : MatchesLodSource(state.Published, key, profile) ? state.Published : null;
            if (candidate == null)
            {
                return false;
            }

            try
            {
                ApplyLodPresentation(key.ObjectId, state, candidate);
            }
            catch (Exception exception)
            {
                Mod.log.Warn("[PO LOD] Could not reuse preview meshes: " + exception.Message);
                return false;
            }

            if (ReferenceEquals(candidate, state.Preview))
            {
                candidate.Key = key;
            }
            else
            {
                PublishedLodState previous = state.Preview;
                state.Preview = null;
                DestroyPublishedLod(previous);
            }

            diagnostics = candidate.Diagnostics.ForProfile(profile).WithKey(key);
            chain = ResolveLodChain(candidate, profile);
            return true;
        }

        internal bool TryPromoteLodPreview(LodGenerationKey previewKey, LodGenerationKey acceptedKey, LodProfile profile, Func<bool> commit)
        {
            if (!IsLodRequestCurrent(previewKey) || !TryGetLodSession(previewKey.ObjectId, previewKey.PreviewSessionId, out LodObjectState state) || state.Pending != null)
            {
                return false;
            }

            PublishedLodState candidate = state.Preview ?? state.Published;
            if (!MatchesLodSource(candidate, previewKey, profile) || (state.Preview != null && candidate.Key != previewKey))
            {
                return false;
            }

            PublishedLodState previous = state.Published;
            LodGenerationKey previousKey = candidate.Key;
            LodProfile previousProfile = candidate.Profile;
            try
            {
                ApplyLodPresentation(previewKey.ObjectId, state, candidate);
                if (!IsLodRequestCurrent(previewKey) || commit == null || !commit())
                {
                    return false;
                }

                candidate.Key = acceptedKey;
                candidate.Chain = ResolveLodChain(candidate, profile);
                candidate.Diagnostics = candidate.Diagnostics.ForProfile(profile).WithKey(acceptedKey);
                candidate.Profile = profile;
                state.AcceptedProfile = state.DraftProfile = profile;
                state.Published = candidate;
                state.Preview = null;
            }
            catch
            {
                candidate.Key = previousKey;
                candidate.Profile = previousProfile;
                ApplyLodPresentation(previewKey.ObjectId, state, ActiveLodSet(state));
                throw;
            }

            if (!ReferenceEquals(previous, candidate))
            {
                DestroyPublishedLod(previous);
            }

            return true;
        }

        private bool IsLodRequestCurrent(LodGenerationKey key)
        {
            return m_IsCurrentLodRequest != null && m_IsCurrentLodRequest(key);
        }

        private bool TryGetLodSession(int objectId, ulong sessionId, out LodObjectState state)
        {
            return m_LodObjects.TryGetValue(objectId, out state) && sessionId != 0 && state.PreviewSessionId == sessionId;
        }

        private static bool MatchesLodSource(PublishedLodState set, LodGenerationKey key, LodProfile profile)
        {
            return set != null && set.Key.WorldEpoch == key.WorldEpoch && set.Key.ObjectId == key.ObjectId && set.Key.PersistentId == key.PersistentId && set.Key.SourceRevision == key.SourceRevision && LodProfile.CanReuseGeneration(set.Profile, profile);
        }

        private static LodAutomaticChain ResolveLodChain(PublishedLodState set, LodProfile profile)
        {
            if (LodProfile.HasSameGeneration(set.Profile, profile))
            {
                return set.Chain;
            }

            LodProfile settings = LodProfile.Resolve(profile);
            return LodAutomaticChain.Resolve(profile, set.Diagnostics.SourceTriangles, settings.Lod1.Enabled ? set.Diagnostics.Lod1Triangles : 0, settings.Lod2.Enabled ? set.Diagnostics.Lod2Triangles : 0, LodGenerationSettings.CreateCalibrationBaseline().MinimumWholeObjectTriangleReduction);
        }

        private static PublishedLodState ActiveLodSet(LodObjectState state)
        {
            return state.PreviewSessionId != 0 ? state.Preview ?? state.Published : state.Published;
        }

        private void DiscardPendingLod(LodObjectState state)
        {
            if (state.Pending == null)
            {
                return;
            }

            LodGenerationKey key = state.Pending.Key;
            DestroyPublicationWork(state.Pending);
            state.Pending = null;
            m_LodPendingResultCount = Math.Max(0, m_LodPendingResultCount - 1);
            ++m_LodDiscardedResultCount;
            m_LodPublicationDiscarded?.Invoke(key);
        }

        private void ApplyLodPresentation(int objectId, LodObjectState state, PublishedLodState set)
        {
            if (!m_Instances.TryGetValue(objectId, out RenderInstance instance) || instance.Root == null)
            {
                throw new InvalidOperationException("The rendered object no longer exists.");
            }

            var levels = new[] { CollectLod0Renderers(instance), new List<Renderer>(), new List<Renderer>() };
            if (set != null)
            {
                foreach (LodRendererEntry entry in set.RendererEntries)
                {
                    if (entry.Renderer != null)
                    {
                        levels[entry.Level].Add(entry.Renderer);
                    }
                }
            }

            if (levels[0].Count == 0)
            {
                throw new InvalidOperationException("The original renderers are missing.");
            }

            LodProfile profile = state.PreviewSessionId != 0 ? state.DraftProfile : state.AcceptedProfile;
            LodAutomaticChain chain = set == null ? default : LodProfile.CanReuseGeneration(set.Profile, profile) ? ResolveLodChain(set, profile) : set.Chain;
            LodPresentation presentation = LodPresentation.Create(chain, levels[1].Count != 0 && chain.Lod1Status != LodLevelStatus.Disabled, levels[2].Count != 0 && chain.Lod2Status != LodLevelStatus.Disabled, state.PreviewSessionId != 0 ? state.Display : LodPreviewDisplay.Auto, state.PreviewSessionId == 0 && m_SelectedLodObjectId == objectId, profile, m_GlobalLod1Distance, m_GlobalLod2Distance);
            var lods = new UnityEngine.LOD[presentation.Levels.Length];
            for (int i = 0; i < lods.Length; ++i)
            {
                lods[i] = new UnityEngine.LOD(presentation.Heights[i], levels[presentation.Levels[i]].ToArray());
            }

            LODGroup group = instance.Root.GetComponent<LODGroup>() ?? instance.Root.AddComponent<LODGroup>();
            UnityEngine.LOD[] previousLods = group.GetLODs();
            bool previousEnabled = group.enabled;
            Vector3 previousCenter = group.localReferencePoint;
            float previousSize = group.size;
            PublishedLodState previousSet = ActiveLodSet(state);
            int previousForce = state.ForcedIndex;
            try
            {
                group.SetLODs(lods);
                Bounds bounds = OriginalLodBounds(instance);
                group.localReferencePoint = bounds.center;
                group.size = Math.Max(bounds.size.x, Math.Max(bounds.size.y, bounds.size.z));
                group.fadeMode = LODFadeMode.None;
                SetLodSetActive(state.Published, null);
                SetLodSetActive(state.Preview, null);
                SetLodSetActive(set, presentation.Levels);
                group.enabled = true;
                group.ForceLOD(presentation.ForcedIndex);
                state.ForcedIndex = presentation.ForcedIndex;
            }
            catch
            {
                SetLodSetActive(set, null);
                group.SetLODs(previousLods);
                group.localReferencePoint = previousCenter;
                group.size = previousSize;
                group.enabled = previousEnabled;
                if (previousSet != null)
                {
                    foreach (LodRendererEntry entry in previousSet.RendererEntries)
                    {
                        bool used = Array.Exists(previousLods, lod => Array.IndexOf(lod.renderers, entry.Renderer) >= 0);
                        if (entry.Renderer != null)
                        {
                            entry.Renderer.gameObject.SetActive(used);
                        }
                    }
                }

                group.ForceLOD(previousForce);
                throw;
            }
        }

        private static Bounds OriginalLodBounds(RenderInstance instance)
        {
            bool found = false;
            Bounds bounds = default;
            foreach (RenderPart part in instance.Parts)
            {
                if (part.HasMainRendererGeometry && part.Mesh != null)
                {
                    Include(part.Mesh.bounds);
                }

                foreach (RenderArea area in part.Areas)
                {
                    if (area.IsolatedMesh != null)
                    {
                        Include(area.IsolatedMesh.bounds);
                    }
                }
            }

            if (!found)
            {
                throw new InvalidOperationException("The original mesh bounds are missing.");
            }

            return bounds;

            void Include(Bounds next)
            {
                if (!found)
                {
                    bounds = next;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(next);
                }
            }
        }

        private static void SetLodSetActive(PublishedLodState set, int[] levels)
        {
            if (set == null)
            {
                return;
            }

            foreach (LodRendererEntry entry in set.RendererEntries)
            {
                if (entry.Renderer != null)
                {
                    entry.Renderer.gameObject.SetActive(levels != null && Array.IndexOf(levels, entry.Level) >= 0);
                }
            }
        }

        private void RetireLodResources(LodRendererCollection collection)
        {
            foreach (GameObject obj in collection.GameObjects)
            {
                if (obj != null)
                {
                    obj.SetActive(false);
                }
            }

            if (collection.Meshes.Count != 0 || collection.GameObjects.Count != 0)
            {
                m_RetiredLodSets.Add(collection);
            }
        }

        private void AdvanceLodRetirement(bool all = false)
        {
            do
            {
                if (m_RetiredLodSets.Count == 0)
                {
                    break;
                }

                LodRendererCollection collection = m_RetiredLodSets[0];
                int last = collection.Meshes.Count - 1;
                if (last >= 0)
                {
                    if (collection.Meshes[last] != null)
                    {
                        UnityEngine.Object.Destroy(collection.Meshes[last]);
                    }

                    collection.Meshes.RemoveAt(last);
                }

                last = collection.GameObjects.Count - 1;
                if (last >= 0)
                {
                    if (collection.GameObjects[last] != null)
                    {
                        UnityEngine.Object.Destroy(collection.GameObjects[last]);
                    }

                    collection.GameObjects.RemoveAt(last);
                }

                if (collection.Meshes.Count == 0 && collection.GameObjects.Count == 0)
                {
                    collection.ClearUnityObjects();
                    m_RetiredLodSets.RemoveAt(0);
                }
            } while (all);
        }
    }
}