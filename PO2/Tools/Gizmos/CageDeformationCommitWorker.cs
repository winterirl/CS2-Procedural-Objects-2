using System;
using UnityEngine;
using PO2.Core.Deformation;
using PO2.Core.Operations;
using PO2.History;
using PO2.Localization;
using PO2.Persistence.Format;
using PO2.Rendering;

namespace PO2.Tools.Gizmos
{
    internal sealed class CageDeformationPartWorkItem
    {
        internal readonly int PartIndex;
        internal readonly DeformationBaseline Baseline;
        internal readonly Vector3[] StartPositions;
        internal readonly Vector3[] StartNormals;
        internal readonly Vector4[] StartTangents;
        internal readonly Vector3[] FinalPositions;
        internal readonly Vector3[] FinalNormals;
        internal readonly Vector4[] FinalTangents;

        internal CageDeformationPartWorkItem(int partIndex, PartGeometrySnapshot snapshot, DeformationBaseline baseline)
        {
            if (partIndex < 0 || baseline == null || baseline.PartIndex != partIndex || snapshot == null || snapshot.PositionsOnly)
            {
                throw new ArgumentException("A deformation worker needs a full part snapshot.");
            }

            PartIndex = partIndex;
            Baseline = baseline;
            StartPositions = snapshot.Positions;
            StartNormals = snapshot.Normals;
            StartTangents = snapshot.Tangents;
            FinalPositions = snapshot.WorkPositions;
            FinalNormals = snapshot.WorkNormals;
            FinalTangents = snapshot.WorkTangents;
        }
    }

    internal sealed class CageDeformationPartCommitResult
    {
        internal readonly int PartIndex;
        internal readonly byte[] RawDelta;
        internal readonly ulong BeforeHash;
        internal readonly ulong AfterHash;
        internal readonly bool Changed;

        internal CageDeformationPartCommitResult(int partIndex, byte[] rawDelta, ulong beforeHash, ulong afterHash, bool changed)
        {
            PartIndex = partIndex;
            RawDelta = rawDelta;
            BeforeHash = beforeHash;
            AfterHash = afterHash;
            Changed = changed;
        }
    }

    internal sealed class CageDeformationCommitResult
    {
        internal byte[] CagePayload;
        internal readonly bool Success;
        internal readonly LocalizedText FailureReason;
        internal readonly CageDeformationPartCommitResult[] Parts;
        internal readonly DeformationRendererPreparation RendererPreparation;

        private CageDeformationCommitResult(bool success, LocalizedText failureReason, CageDeformationPartCommitResult[] parts, DeformationRendererPreparation rendererPreparation)
        {
            Success = success;
            FailureReason = failureReason;
            Parts = parts;
            RendererPreparation = rendererPreparation;
        }

        internal static CageDeformationCommitResult Passed(CageDeformationPartCommitResult[] parts, DeformationRendererPreparation rendererPreparation)
        {
            return new CageDeformationCommitResult(true, LocalizedText.Empty, parts, rendererPreparation);
        }

        internal static CageDeformationCommitResult Failed(LocalizedText failureReason)
        {
            return new CageDeformationCommitResult(false, failureReason, null, null);
        }
    }

    // The worker only reads detached start/final streams and immutable index
    // maps. Live PO state and Unity objects remain on the main thread.
    internal static class CageDeformationCommitWorker
    {
        internal static CageDeformationCommitResult RunCage(CageDeformationPartWorkItem[] parts, DeformationRecipe recipe, DeformationRendererWorkItem rendererWork, DeformationCage transitionPayload = null, bool bake = false)
        {
            var result = Run(parts, rendererWork, part =>
            {
                if (bake)
                {
                    if (part.StartNormals != null) Array.Copy(part.StartNormals, part.FinalNormals, part.StartNormals.Length);
                    if (part.StartTangents != null) Array.Copy(part.StartTangents, part.FinalTangents, part.StartTangents.Length);
                    return LocalizedText.Empty;
                }

                return recipe.Directions(part.Baseline, part.FinalNormals, part.FinalTangents, out LocalizedText reason) ? LocalizedText.Empty : reason;
            });
            if (result.Success && transitionPayload != null)
            {
                try
                {
                    result.CagePayload = DeformationCodec.Encode(transitionPayload);
                }
                catch (Exception e)
                {
                    return CageDeformationCommitResult.Failed(LocalizedText.FromException(e));
                }
            }

            return result;
        }

        private static CageDeformationCommitResult Run(CageDeformationPartWorkItem[] parts, DeformationRendererWorkItem rendererWork, Func<CageDeformationPartWorkItem, LocalizedText> calculateDirections)
        {
            if (parts == null || parts.Length == 0 || calculateDirections == null)
            {
                return CageDeformationCommitResult.Failed(LocalizedText.For("PO2.Object.Deformation.Error.WorkerEmpty"));
            }

            try
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    CageDeformationPartWorkItem part = parts[i];
                    if (part == null)
                    {
                        return CageDeformationCommitResult.Failed(LocalizedText.For("PO2.Object.Deformation.Error.WorkerSnapshotMissing", ("index", i)));
                    }

                    LocalizedText failureReason = calculateDirections(part);
                    if (!failureReason.IsEmpty)
                    {
                        return CageDeformationCommitResult.Failed(failureReason);
                    }
                }

                var partResults = new CageDeformationPartCommitResult[parts.Length];

                for (int i = 0; i < parts.Length; i++)
                {
                    CageDeformationPartWorkItem part = parts[i];
                    var start = new GeometryStreams(part.StartPositions, part.StartNormals, part.StartTangents);
                    var final = new GeometryStreams(part.FinalPositions, part.FinalNormals, part.FinalTangents);
                    byte[] rawDelta = start.CreateRawXor(final, out ulong beforeHash, out ulong afterHash, out bool changed);

                    partResults[i] = new CageDeformationPartCommitResult(part.PartIndex, rawDelta, beforeHash, afterHash, changed);
                }

                DeformationRendererPreparation rendererPreparation = rendererWork?.Build(parts);

                return CageDeformationCommitResult.Passed(partResults, rendererPreparation);
            }
            catch (Exception exception)
            {
                return CageDeformationCommitResult.Failed(LocalizedText.For("PO2.Object.Deformation.Error.WorkerFailed", ("reason", LocalizedText.FromException(exception))));
            }
        }
    }
}