using MeshTopology = PO2.Geometry.Topology.MeshTopology;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PO2;
using UnityEngine;
using PO2.Core;
using PO2.Core.Deformation;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Materials;
using PO2.Tools.ObjectTool;

internal static partial class Program {
    private static int s_Checks;
    private static void Check(bool value, string reason) {
        s_Checks++;
        if (!value) throw new Exception(reason);
    }

    private static void Main(string[] args) {
        ClipboardTests();
        if (args.Contains("--clipboard")) { Console.WriteLine($"PASS: {s_Checks} clipboard checks."); return; }
        HistoryRendererTests();
        if (args.Contains("--history-renderer")) { Console.WriteLine($"PASS: {s_Checks} history renderer checks."); return; }
        OperationStateTests();
        if (args.Contains("--operation-state")) { Console.WriteLine($"PASS: {s_Checks} operation state checks."); return; }
        MultiCutTests();
        if (args.Contains("--cuts")) { Console.WriteLine($"PASS: {s_Checks} multi-cut checks."); return; }
        ArrayTests();
        if (args.Contains("--array")) { Console.WriteLine($"PASS: {s_Checks} array checks."); return; }
        if (args.Contains("--mirror-large")) { LargeMirrorRoundTrip(); Console.WriteLine("PASS: large mirror with exact undo/redo."); return; }
        MirrorTests();
        WrappedPrimitiveTests();
        if (args.Contains("--mirror")) { Console.WriteLine($"PASS: {s_Checks} mirror checks."); return; }
        if (args.Contains("--history7")) { VertexHistoryPreparationTests(); Console.WriteLine($"PASS: {s_Checks} history preparation checks."); return; }
        if (args.Contains("--snap7")) { TranslationSnapSourceTests(); Console.WriteLine($"PASS: {s_Checks} pass 7 snapping checks."); return; }
        if (args.Contains("--residual-work")) { ResidualWorkTests(); Console.WriteLine($"PASS: {s_Checks} step 6.6 residual work checks."); return; }
        if (args.Contains("--renderer-updates")) { RendererUpdateTests(); Console.WriteLine($"PASS: {s_Checks} renderer update checks."); return; }
        VertexHistoryPreparationTests();
        ResidualWorkTests();
        RendererUpdateTests();
        if (args.Contains("--snap-source")) {
            TranslationSnapSourceTests();
            Console.WriteLine($"PASS: {s_Checks} snapping source checks.");
            return;
        }
        TranslationSnapSourceTests();
        LargeSnapTests();
        DissolvePolygonLimits();
        UV1Tests();
        if (args.Contains("--uv1")) { Console.WriteLine($"PASS: {s_Checks} UV1 checks."); return; }
        ConvertedSubdivisionTests();
        if (args.Contains("--converted")) { Console.WriteLine($"PASS: {s_Checks} converted subdivision checks."); return; }
        VertexDissolutionTests();
        if (args.Contains("--dissolve")) { Console.WriteLine($"PASS: {s_Checks} dissolve and renderer checks."); return; }
        VertexDeletionTests();
        Masks();
        QuadStripAndFanLayouts();
        BoxTopOppositeEdges();
        SharedEdgesAndSeams();
        OverlappingGroups();
        Rejections();
        HistoryCycles();
        ExistingOperations();
        CommandIntegration();
        ReversedWinding();
        FoldedQuad();
        Console.WriteLine($"PASS: {s_Checks} production topology and history checks.");
    }

    private static MeshPart Make(Vector3[] vertices, StructuralFace[] faces,
        bool streams = true, int[] mapping = null, int[] areas = null) {
        var triangles = new List<int>();
        foreach (var face in faces) {
            triangles.Add(face.GetRenderVertexIndex(0));
            triangles.Add(face.GetRenderVertexIndex(1));
            triangles.Add(face.GetRenderVertexIndex(2));
            if (face.IsQuad) {
                triangles.Add(face.GetRenderVertexIndex(0));
                triangles.Add(face.GetRenderVertexIndex(2));
                triangles.Add(face.GetRenderVertexIndex(3));
            }
        }
        Vector3[] normals = streams ? new Vector3[vertices.Length] : null;
        Vector4[] tangents = streams ? new Vector4[vertices.Length] : null;
        Vector4[] uv = streams ? new Vector4[vertices.Length] : null;
        Color32[] colors = streams ? new Color32[vertices.Length] : null;
        Vector4[] extra = streams ? new Vector4[vertices.Length] : null;
        for (int i = 0; streams && i < vertices.Length; i++) {
            normals[i] = new Vector3(0.03f * i, 0.01f * i, 1);
            tangents[i] = new Vector4(1, 0.02f * i, 0, -1);
            uv[i] = new Vector4(i * i * 0.125f, i * 0.3f, 7, 9);
            colors[i] = new Color32((byte)(i * 16), 64, 128, 255);
            extra[i] = new Vector4(0, 0, 0, 1);
        }
        var mesh = new MeshData(vertices, normals, tangents, uv, colors, extra, triangles.ToArray());
        if (mapping == null && areas == null) {
            return new MeshPart("Test", mesh, MaterialData.CreateDefault(), faces);
        }
        if (mapping == null) {
            var topology = MeshTopology.Build(mesh);
            mapping = Enumerable.Range(0, vertices.Length).Select(topology.GetLogicalVertexIdForRenderVertex).ToArray();
        }
        return MeshPart.Restore("Test", mesh, mapping, mapping.Max() + 1, faces,
            new List<SurfaceArea> {
                new SurfaceArea(0, MaterialData.CreateDefault()),
                new SurfaceArea(1, MaterialData.CreateDefault())
            }, areas ?? new int[triangles.Count / 3], 2, PO2.Geometry.Topology.StructuralTopologySource.Authored);
    }

