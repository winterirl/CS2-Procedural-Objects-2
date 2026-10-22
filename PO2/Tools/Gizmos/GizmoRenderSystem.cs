using Colossal;
using Colossal.Mathematics;
using Game;
using Game.Tools;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using PO2.Tools.MeshEdit;
using PO2.Tools.ObjectTool;
using PO2.Tools.Snapping;

namespace PO2.Tools.Gizmos
{
    // Draws through GizmoBatcher. The tool and GizmoMath choose targets, sizes, and handles.
    public partial class GizmoRenderSystem : GameSystemBase
    {
        private struct RenderGizmoJob : IJob
        {
            public GizmoBatcher Batcher;

            public GizmoMode Mode;
            public float3 Pivot;
            public float GizmoSize;
            public float3 FrameX;
            public float3 FrameY;
            public float3 FrameZ;
            public GizmoHandle HoveredHandle;
            public GizmoHandle ActiveHandle;
            public float ActiveScaleHandleLength;
            public CageOverlay Cage;
            public CageOverlay MarkerSizes;
            public bool HasSnapFeedback;
            public SnapTargetKind SnapTargetKind;
            public float3 SnapSource;
            public float3 SnapTarget;
            public bool HasArray;
            public float3 ArrayStart, ArrayEnd;
            public float ArrayMarkerSize;
            public bool HasCurve;
            public float3 ArrayControl1, ArrayControl2;
            public float3 ArrayPointSizes;
            public int ArraySelectedPoint;
            public bool HasArrayRotation;
            public GizmoContext ArrayRotation;

            public void Execute()
            {
                if (HasArray)
                {
                    Color color = new Color(0.15f, 0.9f, 1f, 1f);
                    // DrawLine culls individual subpixel segments, which makes
                    // finely sampled bends disappear. DrawBezier culls the whole
                    // curve and chooses its detail from the screen size instead.
                    if (HasCurve)
                        Batcher.DrawBezier(new Bezier4x3
                        {
                            a = ArrayStart, b = ArrayControl1, c = ArrayControl2, d = ArrayEnd,
                        }, color, segmentsCount: 256);
                    else Batcher.DrawLine(ArrayStart, ArrayEnd, color);
                    if (HasCurve)
                    {
                        Color handleColor = new Color(0.9f, 0.65f, 0.2f, 1f);
                        Batcher.DrawLine(ArrayStart, ArrayControl1, handleColor);
                        Batcher.DrawLine(ArrayEnd, ArrayControl2, handleColor);
                        Batcher.DrawWireCube(float4x4.identity, ArrayControl1, new float3(ArrayPointSizes.y), ArraySelectedPoint == 1 ? Color.white : handleColor);
                        Batcher.DrawWireCube(float4x4.identity, ArrayControl2, new float3(ArrayPointSizes.z), ArraySelectedPoint == 2 ? Color.white : handleColor);
                    }

                    Batcher.DrawWireCube(float4x4.identity, ArrayStart, new float3(ArrayMarkerSize), color);
                    Batcher.DrawWireCube(float4x4.identity, ArrayEnd, new float3(ArrayMarkerSize), HasCurve && ArraySelectedPoint == 0 ? Color.white : color);
                }

                if (Cage.Visible) DrawCage();
                if (GizmoSize > 0f)
                    switch (Mode)
                    {
                        case GizmoMode.Translate:
                            DrawTranslationGizmo();
                            break;

                        case GizmoMode.Rotate:
                            DrawRotationGizmo();
                            break;

                        case GizmoMode.Scale:
                            DrawScaleGizmo();
                            break;
                    }

                if (HasSnapFeedback)
                {
                    DrawSnapFeedback();
                }

                if (HasArrayRotation)
                {
                    Pivot = ArrayRotation.Pivot;
                    GizmoSize = ArrayRotation.Size;
                    FrameX = ArrayRotation.Frame.XAxis;
                    FrameY = ArrayRotation.Frame.YAxis;
                    FrameZ = ArrayRotation.Frame.ZAxis;
                    DrawRotationGizmo();
                }
            }

            private void DrawSnapFeedback()
            {
                Color targetColor;

                switch (SnapTargetKind)
                {
                    case SnapTargetKind.POVertex:
                        targetColor = new Color(1f, 0.8f, 0.15f, 1f);
                        break;

                    case SnapTargetKind.POEdge:
                        targetColor = new Color(0.25f, 1f, 0.5f, 1f);
                        break;

                    default:
                        targetColor = new Color(0.15f, 0.9f, 1f, 1f);
                        break;
                }

                float markerSize = math.max(0.12f, GizmoSize * 0.07f);

                Batcher.DrawLine(SnapSource, SnapTarget, targetColor);

                Batcher.DrawWireCube(float4x4.identity, SnapSource, new float3(markerSize, markerSize, markerSize), Color.white);

                Batcher.DrawWireCube(float4x4.identity, SnapTarget, new float3(markerSize, markerSize, markerSize), targetColor);
            }

