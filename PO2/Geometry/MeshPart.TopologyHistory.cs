using System;

namespace PO2.Geometry
{
    public sealed partial class MeshPart
    {
        internal MeshPart CaptureTopologyReference()
        {
            var captured = (MeshPart)MemberwiseClone();
            captured.Mesh = Mesh.CaptureTopologyReference();
            return captured;
        }

        internal void ApplyHistoryTopology(MeshPart staging)
        {
            if (staging == null || ReferenceEquals(staging, this)) throw new ArgumentException("Expected a detached topology replacement.");
            // The detached part has already validated streams and built adjacency.
            Mesh.AdoptPreparedTopology(staging.Mesh);
            Topology = staging.Topology;
            StructuralTopology = staging.StructuralTopology;
            StructuralTopologySource = staging.StructuralTopologySource;
            m_SurfaceAreas = staging.m_SurfaceAreas;
            m_TriangleSurfaceAreaIds = staging.m_TriangleSurfaceAreaIds;
            m_NextSurfaceAreaId = staging.m_NextSurfaceAreaId;
            m_StructuralFaceTriangles = staging.m_StructuralFaceTriangles;
            m_TriangleStructuralFaceIds = staging.m_TriangleStructuralFaceIds;
            m_HasTopologyEditCapabilities = false;
            SurfaceAreaMembershipVersion++;
            TopologyVersion++;
            MarkGeometryChanged();
        }
    }
}