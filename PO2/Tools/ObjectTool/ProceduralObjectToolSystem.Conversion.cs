using System.Threading.Tasks;
using Game;
using Game.Common;
using Game.Input;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Conversion;
using PO2.Core;
using PO2.Localization;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private AssetMeshExtractor m_AssetMeshExtractor;
        private Task<AssetConversionBuildResult> m_AssetConversionTask;
        private AssetConversionWorkItem m_AssetConversionWorkItem;
        private bool m_AssetConversionAbandoned;
        private readonly AssetConversionStatus m_AssetConversionStatus = new AssetConversionStatus();
        public bool AssetConversionBusy => m_AssetConversionTask != null;
        public LocalizedText AssetConversionStatus => m_AssetConversionStatus.Message;
        public AssetConversionPhase AssetConversionPhase => m_AssetConversionStatus.GetPhase(m_Mode == ObjectToolMode.ConvertAsset, AssetConversionBusy, m_AssetConversionAbandoned);

        public void BeginAssetConversion()
        {
            if (m_AssetConversionTask != null)
            {
                m_AssetConversionStatus.SetMessage(m_AssetConversionAbandoned ? LocalizedText.For("PO2.Convert.Status.Cancelling") : LocalizedText.For("PO2.Convert.Status.AlreadyRunning"));
                return;
            }

            if (m_Mode == ObjectToolMode.ConvertAsset)
            {
                return;
            }

            CaptureConvertReturnTarget();

            if (IsObjectEditorMode)
            {
                LeaveMeshEdit(commitActiveDrag: true);
            }

            CancelGizmoDrag(restoreTransform: true);
            ClearPrimitivePartPlacementParent();
            ClearCombineParent();
            m_AssetConversionStatus.SetMessage(LocalizedText.For("PO2.Convert.Target.ReadyInstruction"));
            m_Mode = ObjectToolMode.ConvertAsset;
            ClearGizmoHover();
            m_ProceduralObjectSystem.ActivateTool();
        }

        private void ProcessPendingAssetConversion()
        {
            if (m_AssetConversionTask == null || !m_AssetConversionTask.IsCompleted)
            {
                return;
            }

            Task<AssetConversionBuildResult> completedTask = m_AssetConversionTask;
            AssetConversionWorkItem workItem = m_AssetConversionWorkItem;
            bool abandoned = m_AssetConversionAbandoned;

            m_AssetConversionTask = null;
            m_AssetConversionWorkItem = null;
            m_AssetConversionAbandoned = false;

            AssetConversionBuildResult buildResult;
            try
            {
                buildResult = completedTask.GetAwaiter().GetResult();
            }
            catch (System.Exception exception)
            {
                if (!abandoned)
                {
                    m_AssetConversionStatus.Fail(LocalizedText.For("PO2.Convert.Error.MeshProcessing"));
                    Mod.log.Error($"[PO Conversion] Background conversion task failed: {exception}");
                    workItem?.Profile?.LogCompleted(false, "Background conversion task faulted.");
                }

                return;
            }

            if (abandoned)
            {
                m_AssetConversionStatus.Clear();
                return;
            }

            // Reject completion outside Convert mode, even if a workspace transition
            // missed marking the task abandoned.
            if (m_Mode != ObjectToolMode.ConvertAsset)
            {
                m_AssetConversionStatus.Clear();
                Mod.log.Warn("[PO Conversion] Background result completed outside conversion mode and was discarded.");
                return;
            }

            if (buildResult == null || !buildResult.Success || buildResult.Conversion == null)
            {
                string failureReason = buildResult != null && !string.IsNullOrWhiteSpace(buildResult.FailureReason) ? buildResult.FailureReason : "The conversion worker produced no usable result.";
                m_AssetConversionStatus.Fail(LocalizedText.For("PO2.Convert.Status.Failed"));
                Mod.log.Error($"[PO Conversion] {failureReason}");
                workItem?.Profile?.LogCompleted(false, failureReason);
                return;
            }

            if (!TryResolveAssetConversionCommitTransform(workItem, out ObjectTransform commitTransform, out string sourceFailureReason))
            {
                m_AssetConversionStatus.Fail(LocalizedText.For("PO2.Convert.Error.SourceChanged"));
                Mod.log.Warn($"[PO Conversion] {sourceFailureReason}");
                workItem?.Profile?.LogCompleted(false, sourceFailureReason);
                return;
            }

            AssetConversionResult workerConversion = buildResult.Conversion;
            AssetConversionResult conversion = new AssetConversionResult(workerConversion.RequestedName, commitTransform, workerConversion.Parts, workerConversion.LocalBounds, workerConversion.RenderPreparation, workerConversion.Profile);

            string sourceReplacementFailureReason = string.Empty;
            ProceduralObject created = m_ProceduralObjectSystem.CreateConvertedObject(conversion, () =>
            {
                bool committed = TryCommitAssetConversionSourceReplacement(workItem, out string failureReason);
                sourceReplacementFailureReason = failureReason;
                return committed;
            });

            if (created != null)
            {
                m_AssetConversionStatus.Clear();
                m_Mode = ObjectToolMode.Transform;
                ClearGizmoHover();
            }
            else if (!string.IsNullOrWhiteSpace(sourceReplacementFailureReason))
            {
                m_AssetConversionStatus.Fail(LocalizedText.For("PO2.Convert.Error.SourceReplacement"));
                Mod.log.Error($"[PO Conversion] Source replacement failed; the staged " + $"PO was rolled back and the original was kept: " + sourceReplacementFailureReason);
            }
            else
            {
                m_AssetConversionStatus.Fail(LocalizedText.For("PO2.Convert.Error.RuntimeCreation"));
            }
        }

        private bool TryResolveAssetConversionCommitTransform(AssetConversionWorkItem workItem, out ObjectTransform transform, out string failureReason)
        {
            transform = default(ObjectTransform);
            if (!TryValidateAssetConversionSource(workItem, out Game.Objects.Transform sourceTransform, out failureReason))
            {
                return false;
            }

            // Moving source entities can advance while the worker runs.
            // Commit at their current transform rather than the click-time snapshot so the
            // eventual PO replaces the source where it actually is.
            Unity.Mathematics.float3 position = sourceTransform.m_Position;
            Unity.Mathematics.float4 rotation = sourceTransform.m_Rotation.value;

            transform = new ObjectTransform(new Vector3(position.x, position.y, position.z), new Quaternion(rotation.x, rotation.y, rotation.z, rotation.w), Vector3.one);
            return true;
        }

        private bool TryCommitAssetConversionSourceReplacement(AssetConversionWorkItem workItem, out string failureReason)
        {
            try
            {
                if (!TryValidateAssetConversionSource(workItem, out Game.Objects.Transform _, out failureReason))
                {
                    return false;
                }

                // Use the same normal lifecycle as vanilla object removal.
                // The source stays alive through capture, worker processing, PO
                // construction, renderer creation, and staged registration.
                EntityManager.AddComponent<Deleted>(workItem.SourceEntity);

                failureReason = string.Empty;
                return true;
            }
            catch (System.Exception exception)
            {
                string sourceEntity = workItem != null ? workItem.SourceEntity.ToString() : "<null>";
                failureReason = $"Could not mark source entity {sourceEntity} " + $"Deleted: {exception.Message}";
                return false;
            }
        }

        private bool TryValidateAssetConversionSource(AssetConversionWorkItem workItem, out Game.Objects.Transform sourceTransform, out string failureReason)
        {
            sourceTransform = default(Game.Objects.Transform);
            failureReason = string.Empty;

            if (workItem == null || workItem.SourceEntity == Unity.Entities.Entity.Null || !EntityManager.Exists(workItem.SourceEntity))
            {
                failureReason = "The source entity no longer exists.";
                return false;
            }

            if (EntityManager.HasComponent<Deleted>(workItem.SourceEntity))
            {
                failureReason = "The source entity was deleted while conversion was processing.";
                return false;
            }

            if (!EntityManager.HasComponent<PrefabRef>(workItem.SourceEntity) || !EntityManager.HasComponent<Game.Objects.Transform>(workItem.SourceEntity))
            {
                failureReason = "The source entity no longer has the required prefab/transform state.";
                return false;
            }

            PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(workItem.SourceEntity);
            if (prefabRef.m_Prefab != workItem.SourcePrefabEntity)
            {
                failureReason = "The source entity changed prefab identity while conversion was processing.";
                return false;
            }

            sourceTransform = EntityManager.GetComponentData<Game.Objects.Transform>(workItem.SourceEntity);
            return true;
        }

        private void AbandonPendingAssetConversion()
        {
            if (m_AssetConversionTask == null || m_AssetConversionAbandoned)
            {
                return;
            }

            m_AssetConversionAbandoned = true;
            m_AssetConversionStatus.SetMessage(LocalizedText.For("PO2.Convert.Status.Cancelling"));
        }

        private JobHandle UpdateAssetConversionMode(JobHandle inputDeps, bool primaryPressed)
        {
            ClearGizmoHover();

            // Once capture has finished, the detached PO build runs on a worker.
            // World input stays disabled for this conversion target until the
            // worker completes or the user backs out.
            if (m_AssetConversionTask != null)
            {
                ClearAssetConversionHover();
                return inputDeps;
            }

            bool hasTarget = GetRaycastResult(out Unity.Entities.Entity entity, out Game.Common.RaycastHit _) && m_AssetMeshExtractor != null && m_AssetMeshExtractor.IsSupportedTarget(entity);

            if (!primaryPressed)
            {
                SetAssetConversionHover(hasTarget ? entity : Unity.Entities.Entity.Null);
                return inputDeps;
            }

            ClearAssetConversionHover();

            if (!hasTarget)
            {
                m_AssetConversionStatus.SetMessage(LocalizedText.For("PO2.Convert.Target.SupportedAssetInstruction"));
                return inputDeps;
            }

            if (!m_AssetMeshExtractor.TryCapture(entity, out AssetConversionWorkItem workItem))
            {
                m_AssetConversionStatus.Fail(LocalizedText.For("PO2.Convert.Error.Capture"));
                return inputDeps;
            }

            m_AssetConversionWorkItem = workItem;
            m_AssetConversionAbandoned = false;
            m_AssetConversionStatus.SetMessage(LocalizedText.For("PO2.Convert.Status.AssetProgress", ("assetName", workItem.RequestedName)));

            // The worker now owns the loaded GeometryAsset native payload as
            // well as the PO build. It decodes/disposes that payload off-thread,
            // then preserves the exact logical-topology algorithm.
            // TODO: Maybe replace the algorithm to one that isn't mixing correctness and
            // scheduling changes together.
            try
            {
                m_AssetConversionTask = Task.Run(() => AssetMeshExtractor.BuildCaptured(workItem));
            }
            catch (System.Exception exception)
            {
                // Once TryCapture succeeds the work item owns persistent native GeometryAsset buffers.
                // If the worker cannot even be scheduled,
                // release them here rather than waiting for a task that does not exist.
                workItem.Dispose();
                m_AssetConversionTask = null;
                m_AssetConversionWorkItem = null;
                m_AssetConversionAbandoned = false;
                m_AssetConversionStatus.Fail(LocalizedText.For("PO2.Convert.Error.WorkerStart"));
                Mod.log.Error($"[PO Conversion] Could not schedule conversion worker: {exception}");
                workItem.Profile?.LogCompleted(false, "Conversion worker could not be scheduled.");
            }

            return inputDeps;
        }
    }
}