using System.Runtime.InteropServices;
using PO2.Core.Lod;
using UnityEngine;
using PO2.Materials;

namespace PO2.History
{
    // A content check supplements the stable target key. Version counters cannot
    // serve this purpose because replay deliberately keeps advancing them.
    internal struct HistoryHash
    {
        private ulong m_Value;
        internal ulong Value => m_Value;
        internal static HistoryHash Start => new HistoryHash { m_Value = 14695981039346656037UL };

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits
        {
            [FieldOffset(0)] internal float Float;
            [FieldOffset(0)] internal int Bits;
        }

        internal void Add(int value)
        {
            unchecked
            {
                for (int i = 0; i < 4; i++)
                {
                    m_Value = (m_Value ^ (byte)value) * 1099511628211UL;
                    value >>= 8;
                }
            }
        }

        internal void Add(ulong value)
        {
            Add(unchecked((int)value));
            Add(unchecked((int)(value >> 32)));
        }

        internal void Add(bool value) => Add(value ? 1 : 0);
        internal void Add(float value) => Add(new FloatBits { Float = value }.Bits);

        internal void Add(string value)
        {
            Add(value == null ? -1 : value.Length);
            if (value != null)
                foreach (char c in value)
                    Add(c);
        }

        internal void Add(Vector2 value)
        {
            Add(value.x);
            Add(value.y);
        }

        internal void Add(Vector3 value)
        {
            Add(value.x);
            Add(value.y);
            Add(value.z);
        }

        internal void Add(Quaternion value)
        {
            Add(value.x);
            Add(value.y);
            Add(value.z);
            Add(value.w);
        }

        internal void Add(Color value)
        {
            Add(value.r);
            Add(value.g);
            Add(value.b);
            Add(value.a);
        }

        internal void Add(LodProfile profile)
        {
            Add(profile != null);
            if (profile == null) return;
            Add(profile.Lod1);
            Add(profile.Lod2);
            Add(profile.Transitions != null);
            if (profile.Transitions != null)
            {
                Add(profile.Transitions.Lod1Distance);
                Add(profile.Transitions.Lod2Distance);
            }
        }

        private void Add(LodLevelSettings level)
        {
            Add(level.Enabled);
            Add((int)level.Method);
            Add(level.TriangleRatio);
            Add(level.TriangleCap ?? 0);
            Add(level.MaximumRelativeError);
            Add(level.PruneRelativeError);
        }

        internal static ulong Source(MaterialData material)
        {
            var hash = Start;
            hash.Add((int)material.SourceKind);
            hash.Add(material.DefinitionId);
            hash.Add(material.SurfaceGuid.ToString());
            hash.Add((int)material.VanillaSpecialKind);
            return hash.Value;
        }

        internal static ulong Scalars(MaterialData material)
        {
            var hash = Start;
            hash.Add(material.Metallic);
            hash.Add(material.Smoothness);
            hash.Add(material.CoatStrength);
            return hash.Value;
        }

        internal static ulong Mapping(TextureMappingData mapping)
        {
            var hash = Start;
            hash.Add((int)mapping.Mode);
            hash.Add(mapping.TextureScale);
            hash.Add(mapping.Rotation);
            hash.Add(mapping.Offset);
            return hash.Value;
        }

        internal static ulong Mask(ColorMaskData mask)
        {
            var hash = Start;
            hash.Add((int)mask.Mode);
            hash.Add(mask.HasSourceMask);
            hash.Add(mask.SourceMask1);
            hash.Add(mask.SourceMask2);
            hash.Add(mask.SourceMask3);
            hash.Add(mask.OverrideMask1);
            hash.Add(mask.OverrideMask2);
            hash.Add(mask.OverrideMask3);
            return hash.Value;
        }

        internal static ulong Appearance(MaterialData material, ColorMaskData mask)
        {
            var hash = Start;
            hash.Add(Source(material));
            hash.Add(Scalars(material));
            hash.Add(Mapping(material.Mapping));
            hash.Add(Mask(mask));
            return hash.Value;
        }
    }
}