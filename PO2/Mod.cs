using System.IO;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using UnityEngine;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.Geometry.Transforms;
using PO2.History;
using PO2.Interface;
using PO2.Localization;
using PO2.Materials;
using PO2.Persistence;
using PO2.Rendering;
using PO2.Rendering.Objects;
using PO2.Tools.Gizmos;
using PO2.Tools.MeshEdit;
using PO2.Tools.ObjectTool;
using PO2.Tools.Snapping;

namespace PO2
{
    // Abandon all hope ye who enter here
    // No seriously this mod is just nuts and actually stupid.
    // Have fun.
    public class Mod : IMod
    {
        public static string ModRootPath { get; private set; }
        public static string UserMaterialRootPath { get; private set; }
        public static string SaveRootPath { get; private set; }
        public static Setting Settings { get; private set; }

        private Setting m_Setting;
        private LocalizationService m_Localization;

        public static readonly ILog log = LogManager.GetLogger($"{nameof(PO2)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info($"PO2 {typeof(Mod).Assembly.GetName().Version} loading.");
            VertexEditPreparationKernels.Initialize();
            VertexHistoryKernels.Initialize();
            HistoryBoundaryKernels.Initialize();
            TranslationSnapBuildKernels.Initialize();
            MeshCentroidKernels.Initialize();
            MeshDependencyKernels.Initialize();
            NormalUpdateKernels.Initialize();
            RenderGeometryKernels.Initialize();
            MirrorKernels.Initialize();
            ArrayKernels.Initialize();
            MirrorHistoryKernels.Initialize();
            WarnManagedKernel("history preparation", VertexEditPreparationKernels.UsesBurst, VertexEditPreparationKernels.Failure);
            WarnManagedKernel("history values", VertexHistoryKernels.UsesBurst, VertexHistoryKernels.Failure);
            WarnManagedKernel("history layout comparison", HistoryBoundaryKernels.CompareUsesBurst, HistoryBoundaryKernels.Failure);
            WarnManagedKernel("history buffer swap", HistoryBoundaryKernels.SwapUsesBurst, HistoryBoundaryKernels.Failure);
            WarnManagedKernel("snapping", TranslationSnapBuildKernels.UsesBurst, TranslationSnapBuildKernels.Failure);
            WarnManagedKernel("selection centroid", MeshCentroidKernels.UsesBurst, MeshCentroidKernels.Failure);
            WarnManagedKernel("mesh dependencies", MeshDependencyKernels.UsesBurst, MeshDependencyKernels.Failure);
            WarnManagedKernel("normals", NormalUpdateKernels.UsesBurst, NormalUpdateKernels.Failure);
            WarnManagedKernel("renderer updates", RenderGeometryKernels.UsesBurst, RenderGeometryKernels.Failure);
            WarnManagedKernel("mirror geometry", MirrorKernels.UsesBurst, MirrorKernels.Failure);
            WarnManagedKernel("mirror history", MirrorHistoryKernels.UsesBurst, MirrorHistoryKernels.Failure);

            // Bundled assets move with the mod. User materials and saves live under
            // persistentDataPath so an update or reinstall cannot replace them.
            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                ModRootPath = Path.GetDirectoryName(asset.path);
                log.Info($"Current mod asset at {asset.path}");
            }

            UserMaterialRootPath = Path.Combine(Application.persistentDataPath, "ModsData", "PO2", "Materials");
            log.Info($"PO2 user material library at {UserMaterialRootPath}");
            UserMaterialService.CleanupDeleteStaging();

            SaveRootPath = Path.Combine(Application.persistentDataPath, "ModsData", "PO2", "Saves");
            log.Info($"PO2 save snapshots at {SaveRootPath}");

            m_Setting = new Setting(this);
            m_Setting.RegisterKeyBindings();
            m_Setting.RegisterInOptionsUI();
            m_Localization = new LocalizationService(GameManager.instance.localizationManager, m_Setting);
            AssetDatabase.global.LoadSettings(nameof(PO2), m_Setting, new Setting(this));
            Settings = m_Setting;

            /* These registrations encode PO2's lifecycle contract:
             *
             * - persistence observes save/load events but never joins vanilla
             *     ECS serialization (literally cannot corrupt the original save)
             * - input and selection update in ToolUpdate
             * - dirty state reaches Unity render objects in the Rendering phase,
             *     which continues to run while simulation time is paused;
             * - transient overlays are resubmitted in DebugGizmos because the
             *     game's overlay buffers are cleared every frame;
             * - UI bindings publish after their backing systems exist.
             */
            updateSystem.UpdateAt<PersistenceCoordinatorSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateBefore<SnapIndexSystem, ProceduralObjectToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<ProceduralObjectToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAfter<ObjectSelectionOverlaySystem, ProceduralObjectToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAfter<ObjectMarkerSystem, ProceduralObjectToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<ProceduralObjectRenderSyncSystem>(SystemUpdatePhase.Rendering);
            updateSystem.UpdateAt<VertexRenderSystem>(SystemUpdatePhase.DebugGizmos);
            updateSystem.UpdateAt<GizmoRenderSystem>(SystemUpdatePhase.DebugGizmos);
            updateSystem.UpdateAt<ProceduralObjectUiSystem>(SystemUpdatePhase.UIUpdate);

            log.Info("Registered procedural object runtime, snap index, tool, selection overlay, marker, render sync, vertex, gizmo, persistence coordinator, and UI systems.");
        }

        private static void WarnManagedKernel(string name, bool usesBurst, string reason)
        {
            if (!usesBurst) log.Warn($"[PO Mesh] {name} is using the managed fallback: {reason}");
        }

        public void OnDispose()
        {
            m_Localization?.Dispose();
            m_Localization = null;
            if (m_Setting != null)
            {
                foreach (var action in m_Setting.GetActions())
                {
                    action.shouldBeEnabled = false;
                }

                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
                Settings = null;
            }
        }
    }
}