using System.Collections.Generic;
using PO2.Geometry;
using PO2.Persistence.Format;
using PO2.Persistence.Model;

namespace PO2.Core
{
    public partial class ProceduralObjectSystem
    {
        internal int RestoreSnapshot(OwnedSnapshotObjects objects)
        {
            return PublishRestoredSnapshot(SnapshotModelRestorer.Restore(objects));
        }

        internal int RestoreSnapshot(IReadOnlyList<PersistenceObjectData> objects)
        {
            if (objects == null)
            {
                throw new System.ArgumentNullException(nameof(objects));
            }

            // Reconstruct and validate the complete set of candidates before clearing the current runtime.
            // DTO or duplicate-identity failure leaves the previous runtime intact.
            List<ProceduralObject> restored = new List<ProceduralObject>(objects.Count);
            HashSet<System.Guid> persistentIds = new HashSet<System.Guid>();

            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] == null)
                {
                    throw new System.InvalidOperationException($"Persistence snapshot object {i} is null.");
                }

                if (!persistentIds.Add(objects[i].PersistentId))
                {
                    throw new System.InvalidOperationException($"Persistence snapshot contains duplicate PO identity " + $"{objects[i].PersistentId}.");
                }

                restored.Add(objects[i].Restore(i + 1));
            }

            return PublishRestoredSnapshot(restored);
        }

        private int PublishRestoredSnapshot(List<ProceduralObject> restored)
        {
            // Keep valid decoded objects even if one cannot create its renderer.
            if (m_ProceduralObjectToolSystem != null)
            {
                m_ProceduralObjectToolSystem.PrepareRuntimeSelectionInvalidation();
            }

            ClearAll();
            int committed = 0;

            int repaired = 0;
            foreach (var obj in restored) repaired += obj.ValidateLibraryReferences();
            m_Renderer.RestoreObjects(restored, obj =>
            {
                m_Objects.Add(obj.Id, obj);
                TrackObjectName(obj);
                committed++;
            }, (obj, exception) =>
            {
                m_Objects.Remove(obj.Id);
                Mod.log.Error($"[PO][Persistence Restore] Skipped object persistentId={obj.PersistentId} after renderer creation failed: {exception}");
            });

            // Skip the full assigned ID range, including objects whose renderers failed.
            m_NextId = restored.Count + 1;
            m_SelectedObjectId = null;
            m_SelectedPartIndex = 0;
            m_SelectedSurfaceAreaId = MeshPart.kDefaultSurfaceAreaId;

            Mod.log.Info($"[PO][Persistence Restore] Restored {committed}/" + $"{restored.Count} object(s); normalized {repaired} invalid " + "material reference(s).");
            return committed;
        }

    }
}
