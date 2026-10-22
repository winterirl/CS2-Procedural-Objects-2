using System;
using System.Diagnostics;
using System.Threading;
using PO2;
using PO2.Core.Lod;
using PO2.Rendering.Lod.Native;
using UnityEngine;
using PO2.Rendering.Lod;

internal static partial class Program {
    private static int s_Checks;

    private static void Check(bool condition, string message) {
        ++s_Checks;
        if (!condition) {
            throw new Exception(message);
        }
    }

    private static void Main(string[] args) {
        ValidateLodProfiles();
        ValidateUV1Streams();
        ValidateAutomaticChains();
        ValidateWorkerRequests();
        ValidatePreviewRendering();
        ValidatePreviewScheduling();
        ValidateCorePreviewLifecycle();
        ValidateLodWorkspace();
        if (Array.IndexOf(args, "--managed-streams") >= 0) {
            ValidateInputContracts();
            Console.WriteLine("PASS: " + s_Checks + " managed LOD stream checks.");
            return;
        }
        MeshOptimizerNative native = LoadNativeForTests(args);

        ValidateInputContracts();
        ValidateMetricPacking();
        ValidateCompaction();
        LodGenerationStatistics statistics = ValidateGeneration(native);
        ValidateSmallRequiredTangentPassThrough(native);
        ValidateSplitVertexGeneration(native);
        ValidateCappedLod2Target(native);
        ValidateAggressivePruning(native);
        ValidateCancellation(native);
        ValidatePerRequestGeneration(native);
        ValidateNativeFailures(native);
        ValidateWorkerCapacity();
        ValidateCaptureBudgetAndSelectionDeferral();
        ValidateGenerationBackpressure();
        ValidateStaleResultRejection();
        ValidateNonBlockingShutdown();

        Console.WriteLine("PASS: " + s_Checks + " LOD generation and scheduler checks.");
    }

    private static void ValidateInputContracts() {
        bool mismatchedStreamRejected = false;
        try {
            new LodMeshData(
                new[] {
                    new Vector3(0.0f, 0.0f, 0.0f),
                    new Vector3(1.0f, 0.0f, 0.0f),
                    new Vector3(0.0f, 1.0f, 0.0f),
                },
                new[] { new Vector3(0.0f, 1.0f, 0.0f) },
                null,
                null,
                null,
                null,
                2,
                new[] { 0, 1, 2 });
        }
        catch (ArgumentException) {
            mismatchedStreamRejected = true;
        }
        Check(mismatchedStreamRejected, "Mismatched streams were accepted.");

        bool invalidIndexRejected = false;
        try {
            new LodMeshData(
                new[] {
                    new Vector3(0.0f, 0.0f, 0.0f),
                    new Vector3(1.0f, 0.0f, 0.0f),
                    new Vector3(0.0f, 1.0f, 0.0f),
                },
                null,
                null,
                null,
                null,
                null,
                2,
                new[] { 0, 1, 3 });
        }
        catch (ArgumentException) {
            invalidIndexRejected = true;
        }
        Check(invalidIndexRejected, "Out-of-range indices were accepted.");

        bool nonFinitePositionRejected = false;
        try {
            new LodMeshData(
                new[] {
                    new Vector3(float.NaN, 0.0f, 0.0f),
                    new Vector3(1.0f, 0.0f, 0.0f),
                    new Vector3(0.0f, 1.0f, 0.0f),
                },
                null,
                null,
                null,
                null,
                null,
                2,
                new[] { 0, 1, 2 });
        }
        catch (ArgumentException) {
            nonFinitePositionRejected = true;
        }
        Check(nonFinitePositionRejected, "Non-finite positions were accepted.");

        bool invalidSettingsRejected = false;
        try {
            new LodLevelSettings(true, LodMethod.EdgeCollapse, 1.0f, null, 0.005f, 0f);
        }
        catch (ArgumentOutOfRangeException) {
            invalidSettingsRejected = true;
        }
        Check(invalidSettingsRejected, "Invalid quality settings were accepted.");


    }

    private static void ValidateMetricPacking() {
        LodMeshData mesh = CreateGrid(
            cellsPerAxis: 2,
            xOffset: 0.0f,
            authoredTangents: true,
            uvDimension: 4);
        LodMetricData metric = LodMeshMath.BuildMetric(
            mesh,
            new Vector3(2.0f, 3.0f, 0.5f),
            LodGenerationSettings.CreateCalibrationBaseline());

        Check(metric.Positions.Length == mesh.VertexCount * 3,
            "Metric position packing length is wrong.");
        Check(metric.AttributeCount == 19,
            "Metric did not pack every renderer stream.");
        Check(metric.Attributes.Length ==
            mesh.VertexCount * metric.AttributeCount,
            "Metric attribute packing length is wrong.");
        Check(metric.AttributeWeights.Length == metric.AttributeCount,
            "Metric weight count is wrong.");
        Check(NearlyEqual(metric.PositionExtent, 4.0f),
            "Metric did not incorporate object scale.");
        for (int i = 0; i < metric.AttributeWeights.Length; ++i) {
            Check(IsFinite(metric.AttributeWeights[i]) &&
                metric.AttributeWeights[i] > 0.0f,
                "Metric produced an invalid attribute weight.");
        }

        LodMetricData positionMetric = LodMeshMath.BuildPositionMetric(
            mesh,
            new Vector3(2.0f, 3.0f, 0.5f));
        Check(positionMetric.Positions.Length == mesh.VertexCount * 3 &&
            positionMetric.AttributeCount == 0 &&
            positionMetric.Attributes.Length == 0 &&
            positionMetric.AttributeWeights.Length == 0 &&
            NearlyEqual(positionMetric.PositionExtent, 4.0f),
            "Position-only LOD metric packing is wrong.");

        LodMetricData uvMetric = LodMeshMath.BuildUvMetric(
            mesh,
            new Vector3(2.0f, 3.0f, 0.5f),
            LodGenerationSettings.CreateCalibrationBaseline());
        Check(uvMetric.Positions.Length == mesh.VertexCount * 3 &&
            uvMetric.AttributeCount == mesh.UV0Dimension &&
            uvMetric.Attributes.Length ==
                mesh.VertexCount * mesh.UV0Dimension &&
            uvMetric.AttributeWeights.Length == mesh.UV0Dimension &&
            NearlyEqual(uvMetric.PositionExtent, 4.0f),
            "UV0-focused LOD metric packing is wrong.");
        for (int i = 0; i < uvMetric.AttributeWeights.Length; ++i) {
            Check(NearlyEqual(uvMetric.AttributeWeights[i], 0.01f),
                "UV0-focused LOD metric used the wrong weight.");
        }
        for (int vertexIndex = 0;
            vertexIndex < mesh.VertexCount;
            ++vertexIndex) {
            int attributeOffset = vertexIndex * uvMetric.AttributeCount;
            Check(NearlyEqual(
                    uvMetric.Attributes[attributeOffset],
                    mesh.UV0[vertexIndex].x) &&
                NearlyEqual(
                    uvMetric.Attributes[attributeOffset + 1],
                    mesh.UV0[vertexIndex].y) &&
                NearlyEqual(
                    uvMetric.Attributes[attributeOffset + 2],
                    mesh.UV0[vertexIndex].z) &&
                NearlyEqual(
                    uvMetric.Attributes[attributeOffset + 3],
                    mesh.UV0[vertexIndex].w),
                "UV0-focused LOD metric changed a UV value.");
        }
    }

