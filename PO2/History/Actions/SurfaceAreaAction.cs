using System;
using System.Collections.Generic;
using PO2.Core;
using PO2.Geometry;
using PO2.History;
using PO2.Localization;
using PO2.Materials;

namespace PO2.History.Actions
{
    internal sealed class SurfaceAreaAction : HistoryCaptureAction
    {
        private sealed class IndexedArea
        {
            internal readonly int Index;
            internal readonly SurfaceArea Area;

            internal IndexedArea(int index, SurfaceArea area)
            {
                Index = index;
                Area = area.Clone();
            }
        }

        private readonly HistoryPartKey m_Key;
        private readonly ulong m_Topology;
        private int[] m_BeforeMemberships;
        private List<SurfaceArea> m_BeforeAreas;
        private int[] m_Indices;
        private int[] m_StoredMemberships;
        private List<IndexedArea> m_StoredAreas;
        private int[] m_RemoveAreaIds;
        private int m_StoredNextId;
        private ulong m_Expected;
        private bool m_Sealed;

        internal SurfaceAreaAction(ProceduralObject obj, int partIndex, LocalizedText description) : base(description)
        {
            var part = obj.Parts[partIndex];
            m_Key = new HistoryPartKey(new HistoryObjectKey(obj.Id, obj.PersistentId), part.PartHistoryId, partIndex);
            m_Topology = TopologyHash(part);
            m_BeforeMemberships = CopyMemberships(part);
            m_BeforeAreas = new List<SurfaceArea>();
            foreach (var area in part.SurfaceAreas) m_BeforeAreas.Add(area.Clone());
            m_StoredNextId = part.NextSurfaceAreaId;
        }

        public override HistoryFootprint Footprint
        {
            get
            {
                long bytes = 160 + 4L * ((m_BeforeMemberships?.Length ?? 0) + (m_Indices?.Length ?? 0) + (m_StoredMemberships?.Length ?? 0) + (m_RemoveAreaIds?.Length ?? 0));
                if (m_StoredAreas != null)
                    foreach (var entry in m_StoredAreas)
                        bytes += AreaBytes(entry.Area);
                if (m_BeforeAreas != null)
                    foreach (var area in m_BeforeAreas)
                        bytes += AreaBytes(area);
                return new HistoryFootprint(bytes, 0, 0, bytes);
            }
        }

        private static long AreaBytes(SurfaceArea area)
        {
            long bytes = 448 + (area.Material.DefinitionId?.Length ?? 0) * 2L + (area.MappingSeed?.Bytes ?? 0);
            if (area.OriginalState != null) bytes += 352 + (area.OriginalState.CreateMaterialClone().DefinitionId?.Length ?? 0) * 2L;
            return bytes;
        }

        private MeshPart Resolve(HistoryContext context)
        {
            if (!context.TryResolve(m_Key, out var part, out _) || TopologyHash(part) != m_Topology)
            {
                throw new InvalidOperationException("Surface Area history target or triangle layout changed.");
            }

            return part;
        }

        internal override void Seal(HistoryContext context)
        {
            if (m_Sealed) throw new InvalidOperationException("Surface Area capture already completed.");
            var part = Resolve(context);
            var indices = new List<int>();
            var values = new List<int>();
            for (int i = 0; i < m_BeforeMemberships.Length; i++)
            {
                if (m_BeforeMemberships[i] == part.TriangleSurfaceAreaIds[i]) continue;
                indices.Add(i);
                values.Add(m_BeforeMemberships[i]);
            }

            var currentIds = new HashSet<int>();
            foreach (var area in part.SurfaceAreas) currentIds.Add(area.Id);
            var oldIds = new HashSet<int>();
            var stored = new List<IndexedArea>();
            for (int i = 0; i < m_BeforeAreas.Count; i++)
            {
                oldIds.Add(m_BeforeAreas[i].Id);
                if (!currentIds.Contains(m_BeforeAreas[i].Id)) stored.Add(new IndexedArea(i, m_BeforeAreas[i]));
            }

            var remove = new List<int>();
            foreach (var area in part.SurfaceAreas)
                if (!oldIds.Contains(area.Id))
                    remove.Add(area.Id);
            m_Indices = indices.ToArray();
            m_StoredMemberships = values.ToArray();
            m_StoredAreas = stored;
            m_RemoveAreaIds = remove.ToArray();
            m_Expected = StateHash(part);
            HasChanges = indices.Count != 0 || stored.Count != 0 || remove.Count != 0 || m_StoredNextId != part.NextSurfaceAreaId;
            m_Sealed = true;
            // Only the sparse patch and absent area records survive the commit.
            m_BeforeAreas = null;
            m_BeforeMemberships = null;
        }

