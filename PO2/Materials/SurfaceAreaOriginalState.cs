using System;

namespace PO2.Materials
{
    // Immutable appearance snapshot captured during asset conversion. It survives
    // later edits so "restore original" means the source asset not the last edit.
    public sealed class SurfaceAreaOriginalState
    {
        private readonly MaterialData m_Material;
        private readonly ColorMaskData m_ColorMask;

        public SurfaceAreaOriginalState(MaterialData material, ColorMaskData colorMask)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            // Never retain caller-owned mutable instances in the snapshot.
            m_Material = material.Clone();
            m_ColorMask = (colorMask ?? new ColorMaskData()).Clone();
        }

        public MaterialData CreateMaterialClone()
        {
            return m_Material.Clone();
        }

        public ColorMaskData CreateColorMaskClone()
        {
            return m_ColorMask.Clone();
        }

        public SurfaceAreaOriginalState Clone()
        {
            return new SurfaceAreaOriginalState(m_Material, m_ColorMask);
        }
    }
}