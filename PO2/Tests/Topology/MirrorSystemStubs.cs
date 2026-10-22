using System;
using PO2.Core;
using PO2.Core.Primitives;
using PO2.Tools.Gizmos;
using PO2.Tools.ObjectTool;

namespace PO2 {
    internal static class MirrorFrameShim {
        internal static UnityEngine.Vector3 GetAxis(this TransformFrame frame, TransformAxis axis) =>
            axis == TransformAxis.X ? frame.XAxis : axis == TransformAxis.Y ? frame.YAxis : frame.ZAxis;
    }
}

namespace PO2.Core.Primitives {
    public enum PrimitiveKind { Box, Plane, Wedge, Cylinder, Cone, Arch, Stairs, Sphere, Dome }
}

namespace PO2.Tools.Gizmos {
    internal enum TransformTarget { Object, ActivePart }
}

namespace PO2.Tools.ObjectTool {
    public partial class ProceduralObjectToolSystem {
        internal TransformTarget m_TransformTarget;
        private bool m_DeformMode;
        private void SynchronizeCustomTransformOrientationTarget() { }
        private TransformFrame ResolveActiveTransformFrame(ProceduralObject obj) =>
            new TransformFrame(UnityEngine.Vector3.right, UnityEngine.Vector3.up, UnityEngine.Vector3.forward);
        private void NotifyHistoryGeometryChanged(ProceduralObject obj) { }
        internal void EnterMirrorForTest() { m_Mode = ObjectToolMode.Transform; SetMirrorMode(); }
    }
}

namespace PO2.Core {

    public sealed partial class ProceduralObjectSystem {
        // Only selection is supplied here.
        // The mirror command, geometry, delta encoding, and replay are production code.
        internal bool TestMirrorFirstPartPrepared => m_MirrorPreparation?.Parts[0]?.Result != null;
        internal ProceduralObject MirrorTestSelected;
        internal ProceduralObject SelectedObject => MirrorTestSelected;
        private bool TryGetSelected(out ProceduralObject obj) {
            obj = MirrorTestSelected;
            return obj != null && Objects.TryGetValue(obj.Id, out var live) && ReferenceEquals(live, obj);
        }
    }
}

namespace UnityEngine {
    public partial struct Vector2 {
        public static implicit operator Vector4(Vector2 v) => new Vector4(v.x, v.y, 0, 0);
    }
    public partial struct Vector3 {
        public static Vector3 left => -right;
        public static Vector3 down => -up;
        public static Vector3 back => -forward;
    }
    public static partial class Mathf { public const float PI = (float)Math.PI; }
}
