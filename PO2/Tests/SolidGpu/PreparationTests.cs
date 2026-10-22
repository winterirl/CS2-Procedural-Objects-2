using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using PO2;
using UnityEngine;
using UnityEngine.Rendering;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Tools.MeshEdit;
using PO2.Tools.ObjectTool;

internal static partial class Program {
    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Pump(Func<bool> done, Action tick) {
        var clock = Stopwatch.StartNew();
        while (!done()) {
            tick(); Thread.Sleep(1);
            if (clock.Elapsed.TotalSeconds > 15) throw new Exception("Preparation did not finish.");
        }
    }
    private static void RunPreparationChecks() {
        // Detached data keeps old contents even if its source is subsequently edited.
        var detachedObject = Grid(8);
        var source = new MeshEditGpuSource(detachedObject, MeshSelectionMode.Vertex);
        source.Copy.Start(CancellationToken.None);
        Pump(() => source.Copy.IsComplete, () => source.Copy.Poll(() => source.Matches(detachedObject, MeshSelectionMode.Vertex), CancellationToken.None));
        detachedObject.Parts[0].Mesh.Vertices[0] = new Vector3(0, 0, 19); detachedObject.Parts[0].GeometryVersion++;
        Check(!source.Matches(detachedObject, MeshSelectionMode.Vertex) && source.Parts[0].Vertices[0].z == 0, "snapshot is detached and rejects a later edit");
        var detached = MeshEditGpuGeometry.Build(source, null, CancellationToken.None);
        Check(detached.Candidates[0].A.z == 0 && !detached.Matches(detachedObject, MeshSelectionMode.Vertex), "worker never reads mutated live streams");
        using (var cts = new CancellationTokenSource()) {
            cts.Cancel(); bool cancelled = false;
            try { MeshEditGpuGeometry.Build(source, null, cts.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "worker observes cancellation");
        }

        var obj = Grid(185); var camera = new Camera(); var controller = new MeshEditController();
        var tool = new ProceduralObjectToolSystem(controller) { Object = obj };
        using (var gpu = new MeshEditGpuSession(controller, tool, new VertexMarkerRenderer())) {
            controller.MeshGpu = gpu;
            controller.TickOnce(obj, camera, default);
            Check(controller.UsesGpuMarkers && gpu.Preparing && !gpu.Ready && gpu.Geometry == null, "cold entry returns pending without a synchronous CPU fallback");
            Check(gpu.Request(default, default, false) == null && gpu.HoverSlot == -1, "preparation cannot create a visibility request or hover");
            tool.Pointer(camera, default, true, false);
            Check(!tool.PointerPending, "click during preparation is ignored rather than replayed later");
            Render(gpu, camera);
            Check(gpu.Geometry == null && Buffers(gpu) == null, "render cannot consume a partial generation");
            var pending = Field(gpu, "m_Preparation");
            camera.worldToCameraMatrix = new Matrix4x4 { Changed = 803 };
            obj.Transform.Position = new Vector3(4, 5, 6); controller.VertexScope = MeshEditVertexScope.AllParts;
            controller.TickOnce(obj, camera, default);
            Check(ReferenceEquals(pending, Field(gpu, "m_Preparation")), "camera, transform, and scope changes retain local preparation");
            double previous = UnityEngine.Time.realtimeSinceStartupAsDouble;
            UnityEngine.Time.realtimeSinceStartupAsDouble += 6;
            controller.TickOnce(obj, camera, default);
            Check(gpu.Active, "HDRP watchdog does not time out CPU preparation");
            ComputeBuffer.LargestPartialUpload = 0;
            int ticks = 0;
            Pump(() => gpu.Ready, () => {
                controller.TickOnce(obj, camera, default);
                ticks++;
            });
            Check(ticks > 1 && ComputeBuffer.LargestPartialUpload <= 256 * 1024, "large publication is sliced and SetData chunks are bounded");
            Check(((Matrix4x4)Field(gpu, "m_View")).Changed == 803 && ((ObjectTransform)Field(gpu, "m_Transform")).Position.z == 6 && (int)Field(gpu, "m_Part") == -1,
                "publication takes latest game camera, object transform, and scope");
            Render(gpu, camera); CommandBuffer.Complete();
            UnityEngine.Time.realtimeSinceStartupAsDouble = previous;
            var data = gpu.Geometry; var buffers = Buffers(gpu);
            MatchesBuffer(buffers, "Candidates", data.Candidates); MatchesBuffer(buffers, "Normals", data.Normals);
            MatchesBuffer(buffers, "Triangles", data.Triangles); MatchesBuffer(buffers, "Nodes", data.Nodes);
            using (var reference = new SolidVisibilityBvh()) EqualTree(data, MeshEditGpuGeometry.Capture(obj, MeshSelectionMode.Vertex, reference));
            int serial = data.TreeSerial;
            controller.SelectionMode = MeshSelectionMode.Edge;
            Check(!gpu.Ready && gpu.Preparing && gpu.HoverSlot == -1, "mode change invalidates input immediately, before the next update");
            controller.Tick(obj, camera, default); CommandBuffer.Complete();
            Check(gpu.Geometry.Mode == MeshSelectionMode.Edge && gpu.Geometry.TreeSerial == serial && ReferenceEquals(data.Nodes, gpu.Geometry.Nodes),
                "mode switch reuses unchanged tree without a native rebuild");
            // After transfer of ownership, only the new generation receives deformation.
            obj.Parts[0].Mesh.Vertices[0] = new Vector3(0, 0, 5);
            uint before = obj.Parts[0].GeometryVersion++;
            gpu.Notice(obj, new GeometryChange(obj.Parts[0], 0, before, new[] { 0 }, Array.Empty<int>()));
            var switched = gpu.Geometry; controller.Tick(obj, camera, default); Render(gpu, camera); CommandBuffer.Complete();
            Check(ReferenceEquals(switched, gpu.Geometry) && CandidateZ(Buffers(gpu)) == 5, "sparse edit remains resident after asynchronous mode switch");
            var bvh = (SolidVisibilityBvh)Field(controller, "m_SolidVisibilityBvh");
            int oldSerial = bvh.BuildSerial; bvh.EnsureBuilt(obj);
            Check(bvh.BuildSerial == oldSerial, "CPU fallback catches up without rebuilding the tree layout");
        }
        CommandBuffer.Complete();
        CheckPreparationCancellation();
        CheckPreparationFailure(false); CheckPreparationFailure(true);
        // Corrupt inputs fail on the worker and do not partially publish.
        obj = Grid(2); obj.Parts[0].Mesh.Vertices[0] = new Vector3(float.NaN, 0, 0);
        controller = new MeshEditController(); tool = new ProceduralObjectToolSystem(controller) { Object = obj };
        using (var gpu = new MeshEditGpuSession(controller, tool, new VertexMarkerRenderer())) {
            controller.MeshGpu = gpu; controller.Tick(obj, camera, default);
            Check(!gpu.Active && gpu.Geometry == null && Buffers(gpu) == null, "non-finite worker data fails cleanly without publication");
        }
        // Empty native trees and marker generations are also publishable.
        obj = new ProceduralObject(); controller = new MeshEditController(); tool = new ProceduralObjectToolSystem(controller) { Object = obj };
        using (var gpu = new MeshEditGpuSession(controller, tool, new VertexMarkerRenderer())) {
            controller.MeshGpu = gpu; controller.Tick(obj, camera, default); Render(gpu, camera);
            Check(gpu.Ready && gpu.Geometry.Nodes.Length == 0 && gpu.Request(default, default, false).Complete, "empty asynchronous generation completes empty queries");
        }
        CommandBuffer.Complete();
    }

    private static void CheckPreparationCancellation() {
        var obj = Grid(185); var controller = new MeshEditController(); var camera = new Camera();
        var tool = new ProceduralObjectToolSystem(controller) { Object = obj };
        using (var gpu = new MeshEditGpuSession(controller, tool, new VertexMarkerRenderer())) {
            controller.MeshGpu = gpu; controller.TickOnce(obj, camera, default);
            var first = Field(gpu, "m_Preparation");
            obj.Parts[0].Mesh.Vertices[0] = new Vector3(0, 0, 7); obj.Parts[0].GeometryVersion++;
            controller.TickOnce(obj, camera, default);
            Check(!ReferenceEquals(first, Field(gpu, "m_Preparation")) && !gpu.Ready, "geometry change cancels capture before publication");
            // Continue until part of an unsubmitted buffer set exists.
            Pump(() => Field(gpu, "m_Publication") != null, () => controller.TickOnce(obj, camera, default));
            var publication = Field(gpu, "m_Publication");
            Pump(() => ((ComputeBuffer)Field(Field(publication, "m_Buffers"), "Candidates")) != null,
                () => controller.TickOnce(obj, camera, default));
            Check(!gpu.Ready, "large publication is still private after its first GPU allocation");
            int created = ComputeBuffer.Created.Count;
            obj.Parts[0].TopologyVersion++;
            controller.TickOnce(obj, camera, default);
            Check(!gpu.Ready && !ReferenceEquals(publication, Field(gpu, "m_Publication")), "topology change discards a partial GPU publication");
            Check(ComputeBuffer.Created.Take(created).Where(b => b.count == obj.Parts[0].Topology.LogicalVertexCount && b.stride == 48).All(b => b.Disposed),
                "unsubmitted cancelled buffers are disposed immediately");
            for (int i = 0; i < 8; i++) {
                controller.SelectionMode = i % 2 == 0 ? MeshSelectionMode.Edge : MeshSelectionMode.Vertex;
                controller.TickOnce(obj, camera, default);
                Check(!gpu.Ready || gpu.Geometry.Mode == controller.SelectionMode, "rapid switches never publish the wrong mode");
            }
            var replacement = Grid(4); replacement.Id = obj.Id;
            tool.Object = replacement; obj = replacement; controller.Tick(obj, camera, default);
            Check(gpu.Ready && ReferenceEquals(gpu.Geometry.Object, replacement) && gpu.Geometry.Candidates.Length == 16,
                "same numeric object ID cannot adopt another object's worker result");
            Render(gpu, camera); CommandBuffer.Complete();
            controller.SelectionMode = MeshSelectionMode.Edge; controller.TickOnce(obj, camera, default);
            gpu.Stop();
            Check(!gpu.Active && !gpu.Ready && gpu.Geometry == null, "stopping the session cancels work and never waits for the CPU worker");
            controller.VisibilityMode = MeshEditVisibilityMode.XRay; controller.TickOnce(obj, camera, default);
            Check(controller.UsesGpuMarkers && controller.MarkerPreparationPending, "X-Ray starts its shared preparation after cancellation");
            controller.VisibilityMode = MeshEditVisibilityMode.Solid; controller.Tick(obj, camera, default);
            Check(gpu.Ready && gpu.Geometry.Mode == MeshSelectionMode.Edge, "re-entry succeeds after cancelled work drains");
        }
        CommandBuffer.Complete();
    }
    private static void CheckPreparationFailure(bool upload) {
        var obj = Grid(2); var controller = new MeshEditController(); var camera = new Camera();
        var tool = new ProceduralObjectToolSystem(controller) { Object = obj };
        int start;
        using (var gpu = new MeshEditGpuSession(controller, tool, new VertexMarkerRenderer())) {
            start = ComputeBuffer.Created.Count;
            controller.MeshGpu = gpu;
            ComputeBuffer.FailAllocation = !upload; ComputeBuffer.FailUpload = upload;
            try { controller.Tick(obj, camera, default); }
            finally { ComputeBuffer.FailAllocation = ComputeBuffer.FailUpload = false; }
            Check(!gpu.Active && !gpu.Ready && gpu.Geometry == null, "publication failure disables backend without adopting partial data");
            Check(ComputeBuffer.Created.Skip(start).All(b => b.Disposed), "publication failure releases all private buffers");
        }
        CommandBuffer.Complete();
    }
}
