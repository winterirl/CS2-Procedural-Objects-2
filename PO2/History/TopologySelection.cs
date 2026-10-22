using System;

namespace PO2.History
{
    internal readonly struct TopologySelectionItem
    {
        internal readonly HistoryPartKey Part;
        internal readonly int Element;
        internal readonly byte Kind;

        internal TopologySelectionItem(HistoryPartKey part, int element, byte kind)
        {
            Part = part;
            Element = element;
            Kind = kind;
        }
    }

    internal sealed class TopologySelection
    {
        internal readonly int Mode;
        internal readonly TopologySelectionItem[] Items;

        internal TopologySelection(int mode, TopologySelectionItem[] items)
        {
            Mode = mode;
            Items = items ?? Array.Empty<TopologySelectionItem>();
        }

        internal long Bytes => 64 + 64L * Items.Length;
    }
}