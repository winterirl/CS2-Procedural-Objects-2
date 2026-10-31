using System;
using System.Collections.Generic;
using Colossal.IO.AssetDatabase.VirtualTexturing;
using Colossal.Serialization.Entities;
using Game;
using Game.SceneFlow;
using Game.Tools;
using UnityEngine;
using PO2.Geometry;
using PO2.History;
using PO2.Rendering;
using PO2.Rendering.Objects;
using PO2.Tools.ObjectTool;

namespace PO2.Core
{
    // This owns the live set of procedural objects and object-level selection state.
    // This is the runtime repo used by tools, persistence, and rendering.
    // POs are plain managed data, NOT ECS entities.
    // It works fine because they are non-functional/visual-only and they use a custom runtime mesh lifecycle.
    //
    // I still question if I made the right choice with this, but it's probably safer and everyone expects
    // POs to be non-interactive from how the original mod worked anyway.

    public partial class ProceduralObjectSystem : GameSystemBase
    {
        // Runtime IDs provide fast local lookup only.
        // Dictionary contents are not serialized, and IDs are reassigned during load.
        private readonly Dictionary<int, ProceduralObject> m_Objects = new Dictionary<int, ProceduralObject>();

        private ProceduralObjectRenderer m_Renderer;

        private ToolSystem m_ToolSystem;
        private ProceduralObjectToolSystem m_ProceduralObjectToolSystem;

        internal ulong ObjectListVersion { get; private set; }

        private void InvalidateObjectList()
        {
            unchecked
            {
                ObjectListVersion++;
            }
        }

        private void TrackObjectName(ProceduralObject obj)
        {
            obj.NameChanged += InvalidateObjectList;
            InvalidateObjectList();
        }

        private void UntrackObjectName(ProceduralObject obj)
        {
            obj.NameChanged -= InvalidateObjectList;
            InvalidateObjectList();
        }

        public int ObjectCount => m_Objects.Count;
        public IEnumerable<ProceduralObject> Objects => m_Objects.Values;

        public bool IsToolActive =>
            m_ToolSystem != null && m_ToolSystem.activeTool == m_ProceduralObjectToolSystem;

        public bool TryGetObject(int objectId, out ProceduralObject proceduralObject)
        {
            return m_Objects.TryGetValue(objectId, out proceduralObject);
        }

        internal bool TryCaptureDeformationRendererWork(ProceduralObject proceduralObject, int[] partIndices, out DeformationRendererWorkItem workItem)
        {
            workItem = null;
            return m_Renderer != null && m_Renderer.TryCaptureDeformationWork(proceduralObject, partIndices, out workItem);
        }

        internal bool TryPublishDeformationRendererPreparation(ProceduralObject proceduralObject, DeformationRendererPreparation preparation, out string failureReason)
        {
            if (m_Renderer == null)
            {
                failureReason = "The procedural object renderer is unavailable.";
                return false;
            }

            return m_Renderer.TryPublishDeformationPreparation(proceduralObject, preparation, out failureReason);
        }

