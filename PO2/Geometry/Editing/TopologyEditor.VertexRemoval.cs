using System;
using System.Collections.Generic;
using System.Threading;
using PO2.Geometry;
using PO2.Persistence.Model;

namespace PO2.Geometry.Editing
{
    public static partial class TopologyEditor
    {
        private static T[] CompactRemovalStream<T>(T[] source, VertexDeletePlan plan, CancellationToken cancellation)
        {
            if (source == null) return null;
            if (source.Length != plan.OldToNew.Length) throw new InvalidOperationException("Invalid vertex stream length.");
            var result = new T[plan.VertexCount];
            for (int i = 0; i < source.Length; i++)
            {
                if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                int index = plan.OldToNew[i];
                if (index >= 0) result[index] = source[i];
            }

            return result;
        }

        internal static PersistencePartData PrepareVertexRemoval(VertexDeletePlan plan, MeshMutationResult mutation)
        {
            var source = plan.Source;
            var previouslyUsedAreas = new HashSet<int>(source.TriangleSurfaceAreaIds);
            var usedAreas = new HashSet<int>(mutation.TriangleSurfaceAreaIds);
            var areas = new List<PersistenceSurfaceAreaData>(source.SurfaceAreas.Length);
            foreach (var area in source.SurfaceAreas)
            {
                if (area.Id == MeshPart.kDefaultSurfaceAreaId || usedAreas.Contains(area.Id) || !previouslyUsedAreas.Contains(area.Id)) areas.Add(area);
            }

            return PrepareTopology(plan.Source, mutation, areas.ToArray());
        }

        private static void SetRemovalVertexMaps(MeshMutationResult result, PersistencePartData source, int[] map, CancellationToken token)
        {
            var logicalMap = new int[source.LogicalVertexCount];
            for (int i = 0; i < logicalMap.Length; i++) logicalMap[i] = -1;
            var renderToLogical = new int[result.Vertices.Length];
            int logicalCount = 0;
            for (int i = 0; i < map.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                if (map[i] < 0) continue;
                int logical = source.RenderToLogical[i];
                if (logicalMap[logical] < 0) logicalMap[logical] = logicalCount++;
                renderToLogical[map[i]] = logicalMap[logical];
            }

            result.SetHistoryVertexMap(map.Length, map);
            result.SetLogicalVertexMap(renderToLogical, logicalCount);
        }
    }
}