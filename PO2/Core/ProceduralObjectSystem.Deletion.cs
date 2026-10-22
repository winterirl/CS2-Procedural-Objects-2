using PO2.History.Actions;
using PO2.Localization;
using PO2.Tools.ObjectTool;

namespace PO2.Core
{
    public partial class ProceduralObjectSystem
    {
        public void DeleteSelected()
        {
            if (!TryGetSelected(out ProceduralObject selected))
            {
                return;
            }

            TryDeleteObject(selected.Id);
        }

        internal bool TryDeleteObject(int objectId)
        {
            if (HistoryBusy || (m_ProceduralObjectToolSystem != null && m_ProceduralObjectToolSystem.MeshMutationBusy)) return false;
            if (m_ProceduralObjectToolSystem != null && (m_ProceduralObjectToolSystem.Mode == ObjectToolMode.MeshEdit || m_ProceduralObjectToolSystem.Mode == ObjectToolMode.Texturing || m_ProceduralObjectToolSystem.Mode == ObjectToolMode.CombineSource))
            {
                return false;
            }

            if (!m_Objects.TryGetValue(objectId, out ProceduralObject obj)) return false;
            if (obj.IsLocked)
            {
                return false;
            }

            if (m_SelectedObjectId == objectId && m_ProceduralObjectToolSystem?.Mode != ObjectToolMode.Delete)
            {
                m_ProceduralObjectToolSystem?.PrepareRuntimeSelectionInvalidation();
            }

            var label = LocalizedText.For("PO2.History.Action.Object.Delete");
            using (var history = BeginLifetimeHistory(label, new LifetimeAction(obj, true, label)))
            {
                if (history == null) return false;
                if (!TryRemoveObject(objectId, true, out ProceduralObject _))
                {
                    Mod.log.Warn($"[PO] Object {objectId} no longer exists.");
                    return false;
                }

                if (!history.Complete()) return false;
                return true;
            }
        }
    }
}