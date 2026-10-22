namespace PO2.Materials
{
    // Describes how the optional normal texture should be read.
    // This is stored in the manifest instead of guessed from the filename at render time.
    public enum MaterialNormalMode
    {
        None,
        TangentSpace,
    }
}