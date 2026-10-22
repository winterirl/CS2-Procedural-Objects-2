using System;
using System.Collections.Generic;
using System.Diagnostics;
using PO2.Core.Lod;
using PO2.Rendering.Lod.Native;

namespace PO2.Rendering.Lod
{
    internal interface ILodGenerationEngine
    {
        LodGenerationAttempt Generate(LodGenerationRequest request, Func<bool> shouldCancel = null);
    }

    internal sealed class LodGenerator : ILodGenerationEngine
    {
        private const MeshOptimizerSimplifyOptions kEdgeCollapseOptions = MeshOptimizerSimplifyOptions.LockBorder | MeshOptimizerSimplifyOptions.Permissive;

        private readonly MeshOptimizerNative m_Native;
        private readonly LodGenerationSettings m_Settings;

        internal LodGenerator(MeshOptimizerNative native, LodGenerationSettings settings)
        {
            m_Native = native ?? throw new ArgumentNullException(nameof(native));
            m_Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        internal LodGenerationAttempt Generate(LodSourceSnapshot snapshot, Func<bool> shouldCancel = null)
        {
            return Generate(new LodGenerationRequest(snapshot), shouldCancel);
        }

        public LodGenerationAttempt Generate(LodGenerationRequest request, Func<bool> shouldCancel = null)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            LodSourceSnapshot snapshot = request.Source;
            bool anyEnabled = request.Lod1.Enabled || request.Lod2.Enabled;

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                ThrowIfCancelled(shouldCancel);

                LodAreaResult[] areaResults = new LodAreaResult[snapshot.Areas.Length];
                LodMeshData[] workingMeshes = new LodMeshData[snapshot.Areas.Length];
                int sourceVertices = 0;
                int sourceTriangles = 0;

                for (int areaIndex = 0; areaIndex < snapshot.Areas.Length; ++areaIndex)
                {
                    ThrowIfCancelled(shouldCancel);
                    LodAreaSource area = snapshot.Areas[areaIndex];
                    LodMeshData workingMesh = area.Mesh;
                    bool needsGeneratedTangents = anyEnabled && area.RequiresTangents && workingMesh.Tangents == null;
                    if (needsGeneratedTangents)
                    {
                        UnityEngine.Vector4[] generatedTangents = LodMeshMath.CalculateTangents(workingMesh);
                        if (generatedTangents != null)
                        {
                            workingMesh = workingMesh.WithTangents(generatedTangents);
                        }
                    }

                    workingMeshes[areaIndex] = workingMesh;
                    sourceVertices = checked(sourceVertices + area.Mesh.VertexCount);
                    sourceTriangles = checked(sourceTriangles + area.Mesh.TriangleCount);
                }

                int lod1TargetTriangles = CalculateTargetTriangleCount(sourceTriangles, request.Lod1);
                int lod2TargetTriangles = CalculateTargetTriangleCount(sourceTriangles, request.Lod2);
                int[] lod1AreaTargets = request.Lod1.Enabled ? AllocateAreaTargets(workingMeshes, lod1TargetTriangles) : null;
                int[] lod2AreaTargets = request.Lod2.Enabled ? AllocateAreaTargets(workingMeshes, lod2TargetTriangles) : null;

                int lod1Vertices = 0;
                int lod1Triangles = 0;
                int lod2Vertices = 0;
                int lod2Triangles = 0;
                int simplifiedAreaLevels = 0;
                int attemptedAreaLevels = 0;
                int limitedAreaLevels = 0;
                int budgetMissedAreaLevels = 0;
                int aggressiveAreaLevels = 0;
                int prunedTriangles = 0;
                int lod1SimplifierOutputTriangles = 0;
                int lod2SimplifierOutputTriangles = 0;
                float lod1MaximumRelativeError = 0.0f;
                float lod2MaximumRelativeError = 0.0f;
                float lod1MaximumAbsoluteError = 0.0f;
                float lod2MaximumAbsoluteError = 0.0f;

                for (int areaIndex = 0; areaIndex < snapshot.Areas.Length; ++areaIndex)
                {
                    ThrowIfCancelled(shouldCancel);
                    LodAreaSource area = snapshot.Areas[areaIndex];
                    LodMeshData workingMesh = workingMeshes[areaIndex];
                    bool simplifyLod1 = request.Lod1.Enabled && lod1AreaTargets[areaIndex] < workingMesh.TriangleCount;
                    bool simplifyLod2 = request.Lod2.Enabled && lod2AreaTargets[areaIndex] < workingMesh.TriangleCount;
                    LodMetricData metric = null;
                    if ((simplifyLod1 || simplifyLod2) && workingMesh.TriangleCount >= m_Settings.MinimumSourceTrianglesPerArea)
                    {
                        bool needsUvMetric = (simplifyLod1 && request.Lod1.Method == LodMethod.EdgeCollapse) || (simplifyLod2 && request.Lod2.Method == LodMethod.EdgeCollapse);
                        metric = needsUvMetric ? LodMeshMath.BuildUvMetric(workingMesh, snapshot.ObjectScale, m_Settings) : LodMeshMath.BuildPositionMetric(workingMesh, snapshot.ObjectScale);
                    }

                    // Each level starts from the original area, never the other LOD.
                    LodLevelMeshResult lod1 = GenerateLevel(area, workingMesh, metric, request.Lod1, lod1AreaTargets?[areaIndex] ?? 0, shouldCancel);
                    if (metric != null && metric.AttributeCount != 0 && request.Lod2.Method == LodMethod.Aggressive)
                    {
                        // The remaining native call only needs positions.
                        metric = new LodMetricData(metric.Positions, Array.Empty<float>(), Array.Empty<float>(), 0, metric.PositionExtent);
                    }

                    LodLevelMeshResult lod2 = GenerateLevel(area, workingMesh, metric, request.Lod2, lod2AreaTargets?[areaIndex] ?? 0, shouldCancel);

                    areaResults[areaIndex] = new LodAreaResult(area, lod1, lod2);
                    lod1Vertices = checked(lod1Vertices + (lod1.Mesh?.VertexCount ?? 0));
                    lod1Triangles = checked(lod1Triangles + (lod1.Mesh?.TriangleCount ?? 0));
                    lod2Vertices = checked(lod2Vertices + (lod2.Mesh?.VertexCount ?? 0));
                    lod2Triangles = checked(lod2Triangles + (lod2.Mesh?.TriangleCount ?? 0));
                    simplifiedAreaLevels += lod1.Simplified ? 1 : 0;
                    simplifiedAreaLevels += lod2.Simplified ? 1 : 0;
                    attemptedAreaLevels += lod1.SimplifierRan ? 1 : 0;
                    attemptedAreaLevels += lod2.SimplifierRan ? 1 : 0;
                    limitedAreaLevels += lod1.SimplifierRan && !lod1.Simplified ? 1 : 0;
                    limitedAreaLevels += lod2.SimplifierRan && !lod2.Simplified ? 1 : 0;
                    budgetMissedAreaLevels += lod1.SimplifierRan && !lod1.BudgetMet ? 1 : 0;
                    budgetMissedAreaLevels += lod2.SimplifierRan && !lod2.BudgetMet ? 1 : 0;
                    aggressiveAreaLevels += lod1.UsedAggressiveSimplification ? 1 : 0;
                    aggressiveAreaLevels += lod2.UsedAggressiveSimplification ? 1 : 0;
                    prunedTriangles = checked(prunedTriangles + lod1.PrunedTriangleCount + lod2.PrunedTriangleCount);
                    lod1SimplifierOutputTriangles += lod1.SimplifierOutputTriangleCount;
                    lod2SimplifierOutputTriangles += lod2.SimplifierOutputTriangleCount;
                    lod1MaximumRelativeError = Math.Max(lod1MaximumRelativeError, lod1.RelativeError);
                    lod2MaximumRelativeError = Math.Max(lod2MaximumRelativeError, lod2.RelativeError);
                    lod1MaximumAbsoluteError = Math.Max(lod1MaximumAbsoluteError, lod1.AbsoluteError);
                    lod2MaximumAbsoluteError = Math.Max(lod2MaximumAbsoluteError, lod2.AbsoluteError);
                }

                ThrowIfCancelled(shouldCancel);
                stopwatch.Stop();

                int totalAreaLevels = checked(snapshot.Areas.Length * ((request.Lod1.Enabled ? 1 : 0) + (request.Lod2.Enabled ? 1 : 0)));
                LodAutomaticChain chain = LodAutomaticChain.Resolve(request.Profile, sourceTriangles, lod1Triangles, lod2Triangles, m_Settings.MinimumWholeObjectTriangleReduction);
                long additionalResultBytes = EstimateAdditionalResultBytes(snapshot.Areas, areaResults);
                LodGenerationStatistics statistics = new LodGenerationStatistics(sourceVertices, sourceTriangles, lod1Vertices, lod1Triangles, lod2Vertices, lod2Triangles, lod1TargetTriangles, lod2TargetTriangles, simplifiedAreaLevels, totalAreaLevels - simplifiedAreaLevels, attemptedAreaLevels, limitedAreaLevels, budgetMissedAreaLevels, aggressiveAreaLevels, prunedTriangles, lod1SimplifierOutputTriangles, lod2SimplifierOutputTriangles, additionalResultBytes, stopwatch.Elapsed.TotalMilliseconds);
                return LodGenerationAttempt.Completed(new LodGenerationResult(snapshot.Key, areaResults, statistics, lod1MaximumRelativeError, lod2MaximumRelativeError, lod1MaximumAbsoluteError, lod2MaximumAbsoluteError, request.Profile, chain));
            }
            catch (OperationCanceledException)
            {
                return LodGenerationAttempt.Cancelled();
            }
            catch (Exception exception)
            {
                return LodGenerationAttempt.Failed(exception.GetType().Name + ": " + exception.Message);
            }
        }

