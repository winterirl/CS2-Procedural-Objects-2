using System.Collections.Generic;
using UnityEngine;
using PO2.Core;

namespace PO2.Rendering.Objects
{
    // The camera can move without changing where an icon belongs in the world.
    internal sealed class ObjectMarkerCache
    {
        private sealed class Entry
        {
            internal ProceduralObject Object;
            internal ObjectTransform Transform;
            internal uint GeometryVersion, PartVersion, Seen;
            internal Vector3 Anchor;
            internal bool Valid;
        }

        private readonly Dictionary<int, Entry> m_Entries = new Dictionary<int, Entry>();
        private readonly List<int> m_Removed = new List<int>();
        private readonly List<Vector3> m_Anchors = new List<Vector3>();
        private uint m_Frame;

        internal IReadOnlyList<Vector3> Anchors => m_Anchors;
        internal uint Version { get; private set; }

        internal void Update(ProceduralObjectSystem system)
        {
            unchecked
            {
                m_Frame++;
            }

            bool changed = false;
            m_Anchors.Clear();
            foreach (ProceduralObject obj in system.Objects)
            {
                if (obj == null) continue;
                if (!m_Entries.TryGetValue(obj.Id, out var entry))
                {
                    entry = new Entry();
                    m_Entries.Add(obj.Id, entry);
                }

                var transform = obj.Transform;
                if (!ReferenceEquals(entry.Object, obj) || entry.GeometryVersion != obj.GeometryVersion || entry.PartVersion != obj.PartVersion || !SameTransform(entry.Transform, transform))
                {
                    entry.Object = obj;
                    entry.GeometryVersion = obj.GeometryVersion;
                    entry.PartVersion = obj.PartVersion;
                    entry.Transform = transform;
                    entry.Valid = system.TryGetWorldBounds(obj.Id, out Bounds bounds);
                    if (entry.Valid)
                    {
                        float offset = Mathf.Clamp(bounds.size.y * 0.03f, 0.5f, 5f);
                        entry.Anchor = new Vector3(bounds.center.x, bounds.max.y + offset, bounds.center.z);
                    }

                    changed = true;
                }

                entry.Seen = m_Frame;
                if (entry.Valid) m_Anchors.Add(entry.Anchor);
            }

            m_Removed.Clear();
            foreach (var pair in m_Entries)
                if (pair.Value.Seen != m_Frame)
                    m_Removed.Add(pair.Key);
            foreach (int id in m_Removed)
            {
                m_Entries.Remove(id);
                changed = true;
            }

            if (changed)
                unchecked
                {
                    Version++;
                }
        }

        internal void Clear()
        {
            if (m_Entries.Count == 0) return;
            m_Entries.Clear();
            m_Removed.Clear();
            m_Anchors.Clear();
            unchecked
            {
                Version++;
            }
        }

        private static bool SameTransform(ObjectTransform a, ObjectTransform b) =>
            a.Position.Equals(b.Position) && a.Rotation.Equals(b.Rotation) && a.Scale.Equals(b.Scale);
    }
}