    private static MeshPart Single(int corners, bool warped = false, bool streams = true) {
        var vertices = new[] {
            new Vector3(0, 0, 0), new Vector3(3, 0, 0),
            new Vector3(2, 2, warped ? 0.6f : 0), new Vector3(0, 3, 0)
        };
        return corners == 3
            ? Make(vertices.Take(3).ToArray(), new[] { new StructuralFace(0, 0, 1, 2) }, streams)
            : Make(vertices, new[] { new StructuralFace(0, 0, 1, 2, 3) }, streams);
    }

    private static void Same(TopologyState a, TopologyState b, string reason) {
        for (int i = 0; i < a.Streams.Length; i++) {
            Check(TopologyState.Equal(a.Streams[i], b.Streams[i]), reason + " stream " + i);
        }
    }

    private static int[] AllEdges(MeshPart part) =>
        Enumerable.Range(0, part.StructuralTopology.StructuralEdgeCount).ToArray();

    private static MeshMutationResult Build(MeshPart part, int[] edges,
        out SubdividedEdge[] children) {
        Check(SubdivisionTestAdapter.Build(part, edges,
            out var mutation, out children, out LocalizedText reason), "Mutation: " + reason);
        return mutation;
    }

    private static int[] ResolveChildren(MeshPart part, SubdividedEdge[] edges) {
        var ids = new List<int>();
        foreach (var edge in edges) {
            Check(!part.StructuralTopology.TryGetEdgeByLogicalVertices(edge.LogicalA, edge.LogicalB, out _),
                "Original edge removed from all adjoining faces");
            Check(part.StructuralTopology.TryGetEdgeByLogicalVertices(edge.LogicalA, edge.LogicalMidpoint, out var a), "First half");
            Check(part.StructuralTopology.TryGetEdgeByLogicalVertices(edge.LogicalMidpoint, edge.LogicalB, out var b), "Second half");
            ids.Add(a.Id);
            ids.Add(b.Id);
        }
        Check(ids.Distinct().Count() == edges.Length * 2, "Child selection has no duplicates");
        return ids.ToArray();
    }

    private static double Area(MeshData mesh) {
        double area = 0;
        for (int i = 0; i < mesh.Triangles.Length; i += 3) {
            var a = mesh.Vertices[mesh.Triangles[i]];
            var b = mesh.Vertices[mesh.Triangles[i + 1]];
            var c = mesh.Vertices[mesh.Triangles[i + 2]];
            var cross = Vector3.Cross(b - a, c - a);
            Check(cross.z > 0, "Positive winding and area");
            area += cross.magnitude * 0.5;
        }
        return area;
    }

    private static void Masks() {
        foreach (int corners in new[] { 3, 4 }) {
            for (int variant = 0; variant < 3; variant++) {
                for (int mask = 0; mask < (1 << corners); mask++) {
                    var part = Single(corners, variant == 1, variant != 2);
                    var before = TopologyState.Capture(part);
                    var source = part.Mesh.Clone();
                    var edges = Enumerable.Range(0, corners).Where(i => (mask & (1 << i)) != 0)
                        .Select(i => part.StructuralTopology.GetBoundaryEdgeId(0, i)).ToArray();
                    if (mask == 0) {
                        Check(!SubdivisionTestAdapter.Build(part, edges,
                            out var empty, out var none, out _), "Empty selection rejected");
                        Check(empty == null && none.Length == 0, "No empty mutation");
                        Same(before, TopologyState.Capture(part), "Empty unchanged");
                        continue;
                    }
                    var mutation = Build(part, edges, out var children);
                    Same(before, TopologyState.Capture(part), "Detached builder leaves source unchanged");
                    int added = edges.Length + (corners == 4 && mask == 15 ? 1 : 0);
                    Check(mutation.Vertices.Length == corners + added, "Render counts");
                    Check(mutation.LogicalVertexCount == corners + added, "Logical counts");
                    for (int i = 0; i < corners; i++) {
                        Check(mutation.Vertices[i].Equals(source.Vertices[i]), "Original position exact");
                        Check(mutation.HistoryOldToNew[i] == i && mutation.RenderToLogical[i] == i, "Identity preserved");
                        if (source.Normals == null) continue;
                        Check(mutation.Normals[i].Equals(source.Normals[i]), "Original normal exact");
                        Check(mutation.Tangents[i].Equals(source.Tangents[i]), "Original tangent exact");
                        Check(mutation.UV0[i].Equals(source.UV0[i]), "Original UV exact");
                        Check(mutation.VertexColors[i].Equals(source.VertexColors[i]), "Original color exact");
                        Check(mutation.TexCoord3[i].Equals(source.TexCoord3[i]), "Original extra exact");
                    }
                    var reordered = Build(part, edges.Reverse().Concat(edges).ToArray(), out var otherChildren);
                    var other = before.Restore();
                    other.ApplyTopologyMutation(reordered);
                    part.ApplyTopologyMutation(mutation);
                    Same(TopologyState.Capture(part), TopologyState.Capture(other), "Selection order independent");
                    Check(children.Length == otherChildren.Length, "Selection deduplicated");
                    bool newQuadLayout = corners == 4 &&
                        (edges.Length == 1 || mask == 5 || mask == 10);
                    double sourceArea = Area(source), resultArea = Area(part.Mesh);
                    if (variant != 1 || !newQuadLayout) {
                        Check(Math.Abs(sourceArea - resultArea) < 0.00001, "Surface area preserved");
                    }
                    int expectedQuads = corners != 4 ? 0 : mask == 15 ? 4 : mask == 5 || mask == 10 ? 2 : 0;
                    Check(part.StructuralTopology.QuadCount == expectedQuads, "Face template kind");
                    VerifyInsertedMidpoints(source, part, children);
                    ResolveChildren(part, children);
                    if (variant == 2) {
                        Check(part.Mesh.Normals == null && part.Mesh.Tangents == null && part.Mesh.UV0 == null &&
                            part.Mesh.VertexColors == null && part.Mesh.TexCoord3 == null, "Missing streams remain missing");
                    }
                    else if (!newQuadLayout) {
                        VerifyInterpolatedSurface(source, part.Mesh);
                    }
                }
            }
        }
    }