    private static void ValidateCompaction() {
        LodMeshData source = CreateGrid(
            cellsPerAxis: 1,
            xOffset: 0.0f,
            authoredTangents: true,
            uvDimension: 4);
        int[] selectedTriangle = {
            source.Triangles[3],
            source.Triangles[4],
            source.Triangles[5],
        };
        LodMeshData compacted = LodMeshCompactor.Compact(
            source,
            selectedTriangle,
            selectedTriangle.Length,
            false,
            false);

        Check(compacted.VertexCount == 3,
            "Compaction retained unused vertices.");
        Check(compacted.TriangleCount == 1,
            "Compaction changed the triangle count.");
        Check(compacted.Triangles[0] == 0 &&
            compacted.Triangles[1] == 1 &&
            compacted.Triangles[2] == 2,
            "Compaction did not remap in first-use order.");
        for (int i = 0; i < 3; ++i) {
            int sourceIndex = selectedTriangle[i];
            Check(Equal(compacted.Vertices[i], source.Vertices[sourceIndex]),
                "Compaction changed a source position.");
            Check(Equal(compacted.UV0[i], source.UV0[sourceIndex]),
                "Compaction changed a source UV.");
            Check(Equal(
                compacted.VertexColors[i],
                source.VertexColors[sourceIndex]),
                "Compaction changed a source color.");
        }
    }

