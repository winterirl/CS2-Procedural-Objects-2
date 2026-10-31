using System;
using System.Collections.Generic;
using System.Diagnostics;
using Game;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Tools.Snapping
{
    // Builds one reusable local-space snap tree per PO.
    // Managed geometry is copied under a small frame budget then one Burst build runs at a time.
    public partial class SnapIndexSystem : GameSystemBase
    {
        private const double kCaptureBudgetMilliseconds = 0.75;
        private const int kBudgetCheckInterval = 256;
        private const float kTargetTieEpsilon = 0.0001f;

        private enum EntryState
        {
            Pending,
            Capturing,
            Building,
            Ready,
            Failed,
            Retired,
        }

        private struct PartCaptureInfo
        {
            public int VertexOffset;
            public int VertexCount;
            public int EdgeOffset;
            public int EdgeCount;
            public bool UseStructuralEdges;
        }

        private sealed class BuildData : IDisposable
        {
            public readonly PartCaptureInfo[] Parts;
            public readonly int TotalEdgeCount;
            public NativeArray<float3> LocalVertices;
            public NativeArray<int2> VertexMetadata;
            public NativeArray<byte> VertexValid;
            public NativeArray<int2> Edges;
            public NativeArray<int2> EdgeMetadata;
            public NativeArray<int> PrimitiveOrder;
            public NativeArray<ObjectSnapIndex.Node> Nodes;
            public NativeArray<ObjectSnapIndex.BuildRange> WorkStack;
            public NativeArray<int> NodeCount;

            public int PartIndex;
            public int ItemIndex;
            public bool CapturingEdges;
            public int ValidPrimitiveCount;

            public int TotalVertexCount => LocalVertices.IsCreated ? LocalVertices.Length : 0;

            public bool CaptureComplete => PartIndex >= Parts.Length;

            public BuildData(PartCaptureInfo[] parts, int totalVertexCount, int totalEdgeCount)
            {
                Parts = parts ?? Array.Empty<PartCaptureInfo>();
                TotalEdgeCount = totalEdgeCount;

                int primitiveCount = checked(totalVertexCount + totalEdgeCount);

                try
                {
                    LocalVertices = CreateArray<float3>(totalVertexCount);

                    VertexMetadata = CreateArray<int2>(totalVertexCount);

                    VertexValid = CreateArray<byte>(totalVertexCount, NativeArrayOptions.ClearMemory);

                    Edges = CreateArray<int2>(Math.Max(1, totalEdgeCount));

                    EdgeMetadata = CreateArray<int2>(totalEdgeCount);

                    PrimitiveOrder = CreateArray<int>(primitiveCount);
                }
                catch
                {
                    // The caller cannot dispose a constructor that never returned.
                    Dispose();
                    throw;
                }
            }

            public void PrepareBuild()
            {
                int nodeCapacity = CalculateNodeCapacity(ValidPrimitiveCount);

                Nodes = CreateArray<ObjectSnapIndex.Node>(nodeCapacity);

                WorkStack = CreateArray<ObjectSnapIndex.BuildRange>(nodeCapacity);

                NodeCount = CreateArray<int>(1, NativeArrayOptions.ClearMemory);
            }

            public ObjectSnapIndex CreateIndex()
            {
                int nodeCount = NodeCount.IsCreated ? NodeCount[0] : 0;

                DisposeArray(ref VertexValid);
                DisposeArray(ref WorkStack);
                DisposeArray(ref NodeCount);

                // Burst jobs require every NativeArray field to be assigned.
                // A vertex-only PO therefore builds against one unused edge slot, which is released before the persistent index owns it.
                if (TotalEdgeCount <= 0)
                {
                    DisposeArray(ref Edges);
                }

                ObjectSnapIndex index = new ObjectSnapIndex(LocalVertices, VertexMetadata, Edges, EdgeMetadata, PrimitiveOrder, Nodes, nodeCount);

                LocalVertices = default;
                VertexMetadata = default;
                Edges = default;
                EdgeMetadata = default;
                PrimitiveOrder = default;
                Nodes = default;

                return index;
            }

            public void Dispose()
            {
                DisposeArray(ref LocalVertices);
                DisposeArray(ref VertexMetadata);
                DisposeArray(ref VertexValid);
                DisposeArray(ref Edges);
                DisposeArray(ref EdgeMetadata);
                DisposeArray(ref PrimitiveOrder);
                DisposeArray(ref Nodes);
                DisposeArray(ref WorkStack);
                DisposeArray(ref NodeCount);
            }

            private static int CalculateNodeCapacity(int primitiveCount)
            {
                if (primitiveCount <= 0)
                {
                    return 0;
                }

                const int leafPrimitiveCount = 8;
                int leafCapacity = 1;

                while ((long)leafCapacity * leafPrimitiveCount < primitiveCount)
                {
                    if (leafCapacity > int.MaxValue / 2)
                    {
                        throw new InvalidOperationException("PO snap index is too large to allocate.");
                    }

                    leafCapacity *= 2;
                }

                return checked(leafCapacity * 2 - 1);
            }

            private static NativeArray<T> CreateArray<T>(int length, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory) where T : struct
            {
                if (length <= 0)
                {
                    return default;
                }

                return new NativeArray<T>(length, Allocator.Persistent, options);
            }

            private static void DisposeArray<T>(ref NativeArray<T> array) where T : struct
            {
                if (!array.IsCreated)
                {
                    return;
                }

                array.Dispose();
                array = default;
            }
        }

        private sealed class Entry
        {
            public readonly ProceduralObject Owner;
            public EntryState State = EntryState.Pending;
            public ObjectSnapIndex Index;
            public BuildData BuildData;
            public JobHandle BuildHandle;
            public bool BuildStale;
            public bool PriorityRequested;
            public uint CapturedGeometryVersion;
            public uint[] CapturedTopologyVersions = Array.Empty<uint>();

            public Entry(ProceduralObject owner)
            {
                Owner = owner;
            }
        }

        private readonly Dictionary<int, Entry> m_Entries = new Dictionary<int, Entry>();

        private readonly HashSet<int> m_SeenObjectIds = new HashSet<int>();

        private readonly List<int> m_RemovedObjectIds = new List<int>();

        private ProceduralObjectSystem m_ProceduralObjectSystem;
        private Entry m_ActiveEntry;

        protected override void OnCreate()
        {
            base.OnCreate();
        }

        protected override void OnUpdate()
        {
            if (m_ProceduralObjectSystem == null)
            {
                m_ProceduralObjectSystem = World.GetOrCreateSystemManaged<ProceduralObjectSystem>();
            }

            if (!IsSupportedRuntimeMode())
            {
                RetireAllEntries();
                AdvanceActiveEntry();
                return;
            }

            SynchronizeEntries();
            AdvanceActiveEntry();

            if (m_ActiveEntry == null)
            {
                StartNextEntry();
            }
        }

        protected override void OnDestroy()
        {
            if (m_ActiveEntry != null && (m_ActiveEntry.State == EntryState.Building || m_ActiveEntry.State == EntryState.Retired))
            {
                m_ActiveEntry.BuildHandle.Complete();
            }

            RetireAllEntries();

            if (m_ActiveEntry != null)
            {
                m_ActiveEntry.BuildData?.Dispose();
                m_ActiveEntry.BuildData = null;
                m_ActiveEntry = null;
            }

            m_ProceduralObjectSystem = null;
            base.OnDestroy();
        }

        internal bool TryCreateObjectTranslationSource(ProceduralObject proceduralObject, ObjectTransform sourceTransform, int includedPartIndex, out ITranslationSnapSource source)
        {
            source = null;

            if (proceduralObject == null || includedPartIndex < -1 || includedPartIndex >= proceduralObject.Parts.Count)
            {
                return false;
            }

            if (!TryGetReadyIndex(proceduralObject, out ObjectSnapIndex index) || !index.TryAcquireLease(out ObjectSnapIndexLease lease))
            {
                RequestPriorityBuild(proceduralObject);
                return false;
            }

            source = new IndexedTranslationSnapSource(lease, sourceTransform, includedPartIndex);

            return true;
        }

        internal bool TryCreateMeshTranslationTargetSession(ProceduralObject movingObject, IReadOnlyList<VertexRef> movingVertices, out MeshTranslationSnapTargetSession session, MeshSnapTargetFilter preparedFilter = null, long initialSequence = -1)
        {
            session = null;
            if (movingObject == null) return false;
            var owners = new List<ProceduralObject>(m_Entries.Count);
            foreach (Entry entry in m_Entries.Values)
                if (entry.Owner != null)
                    owners.Add(entry.Owner);
            session = new MeshTranslationSnapTargetSession(this, owners.ToArray(), movingObject, preparedFilter ?? (movingVertices == null ? null : new MeshSnapTargetFilter(movingVertices)), initialSequence);
            return true;
        }

        internal bool IsMeshTargetOwnerCurrent(ProceduralObject owner) => owner != null && m_Entries.TryGetValue(owner.Id, out var entry) && ReferenceEquals(entry.Owner, owner);

        internal bool TryAcquireMeshTarget(ProceduralObject owner, out ObjectSnapIndexLease lease)
        {
            lease = null;
            if (TryGetReadyIndex(owner, out var index) && index.TryAcquireLease(out lease)) return true;
            RequestPriorityBuild(owner);
            return false;
        }

        internal bool TryFindObjectTranslationTarget(Camera camera, int movingObjectId, int excludedPartIndex, Vector2 cursorPosition, float maximumDistancePixels, bool verticesEnabled, bool edgesEnabled, out SnapTarget target)
        {
            target = default;

            if (camera == null || (!verticesEnabled && !edgesEnabled))
            {
                return false;
            }

            bool found = false;
            float bestDistanceSquared = maximumDistancePixels * maximumDistancePixels;

            float screenQueryRadius = -1f;
            ObjectSnapScreenQuery screenQuery = default;

            foreach (Entry entry in m_Entries.Values)
            {
                ProceduralObject owner = entry.Owner;

                if (owner == null || (owner.Id == movingObjectId && excludedPartIndex < 0) || entry.State != EntryState.Ready || entry.Index == null || !MatchesCapturedVersions(entry))
                {
                    continue;
                }

                float queryRadius = found ? Mathf.Sqrt(bestDistanceSquared + kTargetTieEpsilon) : maximumDistancePixels;

                if (!queryRadius.Equals(screenQueryRadius))
                {
                    if (!ObjectSnapScreenQuery.TryCreate(camera, cursorPosition, queryRadius, out screenQuery))
                    {
                        continue;
                    }

                    screenQueryRadius = queryRadius;
                }

                bool objectFound = entry.Index.TryFindTarget(camera, owner.Transform, owner.Id, screenQuery, verticesEnabled, edgesEnabled, owner.Id == movingObjectId ? excludedPartIndex : -1, null, out SnapTarget candidate);

                if (!objectFound)
                {
                    continue;
                }

                float candidateDistanceSquared = candidate.CursorDistancePixels * candidate.CursorDistancePixels;
                bool closer = candidateDistanceSquared < bestDistanceSquared - kTargetTieEpsilon;
                bool tied = Mathf.Abs(candidateDistanceSquared - bestDistanceSquared) <= kTargetTieEpsilon;

                if (!closer && !(tied && IsPreferredTarget(candidate, target)))
                {
                    continue;
                }

                found = true;
                bestDistanceSquared = candidateDistanceSquared;
                target = candidate;
            }

            return found;
        }

        internal int GetObjectTranslationQueryStamp(int movingObjectId, int excludedPartIndex)
        {
            unchecked
            {
                int stamp = 17 * 31 + excludedPartIndex;
                int objectCount = 0;

                foreach (Entry entry in m_Entries.Values)
                {
                    ProceduralObject owner = entry.Owner;

                    if (owner == null || (owner.Id == movingObjectId && excludedPartIndex < 0))
                    {
                        continue;
                    }

                    ObjectTransform transform = owner.Transform;

                    stamp = stamp * 31 + owner.Id;
                    stamp = stamp * 31 + (int)entry.State;
                    stamp = stamp * 31 + (entry.Index != null ? 1 : 0);
                    stamp = stamp * 31 + (int)owner.GeometryVersion;
                    stamp = stamp * 31 + transform.Position.x.GetHashCode();
                    stamp = stamp * 31 + transform.Position.y.GetHashCode();
                    stamp = stamp * 31 + transform.Position.z.GetHashCode();
                    stamp = stamp * 31 + transform.Rotation.x.GetHashCode();
                    stamp = stamp * 31 + transform.Rotation.y.GetHashCode();
                    stamp = stamp * 31 + transform.Rotation.z.GetHashCode();
                    stamp = stamp * 31 + transform.Rotation.w.GetHashCode();
                    stamp = stamp * 31 + transform.Scale.x.GetHashCode();
                    stamp = stamp * 31 + transform.Scale.y.GetHashCode();
                    stamp = stamp * 31 + transform.Scale.z.GetHashCode();

                    objectCount++;
                }

                return stamp * 31 + objectCount;
            }
        }

        private void SynchronizeEntries()
        {
            m_SeenObjectIds.Clear();

            foreach (ProceduralObject proceduralObject in m_ProceduralObjectSystem.Objects)
            {
                if (proceduralObject == null)
                {
                    continue;
                }

                int objectId = proceduralObject.Id;
                m_SeenObjectIds.Add(objectId);

                if (!m_Entries.TryGetValue(objectId, out Entry entry))
                {
                    m_Entries.Add(objectId, new Entry(proceduralObject));
                    continue;
                }

                if (!ReferenceEquals(entry.Owner, proceduralObject))
                {
                    m_Entries.Remove(objectId);
                    RetireEntry(entry);
                    m_Entries.Add(objectId, new Entry(proceduralObject));
                    continue;
                }

                if (entry.State == EntryState.Ready || entry.State == EntryState.Failed)
                {
                    if (!MatchesCapturedVersions(entry))
                    {
                        entry.Index?.Retire();
                        entry.Index = null;
                        entry.State = EntryState.Pending;
                    }

                    continue;
                }

                if ((entry.State == EntryState.Capturing || entry.State == EntryState.Building) && !MatchesCapturedVersions(entry))
                {
                    entry.BuildStale = true;
                }
            }

            m_RemovedObjectIds.Clear();

            foreach (KeyValuePair<int, Entry> pair in m_Entries)
            {
                if (!m_SeenObjectIds.Contains(pair.Key))
                {
                    m_RemovedObjectIds.Add(pair.Key);
                }
            }

            for (int i = 0; i < m_RemovedObjectIds.Count; i++)
            {
                int objectId = m_RemovedObjectIds[i];
                Entry entry = m_Entries[objectId];
                m_Entries.Remove(objectId);
                RetireEntry(entry);
            }
        }

        private void AdvanceActiveEntry()
        {
            Entry entry = m_ActiveEntry;

            if (entry == null)
            {
                return;
            }

            if (entry.State == EntryState.Capturing)
            {
                if (entry.BuildStale || entry.State == EntryState.Retired || !MatchesCapturedVersions(entry))
                {
                    DiscardActiveBuild(entry);
                    return;
                }

                CaptureEntry(entry);
                return;
            }

            if (entry.State != EntryState.Building && entry.State != EntryState.Retired)
            {
                m_ActiveEntry = null;
                return;
            }

            if (!entry.BuildHandle.IsCompleted)
            {
                return;
            }

            entry.BuildHandle.Complete();

            bool canPublish = entry.State != EntryState.Retired && !entry.BuildStale && MatchesCapturedVersions(entry);

            if (canPublish)
            {
                PublishActiveBuild(entry);
            }
            else
            {
                entry.BuildData?.Dispose();
                entry.BuildData = null;

                if (entry.State != EntryState.Retired)
                {
                    entry.State = EntryState.Pending;
                }
            }

            m_ActiveEntry = null;
        }

        private void StartNextEntry()
        {
            Entry next = FindNextPendingEntry();

            if (next == null)
            {
                return;
            }

            try
            {
                BeginCapture(next);
                m_ActiveEntry = next;
                CaptureEntry(next);
            }
            catch (Exception exception)
            {
                next.BuildData?.Dispose();
                next.BuildData = null;
                next.State = EntryState.Failed;
                next.CapturedGeometryVersion = next.Owner != null ? next.Owner.GeometryVersion : 0;

                CaptureTopologyVersions(next);
                m_ActiveEntry = null;

                Mod.log.Error($"[PO Snap Index] Could not begin index build for " + $"object {next.Owner?.Id}: {exception}");
            }
        }

        private Entry FindNextPendingEntry()
        {
            Entry selectedEntry = null;
            int selectedObjectId = m_ProceduralObjectSystem.SelectedObjectId ?? -1;

            Entry priorityEntry = null;
            Entry lowestIdEntry = null;

            foreach (Entry entry in m_Entries.Values)
            {
                if (entry.State != EntryState.Pending || entry.Owner == null)
                {
                    continue;
                }

                if (entry.Owner.Id == selectedObjectId)
                {
                    selectedEntry = entry;
                    break;
                }

                if (entry.PriorityRequested && (priorityEntry == null || entry.Owner.Id < priorityEntry.Owner.Id))
                {
                    priorityEntry = entry;
                }

                if (lowestIdEntry == null || entry.Owner.Id < lowestIdEntry.Owner.Id)
                {
                    lowestIdEntry = entry;
                }
            }

            return selectedEntry ?? priorityEntry ?? lowestIdEntry;
        }

        private void BeginCapture(Entry entry)
        {
            ProceduralObject owner = entry.Owner;

            if (owner == null)
            {
                throw new InvalidOperationException("Cannot capture a null PO.");
            }

            PartCaptureInfo[] parts = new PartCaptureInfo[owner.Parts.Count];

            long totalVertexCount = 0;
            long totalEdgeCount = 0;

            for (int partIndex = 0; partIndex < owner.Parts.Count; partIndex++)
            {
                MeshPart part = owner.Parts[partIndex];

                int vertexCount = part.Topology.LogicalVertexCount;

                bool useStructuralEdges = part.StructuralTopology.StructuralEdgeCount > 0;

                int edgeCount = useStructuralEdges ? part.StructuralTopology.StructuralEdgeCount : part.Topology.LogicalEdgeCount;

                if (totalVertexCount > int.MaxValue || totalEdgeCount > int.MaxValue)
                {
                    throw new InvalidOperationException("PO snap index feature count exceeds supported limits.");
                }

                parts[partIndex] = new PartCaptureInfo
                {
                    VertexOffset = (int)totalVertexCount,
                    VertexCount = vertexCount,
                    EdgeOffset = (int)totalEdgeCount,
                    EdgeCount = edgeCount,
                    UseStructuralEdges = useStructuralEdges,
                };

                totalVertexCount += vertexCount;
                totalEdgeCount += edgeCount;
            }

            if (totalVertexCount > int.MaxValue || totalEdgeCount > int.MaxValue || totalVertexCount + totalEdgeCount > int.MaxValue)
            {
                throw new InvalidOperationException("PO snap index feature count exceeds supported limits.");
            }

            entry.Index?.Retire();
            entry.Index = null;
            entry.BuildData = new BuildData(parts, (int)totalVertexCount, (int)totalEdgeCount);

            entry.CapturedGeometryVersion = owner.GeometryVersion;

            CaptureTopologyVersions(entry);
            entry.BuildStale = false;
            entry.PriorityRequested = false;
            entry.State = EntryState.Capturing;
        }

        private void CaptureEntry(Entry entry)
        {
            BuildData data = entry.BuildData;

            if (data == null)
            {
                entry.State = EntryState.Failed;
                m_ActiveEntry = null;
                return;
            }

            long workStart = Stopwatch.GetTimestamp();
            long budgetTicks = (long)(Stopwatch.Frequency * kCaptureBudgetMilliseconds / 1000.0);

            long deadline = workStart + Math.Max(1L, budgetTicks);

            int budgetCheck = kBudgetCheckInterval;

            while (!data.CaptureComplete)
            {
                CaptureOneFeature(entry.Owner, data);

                budgetCheck--;
                if (budgetCheck > 0)
                {
                    continue;
                }

                budgetCheck = kBudgetCheckInterval;

                if (Stopwatch.GetTimestamp() >= deadline)
                {
                    break;
                }
            }

            if (!data.CaptureComplete)
            {
                return;
            }

            if (entry.BuildStale || !MatchesCapturedVersions(entry))
            {
                DiscardActiveBuild(entry);
                return;
            }

            if (data.ValidPrimitiveCount <= 0)
            {
                data.Dispose();
                entry.BuildData = null;
                entry.State = EntryState.Ready;
                m_ActiveEntry = null;
                return;
            }

            try
            {
                data.PrepareBuild();

                ObjectSnapIndex.BuildBvhJob buildJob = new ObjectSnapIndex.BuildBvhJob
                {
                    LocalVertices = data.LocalVertices,
                    Edges = data.Edges,
                    PrimitiveOrder = data.PrimitiveOrder,
                    PrimitiveCount = data.ValidPrimitiveCount,
                    Nodes = data.Nodes,
                    WorkStack = data.WorkStack,
                    NodeCount = data.NodeCount,
                };

                entry.BuildHandle = buildJob.Schedule();
                entry.State = EntryState.Building;
            }
            catch (Exception exception)
            {
                data.Dispose();
                entry.BuildData = null;
                entry.State = EntryState.Failed;
                m_ActiveEntry = null;

                Mod.log.Error($"[PO Snap Index] Could not schedule index build for " + $"object {entry.Owner.Id}: {exception}");
            }
        }

        private static void CaptureOneFeature(ProceduralObject owner, BuildData data)
        {
            PartCaptureInfo info = data.Parts[data.PartIndex];

            MeshPart part = owner.Parts[data.PartIndex];

            if (!data.CapturingEdges)
            {
                if (data.ItemIndex < info.VertexCount)
                {
                    int vertexIndex = data.ItemIndex++;
                    int globalVertexIndex = info.VertexOffset + vertexIndex;
                    Vertex vertex = part.Topology.GetVertex(vertexIndex);
                    Vector3 localPosition = vertex.GetPosition(part.Mesh);
                    float3 position = new float3(localPosition.x, localPosition.y, localPosition.z);
                    data.LocalVertices[globalVertexIndex] = position;
                    data.VertexMetadata[globalVertexIndex] = new int2(data.PartIndex, vertexIndex);

                    if (IsFinite(position))
                    {
                        data.VertexValid[globalVertexIndex] = 1;
                        data.PrimitiveOrder[data.ValidPrimitiveCount++] = globalVertexIndex;
                    }

                    return;
                }

                data.CapturingEdges = true;
                data.ItemIndex = 0;
            }

            if (data.ItemIndex < info.EdgeCount)
            {
                int edgeIndex = data.ItemIndex++;
                int vertexA;
                int vertexB;

                if (info.UseStructuralEdges)
                {
                    StructuralEdge edge = part.StructuralTopology.GetEdge(edgeIndex);

                    vertexA = edge.LogicalVertexA;
                    vertexB = edge.LogicalVertexB;
                }
                else
                {
                    LogicalEdge edge = part.Topology.GetEdge(edgeIndex);

                    vertexA = edge.VertexA;
                    vertexB = edge.VertexB;
                }

                int globalEdgeIndex = info.EdgeOffset + edgeIndex;
                int globalVertexA = info.VertexOffset + vertexA;
                int globalVertexB = info.VertexOffset + vertexB;

                bool valid = vertexA >= 0 && vertexA < info.VertexCount && vertexB >= 0 && vertexB < info.VertexCount && data.VertexValid[globalVertexA] != 0 && data.VertexValid[globalVertexB] != 0;

                if (valid)
                {
                    data.Edges[globalEdgeIndex] = new int2(globalVertexA, globalVertexB);
                    data.EdgeMetadata[globalEdgeIndex] = new int2(data.PartIndex, edgeIndex);
                    data.PrimitiveOrder[data.ValidPrimitiveCount++] = data.TotalVertexCount + globalEdgeIndex;
                }

                return;
            }

            data.PartIndex++;
            data.ItemIndex = 0;
            data.CapturingEdges = false;
        }

        private void PublishActiveBuild(Entry entry)
        {
            BuildData data = entry.BuildData;

            if (data == null)
            {
                entry.State = EntryState.Failed;
                return;
            }

            ObjectSnapIndex index = data.CreateIndex();
            data.Dispose();
            entry.BuildData = null;
            entry.Index = index;
            entry.State = EntryState.Ready;
        }

        private void DiscardActiveBuild(Entry entry)
        {
            entry.BuildData?.Dispose();
            entry.BuildData = null;
            entry.BuildStale = false;

            if (entry.State != EntryState.Retired)
            {
                entry.State = EntryState.Pending;
            }

            m_ActiveEntry = null;
        }

        private bool TryGetReadyIndex(ProceduralObject proceduralObject, out ObjectSnapIndex index)
        {
            index = null;

            if (proceduralObject == null || !m_Entries.TryGetValue(proceduralObject.Id, out Entry entry) || !ReferenceEquals(entry.Owner, proceduralObject) || entry.State != EntryState.Ready || entry.Index == null || !MatchesCapturedVersions(entry))
            {
                return false;
            }

            index = entry.Index;
            return true;
        }

        private void RequestPriorityBuild(ProceduralObject proceduralObject)
        {
            if (proceduralObject == null || !m_Entries.TryGetValue(proceduralObject.Id, out Entry entry) || !ReferenceEquals(entry.Owner, proceduralObject))
            {
                return;
            }

            entry.PriorityRequested = true;
        }

        private static bool MatchesCapturedVersions(Entry entry)
        {
            ProceduralObject owner = entry.Owner;

            if (owner == null || owner.GeometryVersion != entry.CapturedGeometryVersion || owner.Parts.Count != entry.CapturedTopologyVersions.Length)
            {
                return false;
            }

            for (int partIndex = 0; partIndex < owner.Parts.Count; partIndex++)
            {
                if (owner.Parts[partIndex].TopologyVersion != entry.CapturedTopologyVersions[partIndex])
                {
                    return false;
                }
            }

            return true;
        }

        private static void CaptureTopologyVersions(Entry entry)
        {
            ProceduralObject owner = entry.Owner;

            if (owner == null)
            {
                entry.CapturedTopologyVersions = Array.Empty<uint>();
                return;
            }

            if (entry.CapturedTopologyVersions.Length != owner.Parts.Count)
            {
                entry.CapturedTopologyVersions = new uint[owner.Parts.Count];
            }

            for (int partIndex = 0; partIndex < owner.Parts.Count; partIndex++)
            {
                entry.CapturedTopologyVersions[partIndex] = owner.Parts[partIndex].TopologyVersion;
            }
        }

        private void RetireEntry(Entry entry)
        {
            if (entry == null)
            {
                return;
            }

            entry.Index?.Retire();
            entry.Index = null;

            if (!ReferenceEquals(entry, m_ActiveEntry))
            {
                entry.BuildData?.Dispose();
                entry.BuildData = null;
                entry.State = EntryState.Retired;
                return;
            }

            if (entry.State == EntryState.Building)
            {
                entry.BuildStale = true;
                entry.State = EntryState.Retired;
                return;
            }

            entry.BuildData?.Dispose();
            entry.BuildData = null;
            entry.State = EntryState.Retired;
            m_ActiveEntry = null;
        }

        private void RetireAllEntries()
        {
            foreach (Entry entry in m_Entries.Values)
            {
                RetireEntry(entry);
            }

            m_Entries.Clear();
            m_SeenObjectIds.Clear();
            m_RemovedObjectIds.Clear();
        }

        private static bool IsPreferredTarget(SnapTarget candidate, SnapTarget best)
        {
            if (!best.IsValid)
            {
                return true;
            }

            if (candidate.Kind != best.Kind)
            {
                return candidate.Kind == SnapTargetKind.POVertex;
            }

            if (candidate.PrimaryId != best.PrimaryId)
            {
                return candidate.PrimaryId < best.PrimaryId;
            }

            if (candidate.SecondaryId != best.SecondaryId)
            {
                return candidate.SecondaryId < best.SecondaryId;
            }

            return candidate.TertiaryId < best.TertiaryId;
        }

        private static bool IsSupportedRuntimeMode()
        {
            return GameManager.instance != null && (GameManager.instance.gameMode == GameMode.Game || GameManager.instance.gameMode == GameMode.Editor);
        }

        private static bool IsFinite(float3 value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
        }
    }
}