using MeshTopology = PO2.Geometry.Topology.MeshTopology;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PO2;
using UnityEngine;
using PO2.Conversion;
using PO2.Core;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.History;
using PO2.Materials;
using PO2.Persistence.Model;
using PO2.Rendering;
using PO2.Rendering.Mapping;
using PO2.Rendering.Objects;
using PO2.Tools.ObjectTool;

internal static partial class Program {
    private static TopologyRendererPreparation InitialRenderer(MeshPart part, Vector3 scale) {
        var work=new TopologyRendererWork{Scale=scale};
        var groups=TopologyRendererWork.GroupTriangles(part.TriangleSurfaceAreaIds,CancellationToken.None);
        bool combinedFallback = part.SurfaceAreas.Any(a => groups.ContainsKey(a.Id) && a.Material.VanillaSpecialKind == VanillaSpecialMaterialKind.None &&
            a.Material.Mapping.Mode != TextureMappingMode.Original);
        foreach(var area in part.SurfaceAreas) {
            var settings=new TopologyRenderAreaWork{Id=area.Id,RequiresTangents=true,TileSize=area.Material.GetEffectiveTileSizeMeters(),
                Isolated=area.Material.VanillaSpecialKind!=VanillaSpecialMaterialKind.None};
            work.Areas.Add(area.Id,settings);
            if(!groups.TryGetValue(area.Id,out var ids))continue;
            ConvertedRenderPreparationBuilder.BuildAreaSourceMesh(part,area,ids,out var mesh,out var indices);
            settings.UsesSourceTangents=area.Material.Mapping.Mode==TextureMappingMode.Original && mesh.Tangents!=null;
            if(area.Material.Mapping.Mode!=TextureMappingMode.Original) {
                var topology=MeshTopology.Build(mesh);var mapping=new GeneratedMapping(mesh,topology,area.SourceVertexAttributes,area.MappingSeed,indices);
                mapping.Update(mesh,topology,scale,area.Material.Mapping,settings.TileSize,true);
                mesh=new MeshData(mapping.Vertices,mapping.Normals,null,mapping.UVs.Select(v=>new Vector4(v.x,v.y,0,0)).ToArray(),mapping.Triangles);
                indices=mapping.SourceVertexIndices.Select(i=>indices[i]).ToArray();
                mesh.RecalculateTangents();
            }
            if (combinedFallback && !settings.Isolated && settings.UsesSourceTangents) {
                mesh = new MeshData(mesh.Vertices,mesh.Normals,null,mesh.UV0,mesh.VertexColors,mesh.TexCoord1,mesh.TexCoord3,mesh.Triangles);
                mesh.RecalculateTangents();settings.UsesSourceTangents=false;
            }
            if (mesh.Tangents == null) mesh.RecalculateTangents();
            settings.Current=mesh;settings.RuntimeToPart=indices;
        }
        return (TopologyRendererPreparation)work.Build(part,PersistencePartData.Capture(part),null,null,CancellationToken.None);
    }
    private static void TopologyRendererTests() {
        foreach(var mode in new[]{TextureMappingMode.Original,TextureMappingMode.Repeat,TextureMappingMode.Stretch}) {
            var part=DissolveFixture();part.SetSurfaceAreaMappingMode(0,mode);
            var obj=new ProceduralObject(1001,Guid.NewGuid(),part);obj.Transform.Scale=Vector3.one;
            var initial=InitialRenderer(part,obj.Transform.Scale);var renderer=new ProceduralObjectRenderer();renderer.TestInitialize(obj,initial);
            Check(renderer.TestMesh(obj).vertices.SequenceEqual(initial.Upload.Positions) && renderer.TestMesh(obj).normals.SequenceEqual(initial.Upload.Directions),"Staged renderer exact packed bytes "+mode);
            var before=TopologyState.Capture(part);var oldMesh=renderer.TestMesh(obj);
            var stamp=new TopologyTargetStamp(obj,0);
            obj.Transform.Scale=new Vector3(2,1,1);
            Check(!stamp.Matches(obj,0),"Root scale invalidates prepared mapping");
            obj.Transform.Scale=Vector3.one;
            using(var cancellation=new CancellationTokenSource()) {
                var cancelledCapture=renderer.CaptureTopology(obj,0,part);
                WaitDelete(() => {
                    cancelledCapture.Poll(cancellation.Token);
                    return oldMesh.ReadLeases != 0;
                }, "Capture reaches native tangent readback");
                Check(oldMesh.ReadLeases==1,"Capture owns native tangent read lease");
                cancellation.Cancel();
                bool cancelled=false;
                try{cancelledCapture.Poll(cancellation.Token);}catch(OperationCanceledException){cancelled=true;}
                cancelledCapture.Dispose();
                Check(cancelled && oldMesh.ReadLeases==0,"Cancelled capture releases native read lease on owner");
            }
            var plan=DissolvePlan(part,4);var mutation=TopologyEditor.CreateVertexDissolutionMutation(plan,CancellationToken.None);
            var candidate=TopologyEditor.PrepareVertexRemoval(plan.Removal,mutation).Restore();
            ITopologyRenderWork work;
            using(var capture=renderer.CaptureTopology(obj,0,candidate)) {
                WaitDelete(()=>capture.Poll(CancellationToken.None),"Real renderer sliced capture");work=capture.Finish();
            }
            Check(oldMesh.ReadLeases==0,"Native read lease released on owner");
            var prepared=Task.Run(()=>(TopologyRendererPreparation)work.Build(candidate,plan.Source,mutation.HistoryOldToNew,new DissolveAppearanceCheck(plan),CancellationToken.None)).GetAwaiter().GetResult();
            using(var stage=renderer.StageTopology(obj,0,candidate,prepared)) {
                int polls=0;
                while(!stage.Poll(CancellationToken.None)) {
                    Check(ReferenceEquals(renderer.TestMesh(obj),oldMesh) && renderer.TestMainVisible(obj),"Old mesh stays visible throughout upload");polls++;
                }
                Check(polls>3,"Publication spread over update slices");
                part.ApplyHistoryTopology(candidate);stage.Adopt();stage.Commit();
            }
            renderer.PollTopologyCleanup(true);
            Check(oldMesh.Destroyed && renderer.TestVersionMatches(obj),"Model and renderer publish matching versions");
            Check(renderer.TestMesh(obj).TestIndices.SequenceEqual(prepared.Upload.Indices),"Published index stream exact");
            var reload=PersistencePartData.Capture(part).Restore();var reloadRender=InitialRenderer(reload,obj.Transform.Scale);
            Check(prepared.Combined.UV0.SequenceEqual(reloadRender.Combined.UV0),"Generated UVs stable after save/load "+mode);
            var cancelMesh=renderer.TestMesh(obj);
            using(var stage=renderer.StageTopology(obj,0,part,reloadRender)) {stage.Poll(CancellationToken.None);}
            renderer.PollTopologyCleanup(true);
            Check(ReferenceEquals(renderer.TestMesh(obj),cancelMesh) && renderer.TestMaterialReferences==part.SurfaceAreas.Count,"Cancelled staging keeps old mesh and releases materials");
            renderer.TestDispose();Check(renderer.TestMaterialReferences==0,"Renderer teardown releases every material reference");
        }
        DissolveCommandTests();
        DeletedGeneratedAreaHistory();
        GeneratedChartRejection();
    }

