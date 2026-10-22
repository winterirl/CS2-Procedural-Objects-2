using System;
using System.Collections.Generic;
using PO2.Geometry;
using PO2.Localization;

namespace PO2.Core
{
    public sealed partial class ProceduralObject
    {
        internal bool TryAppendHistoryParts(MeshPart[] parts, int index, out LocalizedText reason)
        {
            reason = LocalizedText.Empty;
            if (parts == null || parts.Length == 0 || index != m_Parts.Count || parts.Length > kMaxPartCount - index)
            {
                reason = LocalizedText.For("PO2.PartArray.Error.PartListChanged");
                return false;
            }

            var ids = new HashSet<Guid>();
            foreach (var existing in m_Parts)
                if (!ids.Add(existing.PartHistoryId))
                {
                    reason = LocalizedText.For("PO2.PartArray.Error.ObjectDuplicateIdentity");
                    return false;
                }

            foreach (var part in parts)
                if (part == null || part.PartHistoryId == Guid.Empty || !ids.Add(part.PartHistoryId))
                {
                    reason = LocalizedText.For("PO2.PartArray.Error.PartIdentityInUse");
                    return false;
                }

            // Reserve before changing the list so allocation failure cannot leave half an array.
            if (m_Parts.Capacity < index + parts.Length) m_Parts.Capacity = index + parts.Length;
            m_Parts.AddRange(parts);
            PartVersion++;
            MarkDirty(ProceduralObjectDirtyFlags.Topology | ProceduralObjectDirtyFlags.Material);
            return true;
        }

        internal bool TryDetachHistoryParts(Guid[] ids, int index, out MeshPart[] removed, out LocalizedText reason)
        {
            removed = null;
            reason = LocalizedText.Empty;
            if (ids == null || ids.Length == 0 || index < 1 || index + ids.Length != m_Parts.Count)
            {
                reason = LocalizedText.For("PO2.PartArray.Error.PartOrderChanged");
                return false;
            }

            var seen = new HashSet<Guid>();
            foreach (var part in m_Parts)
                if (!seen.Add(part.PartHistoryId))
                {
                    reason = LocalizedText.For("PO2.PartArray.Error.ObjectDuplicateIdentity");
                    return false;
                }

            for (int i = 0; i < ids.Length; i++)
                if (m_Parts[index + i].PartHistoryId != ids[i])
                {
                    reason = LocalizedText.For("PO2.PartArray.Error.PartReplaced");
                    return false;
                }

            removed = new MeshPart[ids.Length];
            m_Parts.CopyTo(index, removed, 0, ids.Length);
            m_Parts.RemoveRange(index, ids.Length);
            PartVersion++;
            MarkDirty(ProceduralObjectDirtyFlags.Topology | ProceduralObjectDirtyFlags.Material);
            return true;
        }
    }
}