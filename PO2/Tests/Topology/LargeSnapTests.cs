using System;
using System.Linq;
using System.Threading;
using PO2;
using UnityEngine;
using PO2.Core;
using PO2.Geometry.Topology;
using PO2.Tools.Snapping;

internal static partial class Program {
    private static MeshTranslationSnapCapture.Input SnapLargeInput(int shape = 0) {
        const int vertexCount = 95440, edgeCount = 183143;
        var vertices = new MeshTranslationSnapVertex[vertexCount];
        var edges = new MeshTranslationSnapCapture.Edge[edgeCount];
        for (int i = 0; i < vertices.Length; i++) {
            float x = shape == 1 ? vertexCount - i : shape == 2 ? Math.Min(i, vertexCount - i) : i % 512;
            vertices[i] = new MeshTranslationSnapVertex { Ref = new VertexRef(0, i),
                Position = shape == 3 ? Vector3.one : new Vector3(x * .25f, i / 512 * .25f, i % 7 * .05f), Valid = true };
        }
        for (int i = 0; i < edges.Length; i++) edges[i] = new MeshTranslationSnapCapture.Edge {
            A = vertices[i % vertexCount].Ref, B = vertices[(i + (i < vertexCount ? 1 : 512)) % vertexCount].Ref, Valid = true
        };
        return new MeshTranslationSnapCapture.Input(vertices, edges,
            new ObjectTransform(new Vector3(17000, -50, 23000), Quaternion.AngleAxis(37, new Vector3(1, 2, 3)), new Vector3(-2, .7f, 3)));
    }
    private static void SnapLargeChecks(TranslationSnapSourceData data) {
        var seen = new bool[data.PrimitiveCount];
        for (int n = 0; n < data.NodeCount; n++) {
            var node = data.Nodes[n];
            if (node.Count == 0) {
                foreach (int child in new[] { node.Left, node.Right }) {
                    Check(child > n && child < data.NodeCount, "Child-first refit order is valid");
                    var bounds = data.Nodes[child];
                    Check(node.Minimum.x <= bounds.Minimum.x && node.Minimum.y <= bounds.Minimum.y && node.Minimum.z <= bounds.Minimum.z &&
                        node.Maximum.x >= bounds.Maximum.x && node.Maximum.y >= bounds.Maximum.y && node.Maximum.z >= bounds.Maximum.z,
                        "Parent bounds contain child bounds");
                }
                continue;
            }
            Check(node.Count <= 8, "Large/adversarial input preserves bounded leaves");
            for (int j = 0; j < node.Count; j++) {
                int id = data.PrimitiveIndices[node.Start + j]; Check(!seen[id], "Each primitive occurs once"); seen[id] = true;
                var p = data.Primitives[id];
                foreach (var point in new[] { p.Start, p.End })
                    Check(point.x >= node.Minimum.x && point.y >= node.Minimum.y && point.z >= node.Minimum.z &&
                        point.x <= node.Maximum.x && point.y <= node.Maximum.y && point.z <= node.Maximum.z, "Leaf bounds enclose exact endpoints");
            }
        }
        Check(seen.All(x => x), "All large-source primitives occur in the tree");
        using (var query = new TranslationSnapSource(data)) {
            for (int q = 0; q < 8; q++) {
                var target = new Vector3(16900 + q * 23, -30 + q * 13, 23000 + q * 7);
                query.TryFindNearest(target, out _, out _, out float actual);
                double best = double.MaxValue;
                foreach (var primitive in data.Primitives) {
                    var a = primitive.Start; var b = primitive.End;
                    double x = (double)b.x - a.x, y = (double)b.y - a.y, z = (double)b.z - a.z;
                    double length = x*x+y*y+z*z;
                    double t = length == 0 ? 0 : Math.Max(0, Math.Min(1, (((double)target.x-a.x)*x+((double)target.y-a.y)*y+((double)target.z-a.z)*z)/length));
                    double dx = a.x+t*x-target.x, dy=a.y+t*y-target.y, dz=a.z+t*z-target.z;
                    best = Math.Min(best, dx*dx+dy*dy+dz*dz);
                }
                Check(Math.Abs(actual-best) <= Math.Max(.5, best * .00002), "Large nearest query agrees with exhaustive double search at distant coordinates");
            }
        }
    }
    private static void LargeSnapTests() {
        for (int shape = 0; shape < 4; shape++) {
            SnapLargeChecks(SnapLargeInput(shape).Build(default));
        }
    }

}
