using System;
using System.Linq;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Materials;
using PO2.Rendering.Mapping;
using UnityEngine;

internal static partial class LoadingTests
{
    private static void PreparedMappingChecks()
    {
        var triangles = new[] { 0, 1, 2, 0, 2, 3 };
        var seed = new GeneratedMappingSeed(triangles, new[] { 0, 0 },
            new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 1) }, new[] { Vector2.one });
        var invalid = new GeneratedMappingSeed(new[] { 8, 9, 10, 8, 10, 11 }, new[] { 0, 0 }, seed.Coordinates, seed.ReferenceMetric);
        for (int scenario = 0; scenario < 9; scenario++)
        {
            var mesh = new MeshData(new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1) },
                Enumerable.Repeat(Vector3.up, 4).ToArray(), null,
                new[] { new Vector4(0, 0, 0, 0), new Vector4(1, 0, 1, 0), new Vector4(1, 1, 1, 1), new Vector4(0, 1, 0, 1) },
                null, null, null, (int[])triangles.Clone());
            if (scenario == 4) mesh.Triangles = new[] { 0, 2, 1, 0, 3, 2 };
            if (scenario == 5) mesh.Triangles = new[] { 2, 3, 0, 1, 2, 0 };
            if (scenario == 6 || scenario == 7) mesh.UV0 = null;
            var flags = scenario == 1 || scenario >= 6 ? VertexAttributeFlags.SurfaceUVCoordinates : VertexAttributeFlags.None;
            var selected = scenario == 0 || scenario == 1 ? null : scenario == 3 || scenario >= 7 ? invalid : seed;
            int[] indices = { 0, 1, 2, 3 };
            var oldTopology = MeshTopology.Build(mesh);
            var eager = new GeneratedMapping(mesh, oldTopology, flags, selected, indices);
            var current = GeneratedMapping.Create(mesh, flags, selected, indices, out var topology);
            bool skip = scenario == 1 || scenario == 2 || scenario == 4 || scenario == 5 || scenario == 6 || scenario == 8;
            Check((topology == null) == skip, "mapping builds topology only for ordinary charts");
            Check(current.MatchesSource(mesh, flags, selected), "mapping source remains eligible");
            Check(!current.MatchesSource(mesh, flags, new GeneratedMappingSeed(triangles, seed.Charts, seed.Coordinates, seed.ReferenceMetric)) || current.UsesSurfaceUVs, "seed replacement requests recreation");
            if (mesh.UV0 != null) Check(!current.MatchesSource(mesh, flags ^ VertexAttributeFlags.SurfaceUVCoordinates, selected), "authored flag transition requests recreation");
            var originalUv = mesh.UV0?.ToArray();
            foreach (var mode in new[] { TextureMappingMode.Repeat, TextureMappingMode.Stretch, TextureMappingMode.Repeat })
            {
                var mapping = new TextureMappingData(mode, new Vector2(2, 3), 23, new Vector2(0.1f, -0.2f));
                foreach (var scale in new[] { Vector3.one, new Vector3(2, 0.5f, 3) })
                {
                    eager.Update(mesh, oldTopology, scale, mapping, Vector2.one, true);
                    current.Update(mesh, topology, scale, mapping, Vector2.one, true);
                    Same(current.Triangles, eager.Triangles, "mapping triangle parity");
                    Same(current.SourceVertexIndices, eager.SourceVertexIndices, "mapping source indices");
                    EqualStream(current.Vertices, eager.Vertices, "mapping positions");
                    EqualStream(current.Normals, eager.Normals, "mapping normals");
                    EqualStream(current.UVs, eager.UVs, "mapping coordinates");
                    mesh.Vertices[2].y += 0.125f;
                    current.Update(mesh, topology, scale, mapping, new Vector2(2, 3), false);
                    eager.Update(mesh, oldTopology, scale, mapping, new Vector2(2, 3), false);
                    current.UpdateMappingParameters(mesh, scale, mapping, Vector2.one);
                    eager.UpdateMappingParameters(mesh, scale, mapping, Vector2.one);
                    EqualStream(current.Vertices, eager.Vertices, "mapping position edit parity");
                    EqualStream(current.UVs, eager.UVs, "mapping edit and parameter parity");
                }
            }
            EqualStream(mesh.UV0, originalUv, "original UV4 payload unchanged");
        }
    }
}