    private static LodGenerationStatistics ValidateGeneration(
        MeshOptimizerNative native) {
        LodMeshData authoredMesh = CreateGrid(
            cellsPerAxis: 64,
            xOffset: -24.0f,
            authoredTangents: true,
            uvDimension: 4);
        LodMeshData generatedTangentMesh = CreateGrid(
            cellsPerAxis: 64,
            xOffset: 4.0f,
            authoredTangents: false,
            uvDimension: 2);
        LodMeshData tinyMesh = CreateTriangle();
        int[] originalAuthoredIndices =
            (int[])authoredMesh.Triangles.Clone();
        int[] originalGeneratedIndices =
            (int[])generatedTangentMesh.Triangles.Clone();

        LodAreaSource[] areas = {
            new LodAreaSource(
                0,
                7,
                0,
                false,
                true,
                true,
                authoredMesh),
            new LodAreaSource(
                1,
                12,
                1,
                true,
                true,
                false,
                generatedTangentMesh),
            new LodAreaSource(
                2,
                99,
                2,
                false,
                false,
                true,
                tinyMesh),
        };
        LodGenerationKey key = new LodGenerationKey(
            17,
            203,
            new Guid("7f6a4309-c98a-41cf-8018-b03064c49b18"),
            42);
        LodSourceSnapshot snapshot = new LodSourceSnapshot(
            key,
            new Vector3(2.0f, 0.75f, 0.5f),
            areas);
        LodGenerator generator = new LodGenerator(
            native,
            LodGenerationSettings.CreateCalibrationBaseline());

        LodGenerationAttempt first = generator.Generate(snapshot);
        Check(first.Status == LodGenerationStatus.Completed,
            "LOD generation failed: " + first.FailureReason);
        Check(first.Result != null, "Completed generation has no result.");
        Check(first.Result.Key == key, "Generation key was not preserved.");
        Check(first.Result.Areas.Length == areas.Length,
            "Area result count changed.");
        Check(first.Result.WorthPublishing,
            "The reducible quality corpus was rejected: source " +
            first.Result.Statistics.SourceTriangles + ", LOD1 " +
            first.Result.Statistics.Lod1Triangles + "/" +
            first.Result.Statistics.Lod1TargetTriangles + ", LOD2 " +
            first.Result.Statistics.Lod2Triangles + "/" +
            first.Result.Statistics.Lod2TargetTriangles +
            ", simplified/attempted " +
            first.Result.Statistics.SimplifiedAreaLevels + "/" +
            first.Result.Statistics.AttemptedAreaLevels + ".");
        Check(first.Result.Statistics.Lod1Triangles <
            first.Result.Statistics.SourceTriangles,
            "LOD1 did not reduce the object.");
        Check(first.Result.Statistics.Lod2Triangles <
            first.Result.Statistics.Lod1Triangles,
            "LOD2 did not reduce LOD1.");
        Check(first.Result.Statistics.Lod1TargetTriangles == 6554 &&
            first.Result.Statistics.Lod2TargetTriangles == 1500,
            "Object-level triangle targets are wrong.");
        Check(first.Result.Statistics.Lod1BudgetMet &&
            !first.Result.Statistics.Lod2BudgetMet &&
            first.Result.Statistics.BudgetMissedAreaLevels == 2,
            "Quality corpus budget accounting is wrong.");
        Check(first.Result.Statistics.AggressiveAreaLevels == 2,
            "LOD2 did not use the aggressive path for eligible areas.");
        Check(first.Result.Lod1MaximumRelativeError > 0.0f &&
            first.Result.Lod1MaximumRelativeError <= 0.005f &&
            first.Result.Lod2MaximumRelativeError > 0.0f &&
            first.Result.Lod2MaximumRelativeError <= 0.01f,
            "LOD generation did not retain its reported relative errors.");
        LodObjectDiagnostics objectDiagnostics =
            new LodObjectDiagnostics(first.Result);
        Check(objectDiagnostics.SourceRevision == key.SourceRevision &&
            objectDiagnostics.SourceTriangles ==
                first.Result.Statistics.SourceTriangles &&
            objectDiagnostics.Lod1Triangles ==
                first.Result.Statistics.Lod1Triangles &&
            objectDiagnostics.Lod2Triangles ==
                first.Result.Statistics.Lod2Triangles &&
            objectDiagnostics.Lod1UsedPositionFocusedSimplification &&
            objectDiagnostics.Lod2UsedAggressiveSimplification,
            "Per-object LOD diagnostics lost generation measurements.");
        Check(first.Result.Statistics.AdditionalResultBytes > 0,
            "Additional LOD memory estimate was not populated.");
        Check(first.Result.Statistics.AttemptedAreaLevels == 4 &&
            first.Result.Statistics.SimplifiedAreaLevels == 4 &&
            first.Result.Statistics.LimitedAreaLevels == 0,
            "Quality-corpus level accounting is wrong.");
        Check(first.Result.Statistics.Lod1SimplifierOutputTriangles ==
                first.Result.Statistics.Lod1Triangles &&
            first.Result.Statistics.Lod2SimplifierOutputTriangles ==
                first.Result.Statistics.Lod2Triangles,
            "Quality-corpus simplifier output accounting is wrong.");
        Check(Equal(authoredMesh.Triangles, originalAuthoredIndices),
            "Native generation mutated authored source indices.");
        Check(Equal(generatedTangentMesh.Triangles, originalGeneratedIndices),
            "Native generation mutated generated-tangent source indices.");
        Check(ReferenceEquals(first.Result.Areas[0].Source, areas[0]) &&
            first.Result.Areas[0].Source.SurfaceAreaId == 7 &&
            first.Result.Areas[1].Source.IsolatedRenderer,
            "Area renderer metadata was not preserved.");
        Check(ReferenceEquals(first.Result.Areas[2].Lod1.Mesh, tinyMesh) &&
            ReferenceEquals(first.Result.Areas[2].Lod2.Mesh, tinyMesh),
            "Small areas should pass through without allocations.");

        ValidateLevelMesh(
            authoredMesh,
            first.Result.Areas[0].Lod1.Mesh,
            false,
            true);
        // Both UV0-focused LOD1 and aggressive LOD2 change topology, so
        // their normal and tangent bases must be rebuilt.
        ValidateLevelMesh(
            authoredMesh,
            first.Result.Areas[0].Lod2.Mesh,
            false,
            true);
        ValidateLevelMesh(
            generatedTangentMesh,
            first.Result.Areas[1].Lod1.Mesh,
            false,
            true);
        ValidateLevelMesh(
            generatedTangentMesh,
            first.Result.Areas[1].Lod2.Mesh,
            false,
            true);

        LodGenerationAttempt second = generator.Generate(snapshot);
        Check(second.Status == LodGenerationStatus.Completed,
            "Repeated generation failed: " + second.FailureReason);
        Check(second.Result.WorthPublishing,
            "Repeated quality decision changed.");
        Check(second.Result.Statistics.Lod1Triangles ==
            first.Result.Statistics.Lod1Triangles &&
            second.Result.Statistics.Lod2Triangles ==
            first.Result.Statistics.Lod2Triangles,
            "Repeated triangle counts are not deterministic.");
        for (int areaIndex = 0; areaIndex < areas.Length; ++areaIndex) {
            Check(Equal(
                first.Result.Areas[areaIndex].Lod1.Mesh.Triangles,
                second.Result.Areas[areaIndex].Lod1.Mesh.Triangles),
                "LOD1 indices are not deterministic.");
            Check(Equal(
                first.Result.Areas[areaIndex].Lod2.Mesh.Triangles,
                second.Result.Areas[areaIndex].Lod2.Mesh.Triangles),
                "LOD2 indices are not deterministic.");
        }

        return first.Result.Statistics;
    }

    private static void ValidateSmallRequiredTangentPassThrough(
        MeshOptimizerNative native) {
        LodMeshData mesh = CreateGrid(
            cellsPerAxis: 1,
            xOffset: 0.0f,
            authoredTangents: false,
            uvDimension: 2);
        LodGenerator generator = new LodGenerator(
            native,
            LodGenerationSettings.CreateCalibrationBaseline());
        LodGenerationAttempt attempt = generator.Generate(
            new LodSourceSnapshot(
                new LodGenerationKey(
                    18,
                    205,
                    new Guid("41a04559-96d4-40ef-830d-2d1e4c231a7f"),
                    43),
                new Vector3(1.0f, 1.0f, 1.0f),
                new[] {
                    new LodAreaSource(
                        0,
                        3,
                        0,
                        false,
                        true,
                        true,
                        mesh),
                }));

        Check(attempt.Status == LodGenerationStatus.Completed,
            "Small tangent pass-through generation failed.");
        Check(!attempt.Result.WorthPublishing,
            "A tiny tangent pass-through mesh became publishable.");
        Check(attempt.Result.Areas[0].Lod1.Method ==
                LodSimplificationMethod.PassThrough &&
            attempt.Result.Areas[0].Lod2.Method ==
                LodSimplificationMethod.PassThrough,
            "A tiny tangent mesh did not use pass-through levels.");
        Check(attempt.Result.Areas[0].Lod1.Mesh.Tangents != null &&
            attempt.Result.Areas[0].Lod1.Mesh.Tangents.Length ==
                mesh.VertexCount &&
            attempt.Result.Areas[0].Lod2.Mesh.Tangents != null &&
            attempt.Result.Areas[0].Lod2.Mesh.Tangents.Length ==
                mesh.VertexCount,
            "Required pass-through tangents were not generated off-thread.");
    }

