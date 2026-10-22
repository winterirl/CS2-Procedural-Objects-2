namespace PO2.Materials
{
    // Keeps the built-in prototype grid separate from normal imported materials.
    // Only DefaultObject goes through the generic HDRP material path.
    public enum MaterialShaderKind
    {
        DefaultObject,
        PrototypeUnlit,
    }
}