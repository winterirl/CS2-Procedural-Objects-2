using System;
using System.Collections.Generic;
using System.Threading;
using PO2.Core.Lod;
using PO2.Rendering.Lod;
using PO2.Rendering.Lod.Native;
using UnityEngine;

internal static partial class Program {
    private static LodLevelSettings TestLevel(
        LodMethod method = LodMethod.EdgeCollapse,
        float ratio = 0.4f,
        int? cap = null,
        bool enabled = true,
        float error = 0.1f,
        float prune = 0.001f) {
        return new LodLevelSettings(enabled, method, ratio, cap, error, prune);
    }

    private static void ValidateAutomaticChains() {
        LodLevelSettings enabled = TestLevel();
        LodLevelSettings disabled = TestLevel(enabled: false);
        foreach (bool firstEnabled in new[] { false, true }) {
            foreach (bool secondEnabled in new[] { false, true }) {
                foreach (int firstCount in new[] { 100, 960, 1000 }) {
                    foreach (int secondCount in new[] { 50, 100, 300, 960, 1000 }) {
                        var profile = new LodProfile(firstEnabled ? enabled : disabled,
                            secondEnabled ? enabled : disabled);
                        LodAutomaticChain chain = LodAutomaticChain.Resolve(
                            profile, 1000, firstCount, secondCount, 0.05f);
                        bool firstUseful = firstEnabled && firstCount <= 950;
                        bool secondUseful = secondEnabled && secondCount <= 950;
                        Check(chain.IncludeLod1 == firstUseful, "Custom LOD1 selection.");
                        Check(chain.IncludeLod2 == (secondUseful &&
                            (!firstUseful || secondCount < firstCount)), "Custom LOD2 selection.");
                        Check(chain.Lod2NotCheaper == (firstUseful && secondUseful &&
                            secondCount >= firstCount), "Non-progressive chain explanation.");
                        Check(!chain.RejectedDefaultChain, "Custom profiles use independent acceptance.");
                        Check(chain.Lod1Status == (!firstEnabled ? LodLevelStatus.Disabled :
                            firstUseful ? LodLevelStatus.Usable : LodLevelStatus.Limited), "LOD1 status.");
                        Check(chain.Lod2Status == (!secondEnabled ? LodLevelStatus.Disabled :
                            secondUseful ? LodLevelStatus.Usable : LodLevelStatus.Limited), "LOD2 status.");
                    }
                }
            }
        }
        foreach (int first in new[] { 100, 960, 1000 }) {
            foreach (int second in new[] { 50, 100, 300, 960, 1000 }) {
                LodAutomaticChain chain = LodAutomaticChain.Resolve(null, 1000, first, second, 0.05f);
                bool valid = first <= 950 && second <= 950 && second < first;
                Check(chain.IncludeLod1 == valid && chain.IncludeLod2 == valid &&
                    chain.RejectedDefaultChain == !valid, "Inherited all-or-nothing chain.");
            }
        }
        var boundary = LodAutomaticChain.Resolve(LodProfile.Default, 100, 95, 94, 0.05f);
        Check(boundary.IncludeLod1 && boundary.IncludeLod2, "Exact reduction threshold is accepted.");
    }