    private static void ValidateSplitVertexGeneration(
        MeshOptimizerNative native) {
        const int cellsPerAxis = 20;
        LodMeshData mesh = CreateSplitGrid(
            cellsPerAxis,
            xOffset: 0.0f,
            authoredTangents: true,
            uvDimension: 4);
        LodSourceSnapshot snapshot = new LodSourceSnapshot(
            new LodGenerationKey(
                19,
                207,
                new Guid("4b38dd1e-3025-44cf-b652-ae7a46f72bc1"),
                43),
            new Vector3(2.0f, 1.0f, 0.5f),
            new[] {
                new LodAreaSource(
                    0,
                    17,
                    0,
                    false,
                    true,
                    true,
                    mesh),
            });
        LodGenerator generator = new LodGenerator(
            native,
            LodGenerationSettings.CreateCalibrationBaseline());

        LodGenerationAttempt attempt = generator.Generate(
            new LodGenerationRequest(snapshot, CreateSplitVertexProfile()));
        Check(attempt.Status == LodGenerationStatus.Completed,
            "Split-vertex generation failed: " + attempt.FailureReason);
        Check(attempt.Result != null && attempt.Result.WorthPublishing,
            "Split-vertex mesh did not produce a publishable chain.");
        Check(attempt.Result.Statistics.Lod1Triangles <
                attempt.Result.Statistics.SourceTriangles &&
            attempt.Result.Statistics.Lod2Triangles <
                attempt.Result.Statistics.Lod1Triangles,
            "Split-vertex chain did not reduce both levels.");
        Check(attempt.Result.Statistics.AttemptedAreaLevels == 2 &&
            attempt.Result.Statistics.SimplifiedAreaLevels == 2 &&
            attempt.Result.Statistics.LimitedAreaLevels == 0,
            "Split-vertex level accounting is wrong.");
        LodLevelMeshResult lod1 = attempt.Result.Areas[0].Lod1;
        LodLevelMeshResult lod2 = attempt.Result.Areas[0].Lod2;
        Check(lod1.SimplifierRan && lod2.SimplifierRan &&
            lod1.Method == LodSimplificationMethod.PositionFocused &&
            lod2.Method == LodSimplificationMethod.Aggressive &&
            lod1.TargetTriangleCount == 320 &&
            lod2.TargetTriangleCount == 80,
            "Split-vertex target accounting is wrong.");
        Check(lod1.SimplifierOutputTriangleCount ==
                lod1.Mesh.TriangleCount &&
            lod2.SimplifierOutputTriangleCount ==
                lod2.Mesh.TriangleCount,
            "Split-vertex raw output accounting is wrong.");

        Check(lod2.Mesh.TriangleCount <= 80,
            "Aggressive split-vertex LOD2 missed its target.");
    }

    private static void ValidateAggressivePruning(
        MeshOptimizerNative native) {
        LodMeshData mesh = CreateGridWithTinyComponent();
        LodSourceSnapshot snapshot = new LodSourceSnapshot(
            new LodGenerationKey(
                23,
                211,
                new Guid("56cbd597-4d29-4fe9-8d0b-ec8b65162698"),
                44),
            new Vector3(1.0f, 1.0f, 1.0f),
            new[] {
                new LodAreaSource(
                    0,
                    21,
                    0,
                    false,
                    true,
                    true,
                    mesh),
            });
        LodGenerator generator = new LodGenerator(
            native,
            LodGenerationSettings.CreateCalibrationBaseline());

        LodGenerationAttempt attempt = generator.Generate(snapshot);
        Check(attempt.Status == LodGenerationStatus.Completed,
            "Aggressive pruning generation failed: " +
            attempt.FailureReason);
        Check(attempt.Result != null &&
            attempt.Result.Statistics.PrunedTriangles >= 1,
            "LOD2 did not report its removed tiny component.");
        Check(attempt.Result.Areas[0].Lod2.PrunedTriangleCount >= 1,
            "LOD2 area did not retain its pruning count.");
    }

    private static LodProfile CreateSplitVertexProfile() {
        // This coarse, split corpus needs a looser error to exercise seam handling.
        return new LodProfile(LodProfile.Default.Lod1,
            new LodLevelSettings(true, LodMethod.Aggressive, 0.10f, 1500, 0.10f, 0.001f));
    }

    private static void ValidateCappedLod2Target(
        MeshOptimizerNative native) {
        LodMeshData mesh = CreateGrid(
            cellsPerAxis: 88,
            xOffset: 0.0f,
            authoredTangents: true,
            uvDimension: 2);
        LodSourceSnapshot snapshot = new LodSourceSnapshot(
            new LodGenerationKey(
                23,
                212,
                new Guid("6015f151-9730-4ef3-b957-26761259543c"),
                45),
            new Vector3(1.0f, 1.0f, 1.0f),
            new[] {
                new LodAreaSource(
                    0,
                    22,
                    0,
                    false,
                    true,
                    true,
                    mesh),
            });
        LodGenerator generator = new LodGenerator(
            native,
            LodGenerationSettings.CreateCalibrationBaseline());

        LodGenerationAttempt attempt = generator.Generate(snapshot);
        Check(attempt.Status == LodGenerationStatus.Completed,
            "Capped LOD2 generation failed: " + attempt.FailureReason);
        Check(attempt.Result != null &&
            attempt.Result.Statistics.SourceTriangles > 15000 &&
            attempt.Result.Statistics.Lod2TargetTriangles == 1500,
            "LOD2 did not apply the object-level triangle cap.");
        Check(attempt.Result.Areas[0].Lod2.Method ==
                LodSimplificationMethod.Aggressive &&
            attempt.Result.Areas[0].Lod2.TargetTriangleCount == 1500,
            "The capped LOD2 target did not reach the aggressive area.");
    }

    private static void ValidateCancellation(MeshOptimizerNative native) {
        LodMeshData mesh = CreateGrid(
            cellsPerAxis: 20,
            xOffset: 0.0f,
            authoredTangents: true,
            uvDimension: 2);
        LodSourceSnapshot snapshot = new LodSourceSnapshot(
            new LodGenerationKey(1, 2, Guid.NewGuid(), 3),
            new Vector3(1.0f, 1.0f, 1.0f),
            new[] {
                new LodAreaSource(
                    0,
                    0,
                    0,
                    false,
                    true,
                    true,
                    mesh),
            });
        LodGenerator generator = new LodGenerator(
            native,
            LodGenerationSettings.CreateCalibrationBaseline());
        LodGenerationAttempt attempt = generator.Generate(
            snapshot,
            () => true);
        Check(attempt.Status == LodGenerationStatus.Cancelled,
            "Cancellation did not stop generation.");
        Check(attempt.Result == null,
            "Cancelled generation retained a publishable result.");
    }

