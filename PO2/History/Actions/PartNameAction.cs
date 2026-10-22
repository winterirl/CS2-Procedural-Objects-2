using System;
using PO2.Core;
using PO2.History;
using PO2.Localization;

namespace PO2.History.Actions
{
    internal sealed class PartNameAction : HistorySwapAction<string>
    {
        private readonly HistoryPartKey m_Key;

        internal PartNameAction(ProceduralObject obj, int index) : base(LocalizedText.For("PO2.History.Action.Part.Rename"), obj.Parts[index].Name)
        {
            m_Key = new HistoryPartKey(new HistoryObjectKey(obj.Id, obj.PersistentId), obj.Parts[index].PartHistoryId, index);
        }

        public override HistoryFootprint Footprint => new HistoryFootprint(128 + (Opposite?.Length ?? 0) * 2L, 0, 0, 128 + (Opposite?.Length ?? 0) * 2L);

        protected override string Capture(HistoryContext context)
        {
            if (!context.TryResolve(m_Key, out var part, out _)) throw new InvalidOperationException("Missing part rename target.");
            return part.Name;
        }

        protected override ulong Fingerprint(string state)
        {
            var hash = HistoryHash.Start;
            hash.Add(state);
            return hash.Value;
        }

        protected override void Apply(HistoryContext context, string state)
        {
            if (!context.TryResolve(m_Key, out var part, out _)) throw new InvalidOperationException("Missing part rename target.");
            if (!context.TryResolve(m_Key.Owner, out var owner)) throw new InvalidOperationException("Missing part owner.");
            owner.ApplyHistoryPartName(m_Key.PartId, state);
        }
    }
}