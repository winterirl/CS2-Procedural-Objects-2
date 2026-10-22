using UnityEngine;

namespace PO2.Tools.Gizmos
{
    // Picking and rendering share handle dimensions so visible handles remain clickable.
    public static class GizmoMath
    {
        private const float kDesiredAxisPixels = 112f;
        private const float kMinimumAxisLength = 1.5f;
        private const float kMaximumScreenAxisLength = 100f;
        private const float kMaximumAdaptiveMultiplier = 4.5f;
        private const float kBoundsPaddingRatio = 1.20f;

        private const float kPickRadiusRatio = 0.09f;
        private const float kMinimumPickRadius = 0.18f;

        internal const float kTranslationPlaneInnerRatio = 0.18f;
        internal const float kTranslationPlaneOuterRatio = 0.34f;

        // Matches the game's own line/plane guard. Near-edge-on planes turn
        // tiny pointer movements into huge world-space jumps.
        internal const float kMinimumPlaneRayIncidence = 0.05f;

        private const float kRotationRadiusRatio = 0.82f;
        private const float kRotationPickToleranceRatio = 0.09f;
        private const float kMinimumRotationPickTolerance = 0.18f;

        private const float kScaleHandleRadiusRatio = 0.075f;
        private const float kMinimumScaleHandleRadius = 0.16f;
        private const float kUniformHandleRadiusRatio = 0.11f;

        public static float GetWorldSizeForPixels(Camera camera, Vector3 worldPosition, float desiredPixels)
        {
            if (camera == null || desiredPixels <= 0f)
            {
                return 0f;
            }

            float screenHeight = Mathf.Max(1f, camera.pixelHeight > 0 ? camera.pixelHeight : Screen.height);

            float worldHeight;

            // Convert a desired screen size into world units at the pivot.
            // Perspective grows with distance; orthographic does not.
            if (camera.orthographic)
            {
                worldHeight = 2f * camera.orthographicSize;
            }
            else
            {
                float distance = Vector3.Distance(camera.transform.position, worldPosition);

                float halfFov = camera.fieldOfView * Mathf.Deg2Rad * 0.5f;

                worldHeight = 2f * Mathf.Max(0.01f, distance) * Mathf.Tan(halfFov);
            }

            return worldHeight / screenHeight * desiredPixels;
        }

        public static float GetScreenSpaceGizmoSize(Camera camera, Vector3 pivot)
        {
            if (camera == null)
            {
                return 8f;
            }

            return Mathf.Clamp(GetWorldSizeForPixels(camera, pivot, kDesiredAxisPixels), kMinimumAxisLength, kMaximumScreenAxisLength);
        }

        public static float GetGizmoSize(Camera camera, Vector3 pivot, Bounds? localBounds, Vector3 objectScale)
        {
            float screenSize = GetScreenSpaceGizmoSize(camera, pivot);

            if (!localBounds.HasValue)
            {
                return screenSize;
            }

            Bounds bounds = localBounds.Value;

            float xExtent = Mathf.Max(Mathf.Abs(bounds.min.x * objectScale.x), Mathf.Abs(bounds.max.x * objectScale.x));

            float yExtent = Mathf.Max(Mathf.Abs(bounds.min.y * objectScale.y), Mathf.Abs(bounds.max.y * objectScale.y));

            float zExtent = Mathf.Max(Mathf.Abs(bounds.min.z * objectScale.z), Mathf.Abs(bounds.max.z * objectScale.z));

            float largestPivotExtent = Mathf.Max(xExtent, Mathf.Max(yExtent, zExtent));

            // Large objects need handles outside their bounds, but cap that
            // growth so a converted building does not get a city-sized gizmo.
            float objectDrivenSize = largestPivotExtent * kBoundsPaddingRatio;

            return Mathf.Min(Mathf.Max(screenSize, objectDrivenSize), screenSize * kMaximumAdaptiveMultiplier);
        }