    private static void DissolveCommandTests() {
        var part=DissolveFixture();var obj=new ProceduralObject(1002,Guid.NewGuid(),part);obj.Transform.Scale=Vector3.one;
        var system=new ProceduralObjectSystem();system.Objects.Add(obj.Id,obj);
        var tool=new ProceduralObjectToolSystem{TestObject=obj,m_ProceduralObjectSystem=system};var controller=tool.m_MeshEditController;
        controller.SelectionMode=MeshSelectionMode.Vertex;controller.Vertices=new[]{new VertexRef(0,4)};
        system.m_HistoryContext=new HistoryContext(system,null,controller.Capture,controller.Restore,controller.AdoptPreparedTopologySelection);
        var before=TopologyState.Capture(part);
        using(system.ActionHistory=new ActionHistory(system.m_HistoryContext)) {
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanDissolveSelectedVertices;},"Dissolve availability finishes");
            int snapshots=controller.SnapshotCount;
            for(int i=0;i<100;i++)Check(tool.CanDissolveSelectedVertices && !tool.CanDeleteSelectedVertices,"Cached operations retain independent eligibility");
            Check(snapshots==controller.SnapshotCount,"Dissolve getters create no snapshot work");
            tool.DissolveSelectedVertices();tool.CancelVertexDelete();
            WaitDelete(()=>{tool.TickVertexDelete();return !tool.TopologyEditBusy;},"Dissolve cancel completes");
            Same(before,TopologyState.Capture(part),"Dissolve cancel leaves mesh unchanged");
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanDissolveSelectedVertices;},"Dissolve after cancel");
            system.RejectTopologyPublication=true;tool.DissolveSelectedVertices();
            WaitDelete(()=>{tool.TickVertexDelete();return !tool.TopologyEditBusy;},"Dissolve injected publication failure");
            Same(before,TopologyState.Capture(part),"Renderer failure rolls back model");
            Check(system.ActionHistory.Count==0 && controller.SelectionCount==1 && system.TopologyRollbacks==1,"Failed publication leaves no history or selection change");
            system.RejectTopologyPublication=false;controller.SelectionVersion++;
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanDissolveSelectedVertices;},"Dissolve after failed publication");
            tool.DissolveSelectedVertices();WaitDelete(()=>{tool.TickVertexDelete();return !tool.TopologyEditBusy;},"Dissolve command completes");
            Check(system.ActionHistory.Count==1,"Dissolve records one history entry");
            var after=TopologyState.Capture(part);
            system.ActionHistory.Request(HistoryDirection.Undo);WaitDelete(()=>{system.ActionHistory.Poll();return !system.ActionHistory.IsBusy;},"Dissolve undo");
            Same(before,TopologyState.Capture(part),"Dissolve command undo exact");
            Check(controller.SelectionCount==1,"Dissolve undo restores selection");
            system.ActionHistory.Request(HistoryDirection.Redo);WaitDelete(()=>{system.ActionHistory.Poll();return !system.ActionHistory.IsBusy;},"Dissolve redo");
            Same(after,TopologyState.Capture(part),"Dissolve command redo exact");
            system.ActionHistory.Request(HistoryDirection.Undo);system.ActionHistory.Clear();
            Check(!system.ActionHistory.IsBusy && system.ActionHistory.Count==0,"Clear during prepared undo releases owner state");
            Same(after,TopologyState.Capture(part),"Clear during undo cannot publish late");
        }
        tool.StopVertexDelete();
    }

    private static void DeletedGeneratedAreaHistory() {
        var fan=DissolveFixture();var part=DissolveFixture(fan.Mesh.Vertices.Concat(fan.Mesh.Vertices.Select(p=>p+new Vector3(4,0,0))).ToArray(),
            fan.Mesh.Triangles.Concat(fan.Mesh.Triangles.Select(i=>i+5)).ToArray());
        int area=part.CreateSurfaceArea(MaterialData.CreateDefault());part.AssignTrianglesToSurfaceArea(new[]{4,5,6,7},area);
        part.SetSurfaceAreaMappingMode(0,TextureMappingMode.Repeat);part.SetSurfaceAreaMappingMode(area,TextureMappingMode.Repeat);
        var obj=new ProceduralObject(1003,Guid.NewGuid(),part);obj.Transform.Scale=Vector3.one;
        var renderer=new ProceduralObjectRenderer();renderer.TestInitialize(obj,InitialRenderer(part,obj.Transform.Scale));
        var system=new ProceduralObjectSystem{TestTopologyRenderer=renderer};system.Objects.Add(obj.Id,obj);
        var tool=new ProceduralObjectToolSystem{TestObject=obj,m_ProceduralObjectSystem=system};var controller=tool.m_MeshEditController;
        controller.SelectionMode=MeshSelectionMode.Vertex;controller.Vertices=new[]{new VertexRef(0,9)};
        system.m_HistoryContext=new HistoryContext(system,null,controller.Capture,controller.Restore,controller.AdoptPreparedTopologySelection);
        var before=TopologyState.Capture(part);
        using(system.ActionHistory=new ActionHistory(system.m_HistoryContext)) {
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanDeleteSelectedVertices;},"Generated area deletion available");
            tool.DeleteSelectedVertices();WaitDelete(()=>{tool.TickVertexDelete();renderer.PollTopologyCleanup();return !tool.TopologyEditBusy;},"Generated area deletion commits");
            Check(system.ActionHistory.Count==1,"Generated area delete succeeds: "+tool.TopologyEditStatus);
            Check(part.SurfaceAreas.Count==1,"Emptied generated area is pruned");
            system.ActionHistory.Request(HistoryDirection.Undo);
            WaitDelete(()=>{system.ActionHistory.Poll();renderer.PollTopologyCleanup();return !system.ActionHistory.IsBusy;},"Generated area undo completes");
            Check(system.ActionHistory.LastFailure.Length==0,"Generated area restoration succeeds: "+system.ActionHistory.LastFailure);
            Same(before,TopologyState.Capture(part),"Generated empty-area undo exact");
            Check(renderer.TestVersionMatches(obj) && part.SurfaceAreas.Count==2,"Restored area publishes without a subsequent topology rebuild");
            system.ActionHistory.Request(HistoryDirection.Redo);
            WaitDelete(()=>{system.ActionHistory.Poll();renderer.PollTopologyCleanup();return !system.ActionHistory.IsBusy;},"Generated area redo completes");
            var after=TopologyState.Capture(part);var currentMesh=renderer.TestMesh(obj);
            renderer.PollTopologyCleanup(true);
            int materials=renderer.TestMaterialReferences;
            system.ActionHistory.Request(HistoryDirection.Undo);
            WaitDelete(()=>{system.ActionHistory.Poll();return renderer.TestMaterialReferences>materials;},"Undo reaches hidden Unity staging");
            system.ActionHistory.Clear();renderer.PollTopologyCleanup(true);
            Check(!system.ActionHistory.IsBusy && renderer.TestMaterialReferences==materials && currentMesh.ReadLeases==0,
                "History clear during Unity staging releases owner resources");
            for(int i=0;i<10;i++)system.ActionHistory.Poll();
            Same(after,TopologyState.Capture(part),"Cleared staged undo cannot publish late");
            Check(ReferenceEquals(renderer.TestMesh(obj),currentMesh),"Cleared staged undo retains visible mesh");
        }
        tool.StopVertexDelete();renderer.TestDispose();
    }

    private static void GeneratedChartRejection() {
        var part=DissolveFixture(new[]{new Vector3(1,0,0),new Vector3(0,0,0),new Vector3(2,0,0),new Vector3(2,1,0),new Vector3(0,1,0)},new[]{0,2,3,1,0,3,1,3,4});
        part.SetSurfaceAreaMappingMode(0,TextureMappingMode.Repeat);
        var obj=new ProceduralObject(1004,Guid.NewGuid(),part);obj.Transform.Scale=Vector3.one;
        var renderer=new ProceduralObjectRenderer();renderer.TestInitialize(obj,InitialRenderer(part,obj.Transform.Scale));
        var plan=DissolvePlan(part,0);var mutation=TopologyEditor.CreateVertexDissolutionMutation(plan,CancellationToken.None);
        var candidate=TopologyEditor.PrepareVertexRemoval(plan.Removal,mutation).Restore();
        ITopologyRenderWork work;
        using(var capture=renderer.CaptureTopology(obj,0,candidate)){WaitDelete(()=>capture.Poll(CancellationToken.None),"Chart rejection capture");work=capture.Finish();}
        bool rejected=false;
        try{work.Build(candidate,plan.Source,mutation.HistoryOldToNew,new DissolveAppearanceCheck(plan),CancellationToken.None);}catch(InvalidOperationException e){rejected=true;}
        Check(rejected,"Dissolve rejects a changed generated chart origin");
        var system=new ProceduralObjectSystem{TestTopologyRenderer=renderer};system.Objects.Add(obj.Id,obj);
        var tool=new ProceduralObjectToolSystem{TestObject=obj,m_ProceduralObjectSystem=system};var controller=tool.m_MeshEditController;
        controller.SelectionMode=MeshSelectionMode.Vertex;controller.Vertices=new[]{new VertexRef(0,0)};
        system.m_HistoryContext=new HistoryContext(system,null,controller.Capture,controller.Restore,controller.AdoptPreparedTopologySelection);
        using(system.ActionHistory=new ActionHistory(system.m_HistoryContext)) {
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanDissolveSelectedVertices;},"Chart candidate geometry eligible");
            tool.DissolveSelectedVertices();
            WaitDelete(()=>{tool.TickVertexDelete();return !tool.TopologyEditBusy;},"Chart command rejection completes");
            for(int i=0;i<100;i++){tool.TickVertexDelete();Thread.Sleep(1);}
            Check(!tool.CanDissolveSelectedVertices,
                "Renderer rejection stays cached after detached cleanup");
            tool.DissolveSelectedVertices();
            Check(!tool.TopologyEditBusy && system.ActionHistory.Count==0,"Repeated rejected command cannot launch work");
            controller.SelectionVersion++;
            WaitDelete(()=>{tool.TickVertexDelete();return tool.CanDissolveSelectedVertices;},"Changed selection invalidates renderer rejection");
        }
        tool.StopVertexDelete();renderer.TestDispose();
    }
}
