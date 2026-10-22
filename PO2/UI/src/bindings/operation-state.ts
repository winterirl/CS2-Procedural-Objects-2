// These values match AssetConversionPhase and MaterialImportMessageKind.
export enum AssetConversionPhase {
    Inactive = 0,
    Ready = 1,
    Running = 2,
    Cancelling = 3,
    Failed = 4,
}

export enum MaterialImportMessageKind {
    None = 0,
    ScanResult = 1,
    Error = 2,
}
