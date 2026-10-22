using System;
using PO2.Core;
using PO2.History;
using PO2.Localization;
using PO2.Materials;

namespace PO2.History.Actions
{
    internal sealed class AppearanceState
    {
        internal readonly MaterialData Material;
        internal readonly ColorMaskData Mask;

        internal AppearanceState(SurfaceArea area)
        {
            Material = area.Material.Clone();
            Mask = area.ColorMask.Clone();
        }

        internal ulong Fingerprint => HistoryHash.Appearance(Material, Mask);
        internal long EstimatedBytes => 320 + (Material.DefinitionId?.Length ?? 0) * 2L;
    }

    internal sealed class AppearanceAction : HistorySwapAction<AppearanceState>
    {
        private readonly HistorySurfaceAreaKey m_Key;

        internal AppearanceAction(ProceduralObject obj, int partIndex, SurfaceArea area, LocalizedText description) : base(description, new AppearanceState(area))
        {
            m_Key = new HistorySurfaceAreaKey(new HistoryPartKey(new HistoryObjectKey(obj.Id, obj.PersistentId), obj.Parts[partIndex].PartHistoryId, partIndex), area.Id);
        }

        public override HistoryFootprint Footprint => new HistoryFootprint(96 + (Opposite?.EstimatedBytes ?? 0), 0, 0, 96 + (Opposite?.EstimatedBytes ?? 0));

        protected override AppearanceState Capture(HistoryContext context)
        {
            if (!context.TryResolve(m_Key, out var area)) throw new InvalidOperationException("Missing history Surface Area " + m_Key.AreaId);
            return new AppearanceState(area);
        }

        protected override ulong Fingerprint(AppearanceState state) => state.Fingerprint;

        protected override void Apply(HistoryContext context, AppearanceState state)
        {
            if (!context.TryResolve(m_Key.Part.Owner, out var obj) || !context.TryResolve(m_Key, out var area))
            {
                throw new InvalidOperationException("Missing history appearance target.");
            }

            if (state.Material.SourceKind == MaterialSourceKind.LibraryMaterial && !MaterialLibrary.TryGet(state.Material.DefinitionId, out MaterialDefinition _))
            {
                throw new InvalidOperationException("History material is no longer available: " + state.Material.DefinitionId);
            }

            area.ApplyHistoryAppearance(state);
            obj.MarkDirty(ProceduralObjectDirtyFlags.Material);
        }
    }
}