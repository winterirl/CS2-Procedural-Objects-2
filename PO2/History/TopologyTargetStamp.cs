using System;
using PO2.Core;
using PO2.Geometry;

namespace PO2.History
{
    // Versions cover the arrays; the small metadata check also catches material
    // updates that happen while a detached topology job is running.
    internal sealed class TopologyTargetStamp
    {
        private readonly ProceduralObject m_Object;
        private readonly MeshPart m_Part;
        private readonly int m_PartIndex;
        private readonly Guid m_PartId;
        private readonly uint m_PartVersion, m_DeformationVersion, m_Geometry, m_Topology, m_Membership, m_Pivot;
        private readonly string m_Name;
        private readonly ulong m_Appearance;
        private readonly UnityEngine.Vector3 m_Scale;
        private readonly bool m_ReceivesDecals;

        internal TopologyTargetStamp(ProceduralObject obj, int index)
        {
            m_Scale = obj.Transform.Scale;
            m_ReceivesDecals = obj.ReceivesDecals;
            m_Object = obj;
            m_PartIndex = index;
            m_Part = obj.Parts[index];
            m_PartId = m_Part.PartHistoryId;
            m_PartVersion = obj.PartVersion;
            m_DeformationVersion = obj.DeformationVersion;
            m_Geometry = m_Part.GeometryVersion;
            m_Topology = m_Part.TopologyVersion;
            m_Membership = m_Part.SurfaceAreaMembershipVersion;
            m_Pivot = m_Part.PivotVersion;
            m_Name = m_Part.Name;
            m_Appearance = Appearance(m_Part);
        }

        internal bool Matches(ProceduralObject obj, int index) => ReferenceEquals(obj, m_Object) && obj.Transform.Scale.Equals(m_Scale) && obj.ReceivesDecals == m_ReceivesDecals && index == m_PartIndex && index >= 0 && index < obj.Parts.Count && ReferenceEquals(obj.Parts[index], m_Part) && m_Part.PartHistoryId == m_PartId && obj.PartVersion == m_PartVersion && obj.DeformationVersion == m_DeformationVersion && m_Part.GeometryVersion == m_Geometry && m_Part.TopologyVersion == m_Topology && m_Part.SurfaceAreaMembershipVersion == m_Membership && m_Part.PivotVersion == m_Pivot && m_Part.Name == m_Name && Appearance(m_Part) == m_Appearance;

        private static ulong Appearance(MeshPart part)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                foreach (var area in part.SurfaceAreas)
                {
                    hash = (hash ^ (uint)area.Id) * 1099511628211UL;
                    hash = (hash ^ area.MaterialSourceVersion) * 1099511628211UL;
                    hash = (hash ^ area.MaterialPropertiesVersion) * 1099511628211UL;
                    hash = (hash ^ area.MappingVersion) * 1099511628211UL;
                    hash = (hash ^ area.ColorMaskVersion) * 1099511628211UL;
                    hash = (hash ^ (uint)area.SourceVertexAttributes) * 1099511628211UL;
                    var tile = area.Material.GetEffectiveTileSizeMeters();
                    hash = (hash ^ (uint)tile.x.GetHashCode()) * 1099511628211UL;
                    hash = (hash ^ (uint)tile.y.GetHashCode()) * 1099511628211UL;
                }

                return hash;
            }
        }
    }
}