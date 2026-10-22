using Game.Common;
using Game.Tools;
using Unity.Entities;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private Entity m_AssetConversionHoveredEntity = Entity.Null;
        private bool m_OwnsAssetConversionHighlight;

        private void SetAssetConversionHover(Entity entity)
        {
            if (entity == m_AssetConversionHoveredEntity)
            {
                if (entity == Entity.Null)
                {
                    return;
                }

                if (!EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity))
                {
                    ClearAssetConversionHover();
                    return;
                }

                // Another vanilla system can release shared Highlighted state
                // while the pointer remains still. Reacquire it on the next frame.
                if (!EntityManager.HasComponent<Highlighted>(entity))
                {
                    m_OwnsAssetConversionHighlight = true;
                    ApplyAssetConversionHighlight(entity, add: true);
                }

                return;
            }

            ClearAssetConversionHover();

            if (entity == Entity.Null || !EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity))
            {
                return;
            }

            m_AssetConversionHoveredEntity = entity;

            // Highlighted is shared vanilla state. If somebody else already
            // owns it, use the existing outline without removing it later.
            if (EntityManager.HasComponent<Highlighted>(entity))
            {
                m_OwnsAssetConversionHighlight = false;
                return;
            }

            m_OwnsAssetConversionHighlight = true;
            ApplyAssetConversionHighlight(entity, add: true);
        }

        private void ClearAssetConversionHover()
        {
            Entity entity = m_AssetConversionHoveredEntity;
            bool removeHighlight = m_OwnsAssetConversionHighlight;

            m_AssetConversionHoveredEntity = Entity.Null;
            m_OwnsAssetConversionHighlight = false;

            if (!removeHighlight || entity == Entity.Null || !EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity))
            {
                return;
            }

            ApplyAssetConversionHighlight(entity, add: false);
        }

        private void ApplyAssetConversionHighlight(Entity entity, bool add)
        {
            if (add)
            {
                if (!EntityManager.HasComponent<Highlighted>(entity))
                {
                    EntityManager.AddComponent<Highlighted>(entity);
                }
            }
            else if (EntityManager.HasComponent<Highlighted>(entity))
            {
                EntityManager.RemoveComponent<Highlighted>(entity);
            }

            if (!EntityManager.HasComponent<BatchesUpdated>(entity))
            {
                EntityManager.AddComponent<BatchesUpdated>(entity);
            }
        }
    }
}