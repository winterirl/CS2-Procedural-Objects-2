using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using PO2.Core;
using PO2.Core.Deformation;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;

namespace PO2.Tools.Gizmos
{
    internal sealed class CageDeformationDragState
    {
        private ProceduralObject m_Object;
        private ProceduralObjectSystem m_System;
        private HistoryEdit m_History;
        private PartGeometrySnapshot[] m_Snapshots;
        private PartDeformationDeltaAction[] m_Actions;
        private MeshPart[] m_Parts;
        private DeformationAction m_CageAction;
        private DeformationCage m_Start;
        private DeformationRecipe m_Recipe;
        private ObjectTransform m_Transform;
        private uint m_PartVersion, m_DeformationVersion;
        private bool m_Created, m_Remove, m_Bake;
        private Task<CageDeformationCommitResult> m_Completion;
        internal DeformationCage Cage { get; private set; }
        internal int ObjectId => m_Object?.Id ?? -1;
        internal int PartIndex => Cage?.PartIndex ?? -1;
        internal bool IsCaptured => m_History != null;
        internal bool IsCompletionPending => m_Completion != null;
        internal LocalizedText CompletionStatus => LocalizedText.For("PO2.Object.Deformation.Status.Finishing");

        internal bool TryBegin(ProceduralObject obj, int partIndex, Matrix4x4 reference, ProceduralObjectSystem system, LocalizedText description, bool remove, bool bake, out LocalizedText reason)
        {
            Reset();
            reason = LocalizedText.Empty;
            if (obj == null || obj.IsLocked || system == null || system.ActionHistory.IsBusy || partIndex < -1 || partIndex >= obj.Parts.Count || obj.Parts.Count == 0)
            {
                reason = LocalizedText.For("PO2.Object.Deformation.Error.TargetUnavailable");
                return false;
            }

            if (!obj.CanEditDeformationScope(partIndex, out reason)) return false;
            var existing = obj.GetDeformation(partIndex);
            if (remove && existing == null)
            {
                reason = LocalizedText.For("PO2.Object.Deformation.Error.NoPendingChanges");
                return false;
            }

            int count = partIndex < 0 ? obj.Parts.Count : 1;
            var snapshots = new PartGeometrySnapshot[count];
            var actions = new PartDeformationDeltaAction[count];
            var parts = new MeshPart[count];
            try
            {
                for (int i = 0; i < count; i++)
                {
                    int index = partIndex < 0 ? i : partIndex;
                    parts[i] = obj.Parts[index];
                    if (!PartGeometrySnapshot.TryCapture(parts[i], false, out snapshots[i], out string diagnostic))
                    {
                        reason = LocalizedText.Diagnostic(diagnostic);
                        foreach (var action in actions) action?.Dispose();
                        return false;
                    }

                    actions[i] = new PartDeformationDeltaAction(obj, index, snapshots[i], description);
                }

                LocalizedText recipeDiagnostic = LocalizedText.Empty;
                var cage = existing ?? DeformationCage.Capture(obj, partIndex, reference, snapshots);
                if (!cage.Matches(obj) || !DeformationRecipe.TryCreate(cage.LocalFromReference, cage.Corners, out var recipe, out recipeDiagnostic))
                {
                    reason = recipeDiagnostic;
                    foreach (var action in actions) action?.Dispose();
                    return false;
                }

                var stateAction = new DeformationAction(obj, partIndex, cage, existing == null || remove, description);
                var history = system.BeginObjectDeformationHistory(description, actions, stateAction);
                if (history == null)
                {
                    stateAction.Dispose();
                    foreach (var action in actions) action?.Dispose();
                    reason = LocalizedText.For("PO2.Object.Deformation.Error.HistoryUnavailable");
                    return false;
                }

                m_Object = obj;
                m_System = system;
                m_History = history;
                m_Snapshots = snapshots;
                m_Actions = actions;
                m_Parts = parts;
                m_CageAction = stateAction;
                m_Start = Cage = cage;
                m_Recipe = recipe;
                m_Transform = obj.Transform;
                m_PartVersion = obj.PartVersion;
                m_DeformationVersion = obj.DeformationVersion;
                m_Created = existing == null;
                m_Remove = remove;
                m_Bake = bake;
                return true;
            }
            catch (Exception e)
            {
                foreach (var action in actions) action?.Dispose();
                reason = LocalizedText.FromException(e);
                Reset();
                return false;
            }
        }