    private static void ValidateWorkerCapacity() {
        using (BlockingGenerationEngine engine =
            new BlockingGenerationEngine(ignoreCancellation: true)) {
            LodGenerationWorker worker = new LodGenerationWorker(
                engine,
                maximumOutstandingJobs: 2);
            try {
                Check(worker.TrySubmit(
                        CreateCapturedTriangleSource(CreateKey(1)),
                        out LodCancellationSignal _),
                    "Worker rejected its first request.");
                Check(engine.WaitUntilStarted(),
                    "Worker did not start its first request.");
                Check(worker.TrySubmit(
                        CreateCapturedTriangleSource(CreateKey(2)),
                        out LodCancellationSignal _),
                    "Worker rejected its second outstanding request.");
                Check(!worker.TrySubmit(
                        CreateCapturedTriangleSource(CreateKey(3)),
                        out LodCancellationSignal _),
                    "Worker exceeded its outstanding request limit.");
                Check(worker.OutstandingJobs == 2,
                    "Worker outstanding accounting is wrong.");

                engine.Release();
                Check(SpinWait.SpinUntil(
                        () => worker.TryDequeueCompletion(
                            out LodGenerationCompletion _),
                        3000),
                    "Worker did not return a completion.");
                Check(worker.HasCapacity,
                    "Draining a completion did not restore worker capacity.");
            }
            finally {
                worker.Dispose();
                engine.Release();
            }
        }
    }

    private static void ValidateCaptureBudgetAndSelectionDeferral() {
        CountingGenerationEngine engine = new CountingGenerationEngine();
        LodSchedulerSettings settings = new LodSchedulerSettings(
            maximumOutstandingJobs: 2,
            maximumCaptureBytesPerUpdate: 32,
            maximumCompletionsPerUpdate: 2);
        LodGenerationCoordinator coordinator =
            new LodGenerationCoordinator(
                new LodGenerationWorker(
                    engine,
                    settings.MaximumOutstandingJobs),
                settings);
        try {
            int objectId = 51;
            Guid persistentId =
                new Guid("28221ba0-f436-450f-a5de-7d037dce6e27");
            coordinator.SetSelectedObject(objectId);
            coordinator.ObserveObject(
                objectId,
                persistentId,
                new Vector3(1.0f, 1.0f, 1.0f),
                sourceContentChanged: false);
            coordinator.Update(
                key => new SteppedCapture(key, 96),
                result => { });
            Check(engine.CallCount == 0,
                "A selected object entered LOD generation.");

            coordinator.SetSelectedObject(null);
            coordinator.Update(
                key => new SteppedCapture(key, 96),
                result => { });
            LodSchedulerDiagnostics first =
                coordinator.CaptureDiagnostics();
            Check(first.CaptureActive && first.CapturedBytes == 32,
                "Capture did not stop at its first update budget.");

            coordinator.Update(
                key => new SteppedCapture(key, 96),
                result => { });
            LodSchedulerDiagnostics second =
                coordinator.CaptureDiagnostics();
            Check(second.CaptureActive && second.CapturedBytes == 64,
                "Capture did not stop at its second update budget.");

            int accepted = 0;
            coordinator.Update(
                key => new SteppedCapture(key, 96),
                result => accepted++);
            Check(coordinator.CaptureDiagnostics().SubmittedJobs == 1,
                "Completed capture was not submitted exactly once.");
            Check(SpinWait.SpinUntil(() => engine.CallCount == 1, 3000),
                "Budgeted capture did not reach the worker.");
            Check(SpinWait.SpinUntil(() => {
                    coordinator.Update(
                        key => new SteppedCapture(key, 96),
                        result => accepted++);
                    return accepted == 1;
                }, 3000),
                "Current scheduler result was not accepted.");
            LodSchedulerDiagnostics acceptedDiagnostics =
                coordinator.CaptureDiagnostics();
            Check(acceptedDiagnostics.AcceptedResults == 1 &&
                acceptedDiagnostics.PublishableResults == 0 &&
                acceptedDiagnostics.PassThroughResults == 1,
                "Scheduler result-quality accounting is wrong.");
            Check(acceptedDiagnostics.HasLastAcceptedResult &&
                !acceptedDiagnostics.LastAcceptedWorthPublishing,
                "Scheduler did not retain the last result state.");
            Check(coordinator.TryCaptureObjectDiagnostics(
                    objectId,
                    out LodObjectDiagnostics objectDiagnostics) &&
                objectDiagnostics.SourceRevision > 0 &&
                !objectDiagnostics.WorthPublishing,
                "Scheduler did not retain per-object LOD diagnostics.");

            coordinator.ObserveObject(
                objectId,
                persistentId,
                new Vector3(1.0f, 1.0f, 1.0f),
                sourceContentChanged: true);
            Check(!coordinator.TryCaptureObjectDiagnostics(
                    objectId,
                    out LodObjectDiagnostics _),
                "Changed geometry retained stale per-object diagnostics.");
        }
        finally {
            coordinator.Dispose();
        }
    }

    private static void ValidateGenerationBackpressure() {
        CountingGenerationEngine engine = new CountingGenerationEngine();
        LodSchedulerSettings settings = new LodSchedulerSettings(
            maximumOutstandingJobs: 2,
            maximumCaptureBytesPerUpdate: 1024,
            maximumCompletionsPerUpdate: 2);
        LodGenerationCoordinator coordinator =
            new LodGenerationCoordinator(
                new LodGenerationWorker(
                    engine,
                    settings.MaximumOutstandingJobs),
                settings);
        try {
            coordinator.ObserveObject(
                61,
                new Guid("0997b9ee-3e9d-4517-b7ea-cda66b7b960a"),
                new Vector3(1.0f, 1.0f, 1.0f),
                sourceContentChanged: false);
            coordinator.ObserveObject(
                62,
                new Guid("2ed5b504-633e-45e9-9bd0-132cecd210e7"),
                new Vector3(1.0f, 1.0f, 1.0f),
                sourceContentChanged: false);

            bool canAdvance = true;
            int accepted = 0;
            coordinator.Update(
                key => new SteppedCapture(key, 64),
                result => {
                    accepted++;
                    canAdvance = false;
                },
                () => canAdvance);
            Check(SpinWait.SpinUntil(() => engine.CallCount == 1, 3000),
                "Backpressure test did not start its first job.");

            Check(SpinWait.SpinUntil(() => {
                    coordinator.Update(
                        key => new SteppedCapture(key, 64),
                        result => {
                            accepted++;
                            canAdvance = false;
                        },
                        () => canAdvance);
                    return accepted == 1;
                }, 3000),
                "Backpressure test did not accept its first result.");
            LodSchedulerDiagnostics paused =
                coordinator.CaptureDiagnostics();
            Check(paused.CapturesStarted == 1 &&
                paused.SubmittedJobs == 1,
                "A new capture started while publication applied backpressure.");

            canAdvance = true;
            coordinator.Update(
                key => new SteppedCapture(key, 64),
                result => accepted++,
                () => canAdvance);
            Check(coordinator.CaptureDiagnostics().CapturesStarted == 2,
                "Generation did not resume after publication backpressure.");
        }
        finally {
            coordinator.Dispose();
        }
    }

