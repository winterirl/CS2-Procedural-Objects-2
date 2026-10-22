using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PO2.Core;
using PO2.Geometry;
using PO2.Materials;
using PO2.Rendering.Objects;
using UnityEngine;
using UnityEngine.Rendering;

internal static partial class Program {
    private static void HistoryRendererTests() {
        foreach (var mode in new[] { TextureMappingMode.Original, TextureMappingMode.Repeat, TextureMappingMode.Stretch })
        foreach (var special in new[] { VanillaSpecialMaterialKind.None, VanillaSpecialMaterialKind.Glass, VanillaSpecialMaterialKind.Water })
        foreach (bool sourceTangents in new[] { true, false }) {
            var part = DissolveFixture();
            var source = part.Mesh;
            source.ReplaceTopology(source.Vertices, source.Normals, sourceTangents ? source.Tangents : null,
                source.UV0, source.VertexColors, source.UV0.ToArray(), source.TexCoord3, source.Triangles);
            part.SetSurfaceAreaSourceVertexAttributes(0, part.SurfaceAreas[0].SourceVertexAttributes | VertexAttributeFlags.TexCoord1);
            part.SetSurfaceAreaMappingMode(0, mode);
            if (special != VanillaSpecialMaterialKind.None) part.SurfaceAreas[0].Material.SetVanillaSpecialMaterial(special);
            var obj = new ProceduralObject(610, Guid.NewGuid(), part) { Transform = ObjectTransform.IdentityAt(new Vector3(4, 5, 6)) };
            obj.Transform.Scale = new Vector3(2, 3, 4);
            var renderer = new ProceduralObjectRenderer();
            var created = new List<GameObject>();
            int owner = Thread.CurrentThread.ManagedThreadId;
            GameObject.TestCreated = go => { Check(Thread.CurrentThread.ManagedThreadId == owner, "Unity objects created on the game thread"); created.Add(go); };
            try {
                using (var expected = InitialRenderer(part, obj.Transform.Scale))
                using (var stage = renderer.PrepareHistoryObject(obj)) {
                    Check(!created[0].activeSelf, "Restored root starts hidden");
                    bool premature = false;
                    try { stage.Publish(); } catch (InvalidOperationException) { premature = true; }
                    Check(premature && !renderer.TestPublished(obj.Id), "Incomplete renderer cannot publish");
                    WaitDelete(() => {
                        Check(!created[0].activeSelf && !renderer.TestPublished(obj.Id), "Preparation never exposes a partial object");
                        return stage.Poll(CancellationToken.None);
                    }, "History renderer finishes");
                    stage.Publish();
                    Check(renderer.TestPublished(obj.Id) && created[0].activeSelf, "Complete renderer publishes once");
                    var picking = renderer.TestPickCache(obj);
                    Check(picking != null && picking.TryPick(new Vector3(0.25f, 0.3f, -10), Vector3.forward, out _, out float pickDistance)
                        && Math.Abs(pickDistance - 10) < 0.001f, "Restored geometry is ready for precise picking immediately");
                    Check(created[0].transform.position.Equals(obj.Transform.Position) && created[0].transform.localScale.Equals(obj.Transform.Scale), "Restored root keeps its transform");
                    var actual = renderer.TestMeshes(obj).First(mesh => mesh.vertexCount != 0);
                    var reference = expected.Areas[0].Runtime;
                    Check(actual.vertices.SequenceEqual(reference.Vertices) && actual.normals.SequenceEqual(reference.Normals), "Restored geometry matches existing preparation");
                    var uv = actual.GetVertexAttributeDimension(VertexAttribute.TexCoord0) == 2
                        ? actual.Read<Vector2>(VertexAttribute.TexCoord0).Select(value => new Vector4(value.x, value.y, 0, 0))
                        : actual.Read<Vector4>(VertexAttribute.TexCoord0);
                    Check(uv.SequenceEqual(reference.UV0), "Restored UV mapping matches existing preparation");
                    bool generated = mode != TextureMappingMode.Original || !sourceTangents;
                    Check(actual.TangentRecalculations == (generated ? 1 : 0), "Unity tangent generation follows the original renderer rules");
                    if (!generated) Check(actual.tangents.SequenceEqual(reference.Tangents), "Source tangent bits preserved");
                    if (mode == TextureMappingMode.Original) {
                        Check(actual.Read<Color32>(VertexAttribute.Color).SequenceEqual(reference.VertexColors), "Vertex colors preserved");
                        Check(actual.Read<Vector4>(VertexAttribute.TexCoord1).SequenceEqual(reference.TexCoord1), "UV1 preserved");
                        Check(actual.Read<Vector4>(VertexAttribute.TexCoord3).SequenceEqual(reference.TexCoord3), "UV3 preserved");
                    }
                }
                Check(renderer.TestPublished(obj.Id), "Disposing staging keeps a published renderer alive");
            }
            finally { GameObject.TestCreated = null; renderer.TestDispose(); }
            Check(renderer.TestMaterialReferences == 0 && renderer.TestUpdateBytes == 0, "Restored renderer releases materials and update buffers");
        }
        HistoryRendererCancellation();
        HistoryRendererFailure();
    }