        internal bool Matches(ProceduralObject obj)
        {
            if (!IsCaptured || !ReferenceEquals(obj, m_Object) || obj.PartVersion != m_PartVersion || obj.DeformationVersion != m_DeformationVersion || !m_Transform.Position.Equals(obj.Transform.Position) || !m_Transform.Rotation.Equals(obj.Transform.Rotation) || !m_Transform.Scale.Equals(obj.Transform.Scale)) return false;
            for (int i = 0; i < m_Snapshots.Length; i++)
            {
                int index = Cage.Parts[i].PartIndex;
                if (!ReferenceEquals(obj.Parts[index], m_Parts[i]) || !m_Snapshots[i].MatchesDestination(m_Parts[i], out _)) return false;
            }

            return true;
        }

        internal bool TryMove(ProceduralObject obj, byte mask, Vector3 worldDelta, out LocalizedText reason)
        {
            var corners = (Vector3[])m_Start.Corners.Clone();
            Vector3 delta = obj.Transform.InverseTransformVector(worldDelta);
            for (int i = 0; i < 8; i++)
                if ((mask & (1 << i)) != 0)
                    corners[i] += delta;
            return TryApply(obj, corners, out reason);
        }

        internal bool TryScale(ProceduralObject obj, byte mask, Matrix4x4 worldFromStart, Vector3 localPivot, bool neutral, out LocalizedText reason)
        {
            reason = LocalizedText.Empty;
            Matrix4x4 root = Matrix4x4.TRS(obj.Transform.Position, obj.Transform.Rotation, obj.Transform.Scale);
            if (!SelectionTransformMath.TryGetLocalLinearTransform(root, worldFromStart, neutral, out var local))
            {
                reason = LocalizedText.For("PO2.Object.Deformation.Error.InvalidScale");
                return false;
            }

            var corners = (Vector3[])m_Start.Corners.Clone();
            if (!neutral)
                for (int i = 0; i < 8; i++)
                {
                    if ((mask & (1 << i)) != 0) corners[i] = localPivot + local.MultiplyVector(corners[i] - localPivot);
                }

            return TryApply(obj, corners, out reason);
        }

        internal bool TryApply(ProceduralObject obj, Vector3[] corners, out LocalizedText reason)
        {
            reason = LocalizedText.Empty;
            if (!Matches(obj) || IsCompletionPending)
            {
                reason = LocalizedText.For("PO2.Object.Deformation.Error.TargetChanged");
                return false;
            }

            if (Cage.SameCorners(corners)) return true;
            if (!DeformationRecipe.TryCreate(Cage.LocalFromReference, corners, out var recipe, out reason)) return false;
            for (int i = 0; i < m_Snapshots.Length; i++)
            {
                if (!recipe.Positions(Cage.Parts[i], m_Snapshots[i].WorkPositions, out reason)) return false;
            }

            for (int i = 0; i < m_Snapshots.Length; i++)
            {
                if (!m_Snapshots[i].TryPublishWorkPositions(m_Parts[i], out string diagnostic))
                {
                    reason = LocalizedText.Diagnostic(diagnostic);
                    return false;
                }
            }

            bool published = PartIndex < 0 ? obj.TryMarkGeometryPreviewChanged(m_Parts, true) : obj.TryMarkPartGeometryPreviewChanged(PartIndex, m_Parts[0], true);
            if (!published)
            {
                reason = LocalizedText.For("PO2.Object.Deformation.Error.ChangedBeforePublish");
                return false;
            }

            Cage = Cage.WithCorners(corners);
            m_Recipe = recipe;
            return true;
        }

