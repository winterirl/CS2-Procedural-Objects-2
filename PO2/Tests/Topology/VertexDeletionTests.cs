using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using PO2;
using UnityEngine;
using PO2.Core;
using PO2.Core.Deformation;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.History;
using PO2.Materials;
using PO2.Persistence.Format;
using PO2.Persistence.Model;
using PO2.Tools.ObjectTool;

internal static partial class Program {
    private static VertexDeletePlan DeletePlan(MeshPart part, params int[] ids) {
        Check(TopologyEditor.TryAnalyzeVertexDeletion(PersistencePartData.Capture(part), ids,
            CancellationToken.None, out var plan, out var reason), "Delete analysis: " + reason);
        return plan;
    }

    private static MeshPart DeleteResult(VertexDeletePlan plan) {
        var mutation = TopologyEditor.CreateVertexDeletionMutation(plan, CancellationToken.None);
        var data = TopologyEditor.PrepareVertexRemoval(plan, mutation);
        var result = data.Restore();
        Check(result.StructuralTopologySource == plan.Source.StructuralTopologySource, "Deletion retains topology source");
        Check(result.Mesh.Triangles.Length == plan.TriangleCount * 3, "Deletion triangle count");
        int[][] streams = { new[] { 0, 12 }, new[] { 1, 12 }, new[] { 2, 16 }, new[] { 3, 16 }, new[] { 4, 4 }, new[] { 5, 16 } };
        var before = TopologyState.Capture(plan.Source);
        var after = TopologyState.Capture(data);
        foreach (var stream in streams) {
            byte[] a = before.Streams[stream[0]], b = after.Streams[stream[0]];
            Check((a == null) == (b == null), "Absent stream remains absent");
            if (a == null) continue;
            for (int old = 0; old < plan.OldToNew.Length; old++) {
                int index = plan.OldToNew[old];
                if (index < 0) continue;
                for (int c = 0; c < stream[1]; c++)
                    Check(a[old * stream[1] + c] == b[index * stream[1] + c], "Surviving vertex bytes unchanged");
            }
        }
        using (var delta = new DeleteDeltaOwner(before, after, mutation.HistoryOldToNew)) {
            Same(before, delta.Value.Apply(after, HistoryDirection.Undo), "Delete delta undo");
            Same(after, delta.Value.Apply(before, HistoryDirection.Redo), "Delete delta redo");
        }
        return result;
    }

    private sealed class DeleteDeltaOwner : IDisposable {
        internal readonly ReversibleTopologyDelta Value;
        internal DeleteDeltaOwner(TopologyState before, TopologyState after, int[] map) {
            Value = new ReversibleTopologyDelta(before, after, map);
        }
        public void Dispose() => Value.Dispose();
    }

    private static MeshPart DeleteFixture(StructuralTopologySource source, bool raw = false) {
        var positions = new[] {
            new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(0,1,0),
            new Vector3(3,0,0), new Vector3(4,0,0), new Vector3(4,1,0), new Vector3(3,1,0),
            new Vector3(10,10,10)
        };
        var part = Make(positions, new[] { new StructuralFace(0,0,1,2,3),
            new StructuralFace(1,4,5,6,7) }, true, Enumerable.Range(0,9).ToArray(), new[] {1,1,0,0});
        var d = PersistencePartData.Capture(part);
        return PersistencePartData.FromDecoded(d.Name, d.Vertices,d.Normals,d.Tangents,d.UV0,d.VertexColors,d.TexCoord1, d.TexCoord3,
            d.Triangles,d.RenderToLogical,d.LogicalVertexCount,raw ? d.StructuralFaces.Take(1).ToArray() : d.StructuralFaces,
            source,d.SurfaceAreas,d.TriangleSurfaceAreaIds,d.NextSurfaceAreaId,d.LocalPivot).Restore();
    }