        public bool TryGetWorldBounds(int objectId, out Bounds bounds)
        {
            bounds = default;

            if (!m_Objects.TryGetValue(objectId, out ProceduralObject proceduralObject) || !proceduralObject.TryGetLocalBounds(out Bounds localBounds))
            {
                return false;
            }

            Vector3 localCenter = localBounds.center;
            Vector3 localExtents = localBounds.extents;
            bool initialized = false;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 localCorner = localCenter + Vector3.Scale(localExtents, new Vector3(x, y, z));
                        Vector3 worldCorner = proceduralObject.Transform.TransformPoint(localCorner);

                        if (!initialized)
                        {
                            bounds = new Bounds(worldCorner, Vector3.zero);
                            initialized = true;
                        }
                        else
                        {
                            bounds.Encapsulate(worldCorner);
                        }
                    }
                }
            }

            return initialized;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            InitializeActionHistory();

            // Renderer handles remain private to this system.
            // Callers mutate PO data and set dirty flags instead of directly touching unity objects
            TextureStreamingSystem textureStreamingSystem = World.GetOrCreateSystemManaged<TextureStreamingSystem>();

            m_Renderer = new ProceduralObjectRenderer(textureStreamingSystem, Mod.ModRootPath);
            InitializeLodGeneration();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_ProceduralObjectToolSystem = World.GetOrCreateSystemManaged<ProceduralObjectToolSystem>();
        }

        protected override void OnUpdate()
        {
            // Rendering-phase sync keeps edits visible while paused.
            // GameSystemBase updates here can follow simulation timing.
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            // New Game can go straight from Game to Game. There may never be a
            // menu frame to clear these managed objects, so reset at the load boundary.
            m_ProceduralObjectToolSystem?.PrepareForWorldChange();
            ClearAll();
        }

        internal void UpdateRuntimeLifecycle()
        {
            // A managed system can survive transitions through unsupported game modes.
            // THIS IS REALLY IMPORTANT TO KNOW
            // Never let objects from the previous city/editor context leak into menus or the next loaded world.
            // NO SERIOUSLY DON'T DO IT
            if (GameManager.instance == null || !IsSupportedRuntimeMode(GameManager.instance.gameMode))
            {
                if (m_Objects.Count != 0 || ActionHistory.Count != 0 || ActionHistory.IsBusy || HasClipboard || ClipboardBusy)
                {
                    ClearAll();
                }

                return;
            }

            m_ProceduralObjectToolSystem?.ProcessHistoryInput();
            m_ProceduralObjectToolSystem?.ProcessClipboardInput();
            PollClipboardOperation();
            ActionHistory.Poll(Mod.Settings?.UndoHistorySteps ?? HistoryLimits.kDefaultCapacity);
            PollArrayEdit();
            m_ProceduralObjectToolSystem?.PollArrayPreview();
            SyncDirtyRenderState();
            m_Renderer?.PollTopologyCleanup();
            UpdateLodGeneration();
        }

        internal void NotifyRendererGeometryChange(ProceduralObject obj, GeometryChange change)
        {
            m_Renderer?.NoticeGeometryChange(obj, change);
        }

        private void SyncDirtyRenderState()
        {
            if (m_Renderer == null)
            {
                return;
            }

            foreach (ProceduralObject proceduralObject in m_Objects.Values)
            {
                ProceduralObjectDirtyFlags dirtyBeforeSync = proceduralObject.DirtyFlags;
                bool lodSourceChanged = (dirtyBeforeSync & (ProceduralObjectDirtyFlags.Geometry | ProceduralObjectDirtyFlags.Topology | ProceduralObjectDirtyFlags.Material)) != 0;
                bool lodScaleChanged = (dirtyBeforeSync & ProceduralObjectDirtyFlags.Transform) != 0 && m_Renderer.HasLodMetricScaleChanged(proceduralObject.Id, proceduralObject.Transform.Scale);
                if (lodSourceChanged || lodScaleChanged)
                {
                    // Stop published meshes before Sync can release or replace the source materials they share.
                    m_Renderer.InvalidateLod(proceduralObject.Id);
                }

                // Renderer.Sync is responsible for clearing only the flags it consumed.
                // Clean objects require no per-frame mesh or material work.
                if (dirtyBeforeSync != ProceduralObjectDirtyFlags.None)
                {
                    m_Renderer.Sync(proceduralObject);
                }

                ObserveLodObject(proceduralObject, dirtyBeforeSync);
            }
        }

        // I almost certainly need to do some extra work to explicitly support the editor later.
        private static bool IsSupportedRuntimeMode(GameMode mode)
        {
            return mode == GameMode.Game || mode == GameMode.Editor;
        }

        protected override void OnDestroy()
        {
            ClearAll();
            ShutdownLodGeneration();
            ActionHistory?.Dispose();

            if (m_Renderer != null)
            {
                m_Renderer.Dispose();
                m_Renderer = null;
            }

            base.OnDestroy();
        }

        public void ActivateTool()
        {
            if (m_ToolSystem == null || m_ProceduralObjectToolSystem == null)
            {
                Mod.log.Warn("[PO] Cannot activate tool because tool systems are unavailable.");
                return;
            }

            if (m_ToolSystem.activeTool == m_ProceduralObjectToolSystem)
            {
                return;
            }

            m_ToolSystem.activeTool = m_ProceduralObjectToolSystem;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private void ClearAll()
        {
            ClearClipboard();
            ResetLodWorld();
            ResetMirrorEdit();
            m_ProceduralObjectToolSystem?.CancelArray();
            ResetArrayEdit();
            ActionHistory?.Clear();
            // Release Unity resources before clearing the model.
            // Failed loads and loads into the current world also need selection and runtime IDs reset.
            m_Renderer?.Clear();
            foreach (ProceduralObject obj in m_Objects.Values) obj.NameChanged -= InvalidateObjectList;
            m_Objects.Clear();
            InvalidateObjectList();
            m_NextId = 1;
            m_SelectedObjectId = null;
            m_SelectedPartIndex = 0;
            m_SelectedSurfaceAreaId = MeshPart.kDefaultSurfaceAreaId;
        }
    }
}