        private LodLevelMeshResult GenerateLevel(LodAreaSource area, LodMeshData source, LodMetricData metric, LodLevelSettings settings, int targetTriangleCount, Func<bool> shouldCancel)
        {
            ThrowIfCancelled(shouldCancel);
            if (!settings.Enabled)
            {
                return LodLevelMeshResult.Disabled;
            }

            switch (settings.Method)
            {
                case LodMethod.EdgeCollapse:
                    return GeneratePositionFocusedLevel(source, metric, area.AllowVertexCacheOptimization, area.RequiresTangents, targetTriangleCount, settings.MaximumRelativeError, shouldCancel);
                case LodMethod.Aggressive:
                    return GenerateAggressiveLevel(source, metric?.Positions, metric?.PositionExtent ?? 0.0f, area.AllowVertexCacheOptimization, area.RequiresTangents, targetTriangleCount, settings.MaximumRelativeError, settings.PruneRelativeError, shouldCancel);
                default:
                    throw new ArgumentOutOfRangeException(nameof(settings.Method));
            }
        }

        private unsafe LodLevelMeshResult GeneratePositionFocusedLevel(LodMeshData source, LodMetricData metric, bool optimizeVertexCache, bool requiresTangents, int targetTriangleCount, float maximumRelativeError, Func<bool> shouldCancel)
        {
            ThrowIfCancelled(shouldCancel);
            if (source.TriangleCount < m_Settings.MinimumSourceTrianglesPerArea)
            {
                return new LodLevelMeshResult(source, false, 0.0f, 0.0f, LodSimplificationMethod.PassThrough, source.TriangleCount, source.TriangleCount, 0);
            }

            if (targetTriangleCount >= source.TriangleCount)
            {
                return new LodLevelMeshResult(source, false, 0.0f, 0.0f, LodSimplificationMethod.PassThrough, targetTriangleCount, source.TriangleCount, 0);
            }

            if (metric == null || metric.PositionExtent <= 0.0f)
            {
                return new LodLevelMeshResult(source, false, 0.0f, 0.0f, LodSimplificationMethod.PassThrough, targetTriangleCount, source.TriangleCount, 0);
            }

            int[] simplifiedIndices = new int[source.Triangles.Length];
            MeshOptimizerStatus simplifyStatus;
            ulong resultIndexCount;
            float resultError;
            ThrowIfCancelled(shouldCancel);
            fixed (int* destination = simplifiedIndices)
            fixed (int* indices = source.Triangles)
            fixed (float* positions = metric.Positions)
            {
                if (metric.AttributeCount == 0)
                {
                    simplifyStatus = m_Native.Simplify((IntPtr)destination, (ulong)simplifiedIndices.Length, (IntPtr)indices, (ulong)source.Triangles.Length, (IntPtr)positions, (ulong)source.VertexCount, sizeof(float) * 3, IntPtr.Zero, 0, IntPtr.Zero, 0, IntPtr.Zero, (ulong)checked(targetTriangleCount * 3), maximumRelativeError, kEdgeCollapseOptions, out resultIndexCount, out resultError);
                }
                else
                {
                    fixed (float* attributes = metric.Attributes)
                    fixed (float* weights = metric.AttributeWeights)
                    {
                        simplifyStatus = m_Native.Simplify((IntPtr)destination, (ulong)simplifiedIndices.Length, (IntPtr)indices, (ulong)source.Triangles.Length, (IntPtr)positions, (ulong)source.VertexCount, sizeof(float) * 3, (IntPtr)attributes, (ulong)(sizeof(float) * metric.AttributeCount), (IntPtr)weights, (uint)metric.AttributeCount, IntPtr.Zero, (ulong)checked(targetTriangleCount * 3), maximumRelativeError, kEdgeCollapseOptions, out resultIndexCount, out resultError);
                    }
                }
            }

            if (simplifyStatus != MeshOptimizerStatus.Success)
            {
                throw new InvalidOperationException("meshoptimizer position and UV0 simplification " + "failed with " + MeshOptimizerNative.GetStatusName(simplifyStatus) + ".");
            }

            if (resultIndexCount == 0 || resultIndexCount > (ulong)simplifiedIndices.Length || resultIndexCount > int.MaxValue || resultIndexCount % 3 != 0)
            {
                throw new InvalidOperationException("meshoptimizer returned an invalid index count.");
            }

            ValidateNativeError(resultError);
            ThrowIfCancelled(shouldCancel);
            int simplifiedIndexCount = (int)resultIndexCount;
            int simplifiedTriangleCount = simplifiedIndexCount / 3;
            float reduction = (source.TriangleCount - simplifiedTriangleCount) / (float)source.TriangleCount;
            if (reduction < m_Settings.MinimumLevelTriangleReduction)
            {
                return new LodLevelMeshResult(source, false, 0.0f, 0.0f, LodSimplificationMethod.PositionFocused, targetTriangleCount, simplifiedTriangleCount, 0);
            }

            if (optimizeVertexCache)
            {
                OptimizeVertexCache(simplifiedIndices, simplifiedIndexCount, source.VertexCount);
            }

            ThrowIfCancelled(shouldCancel);
            bool recalculateNormals = source.Normals != null;
            bool recalculateLodTangents = requiresTangents && source.UV0 != null && source.Normals != null;
            LodMeshData compacted = LodMeshCompactor.Compact(source, simplifiedIndices, simplifiedIndexCount, recalculateNormals, recalculateLodTangents);
            float absoluteError = resultError * metric.PositionExtent;
            return new LodLevelMeshResult(compacted, true, resultError, absoluteError, LodSimplificationMethod.PositionFocused, targetTriangleCount, simplifiedTriangleCount, 0);
        }

