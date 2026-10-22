using System;
using System.Reflection;
using System.Threading;
using PO2;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Tools.MeshEdit;
using PO2.Tools.ObjectTool;

internal static partial class Program
{
    private static int checks;
    private static void Check(bool value, string label)
    {
        checks++;
        if (!value)
            throw new Exception(label);
    }

    private static readonly MethodInfo RenderMethod = typeof(MeshEditGpuSession).GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Instance);
    private static void Render(MeshEditGpuSession session, Camera camera)
    {
        var cmd = new CommandBuffer();
        RenderMethod.Invoke(session, new object[] { new CustomPassContext { cmd = cmd, hdCamera = new HDCamera { camera = camera }, propertyBlock = new MaterialPropertyBlock() } });
        cmd.Execute();
    }

    private static void Main(string[] args)
    {
        if (Array.IndexOf(args, "--large-preparation") >= 0) { RunLargePreparationChecks(); return; }
        var controller = new MeshEditController();
        var obj = new ProceduralObject();
        obj.Parts.Add(new MeshPart());
        var camera = Camera.main = new Camera();
        var tool = new ProceduralObjectToolSystem(controller)
        {
            Object = obj
        };
        using (var gpu = new MeshEditGpuSession(controller, tool, new VertexMarkerRenderer()))
        {
            controller.MeshGpu = gpu;
            Vector2 pointer = new Vector2(100, 100);
            controller.Tick(obj, camera, pointer);
            Render(gpu, camera);
            Check(controller.UsesGpuMarkers, "GPU backend active");
            Check(controller.CaptureCount == 1, "initial geometry capture");
            int initialProjectionStates = controller.PublishedCameraStates;
            for (int i = 0; i < 100; i++)
            {
                camera.worldToCameraMatrix = new Matrix4x4
                {
                    Changed = i
                };
                controller.Tick(obj, camera, pointer);
                Render(gpu, camera);
                CommandBuffer.Complete();
            }

            Check(controller.CaptureCount == 1, "camera changes keep resident geometry");
            Check(controller.PublishedCameraStates == initialProjectionStates + 100, "GPU branch advances controller projection state");
            controller.Tick(obj, camera, pointer);
            CommandBuffer.Complete();
            var request = gpu.Request(pointer, default, false);
            Render(gpu, camera);
            CommandBuffer.Complete();
            Check(gpu.Valid(request), "completed matching request accepted");
            Check(request.Data[1] == 1, "owned pick payload");
            camera.worldToCameraMatrix = new Matrix4x4
            {
                Changed = 101
            };
            controller.Tick(obj, camera, pointer);
            Check(!gpu.Valid(request), "camera revision rejects old result");
            request = gpu.Request(pointer, default, false);
            Render(gpu, camera);
            controller.ClearSelection();
            CommandBuffer.Complete();
            Check(!gpu.Valid(request), "selection change rejects outstanding result");
            controller.Tick(obj, camera, pointer);
            request = gpu.Request(pointer, default, false);
            Render(gpu, camera);
            obj.Parts[0].GeometryVersion++;
            controller.Tick(obj, camera, pointer);
            CommandBuffer.Complete();
            Check(!gpu.Valid(request), "deformation rejects outstanding result");
            Check(controller.CaptureCount == 1, "deformation keeps resident layout");
            request = gpu.Request(pointer, default, false);
            Render(gpu, camera);
            obj.Parts[0] = new MeshPart();
            controller.Tick(obj, camera, pointer);
            CommandBuffer.Complete();
            Check(!gpu.Valid(request), "replacement part rejects old IDs despite matching counts");
            request = gpu.Request(pointer, default, false);
            Render(gpu, camera);
            controller.VertexScope = MeshEditVertexScope.AllParts;
            controller.Tick(obj, camera, pointer);
            CommandBuffer.Complete();
            Check(!gpu.Valid(request), "scope rejects outstanding request");
            controller.Tick(obj, camera, pointer);
            request = gpu.Request(pointer, default, false);
            Render(gpu, camera);
            obj.Parts[0].TopologyVersion++;
            controller.Tick(obj, camera, pointer);
            CommandBuffer.Complete();
            Check(!gpu.Valid(request), "topology change rejects outstanding request");
            controller.Tick(obj, camera, pointer);
            request = gpu.Request(pointer, default, false);
            Render(gpu, camera);
            obj.Transform.Position = new Vector3(5, 0, 0);
            controller.Tick(obj, camera, pointer);
            CommandBuffer.Complete();
            Check(!gpu.Valid(request), "object transform rejects outstanding request");
            controller.Tick(obj, camera, pointer);
            Mouse.current.leftButton.isPressed = true;
            tool.Pointer(camera, pointer, true, false);
            Check(tool.PointerPending, "click waits for confirmation");
            Check(controller.GetSelectionSnapshot().Length == 0, "pending hit does not commit early");
            Render(gpu, camera);
            CommandBuffer.Complete();
            controller.Tick(obj, camera, pointer);
            tool.Pointer(camera, pointer, false, false);
            Check(controller.GetSelectionSnapshot().Length == 1, "confirmed hit selects one vertex");
            Check(!tool.PointerPending, "completed pointer intent released");
            controller.Tick(obj, camera, pointer);
            CommandBuffer.PickSlot = 0;
            tool.Pointer(camera, pointer, true, false);
            Vector2 end = new Vector2(140, 160);
            Mouse.current.leftButton.isPressed = false;
            tool.Pointer(camera, end, false, true);
            Check(controller.GetSelectionSnapshot().Length == 1, "pending miss keeps previous selection");
            Render(gpu, camera);
            CommandBuffer.Complete();
            controller.Tick(obj, camera, end);
            tool.Pointer(camera, end, false, false);
            Check(controller.GpuSelectionPending, "released drag replayed into GPU box query");
            Check(controller.GetSelectionSnapshot().Length == 1, "box keeps selection until prepared");
            CommandBuffer.BoxBits = 3;
            Render(gpu, camera);
            CommandBuffer.Complete();
            for (int i = 0; i < 200 && controller.GpuSelectionPending; i++)
            {
                controller.Tick(obj, camera, end);
                Thread.Sleep(1);
            }

            Check(!controller.GpuSelectionPending, "box preparation completes");
            Check(controller.GetSelectionSnapshot().Length == 2, "box selection adopted atomically");
            controller.ApplyBoxSelection(obj, new Rect(0, 0, 200, 200), BoxSelectionMode.Remove);
            CommandBuffer.BoxBits = 1;
            Render(gpu, camera);
            CommandBuffer.Complete();
            for (int i = 0; i < 200 && controller.GpuSelectionPending; i++)
            {
                controller.Tick(obj, camera, end);
                Thread.Sleep(1);
            }

            Check(controller.GetSelectionSnapshot().Length == 1, "box remove merges against previous selection");
            controller.ApplyBoxSelection(obj, new Rect(0, 0, 200, 200), BoxSelectionMode.Add);
            Render(gpu, camera);
            CommandBuffer.Complete();
            for (int i = 0; i < 200 && controller.GpuSelectionPending; i++)
            {
                controller.Tick(obj, camera, end);
                Thread.Sleep(1);
            }

            Check(controller.GetSelectionSnapshot().Length == 2, "box add preserves earlier selection");
            controller.Tick(obj, camera, end);
            CommandBuffer.PickSlot = 1;
            Mouse.current.leftButton.isPressed = true;
            tool.Pointer(camera, end, true, false);
            Render(gpu, camera);
            tool.Cancel();
            CommandBuffer.Complete();
            tool.Pointer(camera, end, false, false);
            Check(controller.GetSelectionSnapshot().Length == 2, "cancelled click cannot change selection");
            controller.Slide = true;
            controller.Tick(obj, camera, end);
            tool.Pointer(camera, end, true, false, kind: "Slide");
            Render(gpu, camera);
            Mouse.current.leftButton.isPressed = false;
            tool.Pointer(camera, end, false, true, kind: "Slide");
            CommandBuffer.Complete();
            controller.Tick(obj, camera, end);
            tool.Pointer(camera, end, false, false, kind: "Slide");
            Check(tool.Slides == 0, "released pending slide never starts a drag");
            controller.Slide = false;
            controller.SelectionMode = MeshSelectionMode.Edge;
            controller.Tick(obj, camera, end);
            controller.ApplyBoxSelection(obj, new Rect(0, 0, 200, 200), BoxSelectionMode.Replace);
            Render(gpu, camera);
            CommandBuffer.Complete();
            for (int i = 0; i < 200 && controller.GpuSelectionPending; i++)
            {
                controller.Tick(obj, camera, end);
                Thread.Sleep(1);
            }

            Check(controller.GetEdgeSelectionSnapshot().Length == 1, "edge box publishes structural edge IDs");
            request = gpu.Request(end, default, false);
            Render(gpu, camera);
            gpu.Stop();
            CommandBuffer.Complete();
            Check(!gpu.Valid(request), "mode exit rejects late callback");
            Check(AsyncGPUReadback.Waits == 0, "no blocking readback during normal work");
            gpu.Poll();
            controller.Tick(obj, camera, end);
            request = gpu.Request(end, default, false);
            Render(gpu, camera);
            CommandBuffer.Complete();
            request.Data[3] = 999;
            gpu.Poll();
            Check(!gpu.Active && !gpu.Valid(request), "invalid reply layout disables backend without accepting selection");
        }

        AsyncGPUReadback.Waits = 0;
        controller = new MeshEditController();
        tool = new ProceduralObjectToolSystem(controller)
        {
            Object = obj
        };
        using (var gpu = new MeshEditGpuSession(controller, tool, new VertexMarkerRenderer()))
        {
            controller.MeshGpu = gpu;
            controller.Tick(obj, camera, default);
            var request = gpu.Request(default, default, false);
            CommandBuffer.ReadbackError = true;
            Render(gpu, camera);
            CommandBuffer.ReadbackError = false;
            CommandBuffer.Complete();
            gpu.Poll();
            Check(!gpu.Active && !gpu.Valid(request), "readback failure enters contained CPU fallback");
            Check(AsyncGPUReadback.Waits == 0, "failure handling also avoids a blocking readback");
        }

        AsyncGPUReadback.Waits = 0;
        SystemInfo.supportsGraphicsFence = false;
        controller = new MeshEditController();
        tool = new ProceduralObjectToolSystem(controller) { Object = obj };
        using (var gpu = new MeshEditGpuSession(controller, tool, new VertexMarkerRenderer())) {
            controller.MeshGpu = gpu;
            controller.Tick(obj, camera, default);
            Render(gpu, camera);
            Check(gpu.Active, "graphics fences are not required for the Solid backend");
            var field = typeof(MeshEditGpuSession).GetField("m_Buffers", BindingFlags.NonPublic | BindingFlags.Instance);
            var buffers = field.GetValue(gpu);
            var candidates = (ComputeBuffer)buffers.GetType().GetField("Candidates", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(buffers);
            int before = AsyncGPUReadback.RetirementRequests;
            gpu.Stop();
            for (int i = 0; i < 20; i++) gpu.Poll();
            Check(AsyncGPUReadback.RetirementRequests == before + 1, "exit queues one completion marker, not one per poll");
            Check(!candidates.Disposed, "exit retains geometry until GPU completion");
            CommandBuffer.Complete();
            gpu.Poll();
            Check(candidates.Disposed, "completed retirement releases geometry");
            controller.Tick(obj, camera, default);
            Render(gpu, camera);
            Check(gpu.Active, "editor re-entry still works after retirement");
            buffers = field.GetValue(gpu);
            candidates = (ComputeBuffer)buffers.GetType().GetField("Candidates", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(buffers);
            AsyncGPUReadback.RetirementError = true;
            gpu.Stop();
            AsyncGPUReadback.RetirementError = false;
            CommandBuffer.Complete();
            gpu.Poll();
            Check(!candidates.Disposed, "failed retirement retains uncertain resources until shutdown");
            Check(!gpu.Prepare(obj, camera, default, new SolidVisibilityBvh()), "failed retirement disables backend without repeated exceptions");
            Check(AsyncGPUReadback.Waits == 0, "retirement never blocks normal updates");
        }

        RunDeformationChecks();
        RunPreparationChecks();
        RunXRayChecks();
        Console.WriteLine("PASS: " + checks + " mesh GPU ownership, input, deformation, and upload checks. Shader execution and HDRP require the game.");
    }
}