        internal bool TryBeginCompletion(ProceduralObject obj, out bool pending, out LocalizedText reason)
        {
            pending = false;
            reason = LocalizedText.Empty;
            if (!Matches(obj) || IsCompletionPending)
            {
                reason = LocalizedText.For("PO2.Object.Deformation.Error.TargetChanged");
                return false;
            }

            if (!m_Remove && m_Start.SameCorners(Cage.Corners))
            {
                foreach (var action in m_Actions) action.SetNoChange();
                return m_History.Complete();
            }

            var work = new CageDeformationPartWorkItem[m_Parts.Length];
            var indices = new int[work.Length];
            for (int i = 0; i < work.Length; i++)
            {
                indices[i] = Cage.Parts[i].PartIndex;
                // Bake and untouched parts still need initialized final positions.
                if (m_Bake) Array.Copy(m_Snapshots[i].Positions, m_Snapshots[i].WorkPositions, m_Snapshots[i].Positions.Length);
                else if (!m_Recipe.Positions(Cage.Parts[i], m_Snapshots[i].WorkPositions, out reason)) return false;
                work[i] = new CageDeformationPartWorkItem(indices[i], m_Snapshots[i], Cage.Parts[i]);
            }

            m_System.TryCaptureDeformationRendererWork(obj, indices, out var renderer);
            var recipe = m_Recipe;
            var payload = m_Created || m_Remove ? Cage : null;
            bool bake = m_Bake;
            try
            {
                m_Completion = Task.Run(() => CageDeformationCommitWorker.RunCage(work, recipe, renderer, payload, bake));
            }
            catch (Exception e)
            {
                reason = LocalizedText.FromException(e);
                return false;
            }

            pending = true;
            return true;
        }

        internal bool TryFinishCompletion(ProceduralObject obj, out bool completed, out LocalizedText reason)
        {
            completed = false;
            reason = LocalizedText.Empty;
            if (m_Completion == null || !m_Completion.IsCompleted) return m_Completion != null;
            completed = true;
            var task = m_Completion;
            m_Completion = null;
            try
            {
                var result = task.GetAwaiter().GetResult();
                if (!result.Success)
                {
                    reason = result.FailureReason;
                    return false;
                }

                if (!Matches(obj) || result.Parts.Length != m_Parts.Length)
                {
                    reason = LocalizedText.For("PO2.Object.Deformation.Error.ChangedWhileFinishing");
                    return false;
                }

                m_CageAction.SetPreparedPayload(result.CagePayload);
                for (int i = 0; i < m_Parts.Length; i++)
                {
                    var part = result.Parts[i];
                    if (part.PartIndex != Cage.Parts[i].PartIndex) throw new LocalizedTextException(LocalizedText.For("PO2.Object.Deformation.Error.WorkerPartOrder"));
                    m_Actions[i].SetPreparedRawDelta(part.RawDelta, part.BeforeHash, part.AfterHash, part.Changed);
                }

                for (int i = 0; i < m_Parts.Length; i++)
                {
                    if (!m_Snapshots[i].TryAdoptWorkStreams(m_Parts[i], out string diagnostic))
                    {
                        reason = LocalizedText.Diagnostic(diagnostic);
                        return false;
                    }
                }

                bool committed = PartIndex < 0 ? obj.TryMarkGeometryDirectionsChanged(m_Parts) && obj.TryCommitGeometryChange(m_Parts) : obj.TryMarkPartGeometryDirectionsChanged(PartIndex, m_Parts[0]) && obj.TryCommitPartGeometryChange(PartIndex, m_Parts[0]);
                if (!committed)
                {
                    reason = LocalizedText.For("PO2.Object.Deformation.Error.ChangedBeforeCompletion");
                    return false;
                }

                obj.SetDeformation(PartIndex, m_Remove ? null : Cage);
                m_System.TryPublishDeformationRendererPreparation(obj, result.RendererPreparation, out _);
                if (!m_History.Complete())
                {
                    reason = LocalizedText.For("PO2.Object.Deformation.Error.HistoryRestored");
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                reason = LocalizedText.FromException(e);
                return false;
            }
        }

        internal void Reset()
        {
            if (m_Completion != null)
                m_Completion.ContinueWith(t =>
                {
                    var ignored = t.Exception;
                }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            m_Completion = null;
            m_History?.Dispose();
            m_History = null;
            m_Object = null;
            m_System = null;
            m_CageAction = null;
            m_Snapshots = null;
            m_Parts = null;
            m_Actions = null;
            m_Start = null;
            Cage = null;
        }
    }
}