        private unsafe LodLevelMeshResult GenerateAggressiveLevel(LodMeshData source, float[] scaledPositions, float positionExtent, bool optimizeVertexCache, bool requiresTangents, int targetTriangleCount, float maximumRelativeError, float pruneRelativeError, Func<bool> shouldCancel)
        {
            ThrowIfCancelled(shouldCancel);
            if (source.TriangleCount < m_Settings.MinimumSourceTrianglesPerArea || targetTriangleCount >= source.TriangleCount || scaledPositions == null || positionExtent <= 0.0f)
            {
                return new LodLevelMeshResult(source, false, 0.0f, 0.0f, LodSimplificationMethod.PassThrough, targetTriangleCount, source.TriangleCount, 0);
            }

            int[] simplifiedIndices = new int[source.Triangles.Length];
            MeshOptimizerStatus simplifyStatus;
            ulong prunedIndexCount;
            ulong resultIndexCount;
            float resultError;
            ThrowIfCancelled(shouldCancel);
            fixed (int* destination = simplifiedIndices)
            fixed (int* indices = source.Triangles)
            fixed (float* positions = scaledPositions)
            {
                simplifyStatus = m_Native.SimplifyAggressive((IntPtr)destination, (ulong)simplifiedIndices.Length, (IntPtr)indices, (ulong)source.Triangles.Length, (IntPtr)positions, (ulong)source.VertexCount, sizeof(float) * 3, (ulong)checked(targetTriangleCount * 3), maximumRelativeError, pruneRelativeError, out prunedIndexCount, out resultIndexCount, out resultError);
            }

            if (simplifyStatus != MeshOptimizerStatus.Success)
            {
                throw new InvalidOperationException("meshoptimizer aggressive simplification failed with " + MeshOptimizerNative.GetStatusName(simplifyStatus) + ".");
            }

            if (prunedIndexCount == 0 || prunedIndexCount > (ulong)source.Triangles.Length || prunedIndexCount > int.MaxValue || prunedIndexCount % 3 != 0 || resultIndexCount == 0 || resultIndexCount > (ulong)simplifiedIndices.Length || resultIndexCount > int.MaxValue || resultIndexCount % 3 != 0)
            {
                throw new InvalidOperationException("meshoptimizer returned invalid aggressive counts.");
            }

            ValidateNativeError(resultError);
            ThrowIfCancelled(shouldCancel);
            int simplifiedIndexCount = (int)resultIndexCount;
            int simplifiedTriangleCount = simplifiedIndexCount / 3;
            int prunedTriangleCount = source.TriangleCount - (int)prunedIndexCount / 3;
            float reduction = (source.TriangleCount - simplifiedTriangleCount) / (float)source.TriangleCount;
            if (reduction < m_Settings.MinimumLevelTriangleReduction)
            {
                return new LodLevelMeshResult(source, false, 0.0f, 0.0f, LodSimplificationMethod.Aggressive, targetTriangleCount, simplifiedTriangleCount, prunedTriangleCount);
            }

            if (optimizeVertexCache)
            {
                OptimizeVertexCache(simplifiedIndices, simplifiedIndexCount, source.VertexCount);
            }

            ThrowIfCancelled(shouldCancel);
            bool recalculateNormals = source.Normals != null;
            bool recalculateLodTangents = requiresTangents && source.UV0 != null && source.Normals != null;
            LodMeshData compacted = LodMeshCompactor.Compact(source, simplifiedIndices, simplifiedIndexCount, recalculateNormals, recalculateLodTangents);
            float absoluteError = resultError * positionExtent;
            return new LodLevelMeshResult(compacted, true, resultError, absoluteError, LodSimplificationMethod.Aggressive, targetTriangleCount, simplifiedTriangleCount, prunedTriangleCount);
        }

