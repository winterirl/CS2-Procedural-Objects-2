using System;
using System.Collections.Generic;
using Game.Prefabs;

namespace PO2.Conversion
{
    internal readonly struct ConversionMeshRange
    {
        internal readonly int Start;
        internal readonly int End;
        internal readonly int ColorOffset;

        internal ConversionMeshRange(int start, int end, int colorOffset)
        {
            Start = start;
            End = end;
            ColorOffset = colorOffset;
        }
    }

    internal readonly struct ConversionMeshSelection
    {
        internal readonly int MeshIndex;
        internal readonly int ColorIndex;

        internal ConversionMeshSelection(int meshIndex, int colorIndex)
        {
            MeshIndex = meshIndex;
            ColorIndex = colorIndex;
        }
    }

    internal static class ConvertedTreeSelection
    {
        internal const SubMeshFlags kStageMask = SubMeshFlags.RequireChild | SubMeshFlags.RequireTeen | SubMeshFlags.RequireAdult | SubMeshFlags.RequireElderly | SubMeshFlags.RequireDead | SubMeshFlags.RequireStump;

        internal static List<ConversionMeshSelection> Select(SubMeshFlags[] flags, SubMeshFlags stage, ConversionMeshRange[] ranges)
        {
            if (flags == null || ranges == null)
            {
                throw new ArgumentNullException(flags == null ? nameof(flags) : nameof(ranges));
            }

            uint stageBits = (uint)stage;
            if ((stage & kStageMask) != stage || stageBits == 0 || (stageBits & (stageBits - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stage), "A tree must resolve to one render stage.");
            }

            var selected = new List<ConversionMeshSelection>();
            foreach (ConversionMeshRange range in ranges)
            {
                if (range.Start < 0 || range.End < range.Start || range.End > flags.Length || range.ColorOffset < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(ranges), "The tree's mesh group range is invalid.");
                }

                for (int i = range.Start; i < range.End; i++)
                {
                    SubMeshFlags required = flags[i] & kStageMask;
                    if ((required & stage) != required || (flags[i] & (SubMeshFlags.OutlineOnly | SubMeshFlags.RequireEditor)) != 0)
                    {
                        continue;
                    }

                    // Color offsets are relative to the original group not the filtered list.
                    selected.Add(new ConversionMeshSelection(i, checked(i - range.Start + range.ColorOffset)));
                }
            }

            return selected;
        }
    }
}