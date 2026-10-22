using PO2.Core;
using PO2.Geometry;

namespace PO2.Conversion
{
    // This is a snapshot returned by the extractor.
    // Once this exists creating the PO does not need the source entity to stay alive.
    internal sealed class AssetConversionResult
    {
        public string RequestedName { get; }
        public ObjectTransform Transform { get; }
        public MeshPart[] Parts { get; }
        public ConversionBoundsSnapshot LocalBounds { get; }
        public ConvertedRenderPreparation RenderPreparation { get; }
        internal ConversionProfile Profile { get; }

        public AssetConversionResult(string requestedName, ObjectTransform transform, MeshPart[] parts, ConversionBoundsSnapshot localBounds, ConvertedRenderPreparation renderPreparation, ConversionProfile profile = null)
        {
            RequestedName = requestedName;
            Transform = transform;
            Parts = parts;
            LocalBounds = localBounds;
            RenderPreparation = renderPreparation;
            Profile = profile;
        }
    }
}