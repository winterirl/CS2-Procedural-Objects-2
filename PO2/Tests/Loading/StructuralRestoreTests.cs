using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PO2.Geometry.Topology;

internal static partial class LoadingTests
{
    private static void StructuralRestoreChecks()
    {
        var random = new Random(174233);
        for (int trial = 0; trial < 80; trial++)
        {
            var faces = new List<StructuralFace>();
            var triangles = new List<int>();
            for (int i = 0; i < 1 + trial; i++)
            {
                int v = i * 4;
                var face = (i + trial) % 3 == 0
                    ? new StructuralFace(i, v, v + 1, v + 2)
                    : new StructuralFace(i, v, v + 1, v + 2, v + 3);
                faces.Add(face);
                AppendFaceTriangles(triangles, face);
            }
            CompareFaceMap(faces.ToArray(), triangles.ToArray());
            CompareStructuralRestore(faces.ToArray(), triangles.ToArray(), faces.Count * 4);

            // Reordering and cyclic rotations must retain duplicate-aware matching.
            var reordered = triangles.Select((value, i) => new { value, i })
                .GroupBy(x => x.i / 3).Select(g => g.Select(x => x.value).ToArray())
                .OrderBy(_ => random.Next()).ToArray();
            foreach (var triangle in reordered)
            {
                int first = triangle[0];
                triangle[0] = triangle[1]; triangle[1] = triangle[2]; triangle[2] = first;
            }
            CompareFaceMap(faces.ToArray(), reordered.SelectMany(t => t).ToArray());
            CompareStructuralRestore(faces.ToArray(), reordered.SelectMany(t => t).ToArray(), faces.Count * 4);
        }

        var mixed = new[] { new StructuralFace(0, 0, 1, 2), new StructuralFace(1, 2, 1, 3, 4) };
        var stream = new[] { 0, 1, 2, 2, 1, 3, 2, 3, 4 };
        CompareStructuralRestore(mixed, stream, 5);
        CompareFaceMap(mixed, stream.Concat(new[] { 8, 9, 10 }).ToArray());
        CompareFaceMap(mixed, new[] { 8, 9, 10 }.Concat(stream).ToArray());
        CompareFaceMap(mixed, new[] { 0, 1, 2 }.Concat(stream).ToArray());
        CompareFaceMap(mixed, stream.Take(3).Concat(new[] { 8, 9, 10 }).Concat(stream.Skip(3)).ToArray());
        CompareStructuralRestore(mixed, stream.Concat(new[] { 6, 7, 8 }).ToArray(), 9);
        CompareFaceMap(mixed, stream.Take(6).ToArray());
        CompareFaceMap(mixed, new[] { 0, 1, 2, 2, 1, 3, 2, 4, 3 });
        CompareFaceMap(new[] { new StructuralFace(1, 0, 1, 2) }, new[] { 0, 1, 2 });
        CompareFaceMap(new[] { default(StructuralFace) }, new[] { 0, 1, 2 });
        CompareFaceMap(Array.Empty<StructuralFace>(), stream);
        CompareFaceMap(Array.Empty<StructuralFace>(), Array.Empty<int>());
        CompareStructuralRestore(Array.Empty<StructuralFace>(), Array.Empty<int>(), 0);

        // High-degree edges include duplicate faces and many distinct memberships.
        var fanFaces = Enumerable.Range(0, 600).Select(i => new StructuralFace(i, 0, 1, 2 + i % 120)).ToArray();
        var fanTriangles = new List<int>();
        foreach (var face in fanFaces) AppendFaceTriangles(fanTriangles, face);
        CompareFaceMap(fanFaces, fanTriangles.ToArray());
        CompareStructuralRestore(fanFaces, fanTriangles.ToArray(), 122);

        var duplicateQuads = new[] { new StructuralFace(0, 0, 1, 2, 3), new StructuralFace(1, 0, 1, 2, 3) };
        CompareFaceMap(duplicateQuads, new[] { 0, 1, 2, 0, 2, 3, 0, 1, 2, 0, 2, 3 });
        CompareStructuralRestore(duplicateQuads, new[] { 0, 1, 2, 0, 2, 3, 0, 1, 2, 0, 2, 3 }, 4);
        var gridFaces = new List<StructuralFace>();
        var gridTriangles = new List<int>();
        for (int y = 0; y < 20; y++)
        for (int x = 0; x < 20; x++)
        {
            int a = y * 21 + x, b = a + 1, c = b + 21, d = a + 21;
            if ((x + y) % 3 == 0)
            {
                gridFaces.Add(new StructuralFace(gridFaces.Count, a, b, c));
                gridFaces.Add(new StructuralFace(gridFaces.Count, a, c, d));
            }
            else gridFaces.Add(new StructuralFace(gridFaces.Count, a, b, c, d));
        }
        foreach (var face in gridFaces) AppendFaceTriangles(gridTriangles, face);
        CompareFaceMap(gridFaces.ToArray(), gridTriangles.ToArray());
        CompareStructuralRestore(gridFaces.ToArray(), gridTriangles.ToArray(), 441);
        for (int trial = 0; trial < 60; trial++)
        {
            var faces = new List<StructuralFace>();
            var triangles = new List<int>();
            for (int i = 0; i < 50 + trial; i++)
            {
                int a = random.Next(40), b, c;
                do { b = random.Next(40); } while (b == a);
                do { c = random.Next(40); } while (c == a || c == b);
                var face = new StructuralFace(i, a, b, c);
                faces.Add(face);
                AppendFaceTriangles(triangles, face);
            }
            CompareStructuralRestore(faces.ToArray(), triangles.ToArray(), 40);
        }
        StructuralMalformedInputs();
    }