        internal override void RestoreCapture(HistoryContext context)
        {
            if (m_Sealed)
            {
                if (HasChanges)
                {
                    var result = Replay(HistoryDirection.Undo, context);
                    if (!result.Success) throw new InvalidOperationException(result.Reason);
                }

                return;
            }

            var part = Resolve(context);
            // On cancellation, avoid a version bump when the operation did nothing.
            bool changed = part.NextSurfaceAreaId != m_StoredNextId || part.SurfaceAreas.Count != m_BeforeAreas.Count;
            for (int i = 0; !changed && i < m_BeforeMemberships.Length; i++) changed = m_BeforeMemberships[i] != part.TriangleSurfaceAreaIds[i];
            if (changed) Apply(context, part, m_BeforeAreas, m_BeforeMemberships, m_StoredNextId);
        }

        public override HistoryResult Replay(HistoryDirection direction, HistoryContext context)
        {
            var part = Resolve(context);
            if (!m_Sealed || StateHash(part) != m_Expected) return new HistoryResult(false, "Surface Area state no longer matches.", true);
            int[] memberships = CopyMemberships(part);
            int[] displaced = new int[m_Indices.Length];
            for (int i = 0; i < m_Indices.Length; i++)
            {
                displaced[i] = memberships[m_Indices[i]];
                memberships[m_Indices[i]] = m_StoredMemberships[i];
            }

            var remove = new HashSet<int>(m_RemoveAreaIds);
            var replacement = new List<SurfaceArea>();
            var inverseAreas = new List<IndexedArea>();
            for (int i = 0; i < part.SurfaceAreas.Count; i++)
            {
                var area = part.SurfaceAreas[i];
                if (remove.Contains(area.Id)) inverseAreas.Add(new IndexedArea(i, area));
                else replacement.Add(area);
            }

            if (inverseAreas.Count != remove.Count) return new HistoryResult(false, "Missing area for history removal.", true);
            var inverseRemove = new int[m_StoredAreas.Count];
            for (int i = 0; i < m_StoredAreas.Count; i++)
            {
                var record = m_StoredAreas[i];
                replacement.Insert(record.Index, record.Area.Clone());
                inverseRemove[i] = record.Area.Id;
            }

            int displacedNext = part.NextSurfaceAreaId;
            // Compute every fallible value before the live replacement.
            ulong inverseExpected = StateHash(replacement, memberships, m_StoredNextId);
            Apply(context, part, replacement, memberships, m_StoredNextId);
            m_StoredMemberships = displaced;
            m_StoredAreas = inverseAreas;
            m_RemoveAreaIds = inverseRemove;
            m_StoredNextId = displacedNext;
            m_Expected = inverseExpected;
            return HistoryResult.Ok;
        }

        private void Apply(HistoryContext context, MeshPart part, IReadOnlyList<SurfaceArea> areas, int[] memberships, int nextId)
        {
            if (!context.TryResolve(m_Key.Owner, out var obj)) throw new InvalidOperationException("Missing Surface Area owner.");
            foreach (var area in areas)
            {
                if (area.Material.SourceKind == MaterialSourceKind.LibraryMaterial && !MaterialLibrary.TryGet(area.Material.DefinitionId, out MaterialDefinition _))
                {
                    throw new InvalidOperationException("History area material is no longer available.");
                }
            }

            part.ApplyHistorySurfaceAreas(areas, memberships, nextId);
            obj.MarkDirty(ProceduralObjectDirtyFlags.Material);
        }

        private static int[] CopyMemberships(MeshPart part)
        {
            var result = new int[part.TriangleSurfaceAreaIds.Count];
            for (int i = 0; i < result.Length; i++) result[i] = part.TriangleSurfaceAreaIds[i];
            return result;
        }

        private static ulong TopologyHash(MeshPart part)
        {
            var hash = HistoryHash.Start;
            hash.Add(part.Mesh.Vertices.Length);
            hash.Add(part.Mesh.Triangles.Length);
            foreach (int index in part.Mesh.Triangles) hash.Add(index);
            return hash.Value;
        }

        private static ulong StateHash(MeshPart part) => StateHash(part.SurfaceAreas, part.TriangleSurfaceAreaIds, part.NextSurfaceAreaId);

        private static ulong StateHash(IReadOnlyList<SurfaceArea> areas, IReadOnlyList<int> memberships, int nextId)
        {
            var hash = HistoryHash.Start;
            hash.Add(nextId);
            hash.Add(areas.Count);
            hash.Add(memberships.Count);
            foreach (int id in memberships) hash.Add(id);
            foreach (var area in areas)
            {
                hash.Add(area.Id);
                hash.Add(HistoryHash.Appearance(area.Material, area.ColorMask));
                hash.Add((int)area.SourceVertexAttributes);
                hash.Add((int)area.NativeWindowKind);
                hash.Add(area.HasOriginalState);
                if (area.HasOriginalState) hash.Add(HistoryHash.Appearance(area.OriginalState.CreateMaterialClone(), area.OriginalState.CreateColorMaskClone()));
            }

            return hash.Value;
        }

        public override void Dispose()
        {
            m_BeforeMemberships = null;
            m_BeforeAreas = null;
            m_Indices = null;
            m_StoredMemberships = null;
            m_StoredAreas = null;
            m_RemoveAreaIds = null;
        }
    }
}