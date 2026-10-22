using UnityEngine;

namespace PO2.Tools.Gizmos
{
    internal enum TranslationConstraintKind
    {
        Axis,
        Plane,
    }

    // Translation is resolved in a frozen world-space frame. Axis movement
    // uses U only; plane movement uses both U and V while preserving Normal.
    internal readonly struct TranslationConstraint
    {
        private const float kMinimumAxisMagnitudeSquared = 0.0000001f;

        internal TranslationConstraintKind Kind { get; }
        internal Vector3 UAxis { get; }
        internal Vector3 VAxis { get; }
        internal Vector3 Normal { get; }

        internal bool IsPlane =>
            Kind == TranslationConstraintKind.Plane;

        internal bool IsValid =>
            UAxis.sqrMagnitude >= kMinimumAxisMagnitudeSquared && (!IsPlane || (VAxis.sqrMagnitude >= kMinimumAxisMagnitudeSquared && Normal.sqrMagnitude >= kMinimumAxisMagnitudeSquared));

        private TranslationConstraint(TranslationConstraintKind kind, Vector3 uAxis, Vector3 vAxis, Vector3 normal)
        {
            Kind = kind;
            UAxis = uAxis;
            VAxis = vAxis;
            Normal = normal;
        }

        internal static TranslationConstraint CreateAxis(Vector3 axis)
        {
            Vector3 normalized = axis.sqrMagnitude >= kMinimumAxisMagnitudeSquared ? axis.normalized : Vector3.up;

            return new TranslationConstraint(TranslationConstraintKind.Axis, normalized, Vector3.zero, Vector3.zero);
        }

        internal static bool TryCreate(GizmoHandle handle, TransformFrame frame, out TranslationConstraint constraint)
        {
            switch (handle)
            {
                case GizmoHandle.MoveX:
                    constraint = CreateAxis(frame.XAxis);
                    return constraint.IsValid;

                case GizmoHandle.MoveY:
                    constraint = CreateAxis(frame.YAxis);
                    return constraint.IsValid;

                case GizmoHandle.MoveZ:
                    constraint = CreateAxis(frame.ZAxis);
                    return constraint.IsValid;

                case GizmoHandle.MoveXY:
                    constraint = CreatePlane(frame.XAxis, frame.YAxis, frame.ZAxis);
                    return constraint.IsValid;

                case GizmoHandle.MoveXZ:
                    constraint = CreatePlane(frame.XAxis, frame.ZAxis, frame.YAxis);
                    return constraint.IsValid;

                case GizmoHandle.MoveYZ:
                    constraint = CreatePlane(frame.YAxis, frame.ZAxis, frame.XAxis);
                    return constraint.IsValid;

                default:
                    constraint = default;
                    return false;
            }
        }

        internal Vector2 ProjectWorldDelta(Vector3 worldDelta)
        {
            return new Vector2(Vector3.Dot(worldDelta, UAxis), IsPlane ? Vector3.Dot(worldDelta, VAxis) : 0f);
        }

        internal Vector3 ComposeWorldDelta(Vector2 components)
        {
            return UAxis * components.x + VAxis * components.y;
        }

        internal static TranslationConstraint CreatePlane(Vector3 uAxis, Vector3 vAxis, Vector3 normal)
        {
            return new TranslationConstraint(TranslationConstraintKind.Plane, uAxis.normalized, vAxis.normalized, normal.normalized);
        }
    }
}