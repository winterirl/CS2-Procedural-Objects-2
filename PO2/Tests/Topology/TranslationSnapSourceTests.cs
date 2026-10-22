using System;
using System.Linq;
using PO2;
using UnityEngine;
using PO2.Core;
using PO2.Geometry.Topology;

internal static partial class Program {
    private static ProceduralObject SnapObject(Vector3[] points) {
        var part = Make(points, Array.Empty<StructuralFace>(), false,
            Enumerable.Range(0, points.Length).ToArray());
        return new ProceduralObject(42, Guid.NewGuid(), part) {
            Transform = ObjectTransform.IdentityAt(Vector3.zero)
        };
    }

    private static void TranslationSnapSourceTests() {
        Check(!CaptureSnapSource(null, null, null, out _), "Null source rejected");
        var one = SnapObject(new[] { new Vector3(2, 3, 4) });
        Check(!CaptureSnapSource(one, Array.Empty<VertexRef>(), null, out _), "Empty source rejected");
        Check(!CaptureSnapSource(one, new[] { new VertexRef(-1, 0), new VertexRef(0, 9) }, null, out _), "Invalid-only source rejected");
        var refs = new[] { new VertexRef(-1, 0), new VertexRef(0, 0), new VertexRef(0, 9) };
        Check(CaptureSnapSource(one, refs, new[] { new StructuralEdgeRef(7, 0) }, out var compact), "Filtered source captured");
        using (compact) {
            Check(compact.TryFindNearest(Vector3.zero, out int id, out Vector3 point, out _)
                && id == 0 && point.Equals(one.Parts[0].Mesh.Vertices[0]), "Filtered IDs remain compact");
            Check(!compact.TryGetClosestPoint(-1, Vector3.zero, out _, out _), "Negative source ID rejected");
            Check(!compact.TryGetClosestPoint(1, Vector3.zero, out _, out _), "Unused capacity is not queryable");
            one.Parts[0].Mesh.Vertices[0] = Vector3.zero;
            Check(compact.TryGetClosestPoint(0, Vector3.zero, out point, out _) && point.Equals(new Vector3(2, 3, 4)), "Source keeps the captured starting pose");
        }

        var random = new Random(5721);
        foreach (int count in new[] { 1, 7, 8, 9, 16, 17, 64, 65, 257, 1025 }) {
            for (int shape = 0; shape < 4; shape++) {
                var points = new Vector3[count];
                for (int i = 0; i < count; i++) {
                    points[i] = shape == 0 ? new Vector3(i * 0.25f, 0, 0)
                        : shape == 1 ? new Vector3(i % 17, i / 17, 0)
                        : shape == 2 ? new Vector3(3, 3, 3)
                        : new Vector3((float)random.NextDouble() * 7, (float)random.NextDouble() * 3, (float)random.NextDouble() * 9);
                }
                var obj = SnapObject(points);
                obj.Transform = new ObjectTransform(new Vector3(17000, -50, 23000),
                    Quaternion.AngleAxis(37, new Vector3(1, 2, 3)), new Vector3(-2, 0.7f, 3));
                var selection = Enumerable.Range(0, count).Reverse().Select(i => new VertexRef(0, i)).ToArray();
                Check(CaptureSnapSource(obj, selection, null, out var source), "Point set captured");
                var world = selection.Select(v => obj.Transform.TransformPoint(points[v.VertexId])).ToArray();
                using (source) {
                    for (int i = 0; i < world.Length; i++) {
                        Check(source.TryGetClosestPoint(i, Vector3.zero, out Vector3 point, out _) && point.Equals(world[i]), "Primitive IDs retain input order");
                    }
                    for (int q = 0; q < 40; q++) {
                        Vector3 target = new Vector3(16980 + (float)random.NextDouble() * 40,
                            -60 + (float)random.NextDouble() * 20, 22980 + (float)random.NextDouble() * 50);
                        Check(source.TryFindNearest(target, out int id, out Vector3 point, out float distance), "Nearest point found");
                        double best = double.MaxValue;
                        foreach (var p in world) {
                            double x = (double)p.x - target.x, y = (double)p.y - target.y, z = (double)p.z - target.z;
                            best = Math.Min(best, x * x + y * y + z * z);
                        }
                        Check(id >= 0 && id < count && point.Equals(world[id]), "Query ID agrees with captured point");
                        Check(Math.Abs(distance - best) <= Math.Max(0.00001, best * 0.000002), "Nearest distance matches exhaustive double-precision search");
                    }
                }
            }
        }
        SnapSegmentsAndSeams();
        TranslationSnapPreparationTests();
        TranslationSnapToolTests();
    }

    private static void SnapSegmentsAndSeams() {
        var points = new[] { new Vector3(0, 0, 0), new Vector3(4, 0, 0), new Vector3(0, 4, 0),
            new Vector3(0, 0, 0), new Vector3(4, 0, 0), new Vector3(0, 4, 0) };
        var part = Make(points, new[] { new StructuralFace(0, 0, 1, 2), new StructuralFace(1, 3, 4, 5) }, false,
            new[] { 0, 1, 2, 0, 1, 2 });
        var obj = new ProceduralObject(7, Guid.NewGuid(), part) { Transform = ObjectTransform.IdentityAt(Vector3.zero) };
        Check(part.StructuralTopology.TryGetEdgeByLogicalVertices(0, 1, out var edge), "Seamed structural edge exists");
        var vertices = new[] { new VertexRef(0, 0), new VertexRef(0, 1) };
        var edges = new[] { new StructuralEdgeRef(-1, 0), new StructuralEdgeRef(0, edge.Id), new StructuralEdgeRef(0, 999) };
        Check(CaptureSnapSource(obj, vertices, edges, out var source), "Mixed source captured");
        using (source) {
            Check(source.TryFindNearest(new Vector3(2, 1, 0), out int id, out Vector3 point, out float distance)
                && id == 2 && point.Equals(new Vector3(2, 0, 0)) && distance == 1, "Partially selected edge interior can be the source");
            Check(source.TryGetClosestPoint(2, new Vector3(10, 1, 0), out point, out _) && point.Equals(points[1]), "Segment projection clamps to endpoint");
            Check(!source.TryGetClosestPoint(3, Vector3.zero, out _, out _), "Filtered edge capacity is not visible");
        }
        // The topology IDs stay valid when an edit collapses an edge.
        part.Mesh.Vertices[1] = part.Mesh.Vertices[0];
        Check(CaptureSnapSource(obj, vertices, edges, out source), "Collapsed edge captured");
        using (source) {
            Check(source.TryGetClosestPoint(2, Vector3.one, out var point, out _) && point.Equals(Vector3.zero), "Zero-length segment remains usable");
        }
    }
}