    private static void VerifyInsertedMidpoints(MeshData source, MeshPart part,
        SubdividedEdge[] children) {
        foreach (var edge in children) {
            // These single-face fixtures have one original render record per logical ID.
            int index = Array.FindIndex(Enumerable.Range(0,part.Mesh.Vertices.Length).ToArray(),
                i => part.Topology.GetLogicalVertexIdForRenderVertex(i) == edge.LogicalMidpoint);
            int a = edge.LogicalA, b = edge.LogicalB;
            Check(index >= 0, "Inserted midpoint exists");
            Check(part.Mesh.Vertices[index].Equals(source.Vertices[a]*0.5f + source.Vertices[b]*0.5f), "Midpoint stays on source edge");
            if (source.UV0 == null) continue;
            Check(part.Mesh.UV0[index].Equals(source.UV0[a]*0.5f + source.UV0[b]*0.5f), "Midpoint UV interpolation");
            Check(part.Mesh.Normals[index].Equals(source.Normals[a]*0.5f + source.Normals[b]*0.5f), "Midpoint normal interpolation");
            Check(part.Mesh.Tangents[index].Equals(source.Tangents[a]*0.5f + source.Tangents[b]*0.5f), "Midpoint tangent interpolation");
        }
    }

    private static void QuadStripAndFanLayouts() {
        foreach (bool reverse in new[] {false,true}) {
            for (int start = 0; start < 4; start++) {
                foreach (bool oppositePair in new[] {false,true}) {
                    var part = Make(new[] {new Vector3(0,0,0),new Vector3(3,0,0),new Vector3(3,2,0),new Vector3(0,2,0)},
                        new[] {reverse ? new StructuralFace(0,0,3,2,1) : new StructuralFace(0,0,1,2,3)});
                    var face = part.StructuralTopology.GetFace(0);
                    int edge = part.StructuralTopology.GetBoundaryEdgeId(0,start);
                    int opposite = part.StructuralTopology.GetBoundaryEdgeId(0,(start+2)%4);
                    var mutation = Build(part,oppositePair ? new[] {edge,opposite} : new[] {edge},out var children);
                    part.ApplyTopologyMutation(mutation);
                    ResolveChildren(part,children);
                    Check(!part.StructuralTopology.TryGetEdgeByLogicalVertices(0,2,out _) &&
                        !part.StructuralTopology.TryGetEdgeByLogicalVertices(1,3,out _), "Old diagonals are not structural edges");
                    if (oppositePair) {
                        Check(part.StructuralTopology.FaceCount == 2 && part.StructuralTopology.QuadCount == 2,
                            "Opposite selected edges produce exactly two quads");
                        Check(part.StructuralTopology.TryGetEdgeByLogicalVertices(children[0].LogicalMidpoint,
                            children[1].LogicalMidpoint,out var connection) && connection.AdjacentFaceCount == 2,
                            "Midpoint connection shared by both quads");
                    }
                    else {
                        int midpoint = children[0].LogicalMidpoint;
                        Check(part.StructuralTopology.FaceCount == 3 && part.StructuralTopology.TriangleCount == 3,
                            "Single selected edge produces exactly three triangles");
                        foreach (var triangle in part.StructuralTopology.Faces) {
                            Check(Enumerable.Range(0,3).Any(i => part.Topology.GetLogicalVertexIdForRenderVertex(
                                triangle.GetRenderVertexIndex(i)) == midpoint), "Every triangle fans from the new midpoint");
                        }
                        for (int offset = 2; offset <= 3; offset++) {
                            int corner = face.GetRenderVertexIndex((start+offset)%4);
                            Check(part.StructuralTopology.TryGetEdgeByLogicalVertices(midpoint,corner,out var spoke) &&
                                spoke.AdjacentFaceCount == 2, "Fan connects midpoint to each opposite corner");
                        }
                    }
                    for (int i = 0; i < part.Mesh.Triangles.Length; i += 3) {
                        var a = part.Mesh.Vertices[part.Mesh.Triangles[i]];
                        var b = part.Mesh.Vertices[part.Mesh.Triangles[i+1]];
                        var c = part.Mesh.Vertices[part.Mesh.Triangles[i+2]];
                        Check(Vector3.Cross(b-a,c-a).z * (reverse ? -1 : 1) > 0, "New layouts preserve winding");
                    }
                }
            }
        }
    }