    private static void VertexDeletionTests() {
        foreach (var source in new[] { PO2.Geometry.Topology.StructuralTopologySource.Authored,
            PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred, PO2.Geometry.Topology.StructuralTopologySource.ConvertedTriangleBaseline }) {
            foreach (bool raw in new[] { false, true }) {
                var part = DeleteFixture(source, raw);
                var original = TopologyState.Capture(part);
                var plan = DeletePlan(part, 0, 0);
                var result = DeleteResult(plan);
                Check(result.Mesh.Triangles.Length == 6 && result.Mesh.Vertices.Length == 5, "Whole quad and newly loose vertices removed");
                Check(result.Mesh.Vertices[4].Equals(new Vector3(10,10,10)), "Unrelated loose vertex retained");
                Check(result.SurfaceAreas.Count == 1 && result.SurfaceAreas[0].Id == 0 && result.NextSurfaceAreaId == 2,
                    "Empty area pruned, default and next id retained");
                Same(original, TopologyState.Capture(part), "Detached deletion leaves source unchanged");
                var loose = DeleteResult(DeletePlan(part,8));
                Check(loose.Mesh.Vertices.Length == 8 && loose.Mesh.Triangles.Length == 12, "Selected loose point removed alone");
                if (raw) {
                    var rawResult = DeleteResult(DeletePlan(part, 5));
                    Check(rawResult.Mesh.Triangles.Length == 9 && rawResult.StructuralTopology.FaceCount == 1,
                        "Deleting an unrepresented triangle keeps the other raw triangle and stored quad");
                }
                Check(!TopologyEditor.TryAnalyzeVertexDeletion(PersistencePartData.Capture(part), new[] {0,4},
                    CancellationToken.None, out _, out _), "Last triangle guard");
                if (source != PO2.Geometry.Topology.StructuralTopologySource.Authored) {
                    Check(!result.SupportsDestructiveTopologyEditing && !result.TopologyEditCapabilities.CanLoopCut &&
                        !result.TopologyEditCapabilities.CanEditStructuralFaces, "Converted deletion does not unlock authored tools");
                }
                var obj = new ProceduralObject(85,Guid.NewGuid(),result);
                var restored = ObjectBlockReader.Read(ObjectBlockWriter.Write(PersistenceObjectData.Capture(obj)),
                    SnapshotFormat.kObjectSchemaVersion).Restore(86);
                Same(TopologyState.Capture(result), TopologyState.Capture(restored.Parts[0]), "Deleted part save/load");
            }
        }
        var cube = Make(new[] {
            new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(0,1,0),
            new Vector3(0,0,1),new Vector3(1,0,1),new Vector3(1,1,1),new Vector3(0,1,1)
        },new[] {new StructuralFace(0,0,3,2,1),new StructuralFace(1,4,5,6,7),
            new StructuralFace(2,0,1,5,4),new StructuralFace(3,3,7,6,2),
            new StructuralFace(4,0,4,7,3),new StructuralFace(5,1,2,6,5)});
        var cubeResult=DeleteResult(DeletePlan(cube,cube.Topology.GetLogicalVertexIdForRenderVertex(0)));
        Check(cubeResult.StructuralTopology.QuadCount==3 && cubeResult.Mesh.Triangles.Length==18,"Cube corner removes three full quads");
        var seam = TwoQuads(true);
        int shared = seam.Topology.GetLogicalVertexIdForRenderVertex(1);
        Check(!TopologyEditor.TryAnalyzeVertexDeletion(PersistencePartData.Capture(seam), new[] {shared},
            CancellationToken.None, out _, out _), "Logical deletion reaches both sides of a seam");
        var overlapping = Make(new[] {Vector3.zero,Vector3.right,Vector3.up,Vector3.zero,Vector3.right,Vector3.up},
            new[] {new StructuralFace(0,0,1,2),new StructuralFace(1,3,4,5)}, true, new[] {0,1,2,3,4,5});
        var separated = DeleteResult(DeletePlan(overlapping,0));
        Check(separated.Topology.LogicalVertexCount==3 && separated.Mesh.Triangles.Length==3,"Coincident distinct groups stay separate");
        var rollback = cube.CaptureTopologyReference();
        var cubeBefore = TopologyState.Capture(cube);
        cube.ApplyHistoryTopology(cubeResult);
        cube.ApplyHistoryTopology(rollback);
        Same(cubeBefore, TopologyState.Capture(cube), "Rollback retains original arrays, topology and metadata");
        var random = new Random(882);
        var emptyAreaPart = DeleteFixture(PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred);
        int emptyAreaId = emptyAreaPart.CreateSurfaceArea(MaterialData.CreateDefault());
        var emptyAreaResult = DeleteResult(DeletePlan(emptyAreaPart, 0));
        Check(emptyAreaResult.SurfaceAreas.Any(area => area.Id == emptyAreaId), "Pre-existing empty area survives deletion");
        for(int run=0;run<20;run++) {
            var part=Grid(5);
            int[] ids=Enumerable.Range(0,6).Select(_=>random.Next(part.Topology.LogicalVertexCount)).Distinct().ToArray();
            if(TopologyEditor.TryAnalyzeVertexDeletion(PersistencePartData.Capture(part),ids,CancellationToken.None,out var plan,out _))
                DeleteResult(plan);
        }
        DeleteCommandTests();
    }