    private static void ValidatePerRequestGeneration(MeshOptimizerNative native) {
        LodMeshData grid = CreateGrid(20, 0f, true, 4);
        var uv1 = new Vector4[grid.VertexCount];
        for (int i = 0; i < uv1.Length; ++i) { uv1[i] = new Vector4(i, -i, i / 3f, 1f); }
        grid = new LodMeshData(grid.Vertices, grid.Normals, grid.Tangents, grid.UV0,
            grid.VertexColors, uv1, grid.TexCoord3, grid.UV0Dimension, grid.Triangles);
        LodSourceSnapshot source = RequestSource(grid, CreateTriangle());
        string original = MeshFingerprint(grid);
        var generator = new LodGenerator(native, LodGenerationSettings.CreateCalibrationBaseline());
        LodLevelSettings disabled = TestLevel(enabled: false);
        LodLevelSettings[] choices = { disabled, TestLevel(), TestLevel(LodMethod.Aggressive) };
        foreach (LodLevelSettings first in choices) {
            foreach (LodLevelSettings choice in choices) {
                var second = new LodLevelSettings(choice.Enabled, choice.Method,
                    0.2f, 70, 0.05f, 0.003f);
                var request = new LodGenerationRequest(source, new LodProfile(first, second));
                LodGenerationResult result = RequireResult(generator.Generate(request));
                Check(ReferenceEquals(result.Profile, request.Profile), "Result retains the request profile.");
                Check(result.Areas.Length == source.Areas.Length, "Every material area is represented.");
                Check(result.Statistics.Lod1TargetTriangles == (first.Enabled ? 320 : 0) &&
                    result.Statistics.Lod2TargetTriangles == (second.Enabled ? 70 : 0),
                    "Independent ratio/cap targets reach the result.");
                Check(result.Statistics.PassThroughAreaLevels + result.Statistics.SimplifiedAreaLevels ==
                    source.Areas.Length * ((first.Enabled ? 1 : 0) + (second.Enabled ? 1 : 0)),
                    "Disabled slots are excluded from area accounting.");
                for (int slot = 0; slot < 2; ++slot) {
                    LodLevelSettings settings = slot == 0 ? first : second;
                    LodLevelMeshResult actual = slot == 0 ? result.Areas[0].Lod1 : result.Areas[0].Lod2;
                    LodLevelMeshResult tiny = slot == 0 ? result.Areas[1].Lod1 : result.Areas[1].Lod2;
                    if (!settings.Enabled) {
                        Check(!actual.Enabled && actual.Mesh == null && !actual.SimplifierRan &&
                            !tiny.Enabled, "Disabled levels have no mesh or simplifier work.");
                        continue;
                    }
                    Check(actual.Method == (settings.Method == LodMethod.EdgeCollapse
                        ? LodSimplificationMethod.PositionFocused : LodSimplificationMethod.Aggressive),
                        "Requested method is used in either slot.");
                    Check(tiny.Enabled && !tiny.Simplified && tiny.Mesh.TriangleCount == 1,
                        "Small material areas survive as pass-through meshes.");
                    var alone = RequireResult(generator.Generate(new LodGenerationRequest(source,
                        new LodProfile(settings, disabled))));
                    Check(MeshFingerprint(actual.Mesh) == MeshFingerprint(alone.Areas[0].Lod1.Mesh) &&
                        actual.RelativeError == alone.Areas[0].Lod1.RelativeError &&
                        actual.TargetTriangleCount == alone.Areas[0].Lod1.TargetTriangleCount,
                        "Generation is independent of the other slot and always uses LOD0.");
                    ValidateLevelMesh(grid, actual.Mesh, false, true);
                    for (int i = 0; i < actual.Mesh.VertexCount; ++i) {
                        int sourceIndex = FindVertex(grid.Vertices, actual.Mesh.Vertices[i]);
                        Check(Equal(actual.Mesh.TexCoord1[i], grid.TexCoord1[sourceIndex]),
                            "Generation retains exact UV1 values.");
                    }
                }
                Check(MeshFingerprint(grid) == original, "Generation leaves source streams unchanged.");
            }
        }

        // Caps below protected area minima are targets, not permission to drop an area.
        var capped = RequireResult(generator.Generate(new LodGenerationRequest(source,
            new LodProfile(TestLevel(cap: 1), TestLevel(LodMethod.Aggressive, cap: 1)))));
        Check(capped.Statistics.Lod1TargetTriangles == 1 && capped.Statistics.Lod2TargetTriangles == 1 &&
            !capped.Statistics.Lod1BudgetMet && !capped.Statistics.Lod2BudgetMet,
            "Missed caps remain visible without deleting protected triangles.");
        Check(capped.Areas[0].Lod1.TargetTriangleCount == 4 &&
            capped.Areas[0].Lod2.TargetTriangleCount == 4 && capped.Areas[1].Lod1.Enabled,
            "Per-area minima apply to either method and slot.");

        var equal = RequireResult(generator.Generate(new LodGenerationRequest(source,
            new LodProfile(TestLevel(), TestLevel()))));
        Check(equal.AutomaticChain.IncludeLod1 && !equal.AutomaticChain.IncludeLod2 &&
            equal.AutomaticChain.Lod2NotCheaper && equal.Areas[0].Lod2.Enabled,
            "An equal-cost candidate is kept for inspection but omitted from Auto.");

        var tinySource = RequestSource(CreateTriangle());
        var limited = RequireResult(generator.Generate(new LodGenerationRequest(tinySource,
            new LodProfile(TestLevel(), disabled))));
        Check(limited.AutomaticChain.Lod1Status == LodLevelStatus.Limited &&
            limited.AutomaticChain.Lod2Status == LodLevelStatus.Disabled && !limited.WorthPublishing,
            "No reduction completes successfully with an original-only chain.");

        var pruned = RequireResult(generator.Generate(new LodGenerationRequest(
            RequestSource(CreateGridWithTinyComponent()), new LodProfile(
                TestLevel(LodMethod.Aggressive, prune: 0.001f), disabled))));
        Check(pruned.Areas[0].Lod1.PrunedTriangleCount > 0 &&
            pruned.Statistics.PrunedTriangles == pruned.Areas[0].Lod1.PrunedTriangleCount &&
            pruned.Statistics.AggressiveAreaLevels == 1,
            "Aggressive diagnostics include LOD1.");

        foreach (LodMethod method in new[] { LodMethod.EdgeCollapse, LodMethod.Aggressive }) {
            foreach (float error in new[] { 0f, 1f }) {
                RequireResult(generator.Generate(new LodGenerationRequest(source, new LodProfile(
                    TestLevel(method, float.Epsilon, 1, error: error, prune: error),
                    TestLevel(method, 0.99999994f, LodLevelSettings.kMaximumTriangleCap,
                        error: error, prune: error)))));
            }
        }
        ValidateGenerationFailures(generator, disabled);
    }

