using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

internal static partial class Program {
    private static MeshPart DissolveFixture(Vector3[] positions = null, int[] triangles = null,
        StructuralFace[] faces = null, int[] logical = null, StructuralTopologySource kind = PO2.Geometry.Topology.StructuralTopologySource.Authored) {
        positions = positions ?? new[] { new Vector3(0,0,0), new Vector3(2,0,0), new Vector3(2,2,0), new Vector3(0,2,0), new Vector3(1,1,0) };
        triangles = triangles ?? new[] { 0,1,4, 1,2,4, 2,3,4, 3,0,4 };
        logical = logical ?? Enumerable.Range(0,positions.Length).ToArray();
        if (faces == null) faces = Enumerable.Range(0,triangles.Length/3).Select(t => new StructuralFace(t,triangles[t*3],triangles[t*3+1],triangles[t*3+2])).ToArray();
        var normals = positions.Select(_=>Vector3.forward).ToArray();
        var tangents = positions.Select(_=>new Vector4(1,0,0,1)).ToArray();
        var uv = positions.Select(p=>new Vector4(p.x,p.y,3+p.x,7-p.y)).ToArray();
        var colors = positions.Select(_=>new Color32(32,64,128,255)).ToArray();
        var extra = positions.Select(_=>new Vector4(1,2,3,4)).ToArray();
        var mesh = new MeshData(positions,normals,tangents,uv,colors,extra,triangles);
        var areas = new List<SurfaceArea> { new SurfaceArea(0,MaterialData.CreateDefault(),
            VertexAttributeFlags.UV0FourComponent | VertexAttributeFlags.VertexColor | VertexAttributeFlags.TexCoord3) };
        return MeshPart.Restore("Dissolve fixture",mesh,logical,logical.Max()+1,faces,areas,new int[triangles.Length/3],1,kind);
    }
    private static VertexDissolvePlan DissolvePlan(MeshPart part, params int[] ids) {
        var source = PersistencePartData.Capture(part);
        var index = new TopologyEditIndex(source,CancellationToken.None);
        Check(TopologyEditor.TryAnalyzeVertexDissolution(index,ids,CancellationToken.None,out var plan,out var reason),"Dissolve analysis: "+reason);
        return plan;
    }
    private static MeshPart DissolveResult(VertexDissolvePlan plan) {
        var mutation = TopologyEditor.CreateVertexDissolutionMutation(plan,CancellationToken.None);
        var data = TopologyEditor.PrepareVertexRemoval(plan.Removal,mutation);
        var result = data.Restore();
        var before = TopologyState.Capture(plan.Source); var after = TopologyState.Capture(data);
        using(var delta = new DeleteDeltaOwner(before,after,mutation.HistoryOldToNew)) {
            Same(before,delta.Value.Apply(after,HistoryDirection.Undo),"Dissolve exact undo");
            Same(after,delta.Value.Apply(before,HistoryDirection.Redo),"Dissolve exact redo");
        }
        Check(result.Topology.LogicalVertexCount==plan.Source.LogicalVertexCount-plan.Patches.Length,"Dissolve removes only selected logical vertices");
        foreach (int old in Enumerable.Range(0,mutation.HistoryOldToNew.Length)) {
            int index=mutation.HistoryOldToNew[old]; if(index<0)continue;
            Check(data.Vertices[index].Equals(plan.Source.Vertices[old]) && data.Normals[index].Equals(plan.Source.Normals[old]) &&
                data.Tangents[index].Equals(plan.Source.Tangents[old]) && data.UV0[index].Equals(plan.Source.UV0[old]) &&
                data.VertexColors[index].Equals(plan.Source.VertexColors[old]) && data.TexCoord3[index].Equals(plan.Source.TexCoord3[old]),"Dissolve keeps surviving streams exact");
        }
        Check(result.StructuralTopologySource==plan.Source.StructuralTopologySource,"Dissolve preserves source kind");
        return result;
    }
    private static void RejectDissolve(MeshPart part, string expected, params int[] ids) {
        var source=PersistencePartData.Capture(part); var before=TopologyState.Capture(part);
        Check(!TopologyEditor.TryAnalyzeVertexDissolution(new TopologyEditIndex(source,CancellationToken.None),ids,CancellationToken.None,out _,out var reason),"Dissolve should reject "+expected);
        Same(before,TopologyState.Capture(part),"Rejected dissolve preserves source");
    }

