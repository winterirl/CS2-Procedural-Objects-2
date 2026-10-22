using UnityEngine;
using PO2.Tools.Gizmos;

namespace PO2.Tools.Snapping
{
    // Keeps the chosen source and target stable while the cursor crosses nearby
    // PO features or overlapping game surfaces.
    internal sealed class TranslationSnapState
    {
        public const float kTargetQueryRadiusPixels = 26f;

        private const float kEnterRadiusPixels = 18f;
        private const float kReleaseRadiusPixels = 26f;
        private const float kSwitchImprovementPixels = 4f;
        private const float kSourceSwitchPixels = 4f;

        private ITranslationSnapSource m_Source;
        private TranslationConstraint m_Constraint;
        private Vector3 m_SourceOffset;

        private bool m_HasActiveSnap;
        private SnapTarget m_Target;
        private int m_SourcePrimitiveIndex = -1;
        private Vector3 m_ResolvedSourcePosition;

        internal SnapTarget CurrentTarget => m_Target;
        public bool HasActiveSnap => m_HasActiveSnap;
        public SnapTargetKind TargetKind => m_Target.Kind;
        public Vector3 TargetPosition => m_Target.Position;

        public Vector3 ResolvedSourcePosition =>
            m_ResolvedSourcePosition;

        public void Begin(ITranslationSnapSource source, Vector3 axis)
        {
            Begin(source, TranslationConstraint.CreateAxis(axis), Vector3.zero);
        }

        public void Begin(ITranslationSnapSource source, Vector3 axis, Vector3 sourceOffset)
        {
            Begin(source, TranslationConstraint.CreateAxis(axis), sourceOffset);
        }

        internal void Begin(ITranslationSnapSource source, TranslationConstraint constraint)
        {
            Begin(source, constraint, Vector3.zero);
        }

        internal void Begin(ITranslationSnapSource source, TranslationConstraint constraint, Vector3 sourceOffset)
        {
            Reset();
            m_Source = source;
            m_Constraint = constraint.IsValid ? constraint : TranslationConstraint.CreateAxis(Vector3.up);
            m_SourceOffset = sourceOffset;
        }

        public bool TryResolve(Camera camera, Vector2 cursorPosition, float preliminaryAxisDistance, in SnapTarget target, out float resolvedAxisDistance)
        {
            bool snapped = TryResolve(camera, cursorPosition, new Vector2(preliminaryAxisDistance, 0f), target, out Vector2 resolvedComponents);

            resolvedAxisDistance = resolvedComponents.x;
            return snapped;
        }

        internal bool TryResolve(Camera camera, Vector2 cursorPosition, Vector2 preliminaryComponents, in SnapTarget target, out Vector2 resolvedComponents)
        {
            resolvedComponents = preliminaryComponents;

            if (camera == null || m_Source == null)
            {
                ClearActiveSnap();
                return false;
            }

            SnapSolution challenger = default;
            bool hasChallenger = target.IsValid && TryEvaluate(camera, target, m_HasActiveSnap && m_Target.HasSameIdentity(target) ? m_SourcePrimitiveIndex : -1, out challenger);

            if (!m_HasActiveSnap)
            {
                if (!hasChallenger || challenger.ScorePixels > kEnterRadiusPixels)
                {
                    return false;
                }

                Accept(challenger);
                resolvedComponents = challenger.Components;
                return true;
            }

            bool sameTarget = hasChallenger && m_Target.HasSameIdentity(challenger.Target);

            if (sameTarget)
            {
                if (challenger.ScorePixels > kReleaseRadiusPixels)
                {
                    ClearActiveSnap();
                    return false;
                }

                Accept(challenger);
                resolvedComponents = challenger.Components;
                return true;
            }

            SnapSolution held = default;

            bool hasHeld = TryGetScreenDistancePixels(camera, m_Target.Position, cursorPosition, out float heldCursorDistance) && heldCursorDistance <= kReleaseRadiusPixels && TryEvaluate(camera, m_Target.WithCursorDistance(heldCursorDistance), m_SourcePrimitiveIndex, out held) && held.ScorePixels <= kReleaseRadiusPixels;

            bool challengerCanEnter = hasChallenger && challenger.ScorePixels <= kEnterRadiusPixels;

            if (challengerCanEnter && (!hasHeld || challenger.ScorePixels + kSwitchImprovementPixels < held.ScorePixels))
            {
                Accept(challenger);
                resolvedComponents = challenger.Components;
                return true;
            }

            if (hasHeld)
            {
                Accept(held);
                resolvedComponents = held.Components;
                return true;
            }

            ClearActiveSnap();
            return false;
        }