    private static void ValidateStaleResultRejection() {
        using (BlockingGenerationEngine engine =
            new BlockingGenerationEngine(ignoreCancellation: true)) {
            LodSchedulerSettings settings = new LodSchedulerSettings(
                maximumOutstandingJobs: 2,
                maximumCaptureBytesPerUpdate: 1024,
                maximumCompletionsPerUpdate: 2);
            LodGenerationCoordinator coordinator =
                new LodGenerationCoordinator(
                    new LodGenerationWorker(
                        engine,
                        settings.MaximumOutstandingJobs),
                    settings);
            try {
                int accepted = 0;
                int objectId = 73;
                Guid persistentId =
                    new Guid("82ef9a52-991c-44a8-ac23-bc8c16b56037");
                coordinator.ObserveObject(
                    objectId,
                    persistentId,
                    new Vector3(1.0f, 1.0f, 1.0f),
                    sourceContentChanged: false);
                coordinator.Update(
                    key => new SteppedCapture(key, 64),
                    result => accepted++);
                Check(engine.WaitUntilStarted(),
                    "Stale-result test did not start generation.");

                coordinator.ObserveObject(
                    objectId,
                    persistentId,
                    new Vector3(2.0f, 1.0f, 1.0f),
                    sourceContentChanged: true);
                engine.Release();

                Check(SpinWait.SpinUntil(() => {
                        coordinator.Update(
                            key => new SteppedCapture(key, 64),
                            result => accepted++);
                        LodSchedulerDiagnostics diagnostics =
                            coordinator.CaptureDiagnostics();
                        return accepted == 1 &&
                            diagnostics.StaleResults >= 1 &&
                            diagnostics.SubmittedJobs >= 2;
                    }, 3000),
                    "A stale result was accepted or its replacement was lost.");
                Check(accepted == 1,
                    "Scheduler accepted more than the current source revision.");

                coordinator.ResetWorld();
                LodSchedulerDiagnostics reset =
                    coordinator.CaptureDiagnostics();
                Check(reset.TrackedObjects == 0 && !reset.CaptureActive,
                    "World reset retained scheduler object state.");
            }
            finally {
                coordinator.Dispose();
                engine.Release();
            }
        }
    }

    private static void ValidateNonBlockingShutdown() {
        using (BlockingGenerationEngine engine =
            new BlockingGenerationEngine(ignoreCancellation: true)) {
            LodGenerationWorker worker = new LodGenerationWorker(
                engine,
                maximumOutstandingJobs: 1);
            Check(worker.TrySubmit(
                    CreateCapturedTriangleSource(CreateKey(91)),
                    out LodCancellationSignal _),
                "Shutdown test request was rejected.");
            Check(engine.WaitUntilStarted(),
                "Shutdown test worker did not start.");

            var shutdown = System.Threading.Tasks.Task.Run(worker.Dispose);
            try {
                Check(shutdown.Wait(TimeSpan.FromSeconds(5)),
                    "Worker disposal must finish while generation is still blocked.");
            }
            finally {
                engine.Release();
                shutdown.GetAwaiter().GetResult();
            }

        }
    }

    private static LodGenerationKey CreateKey(int objectId) {
        byte[] guidBytes = new byte[16];
        BitConverter.GetBytes(objectId).CopyTo(guidBytes, 0);
        return new LodGenerationKey(
            1,
            objectId,
            new Guid(guidBytes),
            (ulong)objectId);
    }

    private static LodCapturedSource CreateCapturedTriangleSource(
        LodGenerationKey key) {
        LodMeshData mesh = CreateTriangle();
        LodCapturedMeshData capturedMesh = new LodCapturedMeshData(
            mesh.Vertices,
            mesh.Normals,
            mesh.Tangents,
            mesh.UV0,
            mesh.VertexColors,
            mesh.TexCoord3,
            mesh.UV0Dimension,
            mesh.Triangles);
        return new LodCapturedSource(
            key,
            new Vector3(1.0f, 1.0f, 1.0f),
            new[] {
                new LodCapturedAreaSource(
                    0,
                    0,
                    0,
                    false,
                    false,
                    true,
                    capturedMesh),
            });
    }

    private static LodGenerationAttempt CreateCompletedAttempt(
        LodSourceSnapshot snapshot, LodProfile profile = null) {
        return LodGenerationAttempt.Completed(
            new LodGenerationResult(
                snapshot.Key,
                Array.Empty<LodAreaResult>(),
                default(LodGenerationStatistics),
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                profile,
                default(LodAutomaticChain)));
    }

    private sealed class CountingGenerationEngine :
        ILodGenerationEngine {
        private int m_CallCount;

        internal int CallCount => Volatile.Read(ref m_CallCount);

        public LodGenerationAttempt Generate(
            LodGenerationRequest request,
            Func<bool> shouldCancel = null) {
            LodSourceSnapshot snapshot = request.Source;
            Interlocked.Increment(ref m_CallCount);
            return shouldCancel != null && shouldCancel()
                ? LodGenerationAttempt.Cancelled()
                : CreateCompletedAttempt(snapshot, request.Profile);
        }
    }

