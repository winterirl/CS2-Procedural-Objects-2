using System;
using System.Collections.Generic;

namespace PO2.Geometry.Topology
{
    // Ordered path through structural quads used by Loop Cut and dissolve.
    // Open strips are valid, not every useful loop actually closes.
    public sealed class StructuralLoop
    {
        private readonly StructuralLoopStep[] m_Steps;

        public IReadOnlyList<StructuralLoopStep> Steps =>
            m_Steps;

        public bool IsClosed { get; }

        public int StepCount =>
            m_Steps.Length;

        public StructuralLoop(StructuralLoopStep[] steps, bool isClosed)
        {
            m_Steps = steps ?? Array.Empty<StructuralLoopStep>();
            IsClosed = isClosed;
        }
    }

    public readonly struct StructuralLoopStep
    {
        // Loop Cut is quad-only.
        // FaceId is a general structural identity
        // QuadId is a compatibility alias for quad-strip cut/dissolve callers.
        // Keep the alias until those APIs are migrated instead oftreating it like obsolete transitional state.
        // this is more technical debt I probs should have dealt with already.
        public int FaceId { get; }
        public int QuadId => FaceId;
        public int EntryEdgeId { get; }
        public int ExitEdgeId { get; }
        public int EntryBoundaryIndex { get; }

        // True when the global loop-cut parameter runs opposite the boundary direction for this quad's entry edge.
        public bool ReverseParameter { get; }

        public StructuralLoopStep(int quadId, int entryEdgeId, int exitEdgeId, int entryBoundaryIndex, bool reverseParameter)
        {
            FaceId = quadId;
            EntryEdgeId = entryEdgeId;
            ExitEdgeId = exitEdgeId;
            EntryBoundaryIndex = entryBoundaryIndex;
            ReverseParameter = reverseParameter;
        }
    }
}