        public void ClearActiveSnap()
        {
            m_HasActiveSnap = false;
            m_Target = default;
            m_SourcePrimitiveIndex = -1;
            m_ResolvedSourcePosition = Vector3.zero;
        }

        public void Reset()
        {
            m_Source?.Dispose();
            m_Source = null;
            m_Constraint = default;
            m_SourceOffset = Vector3.zero;
            ClearActiveSnap();
        }

        private bool TryEvaluate(Camera camera, in SnapTarget target, int preferredSourcePrimitiveIndex, out SnapSolution solution)
        {
            solution = default;

            if (!TryFindSource(camera, target.Position, preferredSourcePrimitiveIndex, out int sourcePrimitiveIndex, out Vector3 sourceInCapturedSpace))
            {
                return false;
            }

            Vector2 components = m_Constraint.ProjectWorldDelta(target.Position - sourceInCapturedSpace);

            Vector3 resolvedSourcePosition = sourceInCapturedSpace + m_Constraint.ComposeWorldDelta(components);

            // The cursor chooses the target. A constrained drag then
            // matches only the active axis or plane coordinates, whether or
            // not the source can touch it on a locked coordinate.
            solution = new SnapSolution
            {
                Target = target,
                SourcePrimitiveIndex = sourcePrimitiveIndex,
                ResolvedSourcePosition = resolvedSourcePosition,
                Components = components,
                ScorePixels = target.CursorDistancePixels,
            };

            return true;
        }

        private bool TryFindSource(Camera camera, Vector3 targetInCapturedSpace, int preferredSourcePrimitiveIndex, out int sourcePrimitiveIndex, out Vector3 sourceInCapturedSpace)
        {
            // Source geometry stays in its captured starting pose. Choosing
            // against the preliminary gizmo position can put the pivot at the
            // target first, then select a trailing edge from the overlap.
            bool found = m_Source.TryFindNearest(targetInCapturedSpace - m_SourceOffset, out sourcePrimitiveIndex, out sourceInCapturedSpace, out float sourceDistanceSquared);

            if (!found)
            {
                return false;
            }

            sourceInCapturedSpace += m_SourceOffset;

            if (preferredSourcePrimitiveIndex >= 0 && preferredSourcePrimitiveIndex != sourcePrimitiveIndex && m_Source.TryGetClosestPoint(preferredSourcePrimitiveIndex, targetInCapturedSpace - m_SourceOffset, out Vector3 preferredPoint, out float preferredDistanceSquared))
            {
                preferredPoint += m_SourceOffset;

                float switchDistance = GizmoMath.GetWorldSizeForPixels(camera, targetInCapturedSpace, kSourceSwitchPixels);

                float nearestDistance = Mathf.Sqrt(sourceDistanceSquared);

                float preferredDistance = Mathf.Sqrt(preferredDistanceSquared);

                if (preferredDistance <= nearestDistance + switchDistance)
                {
                    sourcePrimitiveIndex = preferredSourcePrimitiveIndex;
                    sourceInCapturedSpace = preferredPoint;
                }
            }

            return true;
        }

        private static bool TryGetScreenDistancePixels(Camera camera, Vector3 worldPosition, Vector2 screenPosition, out float distancePixels)
        {
            distancePixels = float.MaxValue;

            Vector3 projected = camera.WorldToScreenPoint(worldPosition);

            if (projected.z <= 0f)
            {
                return false;
            }

            distancePixels = Vector2.Distance(new Vector2(projected.x, projected.y), screenPosition);

            return true;
        }

        private void Accept(SnapSolution solution)
        {
            m_HasActiveSnap = true;
            m_Target = solution.Target;
            m_SourcePrimitiveIndex = solution.SourcePrimitiveIndex;
            m_ResolvedSourcePosition = solution.ResolvedSourcePosition;
        }

        private struct SnapSolution
        {
            public SnapTarget Target;
            public int SourcePrimitiveIndex;
            public Vector3 ResolvedSourcePosition;
            public Vector2 Components;
            public float ScorePixels;
        }
    }
}