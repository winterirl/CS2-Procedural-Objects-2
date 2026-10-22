using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using PO2;
using UnityEngine;
using PO2.Core;
using PO2.Core.Operations;
using PO2.History;
using PO2.Localization;
using PO2.Materials;
using PO2.Persistence.Model;
using PO2.Rendering.Objects;

internal static partial class Program {
    private sealed class PreparationAppearance : ITopologyAppearanceCheck {
        public void Validate(PersistencePartData before, int[] map, ITopologyRenderData candidate, CancellationToken token) { }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static TopologyPreparation BeginPreparationLifetimeTest(ProceduralObject obj, ProceduralObjectSystem system, out WeakReference[] temporary) {
        var source = PersistencePartData.Capture(obj.Parts[0]);
        var map = Enumerable.Range(0, source.Vertices.Length).ToArray();
        var appearance = new PreparationAppearance();
        temporary = new[] { new WeakReference(source), new WeakReference(map), new WeakReference(appearance) };
        return new TopologyPreparation(obj, 0, new TopologyTargetStamp(obj, 0), source, system, CancellationToken.None,
            (data, token) => new TopologyPreparedResult {
                Part = MirrorHistoryAfter(data).RestoreOwned(), OldToNew = map, Appearance = appearance
            });
    }

    private static void PreparationLifetimeTest() {
        var obj = new ProceduralObject(9330, Guid.NewGuid(), MirrorFixture());
        var system = new ProceduralObjectSystem();
        using (var preparation = BeginPreparationLifetimeTest(obj, system, out var temporary)) {
            WaitDelete(() => preparation.Poll(), "Lifetime test preparation completes");
            WaitDelete(() => {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                return temporary.All(reference => !reference.IsAlive);
            }, "Completed preparation releases before snapshot, renderer scratch and worker closures");
            Check(preparation.Publish(() => true), "Prepared result still publishes after scratch collection");
            GC.KeepAlive(preparation);
        }
    }

    private static void MirrorSizeTests() {
        foreach (var part in new[] { MirrorFixture(), Single(3, streams: false), Grid(8), AsConverted(Grid(8)) }) {
            var size = TopologySize.ForPart(part);
            var data = PersistencePartData.Capture(part);
            var mirrored = MirrorHistoryAfter(data);
            size.ValidateCandidate(mirrored.RestoreOwned());
            using (var delta = new MirrorDelta(data, mirrored, CancellationToken.None)) {
                Check(delta.RawBytes > 0, "Mirror keeps an exact raw delta without a byte allowance");
            }
            var tooSmall = new TopologySize(0, 0, 0, 0, 0, 0, 0, 0);
            bool rejected = false;
            try { tooSmall.ValidateCandidate(part); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Unexpected topology growth remains invalid");
        }
    }

    private static void LargeMirrorRoundTrip() {
        var seed = DissolveFixture(); int fans = 160000;
        var positions = new Vector3[fans * 5]; var triangles = new int[fans * 12];
        for (int f = 0; f < fans; f++) {
            for (int i = 0; i < 5; i++) positions[f * 5 + i] = seed.Mesh.Vertices[i] + new Vector3((f % 256) * 4, (f / 256) * 4, 0);
            for (int i = 0; i < 12; i++) triangles[f * 12 + i] = seed.Mesh.Triangles[i] + f * 5;
        }
        var part = DissolveFixture(positions, triangles);
        part.SetSurfaceAreaMappingMode(0, TextureMappingMode.Original);
        var obj = new ProceduralObject(9320, Guid.NewGuid(), part); obj.Transform = ObjectTransform.IdentityAt(Vector3.zero);
        obj.SetLocalPivot(new Vector3(15, 6, 2));
        var before = TopologyState.Capture(part);
        var renderer = new ProceduralObjectRenderer(); renderer.TestInitialize(obj, InitialRenderer(part, Vector3.one));
        var system = new ProceduralObjectSystem { MirrorTestSelected = obj, TestTopologyRenderer = renderer };
        system.Objects.Add(obj.Id, obj); system.m_HistoryContext = new HistoryContext(system);
        using (system.ActionHistory = new ActionHistory(system.m_HistoryContext)) {
            Check(system.TryMirrorSelected(-1, Vector3.right, LocalizedText.Diagnostic("fixture"), out var reason), "Large mirror begins");
            Check(FinishMirror(system, out reason), "Large mirror succeeds: " + reason);
            Check(renderer.TestVersionMatches(obj), "Large mirror publishes current renderer");
            var after = TopologyState.Capture(part);
            Check(system.ActionHistory.Request(HistoryDirection.Undo), "Large mirror undo begins");
            WaitDelete(() => { system.ActionHistory.Poll(); return !system.ActionHistory.IsBusy; }, "Large mirror undo completes");
            MirrorSame(before, part, "Large mirror undo restores exact state");
            Check(renderer.TestVersionMatches(obj), "Large undo publishes current renderer");
            Check(system.ActionHistory.Request(HistoryDirection.Redo), "Large mirror redo begins");
            WaitDelete(() => { system.ActionHistory.Poll(); return !system.ActionHistory.IsBusy; }, "Large mirror redo completes");
            Check(system.ActionHistory.Count == 1, "Large mirror remains one valid history entry");
            MirrorSame(after, part, "Large mirror redo restores exact state");
            Check(renderer.TestVersionMatches(obj), "Large redo publishes current renderer");
        }
        renderer.TestDispose();
    }
}