    private sealed class BlockingGenerationEngine :
        ILodGenerationEngine,
        IDisposable {
        private readonly bool m_IgnoreCancellation;
        private readonly ManualResetEventSlim m_Started =
            new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim m_Release =
            new ManualResetEventSlim(false);

        internal BlockingGenerationEngine(bool ignoreCancellation) {
            m_IgnoreCancellation = ignoreCancellation;
        }

        public LodGenerationAttempt Generate(
            LodGenerationRequest request,
            Func<bool> shouldCancel = null) {
            LodSourceSnapshot snapshot = request.Source;
            m_Started.Set();
            while (!m_Release.Wait(1)) {
                if (!m_IgnoreCancellation &&
                    shouldCancel != null && shouldCancel()) {
                    return LodGenerationAttempt.Cancelled();
                }
            }
            return CreateCompletedAttempt(snapshot, request.Profile);
        }

        internal bool WaitUntilStarted() {
            return m_Started.Wait(3000);
        }

        internal void Release() {
            m_Release.Set();
        }

        public void Dispose() {
            m_Release.Set();
            m_Started.Dispose();
            m_Release.Dispose();
        }
    }

    private sealed class SteppedCapture : ILodSourceCapture {
        private readonly long m_TotalBytes;

        internal SteppedCapture(
            LodGenerationKey key,
            long totalBytes) {
            Key = key;
            m_TotalBytes = totalBytes;
        }

        public LodGenerationKey Key { get; }
        public long TotalBytes => m_TotalBytes;
        public long CopiedBytes { get; private set; }
        public bool IsComplete => CopiedBytes == TotalBytes;

        public void CopyNext(int maximumBytes) {
            CopiedBytes = Math.Min(
                TotalBytes,
                CopiedBytes + maximumBytes);
        }

        public LodCapturedSource Complete() {
            if (!IsComplete) {
                throw new InvalidOperationException(
                    "Test capture completed before its budgeted copies.");
            }
            return CreateCapturedTriangleSource(Key);
        }
    }

    private static void ValidateLevelMesh(
        LodMeshData source,
        LodMeshData result,
        bool expectPreservedTangents,
        bool expectRebuiltTangents) {
        Check(result.VertexCount <= source.VertexCount,
            "A LOD gained vertices.");
        Check(result.TriangleCount < source.TriangleCount,
            "A quality-corpus LOD did not reduce triangles.");
        Check(result.Normals != null &&
            result.Normals.Length == result.VertexCount,
            "LOD normal stream is invalid.");
        Check(result.UV0 != null &&
            result.UV0.Length == result.VertexCount,
            "LOD UV0 stream is invalid.");
        Check(result.VertexColors != null &&
            result.VertexColors.Length == result.VertexCount,
            "LOD color stream is invalid.");
        Check(result.TexCoord3 != null &&
            result.TexCoord3.Length == result.VertexCount,
            "LOD TexCoord3 stream is invalid.");
        Check(result.Tangents != null &&
            result.Tangents.Length == result.VertexCount,
            "LOD tangent stream is invalid.");

        for (int i = 0; i < result.Triangles.Length; ++i) {
            Check(result.Triangles[i] >= 0 &&
                result.Triangles[i] < result.VertexCount,
                "LOD contains an invalid compacted index.");
        }

        for (int resultIndex = 0;
            resultIndex < result.VertexCount;
            ++resultIndex) {
            int sourceIndex = FindVertex(
                source.Vertices,
                result.Vertices[resultIndex]);
            Check(sourceIndex >= 0,
                "LOD introduced a position not present in LOD0.");
            Check(Equal(result.Normals[resultIndex], source.Normals[sourceIndex]),
                "LOD changed a retained source normal.");
            Check(Equal(result.UV0[resultIndex], source.UV0[sourceIndex]),
                "LOD changed a retained source UV.");
            Check(Equal(
                result.VertexColors[resultIndex],
                source.VertexColors[sourceIndex]),
                "LOD changed a retained source color.");
            Check(Equal(
                result.TexCoord3[resultIndex],
                source.TexCoord3[sourceIndex]),
                "LOD changed a retained source TexCoord3 value.");

            Vector4 tangent = result.Tangents[resultIndex];
            Check(IsFinite(tangent.x) &&
                IsFinite(tangent.y) &&
                IsFinite(tangent.z) &&
                IsFinite(tangent.w),
                "LOD produced a non-finite tangent.");
            if (expectPreservedTangents) {
                Check(Equal(tangent, source.Tangents[sourceIndex]),
                    "LOD changed a source-authored tangent.");
            }
            if (expectRebuiltTangents) {
                float lengthSquared =
                    tangent.x * tangent.x +
                    tangent.y * tangent.y +
                    tangent.z * tangent.z;
                Check(NearlyEqual(lengthSquared, 1.0f),
                    "Rebuilt tangent is not normalized.");
                Check(tangent.w == -1.0f || tangent.w == 1.0f,
                    "Rebuilt tangent handedness is invalid.");
                float normalDotTangent =
                    result.Normals[resultIndex].x * tangent.x +
                    result.Normals[resultIndex].y * tangent.y +
                    result.Normals[resultIndex].z * tangent.z;
                Check(NearlyEqual(normalDotTangent, 0.0f),
                    "Rebuilt tangent is not perpendicular to its normal.");
            }
        }
    }

    private static LodMeshData CreateGrid(
        int cellsPerAxis,
        float xOffset,
        bool authoredTangents,
        int uvDimension) {
        int verticesPerAxis = cellsPerAxis + 1;
        int vertexCount = verticesPerAxis * verticesPerAxis;
        Vector3[] vertices = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector4[] tangents = authoredTangents
            ? new Vector4[vertexCount]
            : null;
        Vector4[] uv0 = new Vector4[vertexCount];
        Color32[] colors = new Color32[vertexCount];
        Vector4[] texCoord3 = new Vector4[vertexCount];

        for (int z = 0; z < verticesPerAxis; ++z) {
            for (int x = 0; x < verticesPerAxis; ++x) {
                int index = z * verticesPerAxis + x;
                float u = x / (float)cellsPerAxis;
                float v = z / (float)cellsPerAxis;
                vertices[index] = new Vector3(
                    xOffset + x,
                    0.0f,
                    z);
                normals[index] = new Vector3(0.0f, 1.0f, 0.0f);
                if (tangents != null) {
                    tangents[index] = new Vector4(
                        1.0f,
                        0.0f,
                        0.0f,
                        1.0f);
                }
                uv0[index] = new Vector4(
                    u,
                    v,
                    uvDimension >= 3 ? u + v : 0.0f,
                    uvDimension >= 4 ? 1.0f : 0.0f);
                colors[index] = new Color32(127, 191, 223, 255);
                texCoord3[index] = new Vector4(u, v, u + v, 1.0f);
            }
        }

        int[] triangles = new int[cellsPerAxis * cellsPerAxis * 6];
        int triangleOffset = 0;
        for (int z = 0; z < cellsPerAxis; ++z) {
            for (int x = 0; x < cellsPerAxis; ++x) {
                int lowerLeft = z * verticesPerAxis + x;
                int lowerRight = lowerLeft + 1;
                int upperLeft = lowerLeft + verticesPerAxis;
                int upperRight = upperLeft + 1;
                triangles[triangleOffset++] = lowerLeft;
                triangles[triangleOffset++] = upperLeft;
                triangles[triangleOffset++] = lowerRight;
                triangles[triangleOffset++] = lowerRight;
                triangles[triangleOffset++] = upperLeft;
                triangles[triangleOffset++] = upperRight;
            }
        }

        return new LodMeshData(
            vertices,
            normals,
            tangents,
            uv0,
            colors,
            texCoord3,
            uvDimension,
            triangles);
    }