        public static float GetPickRadius(float axisLength)
        {
            return Mathf.Max(kMinimumPickRadius, axisLength * kPickRadiusRatio);
        }

        public static float GetRotationRadius(float axisLength)
        {
            return axisLength * kRotationRadiusRatio;
        }

        public static float GetRotationPickTolerance(float radius)
        {
            return Mathf.Max(kMinimumRotationPickTolerance, radius * kRotationPickToleranceRatio);
        }

        public static bool TryGetTransformAxis(GizmoHandle handle, out TransformAxis axis)
        {
            switch (handle)
            {
                case GizmoHandle.MoveX:
                case GizmoHandle.RotateX:
                case GizmoHandle.ScaleX:
                    axis = TransformAxis.X;
                    return true;

                case GizmoHandle.MoveY:
                case GizmoHandle.RotateY:
                case GizmoHandle.ScaleY:
                    axis = TransformAxis.Y;
                    return true;

                case GizmoHandle.MoveZ:
                case GizmoHandle.RotateZ:
                case GizmoHandle.ScaleZ:
                    axis = TransformAxis.Z;
                    return true;

                default:
                    axis = default;
                    return false;
            }
        }

        public static bool IsTranslationHandle(GizmoHandle handle)
        {
            return handle == GizmoHandle.MoveX || handle == GizmoHandle.MoveY || handle == GizmoHandle.MoveZ || IsTranslationPlaneHandle(handle);
        }

        public static bool IsTranslationPlaneHandle(GizmoHandle handle)
        {
            return handle == GizmoHandle.MoveXY || handle == GizmoHandle.MoveXZ || handle == GizmoHandle.MoveYZ;
        }

        public static bool IsRotationHandle(GizmoHandle handle)
        {
            return handle == GizmoHandle.RotateX || handle == GizmoHandle.RotateY || handle == GizmoHandle.RotateZ;
        }

        public static bool IsScaleHandle(GizmoHandle handle)
        {
            return handle == GizmoHandle.ScaleX || handle == GizmoHandle.ScaleY || handle == GizmoHandle.ScaleZ || handle == GizmoHandle.ScaleUniform;
        }

        public static bool IsAxisScaleHandle(GizmoHandle handle)
        {
            return handle == GizmoHandle.ScaleX || handle == GizmoHandle.ScaleY || handle == GizmoHandle.ScaleZ;
        }

        public static int GetScaleAxisIndex(GizmoHandle handle)
        {
            switch (handle)
            {
                case GizmoHandle.ScaleX:
                    return 0;

                case GizmoHandle.ScaleY:
                    return 1;

                case GizmoHandle.ScaleZ:
                    return 2;

                default:
                    return -1;
            }
        }

        public static float GetScaleHandleRadius(float gizmoSize)
        {
            return Mathf.Max(kMinimumScaleHandleRadius, gizmoSize * kScaleHandleRadiusRatio);
        }

        public static float GetUniformScaleHandleRadius(float gizmoSize)
        {
            return Mathf.Max(kMinimumScaleHandleRadius, gizmoSize * kUniformHandleRadiusRatio);
        }

        public static bool TryPickTranslationHandle(Ray ray, Vector3 pivot, TransformFrame frame, float axisLength, out GizmoHandle handle, out float rayDistance)
        {
            handle = GizmoHandle.None;
            rayDistance = float.MaxValue;

            float planeInner = axisLength * kTranslationPlaneInnerRatio;

            float planeOuter = axisLength * kTranslationPlaneOuterRatio;

            TryPickTranslationPlane(ray, pivot, frame.XAxis, frame.YAxis, frame.ZAxis, planeInner, planeOuter, GizmoHandle.MoveXY, ref handle, ref rayDistance);

            TryPickTranslationPlane(ray, pivot, frame.XAxis, frame.ZAxis, frame.YAxis, planeInner, planeOuter, GizmoHandle.MoveXZ, ref handle, ref rayDistance);

            TryPickTranslationPlane(ray, pivot, frame.YAxis, frame.ZAxis, frame.XAxis, planeInner, planeOuter, GizmoHandle.MoveYZ, ref handle, ref rayDistance);

            // The square interior owns the click. Its inset keeps it clear of
            // the axis pick volumes, and returning here avoids view-dependent
            // fights where two handles overlap on screen.
            if (handle != GizmoHandle.None)
            {
                return true;
            }

            float pickRadius = GetPickRadius(axisLength);

            TryPickAxis(ray, pivot, frame.XAxis, axisLength, pickRadius, GizmoHandle.MoveX, ref handle, ref rayDistance);

            TryPickAxis(ray, pivot, frame.YAxis, axisLength, pickRadius, GizmoHandle.MoveY, ref handle, ref rayDistance);

            TryPickAxis(ray, pivot, frame.ZAxis, axisLength, pickRadius, GizmoHandle.MoveZ, ref handle, ref rayDistance);

            return handle != GizmoHandle.None;
        }

