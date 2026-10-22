using System;

namespace PO2.Tools.ObjectTool
{
    // A UI workspace can contain several input modes. Views such as the material
    // library can stay open without taking over world input.
    public enum Workspace
    {
        Select = 0,
        Create = 1,
        MaterialLibrary = 2,
        Transform = 3,
        MeshEdit = 4,
        Texturing = 5,
        ObjectList = 6,
        Convert = 7,
        SaveManager = 8,
        Delete = 9,
        Copy = 10,
        Paste = 11,
        Lod = 12,
    }
}