    private static void HistoryRendererCancellation() {
        var obj = new ProceduralObject(611, Guid.NewGuid(), Grid(96), Grid(96));
        var renderer = new ProceduralObjectRenderer();
        var created = new List<GameObject>();
        GameObject.TestCreated = created.Add;
        using (var token = new CancellationTokenSource())
        using (var stage = renderer.PrepareHistoryObject(obj)) {
            try {
                WaitDelete(() => {
                    stage.Poll(token.Token);
                    return renderer.TestMaterialReferences > 0;
                }, "Large restore reaches staged upload");
                Check(!renderer.TestPublished(obj.Id) && !created[0].activeSelf, "Large restore remains hidden during upload");
                token.Cancel();
                bool cancelled = false;
                try { stage.Poll(token.Token); } catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "Pending restore observes cancellation");
                stage.Dispose(); stage.Drain.GetAwaiter().GetResult(); renderer.PollTopologyCleanup(true);
                Check(created[0].Destroyed && renderer.TestMaterialReferences == 0 && renderer.TestUpdateBytes == 0, "Cancellation releases staged Unity resources");
                Check(!renderer.TestPublished(obj.Id), "Cancelled renderer never enters the live scene");
            }
            finally { GameObject.TestCreated = null; renderer.TestDispose(); }
        }
        var complete = new ProceduralObjectRenderer();
        using (var stage = complete.PrepareHistoryObject(obj)) {
            WaitDelete(() => stage.Poll(CancellationToken.None), "Large multipart restore finishes"); stage.Publish();
            for (int part = 0; part < obj.Parts.Count; part++) {
                var mesh = complete.TestMesh(obj, part);
                Check(mesh.vertexCount > 2048 && mesh.Writes.All(write => write.Bytes <= 2048 * mesh.Strides[write.Stream]), "Large vertex uploads use bounded slices");
                Check(complete.TestPickCache(obj, part) != null, "Every restored part has a prepared picking cache");
            }
        }
        complete.TestDispose();
    }

    private static void HistoryRendererFailure() {
        var obj = new ProceduralObject(612, Guid.NewGuid(), DissolveFixture());
        var renderer = new ProceduralObjectRenderer();
        var created = new List<GameObject>();
        GameObject.TestCreated = created.Add;
        var injectedFailure = new InvalidOperationException("Injected Unity allocation failure");
        MeshRenderer.TestCreationWork = () => throw injectedFailure;
        using (var stage = renderer.PrepareHistoryObject(obj)) {
            try {
                bool failed = false;
                WaitDelete(() => {
                    try { stage.Poll(CancellationToken.None); }
                    catch (Exception exception) { failed = ReferenceEquals(exception.GetBaseException(), injectedFailure); }
                    return failed;
                }, "Renderer creation failure surfaced");
                stage.Dispose(); stage.Drain.GetAwaiter().GetResult(); renderer.PollTopologyCleanup(true);
                Check(created.All(go => go.Destroyed) && !renderer.TestPublished(obj.Id), "Failed Unity allocation leaves no root or part behind");
            }
            finally { MeshRenderer.TestCreationWork = null; GameObject.TestCreated = null; renderer.TestDispose(); }
        }
    }
}
