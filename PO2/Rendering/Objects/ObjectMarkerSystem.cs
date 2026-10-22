using System;
using Game;
using Game.Prefabs;
using Unity.Entities;
using PO2.Core;
using PO2.Tools.ObjectTool;

namespace PO2.Rendering.Objects
{
    public partial class ObjectMarkerSystem : GameSystemBase
    {
        private readonly ObjectMarkerCache m_Cache = new ObjectMarkerCache();
        private ProceduralObjectSystem m_ProceduralObjectSystem;
        private ProceduralObjectToolSystem m_ProceduralObjectToolSystem;
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_ConfigurationQuery;
        private ObjectMarkerRenderer m_Renderer;
        private bool m_InitializationFailed;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ProceduralObjectSystem = World.GetOrCreateSystemManaged<ProceduralObjectSystem>();
            m_ProceduralObjectToolSystem = World.GetOrCreateSystemManaged<ProceduralObjectToolSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_ConfigurationQuery = GetEntityQuery(ComponentType.ReadOnly<IconConfigurationData>());
        }

        protected override void OnUpdate()
        {
            if (!ShouldShowMarkers() || m_InitializationFailed)
            {
                m_Renderer?.Hide();
                m_Cache.Clear();
                return;
            }

            if (m_Renderer == null)
            {
                // Prefabs can still be loading when the tool systems are created.
                if (m_ConfigurationQuery.IsEmptyIgnoreFilter) return;
                try
                {
                    var configuration = m_PrefabSystem.GetSingletonPrefab<IconConfigurationPrefab>(m_ConfigurationQuery);
                    m_Renderer = new ObjectMarkerRenderer(configuration);
                }
                catch (Exception e)
                {
                    m_InitializationFailed = true;
                    Mod.log.Error("[PO Markers] Renderer initialization failed: " + e.Message);
                    return;
                }
            }

            m_Cache.Update(m_ProceduralObjectSystem);
            m_Renderer.Update(m_Cache);
        }

        private bool ShouldShowMarkers() => m_ProceduralObjectSystem.IsToolActive && m_ProceduralObjectToolSystem.ShowObjectMarkers && m_ProceduralObjectToolSystem.Mode == ObjectToolMode.Select && !m_ProceduralObjectSystem.SelectedObjectId.HasValue;

        protected override void OnStopRunning()
        {
            m_Renderer?.Hide();
            m_Cache.Clear();
            base.OnStopRunning();
        }

        protected override void OnDestroy()
        {
            m_Renderer?.Dispose();
            m_Renderer = null;
            m_Cache.Clear();
            base.OnDestroy();
        }
    }
}