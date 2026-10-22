using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using PO2;
using UnityEngine;
using UnityEngine.Rendering;
using PO2.Core;
using PO2.Rendering.Objects;
using PO2.Tools.ObjectTool;

internal static class Program {
    private static int s_Checks;
    private static void Check(bool value,string message) {s_Checks++;if(!value)throw new Exception(message);}
    private static void Near(float a,float b,string message) => Check(MathF.Abs(a-b)<0.0002f*MathF.Max(1,MathF.Abs(b)),message);
    private static ProceduralObject Object(int id,float x=0,float z=10) => new ProceduralObject {
        Id=id,Bounds=new Bounds(new Vector3(x,0,z),new Vector3(2,2,2))
    };
    private static Matrix4x4 Perspective() => new Matrix4x4 {
        Value=new System.Numerics.Matrix4x4(1,0,0,0, 0,1,0,0, 0,0,1,1, 0,0,0,0)
    };
    private static ObjectMarkerFrame.View View(bool orthographic=false) => new ObjectMarkerFrame.View {
        ViewProjection=orthographic?Matrix4x4.identity:Perspective(),Forward=new Vector3(0,0,1),Up=new Vector3(0,1,0),
        ProjectionY=1,PixelHeight=1080,IconPixels=22,Orthographic=orthographic
    };
    private static Vector3 Position(Game.Rendering.NotificationIconBufferSystem.InstanceData value) =>
        new Vector3(value.m_Position.x,value.m_Position.y,value.m_Position.z);
    private static void Main() {
        Cache();Frames();Owner();
        Console.WriteLine("PASS: "+s_Checks+" marker cache, camera, batch and lifecycle checks.");
    }
    private static void Cache() {
        var system=new ProceduralObjectSystem();var first=Object(1);var second=Object(2);
        system.Store.Add(first);system.Store.Add(second);
        var cache=new ObjectMarkerCache();cache.Update(system);
        Check(system.BoundsReads==2&&cache.Anchors.Count==2,"Read each initial anchor once");
        Near(cache.Anchors[0].y,1.5f,"Original minimum vertical offset");
        uint version=cache.Version;
        for(int i=0;i<1000;i++)cache.Update(system);
        Check(system.BoundsReads==2&&cache.Version==version,"Stationary objects do not revisit bounds");
        first.Transform.Position=new Vector3(1,0,0);cache.Update(system);
        Check(system.BoundsReads==3&&cache.Version!=version,"Moved object invalidates only its anchor");
        first.GeometryVersion++;first.Bounds=new Bounds(new Vector3(0,0,10),new Vector3(2,1000,2));cache.Update(system);
        Near(cache.Anchors[0].y,505,"Original maximum vertical offset");
        first.PartVersion++;cache.Update(system);Check(system.BoundsReads==5,"Part change refreshes bounds");
        first.Transform.Scale=new Vector3(-2,3,1);cache.Update(system);Check(system.BoundsReads==6,"Scale refreshes anchor");
        first.Transform.Rotation=new Quaternion {Value=System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY,1)};
        cache.Update(system);Check(system.BoundsReads==7,"Rotation refreshes anchor");
        system.Store[0]=Object(1,5);cache.Update(system);Check(system.BoundsReads==8,"Reloaded object identity invalidates reused runtime ID");
        system.Store.RemoveAt(1);cache.Update(system);Check(cache.Anchors.Count==1,"Deleted PO releases cached entry");
        var invalid=Object(3);invalid.HasBounds=false;system.Store.Add(invalid);cache.Update(system);
        int reads=system.BoundsReads;cache.Update(system);Check(system.BoundsReads==reads&&cache.Anchors.Count==1,"Empty object is cached too");
        invalid.HasBounds=true;invalid.GeometryVersion++;cache.Update(system);Check(cache.Anchors.Count==2,"Empty object gains geometry");
        cache.Clear();Check(cache.Anchors.Count==0,"Off/stop clears references");
        cache.Update(system);Check(system.BoundsReads==reads+3,"Reactivation refreshes current objects");
    }
    private static void Frames() {
        var frame=new ObjectMarkerFrame();var anchors=new[] {new Vector3(0,0,10),new Vector3(0,0,100)};
        var view=View();Check(frame.Update(anchors,1,view),"Initial batch builds");Check(frame.Count==2,"Both visible anchors retained");
        Check(frame.Instances[0].m_Position.z>frame.Instances[1].m_Position.z,"Farther camera depth first");
        Check(!frame.Update(anchors,1,view),"Unchanged frame skips sorting/upload");
        foreach(bool ortho in new[] {false,true})foreach(float scale in new[] {0.5f,1f,2f}) {
            view=View(ortho);view.PixelHeight=1080*scale;view.IconPixels=22*scale;
            frame.Update(anchors,2,view);
            foreach(var instance in frame.Instances.Take(frame.Count)) {
                Vector3 origin=Position(instance);
                float radius=instance.m_Params.x*MathF.Pow(Vector3.Distance(origin,view.Position),0.6f)*0.063f;
                float pixels=radius*view.ProjectionY*view.PixelHeight/(ortho?1:origin.z);
                Near(pixels,view.IconPixels,"Radius projects to the requested full icon size");
                Near(origin.y+radius,0,"Bottom-origin shader keeps badge centered");
                Check(instance.m_Params.y==0&&instance.m_Params.z==1&&instance.m_Params.w==1,"No pulsation or category dimming");
            }
        }
        view=View();
        var filtered=new[] {new Vector3(0,0,-1),new Vector3(11,0,10),new Vector3(0,11,10),new Vector3(float.NaN,0,10),new Vector3(0,0,0),new Vector3(10,0,10)};
        frame.Update(filtered,3,view);Check(frame.Count==1,"Behind/offscreen/nonfinite anchors culled; viewport boundary retained");
        view.Up=new Vector3(1,0,0);frame.Update(new[] {new Vector3(0,0,10)},4,view);
        Check(frame.Instances[0].m_Position.x<0&&frame.Instances[0].m_Position.y==0,"Camera roll changes centering direction");
        view.PixelHeight=0;frame.Update(anchors,4,view);Check(frame.Count==0,"Invalid viewport hides batch");
        frame.Invalidate();Check(frame.Count==0,"Hidden frame has no draw count");
        frame.Update(Array.Empty<Vector3>(),5,View());Check(frame.Count==0,"Empty scene hides batch");
    }
    private static void Owner() {
        Camera.main=new Camera {projectionMatrix=Perspective()};var cameras=new List<Camera> {Camera.main};
        var markerSystem=new ObjectMarkerSystem();markerSystem.Create();
        var system=markerSystem.World.GetOrCreateSystemManaged<ProceduralObjectSystem>();
        var tool=markerSystem.World.GetOrCreateSystemManaged<ProceduralObjectToolSystem>();
        system.Store.Add(Object(1));markerSystem.Tick();RenderPipelineManager.Render(cameras);
        Check(Graphics.Draws==1&&RenderPipelineManager.Subscribers==1,"One main-camera batch and one callback");
        int uploads=ComputeBuffer.Created.Sum(b=>b.Uploads);
        markerSystem.Tick();RenderPipelineManager.Render(cameras);
        Check(Graphics.Draws==2&&ComputeBuffer.Created.Sum(b=>b.Uploads)==uploads,"Stationary draw reuses GPU buffer");
        Camera.main.worldToCameraMatrix=new Matrix4x4 {Value=System.Numerics.Matrix4x4.CreateTranslation(-0.1f,0,0)};
        markerSystem.Tick();RenderPipelineManager.Render(cameras);
        Check(system.BoundsReads==1&&ComputeBuffer.Created.Sum(b=>b.Uploads)==uploads+2,"Camera movement uploads compact data without rereading bounds");
        RenderPipelineManager.Render(new List<Camera> {new Camera()});Check(Graphics.Draws==3,"Unrelated cameras do not draw PO icons");
        foreach(int gate in new[] {0,1,2,3}) {
            if(gate==0)tool.ShowObjectMarkers=false;
            if(gate==1)system.IsToolActive=false;
            if(gate==2)system.SelectedObjectId=1;
            if(gate==3)tool.Mode=ObjectToolMode.MeshEdit;
            int draws=Graphics.Draws;markerSystem.Tick();RenderPipelineManager.Render(cameras);
            Check(Graphics.Draws==draws,"Marker visibility gate "+gate);
            tool.ShowObjectMarkers=true;system.IsToolActive=true;system.SelectedObjectId=null;tool.Mode=ObjectToolMode.Select;
            markerSystem.Tick();RenderPipelineManager.Render(cameras);
            Check(Graphics.Draws==draws+1,"Markers restore after gate "+gate);
        }
        for(int i=2;i<80;i++)system.Store.Add(Object(i));
        markerSystem.Tick();RenderPipelineManager.Render(cameras);
        Check(ComputeBuffer.Created.Any(b=>b.stride==36&&b.Released),"Growing buffer releases prior storage");
        markerSystem.Stop();int stopped=Graphics.Draws;RenderPipelineManager.Render(cameras);Check(Graphics.Draws==stopped,"Stopped system cannot submit stale markers");
        markerSystem.Tick();RenderPipelineManager.Render(cameras);Check(Graphics.Draws==stopped+1,"Restart restores markers");
        int drawsBeforeFailure=Graphics.Draws;
        ComputeBuffer.FailUpload=true;Camera.main.pixelHeight=720;RenderPipelineManager.Render(cameras);
        Check(Graphics.Draws==drawsBeforeFailure,"Failed upload cannot draw partial markers");
        markerSystem.Tick();RenderPipelineManager.Render(cameras);Check(Graphics.Draws==drawsBeforeFailure,"Failed renderer stays disabled");
        ComputeBuffer.FailUpload=false;markerSystem.Destroy();
        Check(RenderPipelineManager.Subscribers==0&&ComputeBuffer.Created.All(b=>b.Released),"Destroy releases callback and buffers");
        markerSystem.Destroy();Check(RenderPipelineManager.Subscribers==0,"Repeated cleanup is safe");
    }
}
