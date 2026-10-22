using UnityEngine;

namespace PO2.Tools.Snapping
{
    // The three integer ids mean different things per target kind. Keeping the
    // comparison here lets hysteresis treat game entities and PO features the
    // same way without packing part and feature ids into one value.
    internal readonly struct SnapTarget
    {
        public SnapTargetKind Kind { get; }
        public int PrimaryId { get; }
        public int SecondaryId { get; }
        public int TertiaryId { get; }
        public Vector3 Position { get; }
        public float CursorDistancePixels { get; }

        public bool IsValid =>
            Kind != SnapTargetKind.None;

        public SnapTarget(SnapTargetKind kind, int primaryId, int secondaryId, int tertiaryId, Vector3 position, float cursorDistancePixels)
        {
            Kind = kind;
            PrimaryId = primaryId;
            SecondaryId = secondaryId;
            TertiaryId = tertiaryId;
            Position = position;
            CursorDistancePixels = cursorDistancePixels;
        }

        public bool HasSameIdentity(in SnapTarget other)
        {
            return Kind == other.Kind && PrimaryId == other.PrimaryId && SecondaryId == other.SecondaryId && TertiaryId == other.TertiaryId;
        }

        public SnapTarget WithCursorDistance(float cursorDistancePixels)
        {
            return new SnapTarget(Kind, PrimaryId, SecondaryId, TertiaryId, Position, cursorDistancePixels);
        }
    }
}