            private void DrawTranslationGizmo()
            {
                // Draw plane handles first so the axis arrows stay visually
                // crisp where the projected shapes happen to cross.
                DrawMovePlane(GizmoHandle.MoveXY, FrameX, FrameY, Color.blue);

                DrawMovePlane(GizmoHandle.MoveXZ, FrameX, FrameZ, Color.green);

                DrawMovePlane(GizmoHandle.MoveYZ, FrameY, FrameZ, Color.red);

                DrawMoveAxis(GizmoHandle.MoveX, FrameX, Color.red);

                DrawMoveAxis(GizmoHandle.MoveY, FrameY, Color.green);

                DrawMoveAxis(GizmoHandle.MoveZ, FrameZ, Color.blue);
            }

            private void DrawRotationGizmo()
            {
                float radius = GizmoSize * 0.82f;

                DrawRotateAxis(GizmoHandle.RotateX, FrameX, FrameY, radius, Color.red);

                DrawRotateAxis(GizmoHandle.RotateY, FrameY, FrameZ, radius, Color.green);

                DrawRotateAxis(GizmoHandle.RotateZ, FrameZ, FrameX, radius, Color.blue);
            }

            private void DrawScaleGizmo()
            {
                DrawScaleAxis(GizmoHandle.ScaleX, FrameX, Color.red);

                DrawScaleAxis(GizmoHandle.ScaleY, FrameY, Color.green);

                DrawScaleAxis(GizmoHandle.ScaleZ, FrameZ, Color.blue);

                Color centerColor = GetHandleColor(GizmoHandle.ScaleUniform, Color.white);

                float centerSize = GizmoSize * 0.13f;

                Batcher.DrawWireCube(float4x4.identity, Pivot, new float3(centerSize, centerSize, centerSize), centerColor);
            }

            private void DrawCage()
            {
                Color wire = new Color(0.8f, 0.77f, 0.9f, 0.82f);
                for (int i = 0; i < 8; i++)
                {
                    float3 point = Cage.Get(i);
                    for (int bit = 1; bit <= 4; bit <<= 1)
                    {
                        if ((i & bit) == 0) Batcher.DrawLine(point, Cage.Get(i | bit), wire);
                    }

                    bool selected = (Cage.Selected & (1 << i)) != 0;
                    bool hovered = i == Cage.Hovered;
                    Color color = selected ? hovered ? VertexMarkerVisualStateUtility.s_SelectedHoveredColor : VertexMarkerVisualStateUtility.s_SelectedColor : hovered ? Color.white : wire;
                    float size = MarkerSizes.Get(i).x;
                    Batcher.DrawWireCube(float4x4.identity, point, new float3(size, size, size), color);
                }
            }

            private void DrawScaleAxis(GizmoHandle handle, float3 axis, Color baseColor)
            {
                Color color = GetHandleColor(handle, baseColor);
                float3 normalizedAxis = math.normalizesafe(axis);
                // During an axis-scale drag the cube follows the live scale so
                // the handle does not appear detached from what it controls.
                float handleLength = handle == ActiveHandle && ActiveScaleHandleLength > 0f ? ActiveScaleHandleLength : GizmoSize;

                float3 end = Pivot + normalizedAxis * handleLength;

                Batcher.DrawLine(Pivot, end, color);

                float handleSize = GizmoSize * 0.12f;

                Batcher.DrawWireCube(float4x4.identity, end, new float3(handleSize, handleSize, handleSize), color);
            }

            private void DrawMoveAxis(GizmoHandle handle, float3 axis, Color baseColor)
            {
                Color color = GetHandleColor(handle, baseColor);
                float3 end = Pivot + axis * GizmoSize;

                Batcher.DrawArrow(Pivot, end, color, GizmoSize * 0.20f);
            }

            private void DrawMovePlane(GizmoHandle handle, float3 uAxis, float3 vAxis, Color baseColor)
            {
                Color color = GetHandleColor(handle, baseColor);
                float3 u = math.normalizesafe(uAxis);
                float3 v = math.normalizesafe(vAxis);

                float inner = GizmoSize * GizmoMath.kTranslationPlaneInnerRatio;

                float outer = GizmoSize * GizmoMath.kTranslationPlaneOuterRatio;

                float3 innerInner = Pivot + u * inner + v * inner;

                float3 outerInner = Pivot + u * outer + v * inner;

                float3 outerOuter = Pivot + u * outer + v * outer;

                float3 innerOuter = Pivot + u * inner + v * outer;

                Batcher.DrawLine(innerInner, outerInner, color);

                Batcher.DrawLine(outerInner, outerOuter, color);

                Batcher.DrawLine(outerOuter, innerOuter, color);

                Batcher.DrawLine(innerOuter, innerInner, color);
            }

