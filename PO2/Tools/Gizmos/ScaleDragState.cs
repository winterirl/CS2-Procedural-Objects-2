using UnityEngine;
using PO2.Core;

namespace PO2.Tools.Gizmos
{
    // Scale has both axis-line and screen-plane fallbacks, plus a separate
    // screen-space path for the center uniform handle.
    public struct ScaleDragState
    {
        public int ObjectId;
        public GizmoHandle Handle;

        public ObjectTransform StartTransform;
        public Vector3 StartScale;
        public TransformFrame Frame;
        public Vector3 Pivot;
        public Vector3 Axis;
        public int AxisIndex;
        public float StartAxisParameter;

        public Plane DragPlane;
        public Vector3 StartMouseHit;

        public Vector2 StartMousePosition;
        public float GizmoSize;
        public float ActiveHandleLength;
    }
}