using Game;
using Game.Rendering;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using PO2.Core;
using PO2.Tools.ObjectTool;

namespace PO2.Rendering.Objects
{
    // OverlayRenderSystem clears its buffer every frame, so resubmit the
    // selection ring on every active update.
    public partial class ObjectSelectionOverlaySystem : GameSystemBase
    {
        private const float kMinimumRingPaddingMeters = 1f;
        private const float kRingPaddingRatio = 0.08f;
        private const float kMinimumOutlineWidthMeters = 0.35f;
        private const float kMaximumOutlineWidthMeters = 1.5f;
        private const float kOutlineWidthRatio = 0.025f;

        private static readonly Color s_SelectionRingColor = new Color(0.08f, 0.52f, 1f, 0.92f);

        private static readonly Color s_TransparentFill = new Color(0f, 0f, 0f, 0f);

        private ProceduralObjectSystem m_ProceduralObjectSystem;
        private ProceduralObjectToolSystem m_ProceduralObjectToolSystem;
        private OverlayRenderSystem m_OverlayRenderSystem;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ProceduralObjectSystem = World.GetOrCreateSystemManaged<ProceduralObjectSystem>();
            m_ProceduralObjectToolSystem = World.GetOrCreateSystemManaged<ProceduralObjectToolSystem>();
            m_OverlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
        }

        protected override void OnUpdate()
        {
            if (m_ProceduralObjectSystem == null || m_ProceduralObjectToolSystem == null || m_OverlayRenderSystem == null || !m_ProceduralObjectSystem.IsToolActive || m_ProceduralObjectToolSystem.Mode == ObjectToolMode.MeshEdit || !m_ProceduralObjectSystem.SelectedObjectId.HasValue)
            {
                return;
            }

            int objectId = m_ProceduralObjectSystem.SelectedObjectId.Value;
            if (!m_ProceduralObjectSystem.TryGetWorldBounds(objectId, out Bounds bounds))
            {
                return;
            }

            // Use the horizontal bounding-box diagonal as the radius.
            // That guarantees the ring encloses a rotated/long object from above.
            float2 horizontalExtents = new float2(math.abs(bounds.extents.x), math.abs(bounds.extents.z));
            float radius = math.length(horizontalExtents);
            if (!math.isfinite(radius) || radius <= 0.001f)
            {
                return;
            }

            float padding = math.max(kMinimumRingPaddingMeters, radius * kRingPaddingRatio);
            float diameter = (radius + padding) * 2f;
            float outlineWidth = math.clamp(diameter * kOutlineWidthRatio, kMinimumOutlineWidthMeters, kMaximumOutlineWidthMeters);

            float3 center = new float3(bounds.center.x, bounds.min.y, bounds.center.z);

            OverlayRenderSystem.Buffer overlayBuffer = m_OverlayRenderSystem.GetBuffer(out JobHandle overlayDeps);

            SelectionRingOverlayJob job = new SelectionRingOverlayJob
            {
                OverlayBuffer = overlayBuffer,
                Center = center,
                Diameter = diameter,
                OutlineWidth = outlineWidth
            };

            JobHandle handle = job.Schedule(overlayDeps);
            m_OverlayRenderSystem.AddBufferWriter(handle);
        }

        protected override void OnDestroy()
        {
            m_ProceduralObjectSystem = null;
            m_ProceduralObjectToolSystem = null;
            m_OverlayRenderSystem = null;

            base.OnDestroy();
        }

        private struct SelectionRingOverlayJob : IJob
        {
            public OverlayRenderSystem.Buffer OverlayBuffer;
            public float3 Center;
            public float Diameter;
            public float OutlineWidth;

            public void Execute()
            {
                OverlayBuffer.DrawCircle(s_SelectionRingColor, s_TransparentFill, OutlineWidth, OverlayRenderSystem.StyleFlags.Projected | OverlayRenderSystem.StyleFlags.DepthFadeBelow, new float2(0f, 1f), Center, Diameter);
            }
        }
    }
}