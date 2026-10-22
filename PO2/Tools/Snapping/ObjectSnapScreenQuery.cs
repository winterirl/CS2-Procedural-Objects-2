using Unity.Mathematics;
using UnityEngine;
using PO2.Core;

namespace PO2.Tools.Snapping
{
    // The target search only needs the narrow volume under the cursor. Keeping
    // its planes in PO-local space lets every BVH node use a cheap AABB test.
    internal readonly struct ObjectSnapScreenQuery
    {
        private readonly ObjectSnapPlane m_Left;
        private readonly ObjectSnapPlane m_Right;
        private readonly ObjectSnapPlane m_Bottom;
        private readonly ObjectSnapPlane m_Top;
        private readonly ObjectSnapPlane m_Front;
        private readonly Vector3 m_CursorRayOrigin;
        private readonly Vector3 m_CursorRayDirection;

        public Vector2 CursorPosition { get; }
        public float MaximumDistancePixels { get; }

        private ObjectSnapScreenQuery(Vector2 cursorPosition, float maximumDistancePixels, in ObjectSnapPlane left, in ObjectSnapPlane right, in ObjectSnapPlane bottom, in ObjectSnapPlane top, in ObjectSnapPlane front, Vector3 cursorRayOrigin, Vector3 cursorRayDirection)
        {
            CursorPosition = cursorPosition;
            MaximumDistancePixels = maximumDistancePixels;
            m_Left = left;
            m_Right = right;
            m_Bottom = bottom;
            m_Top = top;
            m_Front = front;
            m_CursorRayOrigin = cursorRayOrigin;
            m_CursorRayDirection = cursorRayDirection;
        }

        public static bool TryCreate(Camera camera, Vector2 cursorPosition, float maximumDistancePixels, out ObjectSnapScreenQuery query)
        {
            query = default;

            if (camera == null || maximumDistancePixels <= 0f || !IsFinite(cursorPosition))
            {
                return false;
            }

            float depth = Mathf.Max(1f, camera.nearClipPlane + 0.01f);

            Vector3 bottomLeft = camera.ScreenToWorldPoint(new Vector3(cursorPosition.x - maximumDistancePixels, cursorPosition.y - maximumDistancePixels, depth));

            Vector3 topLeft = camera.ScreenToWorldPoint(new Vector3(cursorPosition.x - maximumDistancePixels, cursorPosition.y + maximumDistancePixels, depth));

            Vector3 topRight = camera.ScreenToWorldPoint(new Vector3(cursorPosition.x + maximumDistancePixels, cursorPosition.y + maximumDistancePixels, depth));

            Vector3 bottomRight = camera.ScreenToWorldPoint(new Vector3(cursorPosition.x + maximumDistancePixels, cursorPosition.y - maximumDistancePixels, depth));

            Vector3 interiorPoint = camera.ScreenToWorldPoint(new Vector3(cursorPosition.x, cursorPosition.y, depth));

            Vector3 cameraPosition = camera.transform.position;

            Vector3 cameraForward = camera.transform.forward;

            Vector3 cursorRayOrigin = camera.orthographic ? interiorPoint : cameraPosition;

            Vector3 cursorRayDirection = camera.orthographic ? cameraForward : interiorPoint - cameraPosition;

            float cursorRayMagnitudeSquared = cursorRayDirection.sqrMagnitude;

            if (!IsFinite(bottomLeft) || !IsFinite(topLeft) || !IsFinite(topRight) || !IsFinite(bottomRight) || !IsFinite(interiorPoint) || !IsFinite(cameraPosition) || !IsFinite(cameraForward) || !IsFinite(cursorRayOrigin) || !IsFinite(cursorRayDirection) || cursorRayMagnitudeSquared <= 0.00000001f || !IsFinite(cursorRayMagnitudeSquared))
            {
                return false;
            }

            cursorRayDirection /= Mathf.Sqrt(cursorRayMagnitudeSquared);

            ObjectSnapPlane left;
            ObjectSnapPlane right;
            ObjectSnapPlane bottom;
            ObjectSnapPlane top;

            if (camera.orthographic)
            {
                if (!ObjectSnapPlane.TryCreateFromPoints(bottomLeft, topLeft, bottomLeft + cameraForward, interiorPoint, out left) || !ObjectSnapPlane.TryCreateFromPoints(topRight, bottomRight, topRight + cameraForward, interiorPoint, out right) || !ObjectSnapPlane.TryCreateFromPoints(bottomRight, bottomLeft, bottomRight + cameraForward, interiorPoint, out bottom) || !ObjectSnapPlane.TryCreateFromPoints(topLeft, topRight, topLeft + cameraForward, interiorPoint, out top))
                {
                    return false;
                }
            }
            else
            {
                if (!ObjectSnapPlane.TryCreateFromPoints(cameraPosition, bottomLeft, topLeft, interiorPoint, out left) || !ObjectSnapPlane.TryCreateFromPoints(cameraPosition, topRight, bottomRight, interiorPoint, out right) || !ObjectSnapPlane.TryCreateFromPoints(cameraPosition, bottomRight, bottomLeft, interiorPoint, out bottom) || !ObjectSnapPlane.TryCreateFromPoints(cameraPosition, topLeft, topRight, interiorPoint, out top))
                {
                    return false;
                }
            }

            if (!ObjectSnapPlane.TryCreateFromNormalAndPoint(cameraForward, cameraPosition, interiorPoint, out ObjectSnapPlane front))
            {
                return false;
            }

            query = new ObjectSnapScreenQuery(cursorPosition, maximumDistancePixels, left, right, bottom, top, front, cursorRayOrigin, cursorRayDirection);

            return true;
        }

        public ObjectSnapLocalQuery ToLocal(in ObjectTransform transform)
        {
            return new ObjectSnapLocalQuery(m_Left.ToLocal(transform), m_Right.ToLocal(transform), m_Bottom.ToLocal(transform), m_Top.ToLocal(transform), m_Front.ToLocal(transform), transform.InverseTransformPoint(m_CursorRayOrigin), transform.InverseTransformVector(m_CursorRayDirection));
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    internal readonly struct ObjectSnapLocalQuery
    {
        private readonly ObjectSnapPlane m_Left;
        private readonly ObjectSnapPlane m_Right;
        private readonly ObjectSnapPlane m_Bottom;
        private readonly ObjectSnapPlane m_Top;
        private readonly ObjectSnapPlane m_Front;
        private readonly float3 m_CursorRayOrigin;
        private readonly float3 m_CursorRayDirection;

        public ObjectSnapLocalQuery(in ObjectSnapPlane left, in ObjectSnapPlane right, in ObjectSnapPlane bottom, in ObjectSnapPlane top, in ObjectSnapPlane front, Vector3 cursorRayOrigin, Vector3 cursorRayDirection)
        {
            m_Left = left;
            m_Right = right;
            m_Bottom = bottom;
            m_Top = top;
            m_Front = front;

            float3 localDirection = new float3(cursorRayDirection.x, cursorRayDirection.y, cursorRayDirection.z);

            float magnitudeSquared = math.lengthsq(localDirection);

            m_CursorRayOrigin = new float3(cursorRayOrigin.x, cursorRayOrigin.y, cursorRayOrigin.z);

            m_CursorRayDirection = magnitudeSquared > 0.00000001f && math.isfinite(magnitudeSquared) ? localDirection * math.rsqrt(magnitudeSquared) : new float3(0f, 0f, 1f);
        }

        public bool Intersects(float3 minimum, float3 maximum)
        {
            float3 center = (minimum + maximum) * 0.5f;
            float3 extents = (maximum - minimum) * 0.5f;

            return m_Left.Intersects(center, extents) && m_Right.Intersects(center, extents) && m_Bottom.Intersects(center, extents) && m_Top.Intersects(center, extents) && m_Front.Intersects(center, extents);
        }

        public ObjectSnapTraversalPriority GetTraversalPriority(float3 minimum, float3 maximum)
        {
            if (TryGetRayEntryDistance(minimum, maximum, out float entryDistance))
            {
                return new ObjectSnapTraversalPriority(true, entryDistance);
            }

            float3 center = (minimum + maximum) * 0.5f;
            float3 extents = (maximum - minimum) * 0.5f;

            float rayDistance = math.max(0f, math.dot(center - m_CursorRayOrigin, m_CursorRayDirection));

            float3 closestRayPoint = m_CursorRayOrigin + m_CursorRayDirection * rayDistance;

            float3 outside = math.max(math.abs(closestRayPoint - center) - extents, new float3(0f));

            return new ObjectSnapTraversalPriority(false, math.lengthsq(outside));
        }

        private bool TryGetRayEntryDistance(float3 minimum, float3 maximum, out float entryDistance)
        {
            float minimumDistance = 0f;
            float maximumDistance = float.MaxValue;

            if (!TryClipRayAxis(m_CursorRayOrigin.x, m_CursorRayDirection.x, minimum.x, maximum.x, ref minimumDistance, ref maximumDistance) || !TryClipRayAxis(m_CursorRayOrigin.y, m_CursorRayDirection.y, minimum.y, maximum.y, ref minimumDistance, ref maximumDistance) || !TryClipRayAxis(m_CursorRayOrigin.z, m_CursorRayDirection.z, minimum.z, maximum.z, ref minimumDistance, ref maximumDistance))
            {
                entryDistance = float.MaxValue;
                return false;
            }

            entryDistance = minimumDistance;
            return maximumDistance >= 0f;
        }

        private static bool TryClipRayAxis(float origin, float direction, float axisMinimum, float axisMaximum, ref float minimumDistance, ref float maximumDistance)
        {
            if (math.abs(direction) <= 0.0000001f)
            {
                return origin >= axisMinimum && origin <= axisMaximum;
            }

            float inverseDirection = 1f / direction;
            float first = (axisMinimum - origin) * inverseDirection;

            float second = (axisMaximum - origin) * inverseDirection;

            if (first > second)
            {
                float swap = first;
                first = second;
                second = swap;
            }

            minimumDistance = math.max(minimumDistance, first);

            maximumDistance = math.min(maximumDistance, second);

            return maximumDistance >= minimumDistance;
        }
    }

    internal readonly struct ObjectSnapTraversalPriority
    {
        private readonly bool m_RayIntersects;
        private readonly float m_Score;

        public ObjectSnapTraversalPriority(bool rayIntersects, float score)
        {
            m_RayIntersects = rayIntersects;
            m_Score = score;
        }

        public bool IsPreferredTo(in ObjectSnapTraversalPriority other)
        {
            if (m_RayIntersects != other.m_RayIntersects)
            {
                return m_RayIntersects;
            }

            return m_Score <= other.m_Score;
        }
    }

    internal readonly struct ObjectSnapPlane
    {
        private const float kBoundsEpsilon = 0.001f;

        private readonly float3 m_Normal;
        private readonly float m_Distance;

        private ObjectSnapPlane(float3 normal, float distance)
        {
            m_Normal = normal;
            m_Distance = distance;
        }

        public static bool TryCreateFromPoints(Vector3 first, Vector3 second, Vector3 third, Vector3 interiorPoint, out ObjectSnapPlane plane)
        {
            Vector3 normal = Vector3.Cross(second - first, third - first);

            return TryCreateFromNormalAndPoint(normal, first, interiorPoint, out plane);
        }

        public static bool TryCreateFromNormalAndPoint(Vector3 normal, Vector3 point, Vector3 interiorPoint, out ObjectSnapPlane plane)
        {
            plane = default;

            float magnitudeSquared = normal.sqrMagnitude;

            if (magnitudeSquared <= 0.00000001f || !IsFinite(magnitudeSquared))
            {
                return false;
            }

            normal /= Mathf.Sqrt(magnitudeSquared);
            float distance = -Vector3.Dot(normal, point);

            if (Vector3.Dot(normal, interiorPoint) + distance < 0f)
            {
                normal = -normal;
                distance = -distance;
            }

            if (!IsFinite(normal.x) || !IsFinite(normal.y) || !IsFinite(normal.z) || !IsFinite(distance))
            {
                return false;
            }

            plane = new ObjectSnapPlane(new float3(normal.x, normal.y, normal.z), distance);

            return true;
        }

        public ObjectSnapPlane ToLocal(in ObjectTransform transform)
        {
            Vector3 worldNormal = new Vector3(m_Normal.x, m_Normal.y, m_Normal.z);

            Vector3 unrotatedNormal = Quaternion.Inverse(transform.Rotation) * worldNormal;

            Vector3 localNormal = Vector3.Scale(transform.Scale, unrotatedNormal);

            float localDistance = Vector3.Dot(worldNormal, transform.Position) + m_Distance;

            return new ObjectSnapPlane(new float3(localNormal.x, localNormal.y, localNormal.z), localDistance);
        }

        public bool Intersects(float3 center, float3 extents)
        {
            float centerDistance = math.dot(m_Normal, center) + m_Distance;

            float projectedRadius = math.dot(math.abs(m_Normal), extents);

            return centerDistance + projectedRadius >= -kBoundsEpsilon;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}