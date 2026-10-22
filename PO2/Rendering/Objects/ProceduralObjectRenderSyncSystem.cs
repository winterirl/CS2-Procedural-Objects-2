using Game;
using PO2.Core;

namespace PO2.Rendering.Objects
{
    // Rendering-phase driver for PO runtime synchronization.
    // ProceduralObjectSystem owns data but intentionally does no work in its own update.
    // This system applies dirty state late enough for same-frame tool edits and
    // independently of simulation speed or whether the simulation is actively ticking.
    // because ooooops I accidentally wired this to the simulation early on and that was stupid :3
    public partial class ProceduralObjectRenderSyncSystem : GameSystemBase
    {
        private ProceduralObjectSystem m_ProceduralObjectSystem;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ProceduralObjectSystem = World.GetOrCreateSystemManaged<ProceduralObjectSystem>();
        }

        protected override void OnUpdate()
        {
            m_ProceduralObjectSystem?.UpdateRuntimeLifecycle();
        }

        protected override void OnDestroy()
        {
            m_ProceduralObjectSystem = null;

            base.OnDestroy();
        }
    }
}