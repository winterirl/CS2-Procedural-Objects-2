using System;
using System.Collections.Generic;
using Game.Rendering;
using Unity.Mathematics;
using UnityEngine;

namespace PO2.Rendering.Objects
{
    // A compact batch in the same format as the game's notification icons.
    // There is no UI payload, and stationary frames keep the previous upload.
    internal sealed class ObjectMarkerFrame
    {
        internal struct View : IEquatable<View>
        {
            internal Matrix4x4 ViewProjection;
            internal Vector3 Position, Forward, Up;
            internal float ProjectionY, PixelHeight, IconPixels;
            internal bool Orthographic;

            public bool Equals(View other) => ViewProjection.Equals(other.ViewProjection) && Position.Equals(other.Position) && Forward.Equals(other.Forward) && Up.Equals(other.Up) && ProjectionY == other.ProjectionY && PixelHeight == other.PixelHeight && IconPixels == other.IconPixels && Orthographic == other.Orthographic;
        }

        private sealed class DepthComparer : IComparer<float>
        {
            internal static readonly DepthComparer s_Instance = new DepthComparer();
            public int Compare(float a, float b) => b.CompareTo(a);
        }

        private View m_LastView;
        private uint m_LastVersion;
        private bool m_HasFrame;
        private float[] m_Depths = new float[16];
        internal NotificationIconBufferSystem.InstanceData[] Instances = new NotificationIconBufferSystem.InstanceData[16];
        internal int Count { get; private set; }
        internal Bounds Bounds { get; private set; }

        internal bool Update(IReadOnlyList<Vector3> anchors, uint version, View view)
        {
            if (m_HasFrame && version == m_LastVersion && view.Equals(m_LastView)) return false;
            m_HasFrame = true;
            m_LastView = view;
            m_LastVersion = version;
            Count = 0;
            if (view.PixelHeight <= 0f || view.IconPixels <= 0f || view.ProjectionY <= 0f) return true;
            if (Instances.Length < anchors.Count)
            {
                int capacity = Math.Max(anchors.Count, Instances.Length * 2);
                Array.Resize(ref Instances, capacity);
                Array.Resize(ref m_Depths, capacity);
            }

            Bounds bounds = default;
            float radiusScale = view.IconPixels / (view.PixelHeight * view.ProjectionY);
            for (int i = 0; i < anchors.Count; i++)
            {
                Vector3 anchor = anchors[i];
                float depth = Vector3.Dot(anchor - view.Position, view.Forward);
                Vector4 clip = view.ViewProjection * new Vector4(anchor.x, anchor.y, anchor.z, 1f);
                if (depth <= 0f || clip.w <= 0f || !Finite(depth) || !Finite(clip.x) || !Finite(clip.y) || !Finite(clip.w) || Mathf.Abs(clip.x) > clip.w || Mathf.Abs(clip.y) > clip.w) continue;

                float radius = radiusScale * (view.Orthographic ? 1f : depth);
                // Notification icons start at their bottom center. Offset that
                // origin so our badge stays centered on the existing PO anchor.
                Vector3 origin = anchor - view.Up * radius;
                float distance = Vector3.Distance(origin, view.Position);
                // Match the notification shader's distance scaling.
                float unitRadius = math.pow(distance, 0.6f) * 0.063f;
                if (!Finite(radius) || !Finite(unitRadius) || unitRadius <= 0f) continue;
                Instances[Count] = new NotificationIconBufferSystem.InstanceData
                {
                    m_Position = new float3(origin.x, origin.y, origin.z),
                    m_Params = new float4(radius / unitRadius, 0f, 1f, 1f),
                    m_Icon = 0f,
                    m_Distance = distance
                };
                m_Depths[Count] = depth;
                var markerBounds = new Bounds(anchor, Vector3.one * (radius * 2f));
                if (Count == 0) bounds = markerBounds;
                else bounds.Encapsulate(markerBounds);
                Count++;
            }

            Array.Sort(m_Depths, Instances, 0, Count, DepthComparer.s_Instance);
            Bounds = bounds;
            return true;
        }

        internal void Invalidate()
        {
            m_HasFrame = false;
            Count = 0;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}