    private static void BoxTopOppositeEdges() {
        var part=Make(new[] {new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(0,1,0),
            new Vector3(0,0,1),new Vector3(1,0,1),new Vector3(1,1,1),new Vector3(0,1,1)},new[] {
            new StructuralFace(0,0,3,2,1),new StructuralFace(1,4,5,6,7),
            new StructuralFace(2,0,1,5,4),new StructuralFace(3,1,2,6,5),
            new StructuralFace(4,2,3,7,6),new StructuralFace(5,3,0,4,7)
        });
        var before=TopologyState.Capture(part);
        var mutation=Build(part,new[] {part.StructuralTopology.GetBoundaryEdgeId(1,0),
            part.StructuralTopology.GetBoundaryEdgeId(1,2)},out var children);
        part.ApplyTopologyMutation(mutation);
        Check(part.StructuralTopology.QuadCount==5 && part.StructuralTopology.TriangleCount==6,
            "Box: two top quads, two three-triangle sides, three untouched quads");
        Check(part.Topology.LogicalVertexCount==10,"Only the two selected edges get new points");
        Check(part.StructuralTopology.Faces.Count(f => f.IsQuad && Enumerable.Range(0,4).All(
            i => part.Mesh.Vertices[f.GetRenderVertexIndex(i)].z == 1))==2,"Top face is two clean quads");
        foreach (int id in ResolveChildren(part,children)) {
            Check(part.StructuralTopology.GetEdge(id).AdjacentFaceCount==2,"Top and side agree on child edges");
        }
        var after=TopologyState.Capture(part);
        var delta=new ReversibleTopologyDelta(before,after,mutation.HistoryOldToNew);
        try {
            Same(before,delta.Apply(after,HistoryDirection.Undo),"Box layout undo exact");
            Same(after,delta.Apply(before,HistoryDirection.Redo),"Box layout redo exact");
        }
        finally { delta.Dispose(); }
        // Save/reload must keep the new faces and explicit logical groups.
        Same(after,TopologyState.Capture(after.Restore()),"Box layout reload exact");
    }

    private static void VerifyInterpolatedSurface(MeshData source, MeshData result) {
        // At each child's centroid, independently recover the original triangle's
        // barycentric coordinates. This catches changed diagonals on warped quads
        // and UV/normal changes which vertex counts alone would miss.
        for (int j = 0; j < result.Triangles.Length; j += 3) {
            int ia = result.Triangles[j], ib = result.Triangles[j + 1], ic = result.Triangles[j + 2];
            var p = (result.Vertices[ia] + result.Vertices[ib] + result.Vertices[ic]) / 3f;
            var uv = (result.UV0[ia] + result.UV0[ib] + result.UV0[ic]) / 3f;
            var normal = (result.Normals[ia] + result.Normals[ib] + result.Normals[ic]) / 3f;
            bool matched = false;
            for (int i = 0; i < source.Triangles.Length; i += 3) {
                int a = source.Triangles[i], b = source.Triangles[i + 1], c = source.Triangles[i + 2];
                var u = source.Vertices[b] - source.Vertices[a];
                var v = source.Vertices[c] - source.Vertices[a];
                var w = p - source.Vertices[a];
                float uu = Vector3.Dot(u,u), uvDot = Vector3.Dot(u,v), vv = Vector3.Dot(v,v);
                float wu = Vector3.Dot(w,u), wv = Vector3.Dot(w,v), d = uu*vv-uvDot*uvDot;
                float y = (wu*vv-wv*uvDot)/d, z = (wv*uu-wu*uvDot)/d, x = 1-y-z;
                if (x < -0.00001f || y < -0.00001f || z < -0.00001f) continue;
                if ((source.Vertices[a]*x+source.Vertices[b]*y+source.Vertices[c]*z-p).magnitude > 0.00001f) continue;
                var expectedUV = source.UV0[a]*x + source.UV0[b]*y + source.UV0[c]*z;
                var expectedNormal = source.Normals[a]*x + source.Normals[b]*y + source.Normals[c]*z;
                Check(Math.Abs(expectedUV.x-uv.x) < 0.00001 && Math.Abs(expectedUV.y-uv.y) < 0.00001, "UV field preserved");
                Check((normal-expectedNormal).magnitude < 0.00001, "Normal field preserved");
                matched = true;
                break;
            }
            Check(matched, "Child lies on original triangle surface");
        }
    }

    private static MeshPart TwoQuads(bool seam) {
        var positions = new[] {
            new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(0,1,0),
            new Vector3(1,0,0),new Vector3(2,0,0),new Vector3(2,1,0),new Vector3(1,1,0)
        };
        var faces = new[] { new StructuralFace(0,0,1,2,3),
            seam ? new StructuralFace(1,4,5,6,7) : new StructuralFace(1,1,5,6,2) };
        return Make(positions, faces, true, null, new[] { 0,0,1,1 });
    }

