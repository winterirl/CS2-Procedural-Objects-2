using System;
using UnityEngine;
using PO2.Core;
using PO2.Geometry;
using PO2.History;
using PO2.Localization;

namespace PO2.History.Actions
{
    internal sealed class PartPivotState
    {
        internal Vector3 LocalPivot;
    }

    internal sealed class PartPivotAction : HistorySwapAction<PartPivotState>
    {
        private readonly HistoryPartKey m_Key;

        internal PartPivotAction(ProceduralObject obj, int partIndex, LocalizedText description) : base(description, new PartPivotState
        {
            LocalPivot = obj.Parts[partIndex].LocalPivot,
        })
        {
            MeshPart part = obj.Parts[partIndex];
            m_Key = new HistoryPartKey(new HistoryObjectKey(obj.Id, obj.PersistentId), part.PartHistoryId, partIndex);
        }

        public override HistoryFootprint Footprint =>
            new HistoryFootprint(160, 0, 0, 160);

        protected override PartPivotState Capture(HistoryContext context)
        {
            if (!context.TryResolve(m_Key, out MeshPart part, out int _))
            {
                throw new InvalidOperationException("Missing part pivot history target.");
            }

            return new PartPivotState
            {
                LocalPivot = part.LocalPivot,
            };
        }

        protected override ulong Fingerprint(PartPivotState state)
        {
            HistoryHash hash = HistoryHash.Start;
            hash.Add(state.LocalPivot);
            return hash.Value;
        }

        protected override void Apply(HistoryContext context, PartPivotState state)
        {
            if (!context.TryResolve(m_Key, out MeshPart part, out int _))
            {
                throw new InvalidOperationException("Missing part pivot history target.");
            }

            if (!part.SetLocalPivot(state.LocalPivot) && !part.LocalPivot.Equals(state.LocalPivot))
            {
                throw new InvalidOperationException("Invalid part pivot history state.");
            }
        }
    }
}