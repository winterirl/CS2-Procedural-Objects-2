using UnityEngine;

namespace PO2.Tools.Gizmos
{
    // Small value snapshot, safe to copy into the gizmo render job.
    internal struct CageOverlay
    {
        internal bool Visible;
        internal Vector3 P0, P1, P2, P3, P4, P5, P6, P7;
        internal byte Selected;
        internal int Hovered;

        internal Vector3 Get(int i)
        {
            switch (i)
            {
                case 0: return P0;
                case 1: return P1;
                case 2: return P2;
                case 3: return P3;
                case 4: return P4;
                case 5: return P5;
                case 6: return P6;
                default: return P7;
            }
        }

        internal void Set(int i, Vector3 p)
        {
            switch (i)
            {
                case 0: P0 = p; break;
                case 1: P1 = p; break;
                case 2: P2 = p; break;
                case 3: P3 = p; break;
                case 4: P4 = p; break;
                case 5: P5 = p; break;
                case 6: P6 = p; break;
                default: P7 = p; break;
            }
        }
    }

    public readonly struct GizmoContext
    {
        public GizmoTargetKind TargetKind { get; }
        public int ObjectId { get; }
        public int PartIndex { get; }
        public GizmoMode Mode { get; }
        public Vector3 Pivot { get; }
        public TransformFrame Frame { get; }
        public float Size { get; }
        public float ActiveScaleHandleLength { get; }

        public GizmoContext(GizmoTargetKind targetKind, int objectId, int partIndex, GizmoMode mode, Vector3 pivot, TransformFrame frame, float size, float activeScaleHandleLength = 0f)
        {
            TargetKind = targetKind;
            ObjectId = objectId;
            PartIndex = partIndex;
            Mode = mode;
            Pivot = pivot;
            Frame = frame;
            Size = size;
            ActiveScaleHandleLength = activeScaleHandleLength;
        }
    }
}