    private static void ValidateGenerationFailures(LodGenerator generator, LodLevelSettings disabled) {
        LodMeshData grid = CreateGrid(8, 0f, false, 2);
        var overflowSource = new LodSourceSnapshot(CreateKey(20),
            new Vector3(float.MaxValue, 1f, 1f), RequestSource(grid).Areas);
        var bothDisabled = RequireResult(generator.Generate(new LodGenerationRequest(overflowSource,
            new LodProfile(disabled, disabled))));
        Check(bothDisabled.Statistics.AdditionalResultBytes == 0 &&
            bothDisabled.Statistics.AttemptedAreaLevels == 0 &&
            bothDisabled.Statistics.Lod1Vertices == 0 && grid.Tangents == null,
            "All-disabled requests skip metrics, tangents and native output allocations.");

        LodGenerationAttempt failed = generator.Generate(new LodGenerationRequest(overflowSource,
            new LodProfile(TestLevel(), disabled)));
        Check(failed.Status == LodGenerationStatus.Failed && failed.Result == null,
            "Derived non-finite positions fail before native generation.");

        var extreme = new[] { new Vector3(-float.MaxValue, 0f, 0f),
            new Vector3(float.MaxValue, 0f, 0f), new Vector3(0f, 1f, 0f) };
        int[] indices = new int[72];
        for (int i = 0; i < indices.Length; ++i) { indices[i] = i % 3; }
        var extremeMesh = new LodMeshData(extreme, null, null, null, null, null, 2, indices);
        failed = generator.Generate(new LodGenerationRequest(RequestSource(grid, extremeMesh),
            new LodProfile(TestLevel(), TestLevel(LodMethod.Aggressive))));
        Check(failed.Status == LodGenerationStatus.Failed && failed.Result == null &&
            failed.Result == null,
            "An invalid later area discards the entire request, including completed levels.");

        int checks = 0;
        LodGenerationAttempt cancelled = generator.Generate(new LodGenerationRequest(RequestSource(grid),
            new LodProfile(TestLevel(), TestLevel(LodMethod.Aggressive))), () => ++checks >= 8);
        Check(cancelled.Status == LodGenerationStatus.Cancelled && cancelled.Result == null && checks >= 8,
            "Cancellation during generation discards all partial output.");
    }

