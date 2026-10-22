using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using PO2.Diagnostics;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        internal PerformanceRendererSnapshot CapturePerformanceDiagnostics()
        {
            long activeMaterialBatchCount = 0L;
            long activeRendererCount = 0L;

            foreach (RenderInstance instance in m_Instances.Values)
            {
                for (int partIndex = 0; partIndex < instance.Parts.Count; partIndex++)
                {
                    RenderPart part = instance.Parts[partIndex];
                    activeMaterialBatchCount += part.Areas.Count;

                    if (part.HasMainRendererGeometry && part.Renderer != null)
                    {
                        activeRendererCount++;
                    }

                    for (int areaIndex = 0; areaIndex < part.Areas.Count; areaIndex++)
                    {
                        RenderArea area = part.Areas[areaIndex];
                        if (area.IsolatedRenderer && area.IsolatedRendererComponent != null)
                        {
                            activeRendererCount++;
                        }
                    }
                }
            }

            long cachedMaterialVariantCount = 0L;
            foreach (SurfaceMaterialEntry entry in m_SurfaceMaterials.Values)
            {
                cachedMaterialVariantCount += CountMaterialVariants(entry);
            }

            foreach (SpecialMaterialEntry entry in m_SpecialMaterials.Values)
            {
                cachedMaterialVariantCount += CountMaterialVariants(entry);
            }

            foreach (LibraryMaterialEntry entry in m_LibraryMaterials.Values)
            {
                cachedMaterialVariantCount += CountMaterialVariants(entry);
            }

            return new PerformanceRendererSnapshot(activeMaterialBatchCount, activeRendererCount, cachedMaterialVariantCount, m_LibraryTextures.Count);
        }

        private static int CountMaterialVariants(MaterialVariantEntry entry)
        {
            if (entry == null)
            {
                return 0;
            }

            int count = 0;
            if (entry.DecalsDisabledMaterial != null)
            {
                count++;
            }

            if (entry.DecalsEnabledMaterial != null)
            {
                count++;
            }

            return count;
        }
    }
}