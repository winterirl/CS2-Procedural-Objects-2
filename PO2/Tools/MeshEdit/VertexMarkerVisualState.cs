using System;
using UnityEngine;
using PO2.Geometry.Topology;
using PO2.Tools.ObjectTool;

namespace PO2.Tools.MeshEdit
{
    // Bit flags are sent straight to the compute shader. Keep these numeric
    // values in sync with the shader instead of treating this as a UI-only enum.
    [Flags]
    internal enum VertexMarkerVisualState : uint
    {
        None = 0,
        Selected = 1u << 0,
        Hovered = 1u << 1,
        AlignmentTarget = 1u << 2,
        SlideTarget = 1u << 3,
    }

    internal static class VertexMarkerVisualStateUtility
    {
        public static readonly Color s_NormalColor = new Color(0.92f, 0.92f, 0.92f, 1f);

        public static readonly Color s_HoveredColor = new Color(1f, 0.96f, 0.68f, 1f);

        public static readonly Color s_SelectedColor = new Color(1f, 0.58f, 0.10f, 1f);

        public static readonly Color s_SelectedHoveredColor = new Color(1f, 0.82f, 0.30f, 1f);

        public static readonly Color s_AlignmentTargetColor = new Color(0.35f, 0.95f, 1f, 1f);

        public static readonly Color s_SlideTargetColor = new Color(0.55f, 1f, 0.45f, 1f);

        public static VertexMarkerVisualState GetState(ProceduralObjectToolSystem toolSystem, VertexRef vertex)
        {
            VertexMarkerVisualState state = VertexMarkerVisualState.None;

            if (toolSystem.IsMeshEditVertexSelected(vertex))
            {
                state |= VertexMarkerVisualState.Selected;
            }

            if (toolSystem.IsMeshEditVertexHovered(vertex))
            {
                state |= VertexMarkerVisualState.Hovered;
            }

            if (toolSystem.IsMeshEditAlignmentTargetCandidate(vertex))
            {
                state |= VertexMarkerVisualState.AlignmentTarget;
            }

            if (toolSystem.IsMeshEditVertexSlideTargetCandidate(vertex))
            {
                state |= VertexMarkerVisualState.SlideTarget;
            }

            return state;
        }

        public static Color GetColor(VertexMarkerVisualState state)
        {
            // Targeting modes outrank ordinary selection/hover so the candidate
            // stays obvious even when it is already selected.
            if ((state & VertexMarkerVisualState.SlideTarget) != VertexMarkerVisualState.None)
            {
                return s_SlideTargetColor;
            }

            if ((state & VertexMarkerVisualState.AlignmentTarget) != VertexMarkerVisualState.None)
            {
                return s_AlignmentTargetColor;
            }

            bool selected = (state & VertexMarkerVisualState.Selected) != VertexMarkerVisualState.None;
            bool hovered = (state & VertexMarkerVisualState.Hovered) != VertexMarkerVisualState.None;

            if (selected && hovered)
            {
                return s_SelectedHoveredColor;
            }

            if (selected)
            {
                return s_SelectedColor;
            }

            if (hovered)
            {
                return s_HoveredColor;
            }

            return s_NormalColor;
        }
    }
}