using System;

namespace PO2.History
{
    internal readonly struct HistoryObjectKey : IEquatable<HistoryObjectKey>
    {
        internal readonly int RuntimeId;
        internal readonly Guid PersistentId;
        internal bool IsValid => RuntimeId > 0 && PersistentId != Guid.Empty;

        internal HistoryObjectKey(int runtimeId, Guid persistentId)
        {
            RuntimeId = runtimeId;
            PersistentId = persistentId;
        }

        public bool Equals(HistoryObjectKey other) =>
            RuntimeId == other.RuntimeId && PersistentId == other.PersistentId;

        public override bool Equals(object obj) => obj is HistoryObjectKey other && Equals(other);
        public override int GetHashCode() => unchecked(RuntimeId * 397 ^ PersistentId.GetHashCode());
        public override string ToString() => $"{RuntimeId}/{PersistentId}";
    }

    internal readonly struct HistoryPartKey : IEquatable<HistoryPartKey>
    {
        internal readonly HistoryObjectKey Owner;

        internal readonly Guid PartId;

        // Hints are not included in equality or hashing.
        internal readonly int IndexHint;
        internal bool IsValid => Owner.IsValid && PartId != Guid.Empty;

        internal HistoryPartKey(HistoryObjectKey owner, Guid partId, int indexHint)
        {
            Owner = owner;
            PartId = partId;
            IndexHint = indexHint;
        }

        public bool Equals(HistoryPartKey other) => Owner.Equals(other.Owner) && PartId == other.PartId;
        public override bool Equals(object obj) => obj is HistoryPartKey other && Equals(other);
        public override int GetHashCode() => unchecked(Owner.GetHashCode() * 397 ^ PartId.GetHashCode());
        public override string ToString() => $"{Owner}/part:{PartId}";
    }

    internal readonly struct HistorySurfaceAreaKey : IEquatable<HistorySurfaceAreaKey>
    {
        internal readonly HistoryPartKey Part;
        internal readonly int AreaId;

        internal HistorySurfaceAreaKey(HistoryPartKey part, int areaId)
        {
            Part = part;
            AreaId = areaId;
        }

        public bool Equals(HistorySurfaceAreaKey other) => Part.Equals(other.Part) && AreaId == other.AreaId;
        public override bool Equals(object obj) => obj is HistorySurfaceAreaKey other && Equals(other);
        public override int GetHashCode() => unchecked(Part.GetHashCode() * 397 ^ AreaId);
    }

    internal readonly struct HistoryFocusState
    {
        internal readonly HistoryObjectKey? Object;
        internal readonly HistoryPartKey? Part;
        internal readonly int SurfaceAreaId;

        internal HistoryFocusState(HistoryObjectKey? obj, HistoryPartKey? part, int areaId)
        {
            Object = obj;
            Part = part;
            SurfaceAreaId = areaId;
        }
    }
}