    private static void AppendFaceTriangles(List<int> values, StructuralFace face)
    {
        values.Add(face.GetRenderVertexIndex(0));
        values.Add(face.GetRenderVertexIndex(1));
        values.Add(face.GetRenderVertexIndex(2));
        if (!face.IsQuad) return;
        values.Add(face.GetRenderVertexIndex(0));
        values.Add(face.GetRenderVertexIndex(2));
        values.Add(face.GetRenderVertexIndex(3));
    }

    private static Exception FailureOf(Action action)
    {
        try { action(); return null; }
        catch (Exception exception) { return exception; }
    }

    private static void CompareFaceMap(StructuralFace[] faces, int[] triangles)
    {
        StructuralFaceTriangles[] expected = null, actual = null;
        int[] expectedReverse = null, actualReverse = null;
        var before = FailureOf(() => ReferenceStructuralFaceTriangleResolver.Build(faces, triangles, out expected, out expectedReverse));
        var after = FailureOf(() => StructuralFaceTriangleResolver.Build(faces, triangles, out actual, out actualReverse));
        Check(before?.GetType() == after?.GetType(), "face resolver rejection parity");
        if (before != null) return;
        Same(actualReverse, expectedReverse, "face reverse map");
        Check(actual.Length == expected.Length, "face forward count");
        for (int i = 0; i < actual.Length; i++)
            Check(actual[i].Count == expected[i].Count && actual[i].TriangleA == expected[i].TriangleA && actual[i].TriangleB == expected[i].TriangleB, "face forward map");
    }

    private static void CompareStructuralRestore(StructuralFace[] faces, int[] triangles, int vertices)
    {
        var mesh = Mesh(vertices, triangles);
        var topology = MeshTopology.Restore(mesh, Enumerable.Range(0, vertices).ToArray(), vertices);
        ReferenceStructuralFaceTriangleResolver.Build(faces, triangles, out var forward, out var reverse);
        var suppressed = new bool[topology.LogicalEdgeCount];
        foreach (var face in faces)
            if (face.IsQuad && topology.TryGetEdge(face.GetRenderVertexIndex(0), face.GetRenderVertexIndex(2), out var edge))
                suppressed[edge.Id] = true;

        ReferenceStructuralTopology expected = null;
        StructuralTopology actual = null;
        var before = FailureOf(() => expected = ReferenceStructuralTopology.BuildFromConvertedFaces(topology, triangles, faces, forward, reverse, suppressed));
        var after = FailureOf(() => actual = StructuralTopology.BuildFromConvertedFaces(topology, triangles, faces, forward, reverse, suppressed));
        Check(before?.GetType() == after?.GetType(), "structural pack rejection parity");
        if (before != null) return;
        CompareStructuralOutputs(actual, expected, vertices);
        CompareStructuralOutputs(StructuralTopology.Build(topology, vertices, faces), expected, vertices);

        var reversed = faces.Select(f => f.IsQuad
            ? new StructuralFace(f.Id, f.GetRenderVertexIndex(0), f.GetRenderVertexIndex(3), f.GetRenderVertexIndex(2), f.GetRenderVertexIndex(1))
            : new StructuralFace(f.Id, f.GetRenderVertexIndex(0), f.GetRenderVertexIndex(2), f.GetRenderVertexIndex(1))).ToArray();
        var mirror = actual.ReverseMirrorFaces(reversed, CancellationToken.None);
        for (int i = 0; i < faces.Length; i++)
            for (int j = 0; j < faces[i].CornerCount; j++)
            {
                int old = faces[i].CornerCount - 1 - j;
                Check(mirror.GetBoundaryEdgeId(i, j) == actual.GetBoundaryEdgeId(i, old), "mirror boundary edge");
                Check(mirror.IsBoundaryForward(i, j) != actual.IsBoundaryForward(i, old), "mirror boundary direction");
            }
        CompareStructuralOutputs(mirror.ReverseMirrorFaces(faces, CancellationToken.None), expected, vertices);
    }

