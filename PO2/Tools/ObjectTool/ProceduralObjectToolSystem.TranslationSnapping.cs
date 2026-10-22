using Game;
using Game.Common;
using Game.Input;
using Game.Prefabs;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Core;
using PO2.Tools.Gizmos;
using PO2.Tools.Snapping;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private MeshTranslationSnapTargetSession m_MeshTranslationSnapTargetSession;
        private bool m_TranslationSceneSnapWasEnabled;
        private bool m_ObjectTranslationSnapCaptureReady;
        private ObjectTranslationSnapTargetCache m_ObjectTranslationSnapTargetCache;

        public bool TryGetActiveTranslationSnap(out SnapTargetKind targetKind, out Vector3 sourcePosition, out Vector3 targetPosition)
        {
            if (!m_IsGizmoDragging || !GizmoMath.IsTranslationHandle(m_ActiveGizmoHandle) || !m_TranslationSnapState.HasActiveSnap)
            {
                targetKind = SnapTargetKind.None;
                sourcePosition = Vector3.zero;
                targetPosition = Vector3.zero;
                return false;
            }

            targetKind = m_TranslationSnapState.TargetKind;
            sourcePosition = m_TranslationSnapState.ResolvedSourcePosition;
            targetPosition = m_TranslationSnapState.TargetPosition;
            return true;
        }

        private void PrepareObjectTranslationSnap(ProceduralObject selected, int includedPartIndex)
        {
            if (m_ObjectTranslationSnapCaptureReady || selected == null || selected.Id != m_TranslateDrag.ObjectId)
            {
                return;
            }

            if (m_SnapIndexSystem == null || !m_SnapIndexSystem.TryCreateObjectTranslationSource(selected, m_TranslateDrag.StartTransform, includedPartIndex, out ITranslationSnapSource snapSource))
            {
                // The persistent index is allowed to finish in the background.
                // Keep retrying during the drag instead of blocking input here.
                m_ObjectTranslationSnapCaptureReady = false;
                return;
            }

            m_ObjectTranslationSnapCaptureReady = true;
            m_ObjectTranslationSnapTargetCache.Clear();
            ClearMeshTranslationSnapTargetSession();

            m_TranslationSnapState.Begin(snapSource, m_TranslateDrag.Constraint);
        }

        private bool IsTargetSnappingActiveForCurrentModifiers()
        {
            if (!m_SnapSettings.POVerticesEnabled && !m_SnapSettings.POEdgesEnabled && !m_SnapSettings.GameSurfacesEnabled)
            {
                return false;
            }

            Keyboard keyboard = Keyboard.current;
            bool controlPressed = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);

            return m_SnapSettings.IsSnappingActive(controlPressed);
        }

        private bool TryResolveTranslationSnap(bool targetSnapEnabled, Vector2 cursorPosition, Vector2 preliminaryComponents, out Vector2 resolvedComponents)
        {
            resolvedComponents = preliminaryComponents;

            if (!targetSnapEnabled)
            {
                m_TranslationSnapState.ClearActiveSnap();
                m_TranslationSceneSnapWasEnabled = false;
                m_ObjectTranslationSnapTargetCache.Clear();
                return false;
            }

            if (m_TranslationSnapState.HasActiveSnap && !IsSnapTargetKindEnabled(m_TranslationSnapState.TargetKind))
            {
                m_TranslationSnapState.ClearActiveSnap();
                m_ObjectTranslationSnapTargetCache.Clear();
            }

            Camera camera = Camera.main;
            SnapTarget target = default;

            bool objectSnapTargetsEnabled = m_SnapIndexSystem != null && (m_SnapSettings.POVerticesEnabled || m_SnapSettings.POEdgesEnabled);

            int excludedPartIndex = m_ActiveGizmoTargetKind == GizmoTargetKind.Part ? m_PartTransformDrag.PartIndex : -1;

            bool canQueryObjectTargets = objectSnapTargetsEnabled && m_ObjectTranslationSnapCaptureReady && camera != null;

            int queryStamp = canQueryObjectTargets ? m_SnapIndexSystem.GetObjectTranslationQueryStamp(m_TranslateDrag.ObjectId, excludedPartIndex) : 0;

            ObjectSnapCameraState cameraState = canQueryObjectTargets ? ObjectSnapCameraState.Capture(camera) : default;

            bool objectTargetCached = canQueryObjectTargets && m_ObjectTranslationSnapTargetCache.TryGet(m_TranslateDrag.ObjectId, excludedPartIndex, cursorPosition, cameraState, queryStamp, m_SnapSettings.POVerticesEnabled, m_SnapSettings.POEdgesEnabled, out target);

            if (canQueryObjectTargets)
            {
                if (!objectTargetCached)
                {
                    m_SnapIndexSystem.TryFindObjectTranslationTarget(camera, m_TranslateDrag.ObjectId, excludedPartIndex, cursorPosition, TranslationSnapState.kTargetQueryRadiusPixels, m_SnapSettings.POVerticesEnabled, m_SnapSettings.POEdgesEnabled, out target);

                    m_ObjectTranslationSnapTargetCache.Store(m_TranslateDrag.ObjectId, excludedPartIndex, cursorPosition, cameraState, queryStamp, m_SnapSettings.POVerticesEnabled, m_SnapSettings.POEdgesEnabled, target);
                }
            }
            else if (m_MeshTranslationSnapTargetSession != null)
            {
                m_MeshTranslationSnapTargetSession.TryFindTarget(camera, cursorPosition, TranslationSnapState.kTargetQueryRadiusPixels, m_SnapSettings.POVerticesEnabled, m_SnapSettings.POEdgesEnabled, out target);
            }

            bool gameSurfaceEnabled = m_SnapSettings.GameSurfacesEnabled;

            if (gameSurfaceEnabled)
            {
                // ToolRaycastSystem publishes the result from its previous input.
                // PO targets do not need this one-update warmup.
                if (m_TranslationSceneSnapWasEnabled)
                {
                    if (!target.IsValid)
                    {
                        TryGetGameSurfaceSnapTarget(out target);
                    }
                }
                else
                {
                    m_TranslationSceneSnapWasEnabled = true;
                }
            }
            else
            {
                m_TranslationSceneSnapWasEnabled = false;
            }

            return m_TranslationSnapState.TryResolve(camera, cursorPosition, preliminaryComponents, target, out resolvedComponents);
        }

        private bool TryGetGameSurfaceSnapTarget(out SnapTarget target)
        {
            target = default;

            if (!GetRaycastResult(out Unity.Entities.Entity entity, out Game.Common.RaycastHit hit))
            {
                return false;
            }

            if (!m_SnapSettings.GameSurfacesEnabled)
            {
                return false;
            }

            Unity.Entities.Entity targetEntity = hit.m_HitEntity != Unity.Entities.Entity.Null ? hit.m_HitEntity : entity;

            Vector3 targetPosition = new Vector3(hit.m_HitPosition.x, hit.m_HitPosition.y, hit.m_HitPosition.z);

            if (!IsFinite(targetPosition))
            {
                return false;
            }

            target = new SnapTarget(SnapTargetKind.GameSurface, targetEntity.Index, targetEntity.Version, 0, targetPosition, 0f);

            return true;
        }

        private bool IsSnapTargetKindEnabled(SnapTargetKind targetKind)
        {
            switch (targetKind)
            {
                case SnapTargetKind.POVertex:
                    return m_SnapSettings.POVerticesEnabled;

                case SnapTargetKind.POEdge:
                    return m_SnapSettings.POEdgesEnabled;

                case SnapTargetKind.GameSurface:
                    return m_SnapSettings.GameSurfacesEnabled;

                default:
                    return false;
            }
        }

        private bool IsTranslationSceneSnappingRequested()
        {
            if (!m_IsGizmoDragging || !GizmoMath.IsTranslationHandle(m_ActiveGizmoHandle) || !m_SnapSettings.GameSurfacesEnabled)
            {
                return false;
            }

            Keyboard keyboard = Keyboard.current;

            bool controlPressed = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);

            return m_SnapSettings.IsSnappingActive(controlPressed);
        }

        private void ClearMeshTranslationSnapTargetSession()
        {
            MeshTranslationSnapTargetSession session = m_MeshTranslationSnapTargetSession;

            if (session == null)
            {
                return;
            }

            m_MeshTranslationSnapTargetSession = null;
            session.Dispose();
        }

        private struct ObjectTranslationSnapTargetCache
        {
            private bool m_Valid;
            private int m_MovingObjectId;
            private int m_ExcludedPartIndex;
            private Vector2 m_CursorPosition;
            private ObjectSnapCameraState m_CameraState;
            private int m_QueryStamp;
            private bool m_VerticesEnabled;
            private bool m_EdgesEnabled;
            private SnapTarget m_Target;

            public bool TryGet(int movingObjectId, int excludedPartIndex, Vector2 cursorPosition, in ObjectSnapCameraState cameraState, int queryStamp, bool verticesEnabled, bool edgesEnabled, out SnapTarget target)
            {
                target = default;

                if (!m_Valid || m_MovingObjectId != movingObjectId || m_ExcludedPartIndex != excludedPartIndex || !m_CursorPosition.Equals(cursorPosition) || !m_CameraState.Matches(cameraState) || m_QueryStamp != queryStamp || m_VerticesEnabled != verticesEnabled || m_EdgesEnabled != edgesEnabled)
                {
                    return false;
                }

                target = m_Target;
                return true;
            }

            public void Store(int movingObjectId, int excludedPartIndex, Vector2 cursorPosition, in ObjectSnapCameraState cameraState, int queryStamp, bool verticesEnabled, bool edgesEnabled, in SnapTarget target)
            {
                m_Valid = true;
                m_MovingObjectId = movingObjectId;
                m_ExcludedPartIndex = excludedPartIndex;
                m_CursorPosition = cursorPosition;
                m_CameraState = cameraState;
                m_QueryStamp = queryStamp;
                m_VerticesEnabled = verticesEnabled;
                m_EdgesEnabled = edgesEnabled;
                m_Target = target;
            }

            public void Clear()
            {
                m_Valid = false;
            }
        }

        private readonly struct ObjectSnapCameraState
        {
            private readonly Vector3 m_Position;
            private readonly Quaternion m_Rotation;
            private readonly float m_FieldOfView;
            private readonly bool m_Orthographic;
            private readonly float m_OrthographicSize;
            private readonly Rect m_PixelRect;

            private ObjectSnapCameraState(Vector3 position, Quaternion rotation, float fieldOfView, bool orthographic, float orthographicSize, Rect pixelRect)
            {
                m_Position = position;
                m_Rotation = rotation;
                m_FieldOfView = fieldOfView;
                m_Orthographic = orthographic;
                m_OrthographicSize = orthographicSize;
                m_PixelRect = pixelRect;
            }

            public static ObjectSnapCameraState Capture(Camera camera)
            {
                return new ObjectSnapCameraState(camera.transform.position, camera.transform.rotation, camera.fieldOfView, camera.orthographic, camera.orthographicSize, camera.pixelRect);
            }

            public bool Matches(in ObjectSnapCameraState other)
            {
                const float positionEpsilonSquared = 0.00000001f;

                const float rotationDotEpsilon = 0.0000001f;

                const float scalarEpsilon = 0.0001f;

                if ((m_Position - other.m_Position).sqrMagnitude > positionEpsilonSquared || m_Orthographic != other.m_Orthographic || Mathf.Abs(m_FieldOfView - other.m_FieldOfView) > scalarEpsilon || Mathf.Abs(m_OrthographicSize - other.m_OrthographicSize) > scalarEpsilon || !m_PixelRect.Equals(other.m_PixelRect))
                {
                    return false;
                }

                float rotationDot = Mathf.Abs(Quaternion.Dot(m_Rotation, other.m_Rotation));

                return 1f - rotationDot <= rotationDotEpsilon;
            }
        }
    }
}