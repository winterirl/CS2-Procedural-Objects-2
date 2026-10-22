using System;
using PO2.Core;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.Localization;
using PO2.Materials;
using PO2.Persistence.Model;

namespace PO2.History
{
    internal sealed class HistoryContext
    {
        private readonly ProceduralObjectSystem m_System;
        internal HistoryLayoutCache Layouts { get; } = new HistoryLayoutCache();
        private readonly Action<ProceduralObject> m_GeometryChanged;
        private readonly Action<ProceduralObject, GeometryChange> m_GeometryPatch;
        private readonly Func<ProceduralObject, TopologySelection> m_CaptureTopology;
        private readonly Action<ProceduralObject, TopologySelection> m_RestoreTopology;
        private readonly Action<ProceduralObject, TopologyPreparedSelection> m_RestorePreparedTopology;

        internal HistoryContext(ProceduralObjectSystem system, Action<ProceduralObject> geometryChanged = null, Func<ProceduralObject, TopologySelection> captureTopology = null, Action<ProceduralObject, TopologySelection> restoreTopology = null, Action<ProceduralObject, TopologyPreparedSelection> restorePreparedTopology = null, Action<ProceduralObject, GeometryChange> geometryPatch = null)
        {
            m_System = system ?? throw new ArgumentNullException(nameof(system));
            m_GeometryChanged = geometryChanged;
            m_GeometryPatch = geometryPatch;
            m_CaptureTopology = captureTopology;
            m_RestoreTopology = restoreTopology;
            m_RestorePreparedTopology = restorePreparedTopology;
        }

        internal TopologyPreparation CreateTopologyPreparation(ProceduralObject obj, int index, TopologyTargetStamp stamp, System.Threading.CancellationToken token, Func<PersistencePartData, System.Threading.CancellationToken, TopologyPreparedResult> build, TopologyReplayProof proof = null, TopologySize size = null) =>
            new TopologyPreparation(obj, index, stamp, null, m_System, token, build, proof, size);

        internal void RestorePreparedTopologySelection(ProceduralObject obj, TopologyPreparedSelection selection)
        {
            if (m_RestorePreparedTopology != null)
            {
                try
                {
                    m_RestorePreparedTopology(obj, selection);
                }
                catch
                {
                }
            }
            else RestoreTopologySelection(obj, selection.Selection);
        }

        internal TopologySelection CaptureTopologySelection(ProceduralObject obj) => m_CaptureTopology?.Invoke(obj);

        internal void RestoreTopologySelection(ProceduralObject obj, TopologySelection selection)
        {
            // Versions update even if an editor notification cannot complete.
            try
            {
                m_RestoreTopology?.Invoke(obj, selection);
            }
            catch
            {
            }
        }

        internal void GeometryChanged(ProceduralObject obj)
        {
            // Notification cannot turn a completed model swap into a failed
            // action. Versions still invalidate renderer caches independently.
            try
            {
                m_GeometryChanged?.Invoke(obj);
            }
            catch
            {
            }
        }

        internal void GeometryChanged(ProceduralObject obj, GeometryChange change)
        {
            if (m_GeometryPatch == null)
            {
                GeometryChanged(obj);
                return;
            }

            try
            {
                m_GeometryPatch(obj, change);
            }
            catch
            {
                GeometryChanged(obj);
            }
        }

        internal bool TryResolve(HistoryObjectKey key, out ProceduralObject obj)
        {
            obj = null;
            if (!key.IsValid || !m_System.TryGetObject(key.RuntimeId, out var found) || found.PersistentId != key.PersistentId) return false;
            obj = found;
            return true;
        }

        internal bool TryResolve(HistoryPartKey key, out MeshPart part, out int index)
        {
            part = null;
            index = -1;
            if (!key.IsValid || !TryResolve(key.Owner, out var obj)) return false;
            // Lists are capped at 4096 parts. Check every ID to reject duplicates,
            // even when the hint happens to match the first one.
            for (int i = 0; i < obj.Parts.Count; i++)
            {
                if (obj.Parts[i].PartHistoryId != key.PartId) continue;
                if (index >= 0)
                {
                    part = null;
                    index = -1;
                    return false;
                }

                index = i;
                part = obj.Parts[i];
            }

            return index >= 0;
        }

        internal bool TryResolve(HistorySurfaceAreaKey key, out SurfaceArea area)
        {
            area = null;
            if (!TryResolve(key.Part, out var part, out _)) return false;
            foreach (var candidate in part.SurfaceAreas)
            {
                if (candidate.Id != key.AreaId) continue;
                if (area != null)
                {
                    area = null;
                    return false;
                }

                area = candidate;
            }

            return area != null;
        }

        internal IHistoryObjectRenderer PrepareObjectRenderer(ProceduralObject obj) => m_System.PrepareHistoryObject(obj);

        internal bool TryRegister(HistoryObjectKey key, ProceduralObject staging, out string reason, IHistoryObjectRenderer renderer = null) =>
            m_System.TryHistoryRegister(key, staging, out reason, renderer);

        internal bool TryRemove(HistoryObjectKey key, out ProceduralObject detached, out string reason) =>
            m_System.TryHistoryRemove(key, out detached, out reason);

        internal HistoryFocusState CaptureFocus() => m_System.CaptureHistoryFocus();
        internal void RestoreFocus(HistoryFocusState focus) => m_System.RestoreHistoryFocus(focus);

        internal bool TryInsertPart(HistoryPartKey key, int index, MeshPart part, out string reason) =>
            m_System.TryHistoryInsertPart(key, index, part, out reason);

        internal bool TryRemovePart(HistoryPartKey key, int index, out MeshPart removed, out string reason) =>
            m_System.TryHistoryRemovePart(key, index, out removed, out reason);

        internal PartArrayPreparation PrepareArrayReplay(ProceduralObject obj, int index, MeshPart[] parts, System.Threading.CancellationToken token, UnityEngine.Matrix4x4? bakeTransform = null, MeshPart sourceReplacement = null, UnityEngine.Matrix4x4[] copyTransforms = null) =>
            m_System.PrepareArrayReplay(obj, index, parts, token, bakeTransform, sourceReplacement, copyTransforms);

        internal bool TryDetachArray(HistoryObjectKey key, int first, Guid[] ids, out MeshPart[] parts, out IArrayRemoval removal, out LocalizedText reason) => m_System.TryDetachArray(key, first, ids, out parts, out removal, out reason);
    }
}