            private void DrawRotateAxis(GizmoHandle handle, float3 normal, float3 from, float radius, Color baseColor)
            {
                Color color = GetHandleColor(handle, baseColor);

                Batcher.DrawWireArc(Pivot, normal, from, 360f, radius, color, 48);
            }

            private Color GetHandleColor(GizmoHandle handle, Color baseColor)
            {
                if (handle == ActiveHandle)
                {
                    return Color.Lerp(baseColor, Color.white, 0.70f);
                }

                if (handle == HoveredHandle)
                {
                    return Color.Lerp(baseColor, Color.white, 0.45f);
                }

                return baseColor;
            }
        }

        private GizmosSystem m_GizmosSystem;
        private ProceduralObjectToolSystem m_ProceduralObjectToolSystem;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_GizmosSystem = World.GetOrCreateSystemManaged<GizmosSystem>();

            m_ProceduralObjectToolSystem = World.GetOrCreateSystemManaged<ProceduralObjectToolSystem>();
        }

        protected override void OnDestroy()
        {
            Dependency.Complete();
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            Camera camera = Camera.main;

            if (camera == null) return;
            bool hasGizmo = m_ProceduralObjectToolSystem.TryGetGizmoContext(camera, out GizmoContext context);
            bool hasCage = m_ProceduralObjectToolSystem.TryGetCageOverlay(out CageOverlay cage);
            bool hasArray = m_ProceduralObjectToolSystem.TryGetArrayLine(out Vector3 arrayStart, out Vector3 arrayEnd);
            bool hasCurve = m_ProceduralObjectToolSystem.TryGetArrayCurveOverlay(out _, out var controls, out int selectedPoint);
            if (!hasGizmo && !hasCage && !hasArray) return;
            CageOverlay sizes = default;
            if (hasCage)
                for (int i = 0; i < 8; i++)
                {
                    float size = GizmoMath.GetWorldSizeForPixels(camera, cage.Get(i), 8f);
                    sizes.Set(i, new Vector3(size, size, size));
                }

            // Wait for this system and the batcher's existing writers before scheduling,
            // then register the new writer.
            bool hasSnapFeedback = m_ProceduralObjectToolSystem.TryGetActiveTranslationSnap(out SnapTargetKind snapTargetKind, out Vector3 snapSource, out Vector3 snapTarget);

            bool hasArrayRotation = m_ProceduralObjectToolSystem.TryGetArrayRotationContext(camera, out var arrayRotation);
            RenderGizmoJob job = new RenderGizmoJob
            {
                HasArrayRotation = hasArrayRotation, ArrayRotation = arrayRotation,
                Batcher = m_GizmosSystem.GetGizmosBatcher(out JobHandle dependencies),
                Mode = context.Mode,
                Pivot = context.Pivot,
                GizmoSize = context.Size,
                FrameX = context.Frame.XAxis,
                FrameY = context.Frame.YAxis,
                FrameZ = context.Frame.ZAxis,
                HoveredHandle = m_ProceduralObjectToolSystem.HoveredGizmoHandle,
                ActiveHandle = m_ProceduralObjectToolSystem.ActiveGizmoHandle,
                ActiveScaleHandleLength = context.ActiveScaleHandleLength,
                Cage = cage,
                MarkerSizes = sizes,
                HasSnapFeedback = hasSnapFeedback,
                SnapTargetKind = snapTargetKind,
                SnapSource = snapSource,
                SnapTarget = snapTarget,
                HasCurve = hasCurve,
                ArrayControl1 = controls.Control1, ArrayControl2 = controls.Control2,
                ArraySelectedPoint = selectedPoint,
                ArrayPointSizes = hasCurve ? new float3(0, GizmoMath.GetWorldSizeForPixels(camera, controls.Control1, 8f), GizmoMath.GetWorldSizeForPixels(camera, controls.Control2, 8f)) : default,
                HasArray = hasArray,
                ArrayStart = arrayStart,
                ArrayEnd = arrayEnd,
                ArrayMarkerSize = hasArray ? GizmoMath.GetWorldSizeForPixels(camera, arrayEnd, 8f) : 0f,
            };

            JobHandle jobHandle = job.Schedule(JobHandle.CombineDependencies(Dependency, dependencies));

            m_GizmosSystem.AddGizmosBatcherWriter(jobHandle);
            Dependency = jobHandle;
        }
    }
}