        private static void TryPickTranslationPlane(Ray ray, Vector3 pivot, Vector3 uAxis, Vector3 vAxis, Vector3 normal, float innerDistance, float outerDistance, GizmoHandle candidate, ref GizmoHandle bestHandle, ref float bestRayDistance)
        {
            Vector3 rayDirection = ray.direction.normalized;
            Vector3 planeNormal = normal.normalized;

            if (Mathf.Abs(Vector3.Dot(rayDirection, planeNormal)) < kMinimumPlaneRayIncidence)
            {
                return;
            }

            Plane plane = new Plane(planeNormal, pivot);

            if (!plane.Raycast(ray, out float enter) || enter < 0f)
            {
                return;
            }

            Vector3 hitDelta = ray.GetPoint(enter) - pivot;
            float uDistance = Vector3.Dot(hitDelta, uAxis.normalized);

            float vDistance = Vector3.Dot(hitDelta, vAxis.normalized);

            if (uDistance < innerDistance || uDistance > outerDistance || vDistance < innerDistance || vDistance > outerDistance || enter >= bestRayDistance)
            {
                return;
            }

            bestHandle = candidate;
            bestRayDistance = enter;
        }

        public static bool TryPickRotationHandle(Ray ray, Vector3 pivot, TransformFrame frame, float radius, out GizmoHandle handle, out float score)
        {
            handle = GizmoHandle.None;
            score = float.MaxValue;

            float tolerance = GetRotationPickTolerance(radius);

            TryPickRing(ray, pivot, frame.XAxis, radius, tolerance, GizmoHandle.RotateX, ref handle, ref score);

            TryPickRing(ray, pivot, frame.YAxis, radius, tolerance, GizmoHandle.RotateY, ref handle, ref score);

            TryPickRing(ray, pivot, frame.ZAxis, radius, tolerance, GizmoHandle.RotateZ, ref handle, ref score);

            return handle != GizmoHandle.None;
        }

        public static bool TryPickScaleHandle(Ray ray, Vector3 pivot, TransformFrame frame, float gizmoSize, out GizmoHandle handle, out float score)
        {
            handle = GizmoHandle.None;
            score = float.MaxValue;

            float axisPickRadius = GetScaleHandleRadius(gizmoSize);
            float uniformRadius = GetUniformScaleHandleRadius(gizmoSize);

            // Test the center first, then let a nearer axis endpoint win.
            // Score is ray distance here because every handle is sphere-picked.
            if (TryPickSphere(ray, pivot, uniformRadius, out float uniformDistance))
            {
                handle = GizmoHandle.ScaleUniform;
                score = uniformDistance;
            }

            TryPickScaleAxis(ray, pivot, frame.XAxis, gizmoSize, axisPickRadius, GizmoHandle.ScaleX, ref handle, ref score);

            TryPickScaleAxis(ray, pivot, frame.YAxis, gizmoSize, axisPickRadius, GizmoHandle.ScaleY, ref handle, ref score);

            TryPickScaleAxis(ray, pivot, frame.ZAxis, gizmoSize, axisPickRadius, GizmoHandle.ScaleZ, ref handle, ref score);

            return handle != GizmoHandle.None;
        }

