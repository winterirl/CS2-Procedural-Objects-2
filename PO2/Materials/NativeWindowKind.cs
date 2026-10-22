namespace PO2.Materials
{
    // Keeps native window identities separate from generic assignable materials.
    // _Wim uses the _Win shader path but retains its frosted-window identity.
    public enum NativeWindowKind
    {
        None = 0,
        Window = 1,
        FrostedWindow = 2,
    }
}