        private unsafe void OptimizeVertexCache(int[] indices, int indexCount, int vertexCount)
        {
            fixed (int* indexPointer = indices)
            {
                MeshOptimizerStatus optimizeStatus = m_Native.OptimizeVertexCache((IntPtr)indexPointer, (ulong)indices.Length, (IntPtr)indexPointer, (ulong)indexCount, (ulong)vertexCount);
                if (optimizeStatus != MeshOptimizerStatus.Success)
                {
                    throw new InvalidOperationException("meshoptimizer vertex-cache optimization failed " + "with " + MeshOptimizerNative.GetStatusName(optimizeStatus) + ".");
                }
            }
        }

        private int[] AllocateAreaTargets(LodMeshData[] meshes, int objectTargetTriangles)
        {
            int[] targets = new int[meshes.Length];
            bool[] eligible = new bool[meshes.Length];
            long eligibleSourceTriangles = 0;
            int eligibleAreaCount = 0;
            int fixedTriangles = 0;

            for (int i = 0; i < meshes.Length; ++i)
            {
                int triangleCount = meshes[i].TriangleCount;
                targets[i] = triangleCount;
                if (triangleCount >= m_Settings.MinimumSourceTrianglesPerArea)
                {
                    eligible[i] = true;
                    eligibleSourceTriangles += triangleCount;
                    eligibleAreaCount++;
                }
                else
                {
                    fixedTriangles = checked(fixedTriangles + triangleCount);
                }
            }

            if (eligibleAreaCount == 0)
            {
                return targets;
            }

            int minimumPerArea = m_Settings.MinimumTargetTrianglesPerArea;
            long minimumEligibleTriangles = (long)eligibleAreaCount * minimumPerArea;
            long requestedEligibleTriangles = Math.Max(minimumEligibleTriangles, (long)objectTargetTriangles - fixedTriangles);
            long eligibleBudget = Math.Min(eligibleSourceTriangles, requestedEligibleTriangles);
            long distributable = eligibleBudget - minimumEligibleTriangles;
            long distributed = 0;
            long[] remainders = new long[meshes.Length];

            for (int i = 0; i < meshes.Length; ++i)
            {
                if (!eligible[i])
                {
                    continue;
                }

                long weighted = distributable * meshes[i].TriangleCount;
                int share = (int)(weighted / eligibleSourceTriangles);
                targets[i] = minimumPerArea + share;
                remainders[i] = weighted % eligibleSourceTriangles;
                distributed += share;
            }

            int leftovers = (int)(distributable - distributed);
            bool[] receivedRemainder = new bool[meshes.Length];
            while (leftovers > 0)
            {
                int bestIndex = -1;
                for (int i = 0; i < meshes.Length; ++i)
                {
                    if (!eligible[i] || receivedRemainder[i])
                    {
                        continue;
                    }

                    if (bestIndex < 0 || remainders[i] > remainders[bestIndex] || (remainders[i] == remainders[bestIndex] && meshes[i].TriangleCount > meshes[bestIndex].TriangleCount))
                    {
                        bestIndex = i;
                    }
                }

                if (bestIndex < 0)
                {
                    break;
                }

                targets[bestIndex]++;
                receivedRemainder[bestIndex] = true;
                leftovers--;
            }

            return targets;
        }

