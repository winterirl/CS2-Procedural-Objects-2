using System;
using System.Collections.Generic;
using PO2.Materials;

namespace PO2.Geometry
{
    public sealed partial class MeshPart
    {
        internal void ApplyHistorySurfaceAreas(IReadOnlyList<SurfaceArea> areas, int[] memberships, int nextId)
        {
            var replacement = new List<SurfaceArea>(areas.Count);
            var ids = new HashSet<int>();
            if (areas.Count == 0 || areas[0].Id != kDefaultSurfaceAreaId || nextId <= 0 || memberships.Length != Mesh.Triangles.Length / 3) throw new InvalidOperationException("Invalid history Surface Area layout.");
            foreach (var area in areas)
            {
                if (area == null || area.Id < 0 || area.Id >= nextId || !ids.Add(area.Id))
                {
                    throw new InvalidOperationException("Invalid or repeated history Surface Area ID.");
                }

                replacement.Add(area);
            }

            foreach (int id in memberships)
                if (!ids.Contains(id))
                    throw new InvalidOperationException("Missing history membership area.");
            ValidateStructuralFaceSurfaceAreas(StructuralTopology, m_StructuralFaceTriangles, memberships);
            // Validation and allocations are complete; the three fields change together.
            m_SurfaceAreas = replacement;
            m_TriangleSurfaceAreaIds = memberships;
            m_NextSurfaceAreaId = nextId;
            SurfaceAreaMembershipVersion++;
        }
    }
}