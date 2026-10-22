using System;

namespace PO2.Input
{
    [Flags]
    internal enum HotkeyScope
    {
        None = 0,
        Inactive = 1 << 0,
        Select = 1 << 1,
        Create = 1 << 2,
        Convert = 1 << 3,
        Object = 1 << 4,
        Edit = 1 << 5,
        Texturing = 1 << 6,
        Copy = 1 << 7,
        Paste = 1 << 8,
        Delete = 1 << 9,
        Mirror = 1 << 10,
        Deform = 1 << 11,
        Array = 1 << 12,
        Axis = 1 << 13,
        FaceSelection = 1 << 14,
        FacePosition = 1 << 15,
        Target = 1 << 16,
        Drag = 1 << 17,
        Management = 1 << 18,
        Busy = 1 << 19,
        Lod = 1 << 20,
        Any = (1 << 21) - 1,
        Open = Any & ~Inactive,
        ObjectModes = Object | Mirror | Deform,
        Editors = ObjectModes | Edit | Texturing,
        TransformEditors = ObjectModes | Edit,
    }
}