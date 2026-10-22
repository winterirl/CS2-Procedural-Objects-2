using UnityEngine;
using PO2.Core;

namespace PO2.Tools.Gizmos
{
    // Everything needed to calculate rotation from the original mouse-down location.
    // No cumulative rotation, so cancelling and precision changes stay exact.
    public struct RotateDragState
    {
        public int ObjectId;
        public GizmoHandle Handle;
        public Vector3 Pivot;
        public ObjectTransform StartTransform;
        public Quaternion StartRotation;
        public TransformFrame Frame;
        public Vector3 Axis;
        public Plane DragPlane;
        public Vector3 StartDirection;
        public float GizmoSize;
    }
}