    private static void SharedEdgesAndSeams() {
        foreach (bool seam in new[] { false, true }) {
            var part = TwoQuads(seam);
            int edge = part.StructuralTopology.GetBoundaryEdgeId(0, 1);
            int sourceLogicalCount = part.Topology.LogicalVertexCount;
            var mutation = Build(part, new[] { edge }, out var children);
            Check(mutation.Vertices.Length == 8 + (seam ? 2 : 1), "Only required render variants inserted");
            part.ApplyTopologyMutation(mutation);
            Check(part.Topology.LogicalVertexCount == sourceLogicalCount + 1, "One logical midpoint across seam");
            Check(part.TriangleSurfaceAreaIds.Count(i => i == 0) == 3 &&
                part.TriangleSurfaceAreaIds.Count(i => i == 1) == 3, "Surface Area assignment inherited");
            foreach (int id in ResolveChildren(part, children)) {
                Check(part.StructuralTopology.GetEdge(id).AdjacentFaceCount == 2, "Both sides share child edge");
            }
            if (seam) Check(!part.Mesh.UV0[8].Equals(part.Mesh.UV0[9]), "UV seam retained");
            var repeat = Build(part, ResolveChildren(part, children), out var grandchildren);
            part.ApplyTopologyMutation(repeat);
            Check(ResolveChildren(part, grandchildren).Length == 4, "Repeat splits selected halves");
        }
        var diverged = TwoQuads(true);
        diverged.Mesh.Vertices[4] += new Vector3(0,0,0.02f);
        diverged.Mesh.Vertices[7] += new Vector3(0,0,0.02f);
        diverged.MarkGeometryChanged();
        var result = Build(diverged, new[] { diverged.StructuralTopology.GetBoundaryEdgeId(0,1) }, out _);
        Check(result.Vertices[8].z == 0 && result.Vertices[9].z == 0.02f, "Existing render separation preserved");
        Check(result.RenderToLogical[8] == result.RenderToLogical[9], "Logical identity survives divergent positions");
    }

    private static void OverlappingGroups() {
        var positions = new[] { new Vector3(0,0,0),new Vector3(2,0,0),new Vector3(2,2,0),new Vector3(0,2,0) };
        var part = Make(positions.Concat(positions).ToArray(), new[] {
            new StructuralFace(0,0,1,2,3),new StructuralFace(1,4,5,6,7)
        }, true, Enumerable.Range(0,8).ToArray());
        var mutation = Build(part, AllEdges(part), out var children);
        part.ApplyTopologyMutation(mutation);
        Check(part.Topology.LogicalVertexCount == 18, "Overlapping corners, midpoints and centers stay separate");
        Check(part.Topology.GetLogicalVertexIdForRenderVertex(12) != part.Topology.GetLogicalVertexIdForRenderVertex(17), "Face centers independent");
        ResolveChildren(part, children);
        var reloaded = TopologyState.Capture(part).Restore();
        Same(TopologyState.Capture(part), TopologyState.Capture(reloaded), "Persistence restores explicit mapping");
    }

    private static void Reject(MeshPart part, int[] edges, string label) {
        var before = TopologyState.Capture(part);
        Check(!SubdivisionTestAdapter.Build(part, edges, out var mutation,
            out var children, out LocalizedText reason), label);
        Check(mutation == null && children.Length == 0 && !reason.IsEmpty, label + " has reason and no result");
        Same(before, TopologyState.Capture(part), label + " leaves source intact");
    }