    private static void VertexDissolutionTests() {
        foreach(var kind in new[] {PO2.Geometry.Topology.StructuralTopologySource.Authored,PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred,PO2.Geometry.Topology.StructuralTopologySource.ConvertedTriangleBaseline}) {
            foreach(bool raw in new[] {false,true}) {
                var part=DissolveFixture(kind:kind,faces:raw ? Array.Empty<StructuralFace>() : null);
                var result=DissolveResult(DissolvePlan(part,4));
                Check(result.StructuralTopology.QuadCount==1 && result.Mesh.Triangles.Length==6,"Interior fan becomes one clean quad");
                if(kind!=PO2.Geometry.Topology.StructuralTopologySource.Authored)Check(!result.SupportsDestructiveTopologyEditing,"Dissolve does not unlock converted topology tools");
                var obj=new ProceduralObject(777,Guid.NewGuid(),result);
                var restored=ObjectBlockReader.Read(ObjectBlockWriter.Write(PersistenceObjectData.Capture(obj)),SnapshotFormat.kObjectSchemaVersion).Restore(778);
                Same(TopologyState.Capture(result),TopologyState.Capture(restored.Parts[0]),"Dissolve save/load exact");
            }
        }
        var boundary=DissolveFixture(new[] {new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(2,0,0),new Vector3(2,1,0),new Vector3(0,1,0)},new[] {0,1,3,1,2,3,0,3,4});
        var boundaryResult=DissolveResult(DissolvePlan(boundary,1));
        Check(boundaryResult.Mesh.Triangles.Length==6,"Straight boundary point dissolves without a hole");
        RejectDissolve(boundary,"straight boundary",0);
        var quadBoundary=DissolveFixture(new[] {new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(2,0,0),new Vector3(2,1,0),new Vector3(1,1,0),new Vector3(0,1,0)},
            new[] {0,1,4,0,4,5,1,2,3,1,3,4},new[] {new StructuralFace(0,0,1,4,5),new StructuralFace(1,1,2,3,4)});
        var quadPlan=DissolvePlan(quadBoundary,1);Check(quadPlan.Patches[0].RemovedTriangles.Length==4,"Both triangles of incident quads are included");
        var quadResult=DissolveResult(quadPlan);Check(quadResult.Mesh.Vertices.Length==5 && quadResult.Mesh.Triangles.Length==9,"Collinear unselected boundary point retained");
        var concave=DissolveFixture(new[] {new Vector3(0,0,0),new Vector3(3,0,0),new Vector3(1,1,0),new Vector3(0,3,0),new Vector3(.5f,.4f,0)});
        var concaveResult=DissolveResult(DissolvePlan(concave,4));Check(concaveResult.StructuralTopology.QuadCount==0 && concaveResult.StructuralTopology.TriangleCount==2,"Concave four-corner patch triangulates safely");
        var curved=DissolveFixture();curved.Mesh.Vertices[4]=new Vector3(1,1,.01f);RejectDissolve(curved,"flat patches",4);
        var uvSeam=DissolveFixture();uvSeam.Mesh.UV0[4]=new Vector4(1.25f,1,4,6);RejectDissolve(uvSeam,"UV",4);
        var normalSeam=DissolveFixture();normalSeam.Mesh.Normals[4]=Vector3.up;RejectDissolve(normalSeam,"normals",4);
        var tangentSeam=DissolveFixture();tangentSeam.Mesh.Tangents[4]=new Vector4(1,0,0,-1);RejectDissolve(tangentSeam,"handedness",4);
        var colors=DissolveFixture();colors.Mesh.VertexColors[4]=new Color32(31,64,128,255);RejectDissolve(colors,"vertex colors",4);
        var packed=DissolveFixture();packed.Mesh.TexCoord3[4]=new Vector4(2,2,3,4);RejectDissolve(packed,"packed",4);
        RejectDissolve(DissolveFixture(),"overlap",0,4);
        var basePart=DissolveFixture();var positions=basePart.Mesh.Vertices.Concat(basePart.Mesh.Vertices.Select(p=>p+new Vector3(4,0,0))).ToArray();
        var triangles=basePart.Mesh.Triangles.Concat(basePart.Mesh.Triangles.Select(i=>i+5)).ToArray();
        var independent=DissolveFixture(positions,triangles);var independentResult=DissolveResult(DissolvePlan(independent,9,4,9));
        Check(independentResult.StructuralTopology.QuadCount==2,"Independent patches commit atomically and deterministically");
        var translated=DissolveFixture(basePart.Mesh.Vertices.Select(p=>p+new Vector3(100000,200000,300000)).ToArray());DissolveResult(DissolvePlan(translated,4));
        var small=DissolveFixture(basePart.Mesh.Vertices.Select(p=>p*0.000001f).ToArray());DissolveResult(DissolvePlan(small,4));
        var reversed=DissolveFixture(triangles:basePart.Mesh.Triangles.Select((v,i)=>basePart.Mesh.Triangles[i/3*3+(2-i%3)]).ToArray());DissolveResult(DissolvePlan(reversed,4));
        var loose=DissolveFixture(basePart.Mesh.Vertices.Concat(new[]{new Vector3(10,10,10)}).ToArray());RejectDissolve(loose,"loose vertices",5);
        var keepLoose=DissolveResult(DissolvePlan(loose,4));Check(keepLoose.Mesh.Vertices.Any(v=>v.Equals(new Vector3(10,10,10))),"Unrelated loose point survives dissolve");
        using(var cancellation=new CancellationTokenSource()) {
            cancellation.Cancel();bool cancelled=false;
            try {new TopologyEditIndex(PersistencePartData.Capture(basePart),cancellation.Token);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"Dissolve index cancellation");
        }
        var disconnected=DissolveFixture(basePart.Mesh.Vertices.Concat(basePart.Mesh.Vertices.Take(4)).ToArray(),
            basePart.Mesh.Triangles.Concat(basePart.Mesh.Triangles.Select(v=>v==4?4:v+5)).ToArray());
        RejectDissolve(disconnected,"disconnected",4);
        var duplicate=DissolveFixture(triangles:basePart.Mesh.Triangles.Concat(new[]{0,1,4}).ToArray());
        RejectDissolve(duplicate,"disk-shaped",4);
        var seamPositions=basePart.Mesh.Vertices.Concat(new[]{basePart.Mesh.Vertices[4]}).ToArray();
        var split=DissolveFixture(seamPositions,new[]{0,1,4,1,2,5,2,3,4,3,0,5},logical:new[]{0,1,2,3,4,4});
        var splitResult=DissolveResult(DissolvePlan(split,4));Check(splitResult.Mesh.Vertices.Length==4,"Coincident render members of the selected logical point are removed together");
        split.Mesh.UV0[5]=new Vector4(9,9,9,9);RejectDissolve(split,"UV",4);
        var exactPayload=DissolveFixture();
        for(int i=0;i<exactPayload.Mesh.TexCoord3.Length;i++)exactPayload.Mesh.TexCoord3[i]=new Vector4(0,2,3,4);
        exactPayload.Mesh.TexCoord3[4]=new Vector4(BitConverter.Int32BitsToSingle(int.MinValue),2,3,4);
        RejectDissolve(exactPayload,"packed",4);
        var materials=DissolveFixture();int secondArea=materials.CreateSurfaceArea(MaterialData.CreateDefault());
        materials.AssignTrianglesToSurfaceArea(new[]{0},secondArea);RejectDissolve(materials,"Surface Area",4);
        // An existing edge outside the disk must not become a third incident face.
        var chord=DissolveFixture(basePart.Mesh.Vertices.Concat(new[]{new Vector3(4,1,0)}).ToArray(),basePart.Mesh.Triangles.Concat(new[]{0,2,5}).ToArray());
        RejectDissolve(chord,"diagonal",4);
        SnapshotCaptureTests();
        TopologyRendererTests();
    }

    private static void SnapshotCaptureTests() {
        var part=DissolveFixture();var obj=new ProceduralObject(992,Guid.NewGuid(),part);
        using(var cancellation=new CancellationTokenSource()) {
            var capture=new TopologySnapshotCapture(obj,0);capture.Start(cancellation.Token);
            WaitDelete(()=>capture.Poll(cancellation.Token),"Sliced snapshot capture");
            Same(TopologyState.Capture(part),TopologyState.Capture(capture.Finish()),"Sliced snapshot equals persistence capture");
            var detached=capture.Finish();var previous=detached.Vertices[0];part.Mesh.Vertices[0]=new Vector3(99,99,99);
            Check(detached.Vertices[0].Equals(previous),"Capture does not alias live positions");
        }
        var stale=new TopologySnapshotCapture(obj,0);stale.Start(CancellationToken.None);part.MarkGeometryChanged();bool rejected=false;
        try{stale.Poll(CancellationToken.None);}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"Capture rejects changed target");
    }
}