    private static LodGenerationResult RequireResult(LodGenerationAttempt attempt) {
        Check(attempt.Status == LodGenerationStatus.Completed && attempt.Result != null,
            "Request failed: " + attempt.FailureReason);
        return attempt.Result;
    }

    private static LodSourceSnapshot RequestSource(params LodMeshData[] meshes) {
        var areas = new LodAreaSource[meshes.Length];
        for (int i = 0; i < areas.Length; ++i) {
            areas[i] = new LodAreaSource(i, i + 10, i, i != 0, true, i == 0, meshes[i]);
        }
        return new LodSourceSnapshot(CreateKey(10), new Vector3(1f, 1f, 1f), areas);
    }

    private static void ValidateWorkerRequests() {
        using (var engine = new RequestRecordingEngine()) {
            using (var worker = new LodGenerationWorker(engine, 3)) {
                var first = new LodProfile(TestLevel(), TestLevel(enabled: false));
                var second = new LodProfile(TestLevel(enabled: false), TestLevel(LodMethod.Aggressive));
                Check(worker.TrySubmit(CreateCapturedTriangleSource(CreateKey(1)), first, out var active),
                    "First immutable request queued.");
                Check(engine.Started.Wait(3000), "Worker started.");
                Check(worker.TrySubmit(CreateCapturedTriangleSource(CreateKey(2)), second, out var queued),
                    "Second immutable request queued.");
                Check(worker.TrySubmit(CreateCapturedTriangleSource(CreateKey(3)), out var inherited),
                    "Inherited request queued.");
                active.Cancel();
                queued.Cancel();
                engine.Release.Set();
                var completions = new List<LodGenerationCompletion>();
                Check(SpinWait.SpinUntil(() => {
                    while (worker.TryDequeueCompletion(out var completion)) { completions.Add(completion); }
                    return completions.Count == 3;
                }, 3000), "Worker finished cancelled and inherited requests.");
                Check(completions[0].Attempt.Status == LodGenerationStatus.Cancelled &&
                    completions[0].Attempt.Result == null &&
                    completions[1].Attempt.Status == LodGenerationStatus.Cancelled,
                    "Worker discards late success after cancellation, and skips queued cancellation.");
                Check(engine.Requests.Count == 2 && ReferenceEquals(engine.Requests[0].Profile, first) &&
                    engine.Requests[1].UsesInheritedDefaults &&
                    ReferenceEquals(engine.Requests[1].Lod1, LodProfile.Default.Lod1),
                    "Worker snapshots retain settings without leaking custom values into inheritance.");
                Check(worker.OutstandingJobs == 0 && worker.HasCapacity, "Cancelled requests release capacity.");
            }
        }
    }

    private sealed class RequestRecordingEngine : ILodGenerationEngine, IDisposable {
        internal readonly ManualResetEventSlim Started = new ManualResetEventSlim(false);
        internal readonly ManualResetEventSlim Release = new ManualResetEventSlim(false);
        internal readonly List<LodGenerationRequest> Requests = new List<LodGenerationRequest>();

        public LodGenerationAttempt Generate(LodGenerationRequest request, Func<bool> shouldCancel = null) {
            Requests.Add(request);
            Started.Set();
            if (!Release.Wait(3000)) { throw new TimeoutException("Test did not release worker."); }
            return CreateCompletedAttempt(request.Source);
        }

        public void Dispose() {
            Release.Set();
            Started.Dispose();
            Release.Dispose();
        }
    }
}