    private static LodMeshData CreateSplitGrid(
        int cellsPerAxis,
        float xOffset,
        bool authoredTangents,
        int uvDimension) {
        LodMeshData shared = CreateGrid(
            cellsPerAxis,
            xOffset,
            authoredTangents,
            uvDimension);
        int vertexCount = shared.Triangles.Length;
        Vector3[] vertices = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector4[] tangents = authoredTangents
            ? new Vector4[vertexCount]
            : null;
        Vector4[] uv0 = new Vector4[vertexCount];
        Color32[] colors = new Color32[vertexCount];
        Vector4[] texCoord3 = new Vector4[vertexCount];
        int[] triangles = new int[vertexCount];

        for (int i = 0; i < vertexCount; ++i) {
            int sourceIndex = shared.Triangles[i];
            vertices[i] = shared.Vertices[sourceIndex];
            normals[i] = shared.Normals[sourceIndex];
            if (tangents != null) {
                tangents[i] = shared.Tangents[sourceIndex];
            }
            uv0[i] = shared.UV0[sourceIndex];
            colors[i] = shared.VertexColors[sourceIndex];
            texCoord3[i] = shared.TexCoord3[sourceIndex];
            triangles[i] = i;
        }

        return new LodMeshData(
            vertices,
            normals,
            tangents,
            uv0,
            colors,
            texCoord3,
            uvDimension,
            triangles);
    }

    private static LodMeshData CreateGridWithTinyComponent() {
        LodMeshData grid = CreateGrid(
            cellsPerAxis: 20,
            xOffset: 0.0f,
            authoredTangents: true,
            uvDimension: 2);
        int firstTinyVertex = grid.VertexCount;
        int vertexCount = grid.VertexCount + 3;
        Vector3[] vertices = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector4[] tangents = new Vector4[vertexCount];
        Vector4[] uv0 = new Vector4[vertexCount];
        Color32[] colors = new Color32[vertexCount];
        Vector4[] texCoord3 = new Vector4[vertexCount];
        int[] triangles = new int[grid.Triangles.Length + 3];

        Array.Copy(grid.Vertices, vertices, grid.VertexCount);
        Array.Copy(grid.Normals, normals, grid.VertexCount);
        Array.Copy(grid.Tangents, tangents, grid.VertexCount);
        Array.Copy(grid.UV0, uv0, grid.VertexCount);
        Array.Copy(grid.VertexColors, colors, grid.VertexCount);
        Array.Copy(grid.TexCoord3, texCoord3, grid.VertexCount);
        Array.Copy(grid.Triangles, triangles, grid.Triangles.Length);

        vertices[firstTinyVertex] = new Vector3(40.0f, 0.0f, 0.0f);
        vertices[firstTinyVertex + 1] =
            new Vector3(40.001f, 0.0f, 0.0f);
        vertices[firstTinyVertex + 2] =
            new Vector3(40.0f, 0.0f, 0.001f);
        for (int i = firstTinyVertex; i < vertexCount; ++i) {
            normals[i] = new Vector3(0.0f, 1.0f, 0.0f);
            tangents[i] = new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
            uv0[i] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
            colors[i] = new Color32(127, 191, 223, 255);
            texCoord3[i] = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
        }

        triangles[grid.Triangles.Length] = firstTinyVertex;
        triangles[grid.Triangles.Length + 1] = firstTinyVertex + 2;
        triangles[grid.Triangles.Length + 2] = firstTinyVertex + 1;
        return new LodMeshData(
            vertices,
            normals,
            tangents,
            uv0,
            colors,
            texCoord3,
            2,
            triangles);
    }

    private static LodMeshData CreateTriangle() {
        return new LodMeshData(
            new[] {
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(1.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 0.0f, 1.0f),
            },
            new[] {
                new Vector3(0.0f, 1.0f, 0.0f),
                new Vector3(0.0f, 1.0f, 0.0f),
                new Vector3(0.0f, 1.0f, 0.0f),
            },
            null,
            new[] {
                new Vector4(0.0f, 0.0f, 0.0f, 0.0f),
                new Vector4(1.0f, 0.0f, 0.0f, 0.0f),
                new Vector4(0.0f, 1.0f, 0.0f, 0.0f),
            },
            null,
            null,
            2,
            new[] { 0, 1, 2 });
    }

    private static int FindVertex(Vector3[] vertices, Vector3 value) {
        for (int i = 0; i < vertices.Length; ++i) {
            if (Equal(vertices[i], value)) {
                return i;
            }
        }
        return -1;
    }

    private static bool Equal(int[] left, int[] right) {
        if (left == null || right == null || left.Length != right.Length) {
            return false;
        }
        for (int i = 0; i < left.Length; ++i) {
            if (left[i] != right[i]) {
                return false;
            }
        }
        return true;
    }

    private static bool Equal(Vector3 left, Vector3 right) {
        return left.x == right.x && left.y == right.y && left.z == right.z;
    }

    private static bool Equal(Vector4 left, Vector4 right) {
        return left.x == right.x &&
            left.y == right.y &&
            left.z == right.z &&
            left.w == right.w;
    }

    private static bool Equal(Color32 left, Color32 right) {
        return left.r == right.r &&
            left.g == right.g &&
            left.b == right.b &&
            left.a == right.a;
    }

    private static bool NearlyEqual(float left, float right) {
        return Math.Abs(left - right) <= 0.00001f;
    }

    private static bool IsFinite(float value) {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