        private static int CalculateTargetTriangleCount(int sourceTriangles, LodLevelSettings settings)
        {
            if (!settings.Enabled)
            {
                return 0;
            }

            // Keep float multiplication here for parity with the shipped defaults.
            int ratioTarget = Math.Max(1, checked((int)Math.Floor(sourceTriangles * settings.TriangleRatio)));
            return Math.Min(ratioTarget, settings.TriangleCap ?? int.MaxValue);
        }

        private static void ValidateNativeError(float error)
        {
            if (float.IsNaN(error) || float.IsInfinity(error) || error < 0.0f)
            {
                throw new InvalidOperationException("meshoptimizer returned an invalid error estimate.");
            }
        }

        private static void ThrowIfCancelled(Func<bool> shouldCancel)
        {
            if (shouldCancel != null && shouldCancel())
            {
                throw new OperationCanceledException();
            }
        }

        private static long EstimateAdditionalResultBytes(LodAreaSource[] sources, LodAreaResult[] results)
        {
            HashSet<Array> sourceArrays = new HashSet<Array>();
            HashSet<Array> countedResultArrays = new HashSet<Array>();
            for (int i = 0; i < sources.Length; ++i)
            {
                AddMeshArrays(sourceArrays, sources[i].Mesh);
            }

            long bytes = 0L;
            for (int i = 0; i < results.Length; ++i)
            {
                bytes += AddResultMeshArrays(sourceArrays, countedResultArrays, results[i].Lod1.Mesh);
                bytes += AddResultMeshArrays(sourceArrays, countedResultArrays, results[i].Lod2.Mesh);
            }

            return bytes;
        }

