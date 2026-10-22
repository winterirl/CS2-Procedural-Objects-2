using Game;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private const float kObjectListCameraPanDuration = 0.35f;
        private const float kObjectListCameraPanOverrideTolerance = 0.01f;

        private bool m_ObjectListCameraPanActive;
        private float m_ObjectListCameraPanElapsed;
        private Vector3 m_ObjectListCameraPanStart;
        private Vector3 m_ObjectListCameraPanTarget;
        private Vector3 m_ObjectListCameraPanLastPivot;

        private void BeginObjectListCameraPan(int objectId)
        {
            CameraController cameraController = m_CameraUpdateSystem?.gamePlayController;

            if (cameraController == null || !cameraController.controllerEnabled || !m_ProceduralObjectSystem.TryGetWorldBounds(objectId, out Bounds worldBounds))
            {
                CancelObjectListCameraPan();
                return;
            }

            Vector3 currentPivot = cameraController.pivot;
            Vector3 targetPivot = new Vector3(worldBounds.center.x, currentPivot.y, worldBounds.center.z);

            Vector2 panDelta = new Vector2(targetPivot.x - currentPivot.x, targetPivot.z - currentPivot.z);
            if (panDelta.sqrMagnitude <= kObjectListCameraPanOverrideTolerance * kObjectListCameraPanOverrideTolerance)
            {
                CancelObjectListCameraPan();
                return;
            }

            m_ObjectListCameraPanActive = true;
            m_ObjectListCameraPanElapsed = 0f;
            m_ObjectListCameraPanStart = currentPivot;
            m_ObjectListCameraPanTarget = targetPivot;
            m_ObjectListCameraPanLastPivot = currentPivot;
        }

        private void UpdateObjectListCameraPan()
        {
            if (!m_ObjectListCameraPanActive)
            {
                return;
            }

            CameraController cameraController = m_CameraUpdateSystem?.gamePlayController;
            if (cameraController == null || !cameraController.controllerEnabled)
            {
                CancelObjectListCameraPan();
                return;
            }

            Vector3 currentPivot = cameraController.pivot;
            Vector2 externalDelta = new Vector2(currentPivot.x - m_ObjectListCameraPanLastPivot.x, currentPivot.z - m_ObjectListCameraPanLastPivot.z);
            if (externalDelta.sqrMagnitude > kObjectListCameraPanOverrideTolerance * kObjectListCameraPanOverrideTolerance)
            {
                CancelObjectListCameraPan();
                return;
            }

            m_ObjectListCameraPanElapsed += UnityEngine.Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(m_ObjectListCameraPanElapsed / kObjectListCameraPanDuration);
            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);

            Vector3 nextPivot = currentPivot;
            nextPivot.x = Mathf.Lerp(m_ObjectListCameraPanStart.x, m_ObjectListCameraPanTarget.x, easedProgress);
            nextPivot.z = Mathf.Lerp(m_ObjectListCameraPanStart.z, m_ObjectListCameraPanTarget.z, easedProgress);

            cameraController.pivot = nextPivot;
            m_ObjectListCameraPanLastPivot = nextPivot;

            if (progress >= 1f)
            {
                CancelObjectListCameraPan();
            }
        }

        private void CancelObjectListCameraPan()
        {
            m_ObjectListCameraPanActive = false;
            m_ObjectListCameraPanElapsed = 0f;
        }
    }
}