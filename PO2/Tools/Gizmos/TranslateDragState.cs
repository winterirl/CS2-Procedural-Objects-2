using UnityEngine;
using PO2.Core;

namespace PO2.Tools.Gizmos
{
    // Measure each update from the captured start instead of accumulating movement.
    public struct TranslateDragState
    {
        public int ObjectId;
        public GizmoHandle Handle;
        public ObjectTransform StartTransform;
        public Vector3 StartPosition;
        public TransformFrame Frame;
        internal TranslationConstraint Constraint;
        public float StartAxisParameter;
        public Plane DragPlane;
        public Vector3 StartMouseHit;
        public float GizmoSize;
    }
}