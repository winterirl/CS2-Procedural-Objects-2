namespace PO2.Tools.Gizmos
{
    // Object transforms and mesh selections share the same rendered gizmo, but
    // the tool applies the resulting drag to very different backing data.
    public enum GizmoTargetKind
    {
        Object,
        Part,
        VertexSelection,
        CageCorners,
        ArrayEndpoint,
        ArrayRotation,
        ArrayControl1,
        ArrayControl2,
    }

    public enum GizmoMode
    {
        Translate,
        Rotate,
        Scale,
    }

    public enum SelectionTransformPivot
    {
        SelectionCenter,
        SavedPivot,
    }

    // Transform axes are independent of gizmo handles so numeric transforms,
    // snapping, and later orientation sources can consume the same frame.
    public enum TransformAxis
    {
        X,
        Y,
        Z,
    }

    public enum TransformOrientation
    {
        Global,
        Local,
        Custom,
    }

    public enum GizmoHandle
    {
        None,

        MoveX,
        MoveY,
        MoveZ,

        RotateX,
        RotateY,
        RotateZ,

        ScaleX,
        ScaleY,
        ScaleZ,
        ScaleUniform,

        // Keep new handles at the end so the existing runtime values do not
        // shift even though gizmo handles are not persisted today.
        MoveXY,
        MoveXZ,
        MoveYZ,
    }
}