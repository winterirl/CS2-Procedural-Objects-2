using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PO2;
using UnityEngine;
using PO2.Core;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.History;
using PO2.Materials;
using PO2.Persistence.Format;
using PO2.Persistence.Model;
using PO2.Rendering;
using PO2.Rendering.Objects;
using PO2.Tools.MeshEdit;
using PO2.Tools.ObjectTool;

internal static partial class Program {
    private static MeshPart AsConverted(MeshPart part,
        StructuralTopologySource kind = PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred,
        StructuralFace[] faces = null, int[] triangles = null, int[] memberships = null) {
        foreach (var area in part.SurfaceAreas) part.SetSurfaceAreaMappingMode(area.Id,TextureMappingMode.Original);
        var d = PersistencePartData.Capture(part);
        return PersistencePartData.FromDecoded(d.Name, d.Vertices, d.Normals, d.Tangents, d.UV0, d.VertexColors, d.TexCoord1, d.TexCoord3,
            triangles ?? d.Triangles, d.RenderToLogical, d.LogicalVertexCount, faces ?? d.StructuralFaces, kind,
            d.SurfaceAreas, memberships ?? d.TriangleSurfaceAreaIds, d.NextSurfaceAreaId, d.LocalPivot).Restore();
    }
    private static MeshPart ConvertedQuad(StructuralTopologySource kind, bool streams = true) {
        var p = Make(new[] {Vector3.zero,new Vector3(2,0,0),new Vector3(2,2,0),new Vector3(0,2,0)},
            new[] {new StructuralFace(0,0,1,2,3)}, streams);
        if (streams) for (int i = 0; i < 4; i++) {
            var v = p.Mesh.Vertices[i];
            p.Mesh.Normals[i] = Vector3.forward;
            p.Mesh.Tangents[i] = new Vector4(1,0,0,-1);
            p.Mesh.UV0[i] = new Vector4(v.x,v.y,v.x + v.y,9);
            p.Mesh.VertexColors[i] = new Color32((byte)(v.x * 40),(byte)(v.y * 40),120,255);
        }
        return AsConverted(p, kind);
    }
    private static MeshPart ConvertedResult(MeshPart part, int[] edges,
        out EdgeSubdivisionPlan plan, out SubdivisionTriangleSource[] parents) {
        Check(SubdivisionTestAdapter.Analyze(part,edges,out plan,out var reason), "Converted analysis: " + reason);
        var mutation = TopologyEditor.CreateEdgeSubdivisionMutation(plan,CancellationToken.None,out var children,out parents);
        var resultData = TopologyEditor.PrepareTopology(plan.Source,mutation);
        var result = resultData.Restore();
        Check(result.StructuralTopologySource == part.StructuralTopologySource && !result.SupportsDestructiveTopologyEditing,
            "Subdivision preserves converted source and other tool restrictions");
        Check(result.Mesh.Vertices.Take(part.Mesh.Vertices.Length).SequenceEqual(part.Mesh.Vertices), "Original position prefix exact");
        Check(result.Mesh.Normals == null ? part.Mesh.Normals == null : result.Mesh.Normals.Take(part.Mesh.Normals.Length).SequenceEqual(part.Mesh.Normals), "Original normals exact");
        Check(result.Mesh.UV0 == null ? part.Mesh.UV0 == null : result.Mesh.UV0.Take(part.Mesh.UV0.Length).SequenceEqual(part.Mesh.UV0), "Original UVs exact");
        Check(result.NextSurfaceAreaId == part.NextSurfaceAreaId && result.SurfaceAreas.Count == part.SurfaceAreas.Count, "All area records and next id retained");
        Check(result.LocalPivot.Equals(part.LocalPivot), "Local pivot retained");
        ResolveChildren(result, children);
        var before = TopologyState.Capture(part); var after = TopologyState.Capture(resultData);
        using (var owner = new DeleteDeltaOwner(before,after,mutation.HistoryOldToNew)) {
            var delta = owner.Value;
            Same(before,delta.Apply(after,HistoryDirection.Undo),"Converted exact undo");
            Same(after,delta.Apply(before,HistoryDirection.Redo),"Converted exact redo");
        }
        var obj = new ProceduralObject(2001,Guid.NewGuid(),result);
        var restored = ObjectBlockReader.Read(ObjectBlockWriter.Write(PersistenceObjectData.Capture(obj)),SnapshotFormat.kObjectSchemaVersion).Restore(2002);
        Same(after,TopologyState.Capture(restored.Parts[0]),"Converted binary save/load");
        Check(restored.Parts[0].StructuralTopology.StructuralEdgeCount == result.StructuralTopology.StructuralEdgeCount,"Persisted quad diagonals remain suppressed");
        return result;
    }
    private static TopologyRendererPreparation ConvertedRender(MeshPart part, MeshPart candidate,
        EdgeSubdivisionPlan plan, SubdivisionTriangleSource[] parents, Vector3 scale) {
        var initial = InitialRenderer(part, scale);
        var work = new TopologyRendererWork { Scale = scale };
        foreach (var area in initial.Areas) {
            area.Settings.Current = area.Runtime; area.Settings.RuntimeToPart = area.RuntimeToPart;
            work.Areas.Add(area.Settings.Id, area.Settings);
        }
        return (TopologyRendererPreparation)work.Build(candidate,plan.Source,null,new SubdivisionAppearanceCheck(plan,parents),CancellationToken.None);
    }
    private static void ConvertedSubdivisionTests() {
        foreach (var kind in new[] {PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred,PO2.Geometry.Topology.StructuralTopologySource.ConvertedTriangleBaseline}) {
            for (int mask = 1; mask < 16; mask++) {
                var part = ConvertedQuad(kind); var edges = Enumerable.Range(0,4).Where(c => (mask & (1 << c)) != 0)
                    .Select(c => part.StructuralTopology.GetBoundaryEdgeId(0,c)).ToArray();
                var result = ConvertedResult(part,edges,out var plan,out var parents);
                Check(result.StructuralTopology.QuadCount == (mask == 15 ? 4 : mask == 5 || mask == 10 ? 2 : 0), "Safe converted quad mask " + mask);
                ConvertedRender(part,result,plan,parents,Vector3.one);
                var reversed = ConvertedResult(part,edges.Reverse().ToArray(),out _,out _);
                Same(TopologyState.Capture(result),TopologyState.Capture(reversed),"Selection order is deterministic");
            }
            for (int mask = 1; mask < 8; mask++) {
                var part = AsConverted(Single(3),kind);
                var edges = Enumerable.Range(0,3).Where(c => (mask & (1 << c)) != 0).Select(c => part.StructuralTopology.GetBoundaryEdgeId(0,c)).ToArray();
                var result = ConvertedResult(part,edges,out var plan,out var parents);
                Check(result.Mesh.Triangles.Length / 3 == 1 + edges.Length,"Converted triangle masks");
                ConvertedRender(part,result,plan,parents,Vector3.one);
            }
            foreach (int field in Enumerable.Range(0,5)) {
                var part = ConvertedQuad(kind);
                if (field == 0) part.Mesh.Vertices[3].z = 0.03f;
                if (field == 1) part.Mesh.UV0[3].z += 1;
                if (field == 2) part.Mesh.Normals[3].x = 0.1f;
                if (field == 3) part.Mesh.Tangents[3].y = 0.1f;
                if (field == 4) part.Mesh.VertexColors[3] = new Color32(120,80,120,255);
                part.MarkGeometryChanged();
                var result = ConvertedResult(part,AllEdges(part),out var plan,out var parents);
                Check(!plan.Regions[0].QuadTemplate && result.StructuralTopology.QuadCount == 0,"Non-affine/folded quad retains diagonal " + field);
                Check(parents.All(p => p.QuadTriangle < 0),"Fallback triangle provenance");
                ConvertedRender(part,result,plan,parents,Vector3.one);
            }
        }
        ConvertedSubdivisionTrianglePairs();
        ConvertedRawAndSeams();
        ConvertedSubdivisionGuards();
        ConvertedSubdivisionRendererAndCommand();
        ConvertedSelectionCaptureTests();
        ConvertedSubdivisionCancellationAndRejection();
        ConvertedSubdivisionPayloads();
    }
    private static int ConvertedEdge(MeshPart part, int a, int b) {
        Check(part.StructuralTopology.TryGetEdgeByLogicalVertices(a,b,out var edge), "Converted boundary edge exists");
        return edge.Id;
    }
    private static MeshPart ConvertedPlanarStreams(MeshPart part) {
        for (int i = 0; i < part.Mesh.Vertices.Length; i++) {
            var v = part.Mesh.Vertices[i];
            part.Mesh.Normals[i] = Vector3.forward;
            part.Mesh.Tangents[i] = new Vector4(1,0,0,-1);
            part.Mesh.UV0[i] = new Vector4(v.x,v.y,0,0);
            part.Mesh.VertexColors[i] = new Color32(80,80,80,255);
        }
        return part;
    }
    private static void ConvertedSubdivisionTrianglePairs() {
        foreach (var kind in new[] {PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred,PO2.Geometry.Topology.StructuralTopologySource.ConvertedTriangleBaseline}) {
            foreach (bool reverse in new[] {false,true}) foreach (bool thin in new[] {false,true}) foreach (int mask in new[] {5,10,15}) {
                var quad = ConvertedQuad(kind);
                if (thin) for (int v = 0; v < 4; v++) quad.Mesh.Vertices[v] = new Vector3(quad.Mesh.Vertices[v].x * 100,quad.Mesh.Vertices[v].y * 0.01f,0);
                var faces = reverse ? new[] {new StructuralFace(0,1,3,0),new StructuralFace(1,1,2,3)} :
                    new[] {new StructuralFace(0,0,1,2),new StructuralFace(1,0,2,3)};
                var triangles = faces.SelectMany(f => Enumerable.Range(0,3).Select(f.GetRenderVertexIndex)).ToArray();
                var part = AsConverted(quad,kind,faces,triangles);
                var edges = Enumerable.Range(0,4).Where(i => (mask & (1 << i)) != 0).Select(i => ConvertedEdge(part,i,(i + 1) % 4)).ToArray();
                var result = ConvertedResult(part,edges,out var plan,out var parents);
                Check(result.StructuralTopology.QuadCount == (mask == 15 ? 4 : 2), "Triangle pair creates clean quads, including thin wall panels");
                Check(plan.Regions.Length == 1 && plan.Regions[0].SecondFaceId >= 0,"Only the selected triangle pair is grouped");
                if (mask != 15) Check(result.StructuralTopology.TryGetEdgeByLogicalVertices(4,5,out _),"Opposite edge midpoints have a direct connecting edge");
                ConvertedRender(part,result,plan,parents,Vector3.one);
                var reordered = ConvertedResult(part,edges.Reverse().ToArray(),out _,out _);
                Same(TopologyState.Capture(result),TopologyState.Capture(reordered),"Paired subdivision selection order is deterministic");
            }
            // The selected edges border neighboring wall panels, as in the reported case.
            var wallFaces = Enumerable.Range(0,3).SelectMany(i => new[] {
                new StructuralFace(i * 2,i * 2,i * 2 + 1,i * 2 + 3),
                new StructuralFace(i * 2 + 1,i * 2,i * 2 + 3,i * 2 + 2)
            }).ToArray();
            var wallVertices = Enumerable.Range(0,8).Select(i => new Vector3(i % 2 * 10,i / 2,0)).ToArray();
            var wall = AsConverted(ConvertedPlanarStreams(Make(wallVertices,wallFaces)),kind);
            var cut = ConvertedResult(wall,new[] {ConvertedEdge(wall,2,3),ConvertedEdge(wall,4,5)},out var wallPlan,out var wallParents);
            Check(cut.StructuralTopology.QuadCount == 2 && cut.StructuralTopology.TriangleCount == 6,"Middle wall gets two quads and adjoining panels stay conforming");
            Check(cut.StructuralTopology.TryGetEdgeByLogicalVertices(8,9,out _),"Wall cut connects selected midpoints");
            ConvertedRender(wall,cut,wallPlan,wallParents,Vector3.one);
        }
        foreach (int guard in Enumerable.Range(0,7)) {
            var quad = ConvertedQuad(PO2.Geometry.Topology.StructuralTopologySource.ConvertedTriangleBaseline);
            if (guard == 0) quad.Mesh.UV0[3].z += 1;
            if (guard == 1) quad.Mesh.Vertices[3].z = 0.1f;
            var part = AsConverted(quad,quad.StructuralTopologySource,new[] {
                new StructuralFace(0,0,1,2),new StructuralFace(1,0,2,3)
            });
            int[] edges = guard == 2 ? new[] {ConvertedEdge(part,0,1)} : guard == 3 ?
                new[] {ConvertedEdge(part,0,1),ConvertedEdge(part,0,3)} : guard == 4 ? AllEdges(part) :
                new[] {ConvertedEdge(part,0,1),ConvertedEdge(part,2,3)};
            if (guard == 5) {
                part = AsConverted(ConvertedPlanarStreams(Make(new[] {Vector3.zero,Vector3.right,new Vector3(1,1,0),Vector3.zero,new Vector3(1,1,0),Vector3.up},
                    new[] {new StructuralFace(0,0,1,2),new StructuralFace(1,3,4,5)},true,new[] {0,1,2,0,2,3})));
                edges = new[] {ConvertedEdge(part,0,1),ConvertedEdge(part,2,3)};
            }
            if (guard == 6) {
                part = AsConverted(ConvertedPlanarStreams(Make(quad.Mesh.Vertices,new[] {
                    new StructuralFace(0,0,1,2),new StructuralFace(1,0,2,3)
                },true,null,new[] {0,1})));
                edges = new[] {ConvertedEdge(part,0,1),ConvertedEdge(part,2,3)};
            }
            var result = ConvertedResult(part,edges,out var plan,out var parents);
            Check(plan.Regions.All(r => r.SecondFaceId < 0) && result.StructuralTopology.QuadCount == 0,"Pairing keeps texture/fold/seam boundaries and explicit diagonal selection: " + guard);
            ConvertedRender(part,result,plan,parents,Vector3.one);
        }
        var ambiguous = AsConverted(ConvertedPlanarStreams(Make(new[] {
            Vector3.zero,new Vector3(2,0,0),new Vector3(1,1,0),new Vector3(1,-1,0),new Vector3(3,1,0)
        },new[] {new StructuralFace(0,0,1,2),new StructuralFace(1,1,0,3),new StructuralFace(2,2,1,4)})));
        var unchangedPairing = ConvertedResult(ambiguous,new[] {ConvertedEdge(ambiguous,2,0),ConvertedEdge(ambiguous,3,1),ConvertedEdge(ambiguous,1,4)},out var ambiguousPlan,out var ambiguousParents);
        Check(ambiguousPlan.Regions.All(r => r.SecondFaceId < 0),"Ambiguous neighbors do not arbitrarily merge");
        ConvertedRender(ambiguous,unchangedPairing,ambiguousPlan,ambiguousParents,Vector3.one);
    }
    private static void ConvertedRawAndSeams() {
        var part = DeleteFixture(PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred,true);
        // Unrelated raw triangles are deliberately degenerate and retain their exact records.
        part.Mesh.Vertices[6] = part.Mesh.Vertices[5]; part.MarkGeometryChanged();
        var result = ConvertedResult(part,new[] {part.StructuralTopology.GetBoundaryEdgeId(0,0)},out _,out var parents);
        Check(result.Mesh.Triangles.Skip(result.Mesh.Triangles.Length - 6).SequenceEqual(part.Mesh.Triangles.Skip(6)),"Skipped raw records survive verbatim");
        Check(parents.Skip(parents.Length - 2).Select(p => p.Triangle).SequenceEqual(new[] {2,3}),"Unchanged raw provenance");
        var baseQuad = ConvertedQuad(PO2.Geometry.Topology.StructuralTopologySource.ConvertedTriangleBaseline);
        part = AsConverted(baseQuad,PO2.Geometry.Topology.StructuralTopologySource.ConvertedTriangleBaseline,Array.Empty<StructuralFace>());
        Check(part.StructuralTopology.FaceCount == 0,"Raw fixture has no stored coverage");
        Check(part.StructuralTopology.StructuralEdgeCount == 0,"Unrepresented triangles do not invent selectable edges");
        part = AsConverted(baseQuad,PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred,new[] {new StructuralFace(0,0,1,2)});
        Check(part.StructuralTopology.TryGetEdgeByLogicalVertices(0,2,out var shared),"Raw/stored shared edge");
        result = ConvertedResult(part,new[] {shared.Id},out _,out _);
        Check(result.Mesh.Triangles.Length == 12,"Both raw and stored sides split");
        part = AsConverted(baseQuad,PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred,null,new[] {0,2,3,0,1,2});
        result = ConvertedResult(part,new[] {part.StructuralTopology.GetBoundaryEdgeId(0,0)},out var orderPlan,out var orderParents);
        ConvertedRender(part,result,orderPlan,orderParents,Vector3.one);
        Check(result.Mesh.Triangles.Length == 9,"Face resolver tolerates non-face triangle order");
        foreach (bool diverged in new[] {false,true}) {
            part = AsConverted(TwoQuads(true));
            if (diverged) { part.Mesh.Vertices[4].z += 0.02f; part.Mesh.Vertices[7].z += 0.02f; part.MarkGeometryChanged(); }
            result = ConvertedResult(part,new[] {part.StructuralTopology.GetBoundaryEdgeId(0,1)},out var seamPlan,out var seamParents);
            Check(result.Mesh.Vertices.Length == part.Mesh.Vertices.Length + 2,"Distinct render midpoint at seam");
            Check(result.Topology.GetLogicalVertexIdForRenderVertex(8) == result.Topology.GetLogicalVertexIdForRenderVertex(9),"One logical midpoint across seam");
            if (diverged) Check(result.Mesh.Vertices[8].z != result.Mesh.Vertices[9].z,"Separated members stay separated");
            ConvertedRender(part,result,seamPlan,seamParents,Vector3.one);
        }
        foreach (float scale in new[] {0.000001f,1000000f}) {
            part = ConvertedQuad(PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred);
            for (int i = 0; i < 4; i++) part.Mesh.Vertices[i] *= scale;
            part.MarkGeometryChanged();
            result = ConvertedResult(part,AllEdges(part),out var plan,out var source);
            ConvertedRender(part,result,plan,source,Vector3.one);
        }
        part = ConvertedQuad(PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred);
        for (int i = 0; i < 4; i++) part.Mesh.Vertices[i] += new Vector3(100000,100000,100000);
        part.MarkGeometryChanged(); ConvertedResult(part,AllEdges(part),out _,out _);
        part = ConvertedQuad(PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred,false);
        result = ConvertedResult(part,AllEdges(part),out _,out _);
        Check(result.Mesh.UV0 == null && result.Mesh.Tangents == null && result.Mesh.TexCoord3 == null && result.Mesh.VertexColors == null,"Absent optional streams stay absent");
    }
    private static void ConvertedSubdivisionGuards() {
        var part = ConvertedQuad(PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred);
        part.SetSurfaceAreaSourceVertexAttributes(0,VertexAttributeFlags.UV0FourComponent | VertexAttributeFlags.VertexColor | VertexAttributeFlags.TexCoord3);
        ConvertedResult(part,AllEdges(part),out _,out _);
        part.Mesh.TexCoord3[1].x = 1; Reject(part,AllEdges(part),"Varying categorical payload rejected");
        part = ConvertedQuad(PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred);part.Mesh.Tangents[1].w = 1;
        Reject(part,AllEdges(part),"Converted mixed handedness rejected");
        part = AsConverted(Make(new[] {Vector3.zero,Vector3.right,Vector3.up,-Vector3.up},new[] {
            new StructuralFace(0,0,1,2),new StructuralFace(1,0,1,3)},false));
        Reject(part,new[] {part.StructuralTopology.GetBoundaryEdgeId(0,0)},"Inconsistent incidence rejected");
        part = AsConverted(Make(new[] {Vector3.zero,Vector3.right,Vector3.up},new[] {
            new StructuralFace(0,0,1,2),new StructuralFace(1,0,1,2)},false));
        Reject(part,AllEdges(part),"Duplicate triangle fan rejected");
        bool rejected = false;
        try { new TopologySize(3,3,3,1,-1,6,12,4); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected,"Negative topology output counts remain invalid");
        var size = new TopologySize(3,3,3,1,6,6,12,4);
        Check(size.MaximumVertices == 6 && size.MaximumIndices == 12,"Replay remembers the larger mesh shape");

    }
    private static void ConvertedSubdivisionRendererAndCommand() {
        foreach (var mode in new[] {TextureMappingMode.Original,TextureMappingMode.Repeat,TextureMappingMode.Stretch}) {
            foreach (var scale in new[] {Vector3.one,new Vector3(2,3,1),new Vector3(-2,3,1)}) {
                var part = ConvertedQuad(PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred);part.SetSurfaceAreaMappingMode(0,mode);
                var result = ConvertedResult(part,AllEdges(part),out var plan,out var parents);
                var prepared = ConvertedRender(part,result,plan,parents,scale);
                var reload = InitialRenderer(PersistencePartData.Capture(result).Restore(),scale);
                Check(prepared.Combined.UV0.SequenceEqual(reload.Combined.UV0),"Subdivision reload mapping " + mode);
            }
        }
        foreach (bool paired in new[] {false,true}) {
            var target = ConvertedQuad(PO2.Geometry.Topology.StructuralTopologySource.ConvertedTriangleBaseline);
            if (paired) target = AsConverted(target,target.StructuralTopologySource,new[] {
                new StructuralFace(0,0,1,2),new StructuralFace(1,0,2,3)
            });
            int[] selected = paired ? new[] {ConvertedEdge(target,0,1),ConvertedEdge(target,2,3)} : AllEdges(target);
            var obj = new ProceduralObject(2201,Guid.NewGuid(),target);obj.Transform.Scale = Vector3.one;
            var system = new ProceduralObjectSystem();system.Objects.Add(obj.Id,obj);
            var tool = new ProceduralObjectToolSystem {TestObject=obj,m_ProceduralObjectSystem=system};var controller=tool.m_MeshEditController;
            controller.ReplaceEdgeSelection(obj,selected.Select(id=>new StructuralEdgeRef(0,id)).ToArray());
            system.m_HistoryContext = new HistoryContext(system,null,controller.Capture,controller.Restore,controller.AdoptPreparedTopologySelection);
            var renderer = new ProceduralObjectRenderer();renderer.TestInitialize(obj,InitialRenderer(target,Vector3.one));system.TestTopologyRenderer=renderer;
            var before=TopologyState.Capture(target);var visible=renderer.TestMesh(obj);
            using (system.ActionHistory=new ActionHistory(system.m_HistoryContext)) {
                WaitDelete(()=>{tool.TickVertexDelete();return tool.CanSubdivideSelectedEdges;},"Converted command available");
                int snapshots=controller.SnapshotCount;
                for(int i=0;i<100;i++) Check(tool.CanSubdivideSelectedEdges,"Cached converted eligibility");
                Check(snapshots==controller.SnapshotCount,"Getters do not recapture large selection");
                tool.SubdivideSelectedEdges();tool.CancelVertexDelete();
                WaitDelete(()=>{tool.TickVertexDelete();return !tool.TopologyEditBusy;},"Converted command cancel");
                Same(before,TopologyState.Capture(target),"Cancelled converted edit unchanged");
                WaitDelete(()=>{tool.TickVertexDelete();return tool.CanSubdivideSelectedEdges;},"Converted retry after cancel");
                tool.SubdivideSelectedEdges();
                WaitDelete(()=>{tool.TickVertexDelete();renderer.PollTopologyCleanup();return !tool.TopologyEditBusy;},"Converted staged commit");
                Check(system.ActionHistory.Count==1 && controller.Edges.Length==selected.Length * 2 && renderer.TestVersionMatches(obj),"Model, renderer, history and child selection committed");
                if (paired) Check(target.StructuralTopology.QuadCount==2,"Paired command publishes two clean quads");
                renderer.PollTopologyCleanup(true);Check(visible.Destroyed,"Old renderer retired");
                var after=TopologyState.Capture(target);
                foreach(var direction in new[] {HistoryDirection.Undo,HistoryDirection.Redo}) {
                    Check(system.ActionHistory.Request(direction),"Converted prepared history request");
                    WaitDelete(()=>{system.ActionHistory.Poll();renderer.PollTopologyCleanup();return !system.ActionHistory.IsBusy;},"Converted resident replay");
                    Check(string.IsNullOrEmpty(system.ActionHistory.LastFailure),"Converted replay succeeds");
                    Same(direction==HistoryDirection.Undo?before:after,TopologyState.Capture(target),"Converted resident replay exact");
                    Check(controller.Edges.Length==selected.Length * (direction==HistoryDirection.Undo?1:2),"Converted replay selection");
                }
            }
            tool.StopVertexDelete();renderer.PollTopologyCleanup(true);renderer.TestDispose();
            Check(renderer.TestMaterialReferences==0,"Converted teardown releases materials");
        }
    }
    private static void ConvertedSelectionCaptureTests() {
        var part = Grid(100);var obj = new ProceduralObject(2301,Guid.NewGuid(),part);
        var edges = AllEdges(part).Select(id=>new StructuralEdgeRef(0,id)).ToArray();
        using (var capture = new TopologySelectionCapture(obj,edges.Length,edges)) {
            capture.Start(CancellationToken.None);
            int slices=0;
            WaitDelete(()=>{slices++;return capture.Poll(()=>true,CancellationToken.None);},"Broad edge selection capture");
            Check(slices>2 && capture.Items.Length==edges.Length,"Broad selection uses bounded slices");
            Check(capture.Copy.CopiedBytes==16L*edges.Length,"Selection capture byte size");
        }
        var key = new HistoryPartKey(new HistoryObjectKey(obj.Id,obj.PersistentId),part.PartHistoryId,0);
        var selection = new TopologySelection(1,edges.Select(e=>new TopologySelectionItem(key,e.EdgeId,1)).ToArray());
        var prepared = Task.Run(()=>new TopologyPreparedSelection(selection,key,0,part,CancellationToken.None)).GetAwaiter().GetResult();
        Check(prepared.Edges.Count==edges.Length && prepared.Endpoints.Count==part.Topology.LogicalVertexCount,"Worker prepares complete broad child/endpoints selection");
        Check(prepared.HasCentroid && prepared.LocalCentroid.Equals(new Vector3(50,50,0)),"Worker prepares broad selection centroid");
        var vertices = new VertexSelection();var selected = new StructuralEdgeSelection();
        selected.AdoptPrepared(prepared.Edges);vertices.AdoptPrepared(prepared.Endpoints);
        Check(ReferenceEquals(selected.Edges,prepared.Edges) && ReferenceEquals(vertices.Vertices,prepared.Endpoints),"Selection adoption is a reference swap");
        using (var capture = new TopologySelectionCapture(obj,edges.Length,edges)) {
            capture.Start(CancellationToken.None);bool stale=false;
            try {capture.Poll(()=>false,CancellationToken.None);}catch(InvalidOperationException){stale=true;}
            Check(stale,"Stale selection capture aborts before reading live edges");
        }
    }
    private static void ConvertedSubdivisionCancellationAndRejection() {
        var part=ConvertedQuad(PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred);
        var obj=new ProceduralObject(2501,Guid.NewGuid(),part);obj.Transform.Scale=Vector3.one;
        var system=new ProceduralObjectSystem();system.Objects.Add(obj.Id,obj);
        var renderer=new ProceduralObjectRenderer();renderer.TestInitialize(obj,InitialRenderer(part,Vector3.one));system.TestTopologyRenderer=renderer;
        var before=TopologyState.Capture(part);var visible=renderer.TestMesh(obj);
        var candidate=ConvertedResult(part,AllEdges(part),out var plan,out var parents);
        foreach(bool ready in new[] {false,true}) {
            using(var cancellation=new CancellationTokenSource()) {
                var preparation=new TopologyPreparation(obj,0,new TopologyTargetStamp(obj,0),plan.Source,system,cancellation.Token,
                    (data,token)=>new TopologyPreparedResult {Part=candidate,Appearance=new SubdivisionAppearanceCheck(plan,parents)},size:plan.Size);
                using(preparation) {
                    WaitDelete(()=>{bool complete=preparation.Poll();return ready ? complete : renderer.TestMaterialReferences>part.SurfaceAreas.Count;},"Subdivision reaches hidden staging");
                    Check(ReferenceEquals(visible,renderer.TestMesh(obj)),"Visible mesh stays unchanged before commit");
                    cancellation.Cancel();bool rejected=false;
                    try {if(ready)preparation.Publish(()=>true);else preparation.Poll();}catch(OperationCanceledException){rejected=true;}
                    Check(rejected,"Cancellation wins at staging or ready-to-publish boundary");
                }
                WaitDelete(()=>preparation.Drain.IsCompleted,"Cancelled subdivision drains workers");
            }
            renderer.PollTopologyCleanup(true);
            Same(before,TopologyState.Capture(part),"Staging cancellation leaves model exact");
            Check(renderer.TestMaterialReferences==part.SurfaceAreas.Count && visible.ReadLeases==0,"Cancellation releases materials and read leases");
        }
        renderer.TestDispose();
        part.SetSurfaceAreaMappingMode(0,TextureMappingMode.Repeat);
        renderer=new ProceduralObjectRenderer();renderer.TestInitialize(obj,InitialRenderer(part,Vector3.one));system.TestTopologyRenderer=renderer;
        var tool=new ProceduralObjectToolSystem {TestObject=obj,m_ProceduralObjectSystem=system};var controller=tool.m_MeshEditController;
        controller.ReplaceEdgeSelection(obj,new[] {new StructuralEdgeRef(0,part.StructuralTopology.GetBoundaryEdgeId(0,0))});
        system.m_HistoryContext=new HistoryContext(system,null,controller.Capture,controller.Restore,controller.AdoptPreparedTopologySelection);
        using(system.ActionHistory=new ActionHistory(system.m_HistoryContext)) {
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanSubdivideSelectedEdges;},"Generated chart candidate analyzed");
            before=TopologyState.Capture(part);tool.SubdivideSelectedEdges();
            WaitDelete(()=>{tool.TickVertexDelete();return !tool.TopologyEditBusy;},"Generated chart rejection finishes");
            WaitDelete(()=>{tool.TickVertexDelete();return tool.TestTopologyDrained;},"Rejected worker drains");
            Check(!tool.CanSubdivideSelectedEdges,"Subdivision caches generated chart rejection");
            Check(tool.TopologyEditStatus.IsEmpty,"Cached rejection has a single message owner");
            tool.SubdivideSelectedEdges();Check(!tool.TopologyEditBusy && system.ActionHistory.Count==0,"Rejected subdivision cannot restart unchanged");
            Same(before,TopologyState.Capture(part),"Rejected chart leaves model unchanged");
            controller.ReplaceEdgeSelection(obj,AllEdges(part).Select(id=>new StructuralEdgeRef(0,id)).ToArray());
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanSubdivideSelectedEdges;},"Selection change clears chart rejection");
            Check(tool.TopologyEditStatus.IsEmpty && tool.SubdivideEdgesUnavailableReason.IsEmpty,"Changing edges clears all prior failure messages");
            tool.SubdivideSelectedEdges();obj.Transform.Scale=new Vector3(2,1,1);
            WaitDelete(()=>{tool.TickVertexDelete();return !tool.TopologyEditBusy;},"Scale change cancels subdivision");
            Same(before,TopologyState.Capture(part),"Stale scale cannot publish");obj.Transform.Scale=Vector3.one;
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanSubdivideSelectedEdges;},"Scale reset reanalyzes");
            tool.SubdivideSelectedEdges();part.SetSurfaceAreaTextureRotation(0,15f);
            WaitDelete(()=>{tool.TickVertexDelete();return !tool.TopologyEditBusy;},"Mapping change cancels subdivision");
            Check(system.ActionHistory.Count==0,"Stale mapping has no history");
        }
        tool.StopVertexDelete();renderer.TestDispose();
    }
    private static void ConvertedSubdivisionPayloads() {
        foreach(var kind in new[] {NativeWindowKind.Window,NativeWindowKind.FrostedWindow}) {
            var part=ConvertedQuad(PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred);
            int area=part.CreateSurfaceArea(MaterialData.CreateDefault(),VertexAttributeFlags.None,new ColorMaskData(),null,kind);
            part.AssignTrianglesToSurfaceArea(new[] {0,1},area);Reject(part,AllEdges(part),"Native window interpolation rejected " + kind);
        }
        var mixed=AsConverted(TwoQuads(true));mixed.SetSurfaceAreaVanillaSpecialMaterial(1,VanillaSpecialMaterialKind.Glass);
        Reject(mixed,new[] {mixed.StructuralTopology.GetBoundaryEdgeId(1,1)},"Affected special material rejected");
        mixed.SetLocalPivot(new Vector3(9,8,7));mixed.CreateSurfaceArea(MaterialData.CreateDefault());
        var candidate=ConvertedResult(mixed,new[] {mixed.StructuralTopology.GetBoundaryEdgeId(0,3)},out var plan,out var provenance);
        ConvertedRender(mixed,candidate,plan,provenance,Vector3.one);
        Check(candidate.SurfaceAreas.Count==3,"Untouched special and empty areas retained");
        var charts=AsConverted(TwoQuads(true));
        for(int i=0;i<charts.Mesh.Normals.Length;i++) {
            charts.Mesh.Normals[i]=Vector3.forward;charts.Mesh.Tangents[i]=new Vector4(1,0,0,1);
            var v=charts.Mesh.Vertices[i];charts.Mesh.UV0[i]=new Vector4(v.x,v.y,0,0);
            charts.Mesh.VertexColors[i]=new Color32(60,80,120,255);
        }
        charts.SetSurfaceAreaMappingMode(0,TextureMappingMode.Repeat);
        var chartResult=ConvertedResult(charts,Enumerable.Range(0,4).Select(c=>charts.StructuralTopology.GetBoundaryEdgeId(0,c)).ToArray(),out var chartPlan,out var chartSources);
        var chartObject=new ProceduralObject(2601,Guid.NewGuid(),charts);chartObject.Transform.Scale=Vector3.one;
        var chartRenderer=new ProceduralObjectRenderer();chartRenderer.TestInitialize(chartObject,InitialRenderer(charts,Vector3.one));
        using(var capture=chartRenderer.CaptureTopology(chartObject,0,chartResult)) {
            WaitDelete(()=>capture.Poll(CancellationToken.None),"Mixed mapping capture");
            var work=capture.Finish();
            var prepared=work.Build(chartResult,chartPlan.Source,null,new SubdivisionAppearanceCheck(chartPlan,chartSources),CancellationToken.None);
            Check(prepared.PreparedBytes>0,"Combined generated/Original padding and derived tangents preserved");
        }
        chartRenderer.TestDispose();
        var baseline=DissolveResult(DissolvePlan(DissolveFixture(kind:PO2.Geometry.Topology.StructuralTopologySource.ConvertedTriangleBaseline),4));
        baseline.SetSurfaceAreaMappingMode(0,TextureMappingMode.Original);
        Check(baseline.StructuralTopology.StructuralEdgeCount==4,"Dissolve-created baseline quad suppresses diagonal");
        ConvertedResult(baseline,AllEdges(baseline),out _,out _);
        for(int mask=0;mask<32;mask++) {
            var quad=ConvertedQuad(PO2.Geometry.Topology.StructuralTopologySource.ConvertedTriangleBaseline);var d=PersistencePartData.Capture(quad);
            var part=PersistencePartData.FromDecoded(d.Name,d.Vertices,(mask&1)!=0?d.Normals:null,(mask&2)!=0?d.Tangents:null,
                (mask&4)!=0?d.UV0:null,(mask&8)!=0?d.VertexColors:null,(mask&16)!=0?d.TexCoord3:null,d.Triangles,d.RenderToLogical,d.LogicalVertexCount,
                d.StructuralFaces,d.StructuralTopologySource,d.SurfaceAreas,d.TriangleSurfaceAreaIds,d.NextSurfaceAreaId,d.LocalPivot).Restore();
            var result=ConvertedResult(part,AllEdges(part),out _,out _);
            Check((result.Mesh.Normals!=null)==((mask&1)!=0) && (result.Mesh.Tangents!=null)==((mask&2)!=0) &&
                (result.Mesh.UV0!=null)==((mask&4)!=0) && (result.Mesh.VertexColors!=null)==((mask&8)!=0) &&
                (result.Mesh.TexCoord3!=null)==((mask&16)!=0),"Optional streams preserved mask " + mask);
        }
    }

}
