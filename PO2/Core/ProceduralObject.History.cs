using System;
using System.Collections.Generic;
using PO2.Geometry;
using PO2.History.Actions;

namespace PO2.Core
{
    public sealed partial class ProceduralObject
    {
        internal void ApplyHistoryValue(ObjectValueKind kind, ObjectValueState state)
        {
            switch (kind)
            {
                case ObjectValueKind.Transform:
                    Transform = state.Transform;
                    MarkDirty(ProceduralObjectDirtyFlags.Transform);
                    break;
                case ObjectValueKind.Name: Name = state.Name; break;
                case ObjectValueKind.Locked: IsLocked = state.Locked; break;
                case ObjectValueKind.ReceivesDecals:
                    SetReceivesDecals(state.ReceivesDecals);
                    break;
                case ObjectValueKind.LodProfile:
                    SetLodProfile(state.LodProfile);
                    break;
                case ObjectValueKind.Pivot:
                    if (!SetLocalPivot(state.Pivot) && !LocalPivot.Equals(state.Pivot))
                    {
                        throw new InvalidOperationException("Invalid object pivot history state.");
                    }

                    break;
                case ObjectValueKind.Orientations:
                    if (state.Orientations == null || state.Orientations.Length > kMaxCustomOrientationCount)
                    {
                        throw new InvalidOperationException("Invalid history orientation count.");
                    }

                    var replacement = new List<CustomOrientation>(state.Orientations.Length);
                    foreach (var orientation in state.Orientations) replacement.Add(orientation.Clone());
                    if (m_CustomOrientations.Capacity < replacement.Count) m_CustomOrientations.Capacity = replacement.Count;
                    // Keep active tool references usable across simple renames.
                    for (int i = 0; i < Math.Min(m_CustomOrientations.Count, replacement.Count); i++)
                    {
                        if (m_CustomOrientations[i].LocalRotation.Equals(replacement[i].LocalRotation))
                        {
                            m_CustomOrientations[i].TryRename(replacement[i].Name);
                            replacement[i] = m_CustomOrientations[i];
                        }
                    }

                    m_CustomOrientations.Clear();
                    m_CustomOrientations.AddRange(replacement);
                    CustomOrientationVersion++;
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        internal void ApplyHistoryPartName(Guid id, string name)
        {
            foreach (var part in m_Parts)
            {
                if (part.PartHistoryId != id) continue;
                if (part.Rename(name)) PartVersion++;
                return;
            }

            throw new InvalidOperationException("Missing part rename target.");
        }

        internal bool TryHistoryInsertPart(Guid expectedId, int index, MeshPart part, out string reason)
        {
            reason = string.Empty;
            if (part == null || expectedId == Guid.Empty || part.PartHistoryId != expectedId || index < 0 || index > m_Parts.Count || m_Parts.Count >= kMaxPartCount)
            {
                reason = "Invalid history part insertion.";
                return false;
            }

            var seen = new HashSet<Guid>();
            foreach (var existing in m_Parts)
            {
                if (existing.PartHistoryId == expectedId || !seen.Add(existing.PartHistoryId))
                {
                    reason = "History part identity is already present or repeated.";
                    return false;
                }
            }

            m_Parts.Insert(index, part);
            PartVersion++;
            MarkDirty(ProceduralObjectDirtyFlags.Topology | ProceduralObjectDirtyFlags.Material);
            return true;
        }

        internal bool TryHistoryRemovePart(Guid expectedId, int index, out MeshPart removed, out string reason)
        {
            removed = null;
            reason = string.Empty;
            if (expectedId == Guid.Empty || index < 0 || index >= m_Parts.Count || m_Parts[index].PartHistoryId != expectedId)
            {
                reason = "History part identity or position does not match.";
                return false;
            }

            int matches = 0;
            foreach (var part in m_Parts)
                if (part.PartHistoryId == expectedId)
                    matches++;
            if (matches != 1)
            {
                reason = "Repeated history part identity.";
                return false;
            }

            return TryRemovePart(index, out removed, out reason);
        }
    }
}