using System;
using System.Collections.Generic;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Persistence.PreparedTopology
{
    internal sealed class TopologyEncodingSession
    {
        // No entry owns a mesh, topology, or live part. Only encoded bytes survive.
        private sealed class Entry
        {
            internal WeakReference<MeshPart> Part;
            internal WeakReference<MeshTopology> Logical;
            internal WeakReference<StructuralTopology> Structural;
            internal WeakReference<StructuralFaceTriangles[]> Faces;
            internal WeakReference<int[]> Triangles;
            internal uint Version;
            internal StructuralTopologySource Source;
            internal EncodedPartTopology Encoded;
            internal bool Matches(PartTopologyCapture capture)
            {
                return Part.TryGetTarget(out var p) && capture.Part.TryGetTarget(out var q) && ReferenceEquals(p, q) &&
                    Version == capture.Version && Source == capture.Source &&
                    Logical.TryGetTarget(out var l) && ReferenceEquals(l, capture.Logical) &&
                    Structural.TryGetTarget(out var s) && ReferenceEquals(s, capture.Structural) &&
                    Faces.TryGetTarget(out var f) && ReferenceEquals(f, capture.FaceTriangles) &&
                    Triangles.TryGetTarget(out var t) && ReferenceEquals(t, capture.TriangleFaces);
            }
        }
        private readonly Dictionary<Guid, Entry> m_Entries = new Dictionary<Guid, Entry>();
        private readonly Dictionary<Guid, Entry> m_Pending = new Dictionary<Guid, Entry>();
        internal void Clear() { m_Entries.Clear(); m_Pending.Clear(); }
        internal EncodedPartTopology Find(PartTopologyCapture capture)
        {
            return capture.Part.TryGetTarget(out var part) && m_Entries.TryGetValue(part.PartHistoryId, out var entry) && entry.Matches(capture) ? entry.Encoded : null;
        }
        internal void Store(PartTopologyCapture capture, EncodedPartTopology encoded) => Store(m_Entries, capture, encoded);
        internal void Stage(PartTopologyCapture capture, EncodedPartTopology encoded) => Store(m_Pending, capture, encoded);
        private static void Store(Dictionary<Guid, Entry> destination, PartTopologyCapture capture, EncodedPartTopology encoded)
        {
            if (encoded == null || !capture.Part.TryGetTarget(out var part)) return;
            destination[part.PartHistoryId] = new Entry
            {
                Part = capture.Part, Logical = new WeakReference<MeshTopology>(capture.Logical),
                Structural = new WeakReference<StructuralTopology>(capture.Structural),
                Faces = new WeakReference<StructuralFaceTriangles[]>(capture.FaceTriangles),
                Triangles = new WeakReference<int[]>(capture.TriangleFaces), Version = capture.Version,
                Source = capture.Source, Encoded = encoded
            };
        }
        internal void Promote(CitySaveCapture capture)
        {
            foreach (var parts in capture.Parts)
                foreach (var part in parts)
                {
                    if (part.Part.TryGetTarget(out var live)) m_Entries.Remove(live.PartHistoryId);
                    if (part.Reusable) Store(part, part.Candidate);
                }
        }
        internal void CommitLoaded(IEnumerable<ProceduralObject> live)
        {
            m_Entries.Clear();
            foreach (var obj in live)
                foreach (var part in obj.Parts)
                    if (m_Pending.TryGetValue(part.PartHistoryId, out var entry) && entry.Part.TryGetTarget(out var target) && ReferenceEquals(part, target))
                        m_Entries[part.PartHistoryId] = entry;
            m_Pending.Clear();
        }

        internal void Retain(IEnumerable<ProceduralObject> live)
        {
            var parts = new Dictionary<Guid, MeshPart>();
            foreach (var obj in live) foreach (var part in obj.Parts) parts[part.PartHistoryId] = part;
            var remove = new List<Guid>();
            foreach (var pair in m_Entries)
                if (!parts.TryGetValue(pair.Key, out var current) || !pair.Value.Part.TryGetTarget(out var previous) || !ReferenceEquals(previous, current)) remove.Add(pair.Key);
            foreach (var id in remove) m_Entries.Remove(id);
        }
    }
}
