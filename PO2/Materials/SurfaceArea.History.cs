using PO2.History;
using PO2.History.Actions;

namespace PO2.Materials
{
    public sealed partial class SurfaceArea
    {
        internal void ApplyHistoryAppearance(AppearanceState state)
        {
            bool sourceChanged = HistoryHash.Source(Material) != HistoryHash.Source(state.Material);
            bool scalarsChanged = HistoryHash.Scalars(Material) != HistoryHash.Scalars(state.Material);
            bool mappingChanged = HistoryHash.Mapping(Material.Mapping) != HistoryHash.Mapping(state.Material.Mapping);
            bool maskChanged = HistoryHash.Mask(ColorMask) != HistoryHash.Mask(state.Mask);
            var material = state.Material.Clone();
            var mask = state.Mask.Clone();
            Material = material;
            ColorMask = mask;
            if (sourceChanged) MaterialSourceVersion++;
            if (scalarsChanged) MaterialPropertiesVersion++;
            if (mappingChanged) MappingVersion++;
            if (maskChanged) ColorMaskVersion++;
        }
    }
}