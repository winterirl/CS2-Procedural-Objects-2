using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Picking;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        private const long kPickCacheBudgetBytes = 128L * 1024 * 1024;
        private readonly ObjectPickWorker m_PickWorker = new ObjectPickWorker();
        private readonly List<PickCandidate> m_PickCandidates = new List<PickCandidate>();
        private readonly List<PickCandidate> m_UnreadyPickCandidates = new List<PickCandidate>();
        private readonly HashSet<int> m_PendingPickObjects = new HashSet<int>();
        private ObjectPickCapture m_PickCapture;
        private long m_PickQuery;
        private uint m_PickSceneVersion;
        internal uint PickSceneVersion => m_PickSceneVersion;

        private struct PickCandidate
        {
            internal RenderInstance Instance;
            internal RenderPart Part;
            internal int PartIndex;
            internal float Entry;
        }

        private static readonly Comparison<PickCandidate> s_ComparePickCandidates = ComparePickCandidates;

        private static int ComparePickCandidates(PickCandidate a, PickCandidate b)
        {
            int result = a.Entry.CompareTo(b.Entry);
            if (result == 0) result = a.Instance.ObjectId.CompareTo(b.Instance.ObjectId);
            return result != 0 ? result : a.PartIndex.CompareTo(b.PartIndex);
        }

        // Keep the coarse public query for existing callers. Object selection
        // uses FindClosestMesh so fallbacks can be resolved across all POs.
        public bool IntersectRay(int objectId, Ray ray, out float distance)
        {
            distance = 0f;
            if (!m_Instances.TryGetValue(objectId, out RenderInstance instance)) return false;
            float nearest = float.PositiveInfinity;
            bool hit = false;
            foreach (RenderPart part in instance.Parts)
            {
                if (TryGetPickBounds(part, ray, out float entry))
                {
                    nearest = Mathf.Min(nearest, entry);
                    hit = true;
                }
            }

            if (hit) distance = nearest;
            return hit;
        }

        internal ObjectPickStatus FindClosestObject(Ray ray, out int objectId) => FindClosestMesh(ray, out objectId, out _, true);

        internal ObjectPickStatus FindClosestMesh(Ray ray, out int objectId, out float distance) =>
            FindClosestMesh(ray, out objectId, out distance, false);

        private ObjectPickStatus FindClosestMesh(Ray ray, out int objectId, out float distance, bool objectOnly)
        {
            objectId = -1;
            distance = float.PositiveInfinity;
            if (!TrianglePickCache.IsFinite(ray.origin) || !TrianglePickCache.IsFinite(ray.direction) || ray.direction.sqrMagnitude <= 0f)
            {
                return ObjectPickStatus.Miss;
            }

            ray.direction = ray.direction.normalized;
            Exception error = m_PickWorker.Poll();
            if (error != null) Mod.log.Warn($"[PO Picking] Mesh cache build failed: {error.Message}");
            m_PickCandidates.Clear();
            m_PendingPickObjects.Clear();
            m_UnreadyPickCandidates.Clear();
            m_PickQuery++;
            int boundsObject = -1;
            float boundsDistance = float.PositiveInfinity;
            bool waitingForSync = false;
            foreach (RenderInstance instance in m_Instances.Values)
            {
                if (instance.Source.DirtyFlags != ProceduralObjectDirtyFlags.None)
                {
                    waitingForSync = true;
                    continue;
                }

                for (int i = 0; i < instance.Parts.Count; i++)
                {
                    RenderPart part = instance.Parts[i];
                    if (!TryGetPickBounds(part, ray, out float entry)) continue;
                    m_PickCandidates.Add(new PickCandidate
                    {
                        Instance = instance, Part = part, PartIndex = i, Entry = entry,
                    });
                    if (BetterObjectHit(entry, instance.ObjectId, boundsDistance, boundsObject))
                    {
                        boundsDistance = entry;
                        boundsObject = instance.ObjectId;
                    }
                }
            }

            if (waitingForSync)
            {
                m_PickCapture = null;
                m_PickCandidates.Clear();
                return ObjectPickStatus.Pending;
            }

            m_PickCandidates.Sort(s_ComparePickCandidates);
            // Check ready parts first so another part of the same PO need not delay hover.
            foreach (var candidate in m_PickCandidates)
            {
                if (candidate.Entry > distance + TrianglePickCache.HitTolerance(distance)) break;
                var state = candidate.Part.PickCache;
                state.LastUsed = m_PickQuery;
                var source = candidate.Part.PickSource;
                state.Observe(source, source.PositionVersion, source.FullGeometryVersion, source.TopologyVersion, source.SurfaceAreaMembershipVersion);
                if (state.Cache != null) PickCachedCandidate(candidate, ray, ref objectId, ref distance);
                else if (!state.Failed) m_UnreadyPickCandidates.Add(candidate);
            }

            if (m_PickCapture != null)
            {
                bool needed = false;
                foreach (var candidate in m_UnreadyPickCandidates)
                {
                    if (candidate.Entry > distance + TrianglePickCache.HitTolerance(distance)) break;
                    if (objectOnly && candidate.Instance.ObjectId == objectId) continue;
                    if (ReferenceEquals(candidate.Part.PickCache, m_PickCapture.Target))
                    {
                        needed = true;
                        break;
                    }
                }

                if (!needed) m_PickCapture = null;
            }

            long captureDeadline = Stopwatch.GetTimestamp() + Math.Max(1, Stopwatch.Frequency / 500);
            foreach (var candidate in m_UnreadyPickCandidates)
            {
                if (candidate.Entry > distance + TrianglePickCache.HitTolerance(distance)) break;
                var part = candidate.Part;
                var state = part.PickCache;
                if (!(objectOnly && objectId == candidate.Instance.ObjectId) && !m_PickWorker.IsBuilding(state))
                {
                    var source = part.PickSource;
                    bool large = source.Mesh.Triangles.Length / 3 > 64 || source.Mesh.Vertices.Length > 192;
                    if (Stopwatch.GetTimestamp() < captureDeadline && (!large || m_PickWorker.HasCapacity))
                        BeginPickCache(part, large, captureDeadline);
                }

                if (state.Cache != null) PickCachedCandidate(candidate, ray, ref objectId, ref distance);
                else if (!state.Failed) m_PendingPickObjects.Add(candidate.Instance.ObjectId);
            }

            m_UnreadyPickCandidates.Clear();
            if (m_PickCapture != null && m_PickCapture.Target.LastUsed != m_PickQuery) m_PickCapture = null;
            TrimPickCaches();
            m_PickCandidates.Clear();
            // More hits on the same PO cannot change its identity. Other POs
            // still need testing before hover or a queued click can proceed.
            bool pending = m_PendingPickObjects.Count != 0 && (!objectOnly || objectId < 0 || m_PendingPickObjects.Count > 1 || !m_PendingPickObjects.Contains(objectId));
            if (pending)
            {
                objectId = -1;
                distance = float.PositiveInfinity;
                return ObjectPickStatus.Pending;
            }

            if (objectId >= 0) return ObjectPickStatus.Hit;
            objectId = boundsObject;
            distance = boundsDistance;
            return objectId >= 0 ? ObjectPickStatus.Hit : ObjectPickStatus.Miss;
        }

        private static void PickCachedCandidate(PickCandidate candidate, Ray ray, ref int objectId, ref float distance)
        {
            ObjectTransform transform = candidate.Instance.Source.Transform;
            if (!CanInvertPickTransform(transform)) return;
            Vector3 localOrigin = transform.InverseTransformPoint(ray.origin);
            Vector3 localDirection = transform.InverseTransformVector(ray.direction);
            // Leave localDirection unnormalized so t remains a world distance.
            if (candidate.Part.PickCache.Cache.TryPick(localOrigin, localDirection, out _, out float hitDistance, distance + TrianglePickCache.HitTolerance(distance)) && BetterObjectHit(hitDistance, candidate.Instance.ObjectId, distance, objectId))
            {
                distance = hitDistance;
                objectId = candidate.Instance.ObjectId;
            }
        }

        private static bool CanInvertPickTransform(ObjectTransform transform)
        {
            return TrianglePickCache.IsFinite(transform.Scale) && Mathf.Abs(transform.Scale.x) >= 0.000001f && Mathf.Abs(transform.Scale.y) >= 0.000001f && Mathf.Abs(transform.Scale.z) >= 0.000001f;
        }

        private static bool BetterObjectHit(float candidate, int candidateId, float best, int bestId)
        {
            if (bestId < 0) return true;
            float tolerance = TrianglePickCache.HitTolerance(Mathf.Max(candidate, best));
            return candidate < best - tolerance || (Mathf.Abs(candidate - best) <= tolerance && candidateId < bestId);
        }

        private static bool TryGetPickBounds(RenderPart part, Ray ray, out float entry)
        {
            entry = float.PositiveInfinity;
            bool hit = false;
            if (part.Renderer != null && part.HasMainRendererGeometry)
            {
                hit |= AccumulatePickBounds(part.Renderer.bounds, ray, ref entry);
            }

            for (int i = 0; i < part.Areas.Count; i++)
            {
                MeshRenderer renderer = part.Areas[i].IsolatedRendererComponent;
                if (renderer != null) hit |= AccumulatePickBounds(renderer.bounds, ray, ref entry);
            }

            return hit;
        }

        private static bool AccumulatePickBounds(Bounds bounds, Ray ray, ref float entry)
        {
            if (!TrianglePickCache.TryIntersectBounds(ray.origin, ray.direction, bounds.min, bounds.max, float.PositiveInfinity, out float candidate)) return false;
            entry = Mathf.Min(entry, candidate);
            return true;
        }

        private void BeginPickCache(RenderPart part, bool background, long deadline)
        {
            ObjectPickCacheState state = part.PickCache;
            try
            {
                if (background && m_PickCapture != null && !ReferenceEquals(m_PickCapture.Target, state)) return;
                if (background && m_PickCapture != null && m_PickCapture.Revision != state.Revision)
                {
                    m_PickCapture = null;
                }

                ObjectPickCapture capture = background ? m_PickCapture : null;
                if (capture == null)
                {
                    HashSet<int> areas = new HashSet<int>();
                    for (int i = 0; i < part.Areas.Count; i++) areas.Add(part.Areas[i].SurfaceAreaId);
                    capture = new ObjectPickCapture(state, part.PickSource.Mesh.Vertices, part.PickSource.Mesh.Triangles, part.PickSource.TriangleSurfaceAreaIds, areas);
                    if (background) m_PickCapture = capture;
                }

                if (background)
                {
                    if (capture.Step(deadline))
                    {
                        m_PickWorker.TryStart(state, capture.CreateBuild());
                        m_PickCapture = null;
                    }

                    return;
                }

                while (!capture.Step())
                {
                }

                state.Cache = capture.CreateBuild()();
                state.RefitSource = null;
            }
            catch (Exception error)
            {
                state.Failed = true;
                state.RefitSource = null;
                m_PickCapture = null;
                Mod.log.Warn($"[PO Picking] Mesh capture failed: {error.Message}");
            }
        }

        private void TrimPickCaches()
        {
            long retained = 0;
            foreach (RenderInstance instance in m_Instances.Values)
            {
                foreach (RenderPart part in instance.Parts) retained += part.PickCache.RetainedBytes;
            }

            // Keep this query's working set. Evict older caches by last use.
            while (retained > kPickCacheBudgetBytes)
            {
                ObjectPickCacheState oldest = null;
                foreach (RenderInstance instance in m_Instances.Values)
                {
                    foreach (RenderPart part in instance.Parts)
                    {
                        ObjectPickCacheState state = part.PickCache;
                        if (state.RetainedBytes > 0 && state.LastUsed != m_PickQuery && (oldest == null || state.LastUsed < oldest.LastUsed)) oldest = state;
                    }
                }

                if (oldest == null) break;
                retained -= oldest.RetainedBytes;
                oldest.Release();
            }
        }
    }
}