    private static void Rejections() {
        foreach (bool oppositePair in new[] {false,true}) {
            var concave = Make(new[] {new Vector3(0,0,0),new Vector3(2,0,0),new Vector3(0.5f,0.5f,0),new Vector3(0,2,0)},
                new[] {new StructuralFace(0,0,1,2,3)});
            int first = concave.StructuralTopology.GetBoundaryEdgeId(0,0);
            int opposite = concave.StructuralTopology.GetBoundaryEdgeId(0,2);
            Reject(concave,oppositePair ? new[] {first,opposite} : new[] {first},
                "Concave face rejects an inverted replacement atomically");
        }
        var part = Single(4);
        Reject(part, new[] { -1 }, "Invalid selection");
        part.Mesh.Vertices[1] = part.Mesh.Vertices[0];
        Reject(part, AllEdges(part), "Collapsed edge");
        part = Single(4); part.Mesh.Vertices[0].x = float.NaN;
        Reject(part, AllEdges(part), "Nonfinite geometry");
        part = Single(4); part.Mesh.Tangents[1].w = 1;
        Reject(part, AllEdges(part), "Mixed handedness");
        part = Single(4); part.Mesh.TexCoord3[1].x = 0.5f;
        Reject(part, AllEdges(part), "Varying native payload");
        part = Single(4); part.Mesh.Normals[1] = -part.Mesh.Normals[0];
        Reject(part, AllEdges(part), "Cancelled inserted normal");
        part = Single(4);
        for (int i = 0; i < 4; i++) part.Mesh.Vertices[i] *= 0.00001f;
        Reject(part, AllEdges(part), "Tiny geometry");
        part = Single(4);
        part.SetSurfaceAreaSourceVertexAttributes(0, VertexAttributeFlags.UV0FourComponent |
            VertexAttributeFlags.VertexColor | VertexAttributeFlags.TexCoord3);
        Reject(part, AllEdges(part), "Packed metadata not blended");
        part = Make(new[] { new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,1,0),
            new Vector3(0,-1,0),new Vector3(0,0,1) }, new[] {
            new StructuralFace(0,0,1,2),new StructuralFace(1,1,0,3),
            new StructuralFace(2,0,1,4)
        });
        Reject(part, new[] { part.StructuralTopology.GetBoundaryEdgeId(0,0) }, "Nonmanifold selection");
        part = Single(3);
        var converted = MeshPart.Restore(part.Name, part.Mesh.Clone(), new[] {0,1,2},3,
            part.StructuralTopology.CloneFaces(), part.SurfaceAreas.Select(a => a.Clone()).ToList(),
            new[] {0},1,PO2.Geometry.Topology.StructuralTopologySource.ConvertedTriangleBaseline);
        Check(!converted.SupportsDestructiveTopologyEditing && SubdivisionTestAdapter.Analyze(converted, AllEdges(converted), out _, out _), "Converted subdivision keeps other capability restrictions");
        var valid = Single(4);
        var mutation = Build(valid, AllEdges(valid), out _);
        bool failed = false;
        try { mutation.SetLogicalVertexMap(new[] {0}, 1); } catch (ArgumentException) { failed = true; }
        Check(failed, "Malformed mapping rejected");
        var before = TopologyState.Capture(valid);
        mutation.RenderToLogical[0] = -1;
        try { valid.ApplyTopologyMutation(mutation); Check(false, "Invalid apply must throw"); }
        catch (InvalidOperationException) { Same(before, TopologyState.Capture(valid), "Failed apply atomic"); }
    }

    private static void HistoryCycles() {
        var part = TwoQuads(true);
        var obj = new ProceduralObject(81, Guid.NewGuid(), part);
        var system = new ProceduralObjectSystem(); system.Objects.Add(81,obj);
        var key = new HistoryPartKey(new HistoryObjectKey(obj.Id,obj.PersistentId),part.PartHistoryId,0);
        var beforeSelection = new TopologySelection(1,new[] {new TopologySelectionItem(key,0,1)});
        TopologySelection selection = beforeSelection;
        var context = new HistoryContext(system,null,o => selection,(o,s) => selection=s);
        using (var history = new ActionHistory(context)) {
            var before = TopologyState.Capture(part);
            var mutation = Build(part, AllEdges(part), out var children);

            using (var edit = new HistoryEdit(context,history.TryBegin(),LocalizedText.Diagnostic("Subdivide Edges"),
                new List<HistoryCaptureAction> {new TopologyAction(obj,0,mutation,LocalizedText.Diagnostic("Subdivide Edges"),context)},null)) {
                part.ApplyTopologyMutation(mutation);
                selection = new TopologySelection(1,ResolveChildren(part,children)
                    .Select(id => new TopologySelectionItem(key,id,1)).ToArray());
                Check(edit.Complete(), "Real topology action commits");
            }
            var afterSelection = selection;
            var after = TopologyState.Capture(part);
            Check(history.Count == 1, "One entry");
            for (int i = 0; i < 8; i++) {
                Check(history.Request(HistoryDirection.Undo), "Undo starts"); history.Poll();
                Same(before,TopologyState.Capture(part),"Exact undo");
                Check(selection == beforeSelection,"Undo selection");
                Check(history.Request(HistoryDirection.Redo), "Redo starts"); history.Poll();
                Same(after,TopologyState.Capture(part),"Exact redo");
                Check(selection == afterSelection,"Redo selection");
            }
            var repeat = Build(part,ResolveChildren(part,children),out _);
            using (var edit = new HistoryEdit(context,history.TryBegin(),LocalizedText.Diagnostic("Cancelled subdivision"),
                new List<HistoryCaptureAction> {new TopologyAction(obj,0,repeat,LocalizedText.Diagnostic("Cancel"),context)},null)) {
                part.ApplyTopologyMutation(repeat); selection=beforeSelection;
            }
            Same(after,TopologyState.Capture(part),"Cancelled commit restores real topology");
            Check(selection == afterSelection && history.Count == 1,"Cancellation restores selection and timeline");
            using (var edit = new HistoryEdit(context,history.TryBegin(),LocalizedText.Diagnostic("Failed subdivision"),
                new List<HistoryCaptureAction> {new TopologyAction(obj,0,repeat,LocalizedText.Diagnostic(""),context)},null)) {
                part.ApplyTopologyMutation(repeat); selection=beforeSelection;
                Check(!edit.Complete(),"Invalid action metadata fails completion");
            }
            Same(after,TopologyState.Capture(part),"Failed completion restores exact state");
            Check(selection == afterSelection && history.Count == 1,"Failed completion restores selection and timeline");
        }
    }

    private static void ExistingOperations() {
        var part = Single(4);
        Check(TopologyEditor.TryCreateSurfaceCutMutation(part,0,0.5f,
            out var mutation,out var inserted,out _),"Existing surface cut builds");
        Check(mutation.RenderToLogical == null,"Existing cut retains old mapping path");
        part.ApplyTopologyMutation(mutation);
        var insertedLogical = inserted.Select(part.Topology.GetLogicalVertexIdForRenderVertex).Distinct().ToArray();
        Check(TopologyEditor.TryCreateSurfaceCutDissolveMutation(part,insertedLogical,
            out var dissolved,out _),"Existing dissolve builds");
        part.ApplyTopologyMutation(dissolved);
        Check(part.StructuralTopology.FaceCount == 1,"Existing dissolve restores one face");
        part = Single(4);
        Check(TopologyEditor.TryCreateVertexMergeMutation(part,new[] {0,1},
            out var merged,out _,out _),"Existing merge builds");
        part.ApplyTopologyMutation(merged);
        Check(part.StructuralTopology.TriangleCount == 1,"Existing merge yields triangle");

        part = Single(4);
        var obj = new ProceduralObject(9,Guid.NewGuid(),part) {Transform=ObjectTransform.IdentityAt(Vector3.zero)};
        Check(FaceRegionAnalyzer.TryAnalyze(obj,0,new[] {true},out var region),"Extrude region analysis");
        Check(FaceExtrusionEditor.TryCreateMutation(part,region,1f,out var extrusion),"Existing extrude builds");
        part.ApplyTopologyMutation(extrusion.MeshMutation);
        Check(part.StructuralTopology.QuadCount==5,"Existing extrude cap and walls");

        part=Make(new[] {new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(0,1,0),
            new Vector3(0,0,1),new Vector3(1,0,1),new Vector3(1,1,1),new Vector3(0,1,1)},new[] {
            new StructuralFace(0,0,3,2,1),new StructuralFace(1,4,5,6,7),
            new StructuralFace(2,0,1,5,4),new StructuralFace(3,1,2,6,5),
            new StructuralFace(4,2,3,7,6),new StructuralFace(5,3,0,4,7)
        },false);
        Check(part.StructuralTopology.TryGetEdgeByLogicalVertices(0,4,out var vertical),"Cube vertical edge");
        Check(TopologyEditor.TryCreateLoopCutMutation(part,vertical.Id,0.5f,
            out var loopMutation,out var loopInserted,out _),"Existing loop cut builds");
        part.ApplyTopologyMutation(loopMutation);
        Check(part.StructuralTopology.QuadCount==10,"Existing loop cut splits four sides");
        Check(TopologyEditor.TryCreateLoopDissolveMutation(part,
            loopInserted.Select(part.Topology.GetLogicalVertexIdForRenderVertex).Distinct().ToArray(),
            out var loopDissolved,out _),"Existing loop dissolve builds");
        part.ApplyTopologyMutation(loopDissolved);
        Check(part.StructuralTopology.QuadCount==6,"Existing loop dissolve restores cube");
    }

    private static void CommandIntegration() {
        var part = Single(4);
        var obj = new ProceduralObject(42,Guid.NewGuid(),part,Single(3));
        var system = new ProceduralObjectSystem(); system.Objects.Add(42,obj);
        var tool = new ProceduralObjectToolSystem {m_ProceduralObjectSystem=system,TestObject=obj};
        var controller = tool.m_MeshEditController;
        system.m_HistoryContext = new HistoryContext(system,null,controller.Capture,controller.Restore,controller.AdoptPreparedTopologySelection);
        using (system.ActionHistory = new ActionHistory(system.m_HistoryContext)) {
            Check(!tool.CanSubdivideSelectedEdges,"Command rejects empty selection");
            controller.ReplaceEdgeSelection(obj,new[] {new StructuralEdgeRef(0,0),new StructuralEdgeRef(1,0)});
            WaitDelete(()=>{tool.TickVertexDelete();return tool.TestTopologyAnalyzed;},"Multi-part selection analysis completes");
            Check(!tool.CanSubdivideSelectedEdges, "Multi-part subdivision is rejected");
            var before = TopologyState.Capture(part);
            tool.SubdivideSelectedEdges();
            Same(before,TopologyState.Capture(part),"Mixed part command atomic");
            controller.ReplaceEdgeSelection(obj,AllEdges(part).Select(id=>new StructuralEdgeRef(0,id)).ToArray());
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanSubdivideSelectedEdges;},"Valid command enabled");
            Check(PartGeometrySnapshot.TryCapture(part, false, out var cageSnapshot, out _), "Cage guard capture");
            var cageReference = UnityEngine.Matrix4x4.Scale(new Vector3(3,3,1));
            obj.SetDeformation(0, DeformationCage.Capture(obj, 0, cageReference, new[] { cageSnapshot }));
            Check(tool.MeshEditRequiresBake, "All Parts displays bake requirement");
            Check(!tool.CanMeshLoopCut && !tool.CanMeshSurfaceCut && !tool.CanMergeSelectedVertices &&
                !tool.CanDissolveSelectedLoop && !tool.CanDissolveSelectedSurfaceCut, "Cage disables topology availability");
            controller.ScopePartIndex = 1;
            Check(!tool.MeshEditRequiresBake, "Unaffected Current Part does not require bake");
            controller.ScopePartIndex = 0;
            Check(tool.MeshEditRequiresBake, "Affected Current Part requires bake");
            controller.ScopePartIndex = -1;
            Check(!tool.CanSubdivideSelectedEdges, "Live cage blocks topology command");
            tool.SubdivideSelectedEdges();
            Same(before, TopologyState.Capture(part), "Blocked cage command leaves mesh unchanged");
            Check(system.BeginVertexHistory(obj, new Dictionary<int,int[]> { [0] = new[] { 0 } }, LocalizedText.Diagnostic("Move")) == null, "Core vertex history guard");
            obj.SetDeformation(0, null);
            Check(!tool.MeshEditRequiresBake, "Bake removes mesh notice");
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanSubdivideSelectedEdges;}, "Bake revision invalidates cached unavailable state without geometry change");
            int snapshots=controller.SnapshotCount;
            for (int i=0;i<100;i++) Check(tool.CanSubdivideSelectedEdges && tool.SubdivideEdgesUnavailableReason.IsEmpty,"Cached enabled state");
            Check(controller.SnapshotCount==snapshots,"Unchanged polling does not collect selection again");
            tool.DeformationCommitBusy=true;
            Check(!tool.CanSubdivideSelectedEdges,"Pending deformation blocks command");
            tool.SubdivideSelectedEdges();
            Same(before,TopologyState.Capture(part),"Busy command unchanged");
            tool.DeformationCommitBusy=false;
            tool.m_IsGizmoDragging=true;
            Check(!tool.CanSubdivideSelectedEdges,"Gizmo blocks command");
            tool.m_IsGizmoDragging=false;
            controller.IsAlignmentTargeting=true;
            Check(!tool.CanSubdivideSelectedEdges,"Alignment blocks command");
            controller.IsAlignmentTargeting=false;
            using (var reservation=system.ActionHistory.TryBegin()) {
                Check(!tool.CanSubdivideSelectedEdges,"Active history transaction blocks command");
                tool.SubdivideSelectedEdges();
            }
            Same(before,TopologyState.Capture(part),"Reserved history unchanged");
            uint topologyVersion=part.TopologyVersion;
            tool.SubdivideSelectedEdges();
            WaitDelete(()=>{tool.TickVertexDelete();return !tool.TopologyEditBusy;},"Subdivision publishes");
            Check(system.ActionHistory.Count==1 && controller.TopologyNotifications==1,"One command, history entry and notification");
            Check(part.TopologyVersion==topologyVersion+1,"One topology publication");
            Check(controller.Edges.Length==8 && controller.SelectionMode==MeshSelectionMode.Edge,"Command selects only eight child halves");
            foreach (var edge in controller.Edges) {
                Check(part.StructuralTopology.GetEdge(edge.EdgeId).AdjacentFaceCount==1,"Center spokes not selected");
            }
            var after=TopologyState.Capture(part);
            Check(system.ActionHistory.Request(HistoryDirection.Undo) && system.ActionHistory.CancelReplay(), "Cancel topology undo");
            WaitDelete(()=>{system.ActionHistory.Poll();return !system.ActionHistory.IsBusy;},"Cancelled topology undo drains");
            Check(system.ActionHistory.Count == 1 && system.ActionHistory.Cursor == 1 && system.ActionHistory.LastFailure.Length == 0,
                "Cancelled topology undo preserves history");
            Same(after, TopologyState.Capture(part), "Cancelled topology undo leaves geometry alone");
            Check(controller.Edges.Length == 8, "Cancelled topology undo leaves selection alone");
            Check(system.ActionHistory.Request(HistoryDirection.Undo),"Command undo"); WaitDelete(()=>{system.ActionHistory.Poll();return !system.ActionHistory.IsBusy;},"Command undo completes");
            Same(before,TopologyState.Capture(part),"Command undo exact");
            Check(controller.Edges.Length==4,"Command undo selection");
            Check(system.ActionHistory.Request(HistoryDirection.Redo) && system.ActionHistory.CancelReplay(), "Cancel topology redo");
            WaitDelete(()=>{system.ActionHistory.Poll();return !system.ActionHistory.IsBusy;},"Cancelled topology redo drains");
            Same(before, TopologyState.Capture(part), "Cancelled topology redo leaves geometry alone");
            Check(system.ActionHistory.Count == 1 && system.ActionHistory.Cursor == 0 && controller.Edges.Length == 4,
                "Cancelled topology redo leaves history and selection alone");
            Check(system.ActionHistory.Request(HistoryDirection.Redo),"Command redo"); WaitDelete(()=>{system.ActionHistory.Poll();return !system.ActionHistory.IsBusy;},"Command redo completes");
            Same(after,TopologyState.Capture(part),"Command redo exact");
            Check(controller.Edges.Length==8,"Command redo selection");
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanSubdivideSelectedEdges;},"Repeat available");
            tool.SubdivideSelectedEdges();
            WaitDelete(()=>{tool.TickVertexDelete();return !tool.TopologyEditBusy;},"Subdivision publishes");
            Check(system.ActionHistory.Count==2 && controller.Edges.Length==16,"Command repeat subdivides child halves");
            tool.TestObject=null;
            Check(!tool.CanSubdivideSelectedEdges,"Editor exit invalidates availability");
        }
    }

    private static void ReversedWinding() {
        var part=Make(new[] {new Vector3(0,0,0),new Vector3(0,2,0),new Vector3(2,2,0),new Vector3(2,0,0)},
            new[] {new StructuralFace(0,0,1,2,3)});
        part.ApplyTopologyMutation(Build(part,AllEdges(part),out _));
        for (int i=0;i<part.Mesh.Triangles.Length;i+=3) {
            int a=part.Mesh.Triangles[i],b=part.Mesh.Triangles[i+1],c=part.Mesh.Triangles[i+2];
            Check(Vector3.Cross(part.Mesh.Vertices[b]-part.Mesh.Vertices[a],part.Mesh.Vertices[c]-part.Mesh.Vertices[a]).z<0,
                "Reversed source winding retained");
        }
    }

    private static void FoldedQuad() {
        var part=Make(new[] {new Vector3(0,0,0),new Vector3(2,0,0),new Vector3(2,2,0),new Vector3(1,0,0.5f)},
            new[] {new StructuralFace(0,0,1,2,3)});
        var source=part.Mesh.Clone();
        part.ApplyTopologyMutation(Build(part,AllEdges(part),out _));
        VerifyInterpolatedSurface(source,part.Mesh);
        Check(part.StructuralTopology.QuadCount==4,"Strongly folded valid quad retains source surface");
    }

    private static MeshPart Grid(int size) {
        var vertices = new Vector3[(size+1)*(size+1)];
        for (int y=0;y<=size;y++) for (int x=0;x<=size;x++) vertices[y*(size+1)+x]=new Vector3(x,y,0);
        var faces = new StructuralFace[size*size];
        for (int y=0;y<size;y++) for (int x=0;x<size;x++) {
            int a=y*(size+1)+x, id=y*size+x;
            faces[id]=new StructuralFace(id,a,a+1,a+size+2,a+size+1);
        }
        return Make(vertices,faces,false);
    }

}
