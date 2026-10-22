using System.Collections.Generic;

namespace PO2.Conversion
{
    // Keep enough context to explain failed conversions and safe fallbacks.
    internal sealed class ConversionProfile
    {
        private readonly List<ConversionPartProfile> m_Parts = new List<ConversionPartProfile>();
        public string PrefabName { get; set; } = string.Empty;

        public ConversionPartProfile AddPart(string partName, string renderPrefabName, string geometryAssetName)
        {
            var part = new ConversionPartProfile(partName, renderPrefabName, geometryAssetName);
            m_Parts.Add(part);
            return part;
        }

        public void LogCompleted(bool success, string failureReason = null)
        {
            if (!success)
            {
                Mod.log.Warn($"[PO Conversion] '{PrefabName}' failed: {failureReason ?? "Unknown failure."}");
            }

            LogFallbacks();
        }

        private void LogFallbacks()
        {
            for (int i = 0; i < m_Parts.Count; i++)
            {
                ConversionPartProfile part = m_Parts[i];
                List<string> fallbacks = null;
                AddFallback(ref fallbacks, "stream decode", part.StreamDecodeFallbackReason);
                AddFallback(ref fallbacks, "logical topology", part.TopologyFallbackReason);
                AddFallback(ref fallbacks, "structural topology", part.StructuralTopologyFallbackReason);
                AddFallback(ref fallbacks, "renderer preparation", part.RendererPreparationFallbackReason);

                if (fallbacks == null)
                {
                    continue;
                }

                Mod.log.Warn($"[PO Conversion] '{PrefabName}/{part.PartName}' " + $"(render prefab '{part.RenderPrefabName}', geometry '{part.GeometryAssetName}') " + $"used safe fallback(s): " + $"{string.Join("; ", fallbacks)}.");
            }
        }

        private static void AddFallback(ref List<string> fallbacks, string stage, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return;
            }

            if (fallbacks == null)
            {
                fallbacks = new List<string>(4);
            }

            fallbacks.Add(stage + ": " + reason);
        }
    }

    internal sealed class ConversionPartProfile
    {
        public string PartName { get; }
        public string RenderPrefabName { get; }
        public string GeometryAssetName { get; }
        public string StreamDecodeFallbackReason { get; set; } = string.Empty;
        public string TopologyFallbackReason { get; set; } = string.Empty;
        public string StructuralTopologyFallbackReason { get; set; } = string.Empty;
        public string RendererPreparationFallbackReason { get; set; } = string.Empty;

        public ConversionPartProfile(string partName, string renderPrefabName, string geometryAssetName)
        {
            PartName = partName ?? string.Empty;
            RenderPrefabName = renderPrefabName ?? string.Empty;
            GeometryAssetName = geometryAssetName ?? string.Empty;
        }
    }
}