        private static long AddResultMeshArrays(HashSet<Array> sourceArrays, HashSet<Array> countedResultArrays, LodMeshData mesh)
        {
            if (mesh == null)
            {
                return 0L;
            }

            long bytes = 0L;
            bytes += AddArray(sourceArrays, countedResultArrays, mesh.Vertices, 12);
            bytes += AddArray(sourceArrays, countedResultArrays, mesh.Normals, 12);
            bytes += AddArray(sourceArrays, countedResultArrays, mesh.Tangents, 16);
            bytes += AddArray(sourceArrays, countedResultArrays, mesh.UV0, 16);
            bytes += AddArray(sourceArrays, countedResultArrays, mesh.VertexColors, 4);
            bytes += AddArray(sourceArrays, countedResultArrays, mesh.TexCoord1, 16);
            bytes += AddArray(sourceArrays, countedResultArrays, mesh.TexCoord3, 16);
            bytes += AddArray(sourceArrays, countedResultArrays, mesh.Triangles, sizeof(int));
            return bytes;
        }

        private static long AddArray(HashSet<Array> sourceArrays, HashSet<Array> countedResultArrays, Array values, int elementBytes)
        {
            if (values == null || sourceArrays.Contains(values) || !countedResultArrays.Add(values))
            {
                return 0L;
            }

            return (long)values.Length * elementBytes;
        }

        private static void AddMeshArrays(HashSet<Array> arrays, LodMeshData mesh)
        {
            AddArray(arrays, mesh.Vertices);
            AddArray(arrays, mesh.Normals);
            AddArray(arrays, mesh.Tangents);
            AddArray(arrays, mesh.UV0);
            AddArray(arrays, mesh.VertexColors);
            AddArray(arrays, mesh.TexCoord1);
            AddArray(arrays, mesh.TexCoord3);
            AddArray(arrays, mesh.Triangles);
        }

        private static void AddArray(HashSet<Array> arrays, Array values)
        {
            if (values != null)
            {
                arrays.Add(values);
            }
        }
    }
}