    private static void WaitDelete(Func<bool> condition, string reason) {
        var timer=Stopwatch.StartNew();
        while (!condition() && timer.ElapsedMilliseconds<30000) Thread.Sleep(1);
        Check(condition(),reason);
    }

    private static void DeleteCommandTests() {
        var part=DeleteFixture(PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred);
        var obj=new ProceduralObject(901,Guid.NewGuid(),part,Single(3));
        var system=new ProceduralObjectSystem();system.Objects.Add(obj.Id,obj);
        var tool=new ProceduralObjectToolSystem {TestObject=obj,m_ProceduralObjectSystem=system};
        var controller=tool.m_MeshEditController;
        controller.SelectionMode=MeshSelectionMode.Vertex;
        controller.Vertices=new[] {new VertexRef(0,0)};
        system.m_HistoryContext=new HistoryContext(system,null,controller.Capture,controller.Restore,controller.AdoptPreparedTopologySelection);
        using(system.ActionHistory=new ActionHistory(system.m_HistoryContext)) {
            var before=TopologyState.Capture(part);
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanDeleteSelectedVertices;},"Delete availability finishes");
            int snapshots=controller.SnapshotCount;
            for(int i=0;i<50;i++) Check(tool.CanDeleteSelectedVertices,"Cached delete availability");
            Check(controller.SnapshotCount==snapshots,"Polling does not recapture selection or mesh");
            obj.IsLocked=true;Check(!tool.CanDeleteSelectedVertices,"Locked delete blocked");obj.IsLocked=false;
            tool.DeformationSnapshotBusy = true;
            Check(!tool.CanDeleteSelectedVertices, "Pending deformation blocks vertex deletion");
            tool.DeformationSnapshotBusy = false;
            tool.m_BoxSelection.IsGestureActive = true;
            Check(!tool.CanDeleteSelectedVertices, "Active selection gesture blocks vertex deletion");
            tool.m_BoxSelection.IsGestureActive = false;
            Check(PartGeometrySnapshot.TryCapture(part, false, out var snapshot, out _), "Delete bake guard capture");
            obj.SetDeformation(0, DeformationCage.Capture(obj, 0,
                Matrix4x4.Scale(new Vector3(12, 12, 12)), new[] { snapshot }));
            Check(!tool.CanDeleteSelectedVertices, "Unbaked delete blocked");
            tool.DeleteSelectedVertices();
            Check(!tool.TopologyEditBusy && system.ActionHistory.Count == 0, "Command repeats bake guard");
            obj.SetDeformation(0, null);
            controller.Vertices=new[] {new VertexRef(0,0),new VertexRef(1,0)};controller.SelectionVersion++;
            WaitDelete(()=>{tool.TickVertexDelete();return tool.TestTopologyAnalyzed;},"Mixed part analysis completes");
            Check(!tool.CanDeleteSelectedVertices, "Mixed part deletion is rejected");
            controller.Vertices=new[] {new VertexRef(0,0)};controller.SelectionVersion++;
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanDeleteSelectedVertices;},"Availability after selection change");
            tool.DeleteSelectedVertices(); Check(tool.TopologyEditBusy,"Delete enters background operation");
            tool.CancelVertexDelete();
            WaitDelete(()=>{tool.TickVertexDelete();return !tool.TopologyEditBusy;},"Cancelled delete finishes");
            Same(before,TopologyState.Capture(part),"Cancel leaves streams untouched");
            Check(controller.SelectionCount==1 && system.ActionHistory.Count==0,"Cancel keeps selection and no history");
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanDeleteSelectedVertices;},"Availability after cancel");
            tool.DeleteSelectedVertices();
            controller.ScopePartIndex = 0;
            WaitDelete(()=>{tool.TickVertexDelete();return !tool.TopologyEditBusy;},"Scope change cancels deletion");
            Same(before,TopologyState.Capture(part),"Scope change cannot publish");
            controller.ScopePartIndex = -1;
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanDeleteSelectedVertices;},"Availability after scope change");
            tool.DeleteSelectedVertices();
            controller.SelectionVersion++;
            WaitDelete(()=>{tool.TickVertexDelete();return !tool.TopologyEditBusy;},"Selection change cancels deletion");
            Same(before,TopologyState.Capture(part),"Selection change cannot publish");
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanDeleteSelectedVertices;},"Availability after selection cancellation");
            tool.DeleteSelectedVertices();
            system.SelectedPartIndex = 1;
            WaitDelete(()=>{tool.TickVertexDelete();return !tool.TopologyEditBusy;},"Active part change cancels deletion");
            Same(before,TopologyState.Capture(part),"Active part change cannot publish");
            system.SelectedPartIndex = 0;
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanDeleteSelectedVertices;},"Availability after active part change");
            tool.DeleteSelectedVertices();
            tool.TestObject = null;
            WaitDelete(()=>{tool.TickVertexDelete();return !tool.TopologyEditBusy;},"Removed object cancels deletion");
            Same(before,TopologyState.Capture(part),"Removed target cannot publish");
            tool.TestObject = obj;
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanDeleteSelectedVertices;},"Availability after removed target");
            tool.DeleteSelectedVertices();
            tool.StopVertexDelete();
            Check(!tool.TopologyEditBusy && !system.ActionHistory.IsCapturing, "Unload releases pending work and reservation");
            Same(before,TopologyState.Capture(part),"Unload leaves streams untouched");
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanDeleteSelectedVertices;},"Availability after unload");
            tool.DeleteSelectedVertices();
            part.MarkGeometryChanged();
            WaitDelete(()=>{tool.TickVertexDelete();return !tool.TopologyEditBusy;},"Stale delete finishes");
            Same(before,TopologyState.Capture(part),"Stale worker cannot publish");
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanDeleteSelectedVertices;},"Availability after stale result");
            tool.DeleteSelectedVertices();system.ActionHistory.Clear();
            WaitDelete(()=>{tool.TickVertexDelete();return !tool.TopologyEditBusy;},"Invalidated reservation stops publication");
            Same(before,TopologyState.Capture(part),"History invalidation keeps streams");
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanDeleteSelectedVertices;},"Availability after history clear");
            Guid id=part.PartHistoryId;
            tool.DeleteSelectedVertices();
            WaitDelete(()=>{tool.TickVertexDelete();return !tool.TopologyEditBusy;},"Delete publishes");
            Check(system.ActionHistory.Count==1 && controller.SelectionCount==0 && part.PartHistoryId==id,"One history step, cleared selection, stable identity");
            var after=TopologyState.Capture(part);
            Check(system.ActionHistory.Request(HistoryDirection.Undo),"Delete undo request");
            WaitDelete(()=>{system.ActionHistory.Poll();return !system.ActionHistory.IsBusy;},"Delete undo finishes");
            Check(string.IsNullOrEmpty(system.ActionHistory.LastFailure),"Delete undo succeeds");
            Same(before,TopologyState.Capture(part),"Resident delete undo exact");
            Check(controller.SelectionCount==1 && controller.Vertices[0].VertexId==0,"Undo restores vertex selection");
            Check(system.ActionHistory.Request(HistoryDirection.Redo),"Delete redo request");
            WaitDelete(()=>{system.ActionHistory.Poll();return !system.ActionHistory.IsBusy;},"Delete redo finishes");
            Same(after,TopologyState.Capture(part),"Resident delete redo exact");
            Check(controller.SelectionCount==0,"Redo restores empty selection");
            Check(system.ActionHistory.Request(HistoryDirection.Undo), "Stale undo request");
            part.MarkGeometryChanged();
            WaitDelete(()=>{system.ActionHistory.Poll();return !system.ActionHistory.IsBusy;},"Stale undo finishes");
            Same(after,TopologyState.Capture(part),"Stale undo cannot publish detached state");
        }
        tool.StopVertexDelete();
    }

}