        private static void TryPickScaleAxis(Ray ray, Vector3 pivot, Vector3 axis, float gizmoSize, float pickRadius, GizmoHandle candidate, ref GizmoHandle bestHandle, ref float bestScore)
        {
            Vector3 handleCenter = pivot + axis.normalized * gizmoSize;

            if (!TryPickSphere(ray, handleCenter, pickRadius * 1.35f, out float endDistance))
            {
                return;
            }

            if (endDistance < bestScore)
            {
                bestScore = endDistance;
                bestHandle = candidate;
            }
        }

        private static bool TryPickSphere(Ray ray, Vector3 center, float radius, out float rayDistance)
        {
            rayDistance = 0f;

            Vector3 direction = ray.direction.normalized;
            Vector3 offset = ray.origin - center;
            float b = Vector3.Dot(offset, direction);
            float c = Vector3.Dot(offset, offset) - radius * radius;
            float discriminant = b * b - c;

            if (discriminant < 0f)
            {
                return false;
            }

            float root = Mathf.Sqrt(discriminant);
            float near = -b - root;
            float far = -b + root;

            if (far < 0f)
            {
                return false;
            }

            rayDistance = near >= 0f ? near : far;
            return true;
        }

        public static bool TryCreateTranslationDragPlane(Camera camera, Vector3 pivot, Vector3 axis, out Plane plane)
        {
            plane = default;

            if (camera == null || axis.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            axis.Normalize();

            // Choose a camera-facing plane that still contains the movement axis.
            // Fall through camera basis vectors when one is parallel.
            Vector3 cameraForward = camera.transform.forward;
            Vector3 normal = cameraForward - Vector3.Dot(cameraForward, axis) * axis;

            if (normal.sqrMagnitude < 0.001f)
            {
                Vector3 cameraUp = camera.transform.up;
                normal = cameraUp - Vector3.Dot(cameraUp, axis) * axis;
            }

            if (normal.sqrMagnitude < 0.001f)
            {
                Vector3 cameraRight = camera.transform.right;
                normal = cameraRight - Vector3.Dot(cameraRight, axis) * axis;
            }

            if (normal.sqrMagnitude < 0.001f)
            {
                return false;
            }

            normal.Normalize();
            plane = new Plane(normal, pivot);
            return true;
        }

        public static bool TryGetClosestRayLineParameter(Ray ray, Vector3 lineOrigin, Vector3 lineDirection, out float lineParameter)
        {
            lineParameter = 0f;

            if (lineDirection.sqrMagnitude < 0.000001f)
            {
                return false;
            }

            Vector3 rayDirection = ray.direction.normalized;
            Vector3 axis = lineDirection.normalized;
            Vector3 offset = ray.origin - lineOrigin;

            float parallel = Vector3.Dot(rayDirection, axis);
            float rayOffset = Vector3.Dot(rayDirection, offset);
            float axisOffset = Vector3.Dot(axis, offset);
            float denominator = 1f - parallel * parallel;

            // Near-parallel ray/axis pairs are numerically unstable.
            // The caller can fall back to the existing drag-plane solution.
            if (denominator < 0.00001f)
            {
                return false;
            }

            float rayParameter = (parallel * axisOffset - rayOffset) / denominator;

            if (rayParameter < 0f)
            {
                return false;
            }

            lineParameter = (axisOffset - parallel * rayOffset) / denominator;

            return true;
        }

        public static Plane CreateRotationDragPlane(Vector3 pivot, Vector3 axis)
        {
            return new Plane(axis.normalized, pivot);
        }

        public static bool TryRayPlane(Ray ray, Plane plane, out Vector3 point)
        {
            point = default;

            if (!plane.Raycast(ray, out float enter))
            {
                return false;
            }

            point = ray.GetPoint(enter);
            return true;
        }

        public static bool TryRayPlane(Ray ray, Plane plane, float minimumIncidence, out Vector3 point)
        {
            point = default;

            Vector3 rayDirection = ray.direction.normalized;

            if (Mathf.Abs(Vector3.Dot(rayDirection, plane.normal)) < minimumIncidence)
            {
                return false;
            }

            return TryRayPlane(ray, plane, out point);
        }

        public static float CalculateSignedAngleRadians(Vector3 axis, Vector3 startDirection, Vector3 currentDirection)
        {
            return Mathf.Atan2(Vector3.Dot(axis, Vector3.Cross(startDirection, currentDirection)), Vector3.Dot(startDirection, currentDirection));
        }

        private static void TryPickRing(Ray ray, Vector3 pivot, Vector3 normal, float radius, float tolerance, GizmoHandle candidate, ref GizmoHandle bestHandle, ref float bestScore)
        {
            Plane plane = new Plane(normal, pivot);

            if (!plane.Raycast(ray, out float enter))
            {
                return;
            }

            Vector3 hit = ray.GetPoint(enter);
            float radialDistance = Vector3.Distance(hit, pivot);
            float radialError = Mathf.Abs(radialDistance - radius);

            if (radialError > tolerance)
            {
                return;
            }

            // Prefer the ring whose projected hit is closest to its ideal radius.
            // ay distance is only a tiny tie breaker for crossings.
            float score = radialError / Mathf.Max(tolerance, 0.0001f) + enter * 0.000001f;

            if (score < bestScore)
            {
                bestScore = score;
                bestHandle = candidate;
            }
        }

        private static void TryPickAxis(Ray ray, Vector3 pivot, Vector3 axis, float axisLength, float pickRadius, GizmoHandle candidate, ref GizmoHandle bestHandle, ref float bestRayDistance)
        {
            Vector3 segmentStart = pivot;
            Vector3 segmentEnd = pivot + axis * axisLength;

            ClosestRaySegment(ray, segmentStart, segmentEnd, out float rayT, out float segmentT, out float distance);

            // Ignore the common pivot area so the three translation axes do
            // not become simultaneously selectable near the center.
            if (segmentT < 0.14f)
            {
                return;
            }

            if (rayT < 0f || distance > pickRadius)
            {
                return;
            }

            if (rayT < bestRayDistance)
            {
                bestRayDistance = rayT;
                bestHandle = candidate;
            }
        }

        private static void ClosestRaySegment(Ray ray, Vector3 segmentStart, Vector3 segmentEnd, out float rayT, out float segmentT, out float distance)
        {
            Vector3 u = ray.direction.normalized;
            Vector3 v = segmentEnd - segmentStart;
            Vector3 w = ray.origin - segmentStart;

            float a = Vector3.Dot(u, u);
            float b = Vector3.Dot(u, v);
            float c = Vector3.Dot(v, v);
            float d = Vector3.Dot(u, w);
            float e = Vector3.Dot(v, w);

            float denominator = a * c - b * b;

            if (c < 0.000001f)
            {
                rayT = Mathf.Max(0f, -d / Mathf.Max(a, 0.000001f));
                segmentT = 0f;
            }
            else if (Mathf.Abs(denominator) < 0.000001f)
            {
                rayT = 0f;
                segmentT = Mathf.Clamp01(e / c);
            }
            else
            {
                rayT = (b * e - c * d) / denominator;
                segmentT = (a * e - b * d) / denominator;

                if (rayT < 0f)
                {
                    rayT = 0f;
                    segmentT = Mathf.Clamp01(e / c);
                }
                else
                {
                    segmentT = Mathf.Clamp01(segmentT);

                    Vector3 segmentPoint = segmentStart + v * segmentT;

                    rayT = Mathf.Max(0f, Vector3.Dot(segmentPoint - ray.origin, u));
                }
            }

            Vector3 rayPoint = ray.origin + u * rayT;
            Vector3 closestSegmentPoint = segmentStart + v * segmentT;

            distance = Vector3.Distance(rayPoint, closestSegmentPoint);
        }
    }
}