    private static void CompareStructuralOutputs(StructuralTopology actual, ReferenceStructuralTopology expected, int vertices)
    {
        Check(actual.FaceCount == expected.FaceCount && actual.StructuralEdgeCount == expected.StructuralEdgeCount, "structural counts");
        for (int i = 0; i < expected.FaceCount; i++)
        {
            var face = expected.GetFace(i);
            Check(actual.GetFace(i).Equals(face), "stored face preserved");
            for (int j = 0; j < face.CornerCount; j++)
            {
                Check(actual.GetBoundaryEdgeId(i, j) == expected.GetBoundaryEdgeId(i, j), "boundary edge ID");
                Check(actual.IsBoundaryForward(i, j) == expected.IsBoundaryForward(i, j), "boundary direction");
                Check(actual.TryGetBoundaryIndex(i, actual.GetBoundaryEdgeId(i, j), out int found) && found == j, "boundary lookup");
            }
        }
        for (int i = 0; i < expected.StructuralEdgeCount; i++)
        {
            var a = actual.GetEdge(i); var b = expected.GetEdge(i);
            Check(a.Id == b.Id && a.LogicalVertexA == b.LogicalVertexA && a.LogicalVertexB == b.LogicalVertexB, "structural edge order");
            Same(a.AdjacentFaceIds, b.AdjacentFaceIds, "structural face membership");
            Check(actual.TryGetEdgeByLogicalVertices(b.LogicalVertexB, b.LogicalVertexA, out var found) && found.Id == i, "structural endpoint lookup");
        }
        for (int i = 0; i < vertices; i++) Same(actual.GetIncidentEdgeIds(i), expected.GetIncidentEdgeIds(i), "incident edge order");
    }

    private static void StructuralMalformedInputs()
    {
        var triangles = new[] { 0, 1, 2, 0, 2, 3 };
        var faces = new[] { new StructuralFace(0, 0, 1, 2, 3) };
        var topology = MeshTopology.Restore(Mesh(4, triangles), new[] { 0, 1, 2, 3 }, 4);
        for (int scenario = 0; scenario < 7; scenario++)
        {
            ReferenceStructuralFaceTriangleResolver.Build(faces, triangles, out var forward, out var reverse);
            var suppressed = new bool[topology.LogicalEdgeCount];
            topology.TryGetEdge(0, 2, out var diagonal);
            suppressed[diagonal.Id] = true;
            if (scenario == 0) reverse[0] = 7;
            if (scenario == 1) reverse[0] = -2;
            if (scenario == 2) forward[0] = new StructuralFaceTriangles(1, 0, 2);
            if (scenario == 3) forward[0] = new StructuralFaceTriangles(0, 5, 2);
            if (scenario == 4) suppressed[diagonal.Id] = false;
            if (scenario == 5) suppressed[0] = true;
            if (scenario == 6) forward[0] = new StructuralFaceTriangles(0, -1, 1);
            var before = FailureOf(() => ReferenceStructuralTopology.BuildFromConvertedFaces(topology, triangles, faces, forward, reverse, suppressed));
            var after = FailureOf(() => StructuralTopology.BuildFromConvertedFaces(topology, triangles, faces, forward, reverse, suppressed));
            Check(before != null && before.GetType() == after?.GetType(), "malformed structural input rejected");
        }
    }
}
