import { usePO2Localization } from "@/l10n/localization";
import { useReportHelpTopics } from "@/help/help-local-context";
import { TwoColumnRow } from "@/components/common/two-column-row";
import { InputLabel } from "@/components/common/input-label";
import { TextInput } from "@/components/common/text-input";
import { useEffect, useState } from "react";
import { MenuButton as Button } from "@/components/common/menu-button";
import { NavigationButton } from "@/components/common/navigation-button";
import { NumericStepper } from "@/components/common/numeric-stepper";
import selectedStyles from "@/components/selected-object/selected-object.module.scss";
import { SurfaceAreaNavigation } from "@/components/selected-object/surface-area-navigation";
import { normalizeHexColor } from "@/utils/format";
import styles from "@/styles/content.module.scss";
import controls from "@/styles/controls.module.scss";
import layout from "@/styles/layout.module.scss";

type TexturingActionArgument = number | string;

type TexturingWorkspaceProps = {
    selectedObjectLocked: boolean;
    selectedObjectReceivesDecals: boolean;
    selectedObjectHasOriginalAppearance: boolean;
    selectedSurfaceCount: number;
    selectedSurfaceAreaId: number;
    selectedSurfaceAreaIds: number[];
    selectedAreaDraftKey: string;
    mappingMode: number;
    mappingTextureWidth: number;
    mappingTextureHeight: number;
    mappingOffsetU: number;
    mappingOffsetV: number;
    mappingRotation: number;
    libraryMaterialCount: number;
    selectedLibraryMaterialIndex: number;
    selectedMaterialName: string;
    selectedMaterialSourceKind: number;
    selectedVanillaSpecialMaterialKind: number;
    materialMetallic: number;
    materialSmoothness: number;
    materialCoatStrength: number;
    colorMaskMode: number;
    colorMaskHasSource: boolean;
    colorMask1Hex: string;
    colorMask2Hex: string;
    colorMask3Hex: string;
    surfaceAreaHasOriginalAppearance: boolean;
    onAction: (action: string, ...args: TexturingActionArgument[]) => void;
};

export const TexturingWorkspace = ({
    selectedObjectLocked,
    selectedObjectReceivesDecals,
    selectedObjectHasOriginalAppearance,
    selectedSurfaceCount,
    selectedSurfaceAreaId,
    selectedSurfaceAreaIds,
    selectedAreaDraftKey,
    mappingMode,
    mappingTextureWidth,
    mappingTextureHeight,
    mappingOffsetU,
    mappingOffsetV,
    mappingRotation,
    libraryMaterialCount,
    selectedLibraryMaterialIndex,
    selectedMaterialName,
    selectedMaterialSourceKind,
    selectedVanillaSpecialMaterialKind,
    materialMetallic,
    materialSmoothness,
    materialCoatStrength,
    colorMaskMode,
    colorMaskHasSource,
    colorMask1Hex,
    colorMask2Hex,
    colorMask3Hex,
    surfaceAreaHasOriginalAppearance,
    onAction,
}: TexturingWorkspaceProps) => {
    const { t } = usePO2Localization();
    const [textureSettingsOpen, setTextureSettingsOpen] = useState(false);
    useReportHelpTopics("texturing", textureSettingsOpen ? "materialSettings" : null, textureSettingsOpen ? (mappingMode === 1 ? "mappingRepeat" : mappingMode === 2 ? "mappingStretch" : "mappingOriginal") : null);
    const [mask1HexValue, setMask1HexValue] = useState("#FFFFFF");
    const [mask2HexValue, setMask2HexValue] = useState("#FFFFFF");
    const [mask3HexValue, setMask3HexValue] = useState("#FFFFFF");

    useEffect(() => {
        setMask1HexValue(colorMask1Hex);
    }, [selectedAreaDraftKey, colorMask1Hex]);

    useEffect(() => {
        setMask2HexValue(colorMask2Hex);
    }, [selectedAreaDraftKey, colorMask2Hex]);

    useEffect(() => {
        setMask3HexValue(colorMask3Hex);
    }, [selectedAreaDraftKey, colorMask3Hex]);

    const commitColorMaskHex = (
        maskIndex: number,
        value: string,
        fallback: string,
        setValue: (nextValue: string) => void,
    ) => {
        if (selectedObjectLocked) {
            setValue(fallback);
            return;
        }

        // Keep partial hex input local. Send only complete six-digit colors,
        // and restore the bound value when a draft is invalid.
        const normalized = normalizeHexColor(value);
        if (normalized === null) {
            setValue(fallback);
            return;
        }

        setValue(normalized);
        onAction(`setColorMask${maskIndex}Hex`, normalized);
    };

    const scalar = (label: string, value: number, suffix: string) => (
        <InputLabel>
            <span>{label}</span>
            <NumericStepper
                value={value}
                digits={2}
                minimum={0}
                maximum={1}
                resetKey={selectedAreaDraftKey}
                ariaLabel={label}
                disabled={selectedObjectLocked}
                onCommit={(next) => onAction(`setMaterial${suffix}`, next)}
                onIncrease={() => onAction(`increaseMaterial${suffix}`)}
                onDecrease={() => onAction(`decreaseMaterial${suffix}`)}
            />
        </InputLabel>
    );
    const mappingField = (label: string, value: number, action: string, minimum?: number, step = 0.1) => (
        <InputLabel>
            <span>{label}</span>
            <NumericStepper
                value={value}
                digits={3}
                minimum={minimum}
                resetKey={selectedAreaDraftKey}
                ariaLabel={label}
                disabled={selectedObjectLocked}
                onCommit={(next) => onAction(action, next)}
                onIncrease={() => onAction(action, Number((value + step).toFixed(3)))}
                onDecrease={() => onAction(action, Math.max(minimum ?? -Infinity, Number((value - step).toFixed(3))))}
            />
        </InputLabel>
    );
    const masks = [
        { value: mask1HexValue, source: colorMask1Hex, set: setMask1HexValue },
        { value: mask2HexValue, source: colorMask2Hex, set: setMask2HexValue },
        { value: mask3HexValue, source: colorMask3Hex, set: setMask3HexValue },
    ];

    return (
        <div className={layout.stack}>
            <div>{t("PO2.Texturing.SurfaceArea.Title")}</div>
            <SurfaceAreaNavigation ids={selectedSurfaceAreaIds} selectedId={selectedSurfaceAreaId} onAction={onAction} />
            <div className={layout.divider} />
            <div>{t("PO2.Common.Select")}</div>
            <div className={layout.actionRow}>
                <Button
                    disabled={selectedObjectLocked}
                    onSelect={() => onAction("selectAllMeshElements")}
                >
                    {t("PO2.Common.All")}
                </Button>
                <Button
                    disabled={selectedObjectLocked}
                    onSelect={() => onAction("invertMeshSelection")}
                >
                    {t("PO2.Texturing.Selection.Invert.Action")}
                </Button>
            </div>
            <div>{t("PO2.Texturing.Selection.Summary", { count: selectedSurfaceCount })}</div>
            <div>{t("PO2.Texturing.Selection.Title")}</div>
            <div className={layout.actionRow}>
                <Button
                    disabled={selectedObjectLocked || selectedSurfaceCount === 0}
                    onSelect={() => onAction("newSurfaceAreaFromSelection")}
                >
                    {t("PO2.Texturing.SurfaceArea.Create.Action")}
                </Button>
                <Button
                    disabled={selectedObjectLocked || selectedSurfaceAreaId === 0}
                    onSelect={() => onAction("deleteSurfaceArea")}
                >
                    {t("PO2.Texturing.SurfaceArea.Delete.Action")}
                </Button>
            </div>
            <TwoColumnRow>
                <Button
                    disabled={selectedObjectLocked || selectedSurfaceCount === 0}
                    onSelect={() => onAction("assignSelectionToSurfaceArea")}
                >
                    {t("PO2.Texturing.SurfaceArea.AddSelection.Action")}
                </Button>
            </TwoColumnRow>
            <div>{t("PO2.Texturing.VanillaShader.Title")}</div>
            <div className={layout.actionRow}>
                {[{ action: "setVanillaSpecialGlass", label: t("PO2.Texturing.VanillaShader.Choice.Glass") },
                    { action: "setVanillaSpecialGrass", label: t("PO2.Texturing.VanillaShader.Choice.Grass") },
                    { action: "setVanillaSpecialWater", label: t("PO2.Texturing.VanillaShader.Choice.Water") }].map(({ action, label }, index) => (
                    <Button
                        key={action}
                        selected={
                            selectedMaterialSourceKind === 2 &&
                            selectedVanillaSpecialMaterialKind === index + 1
                        }
                        disabled={selectedObjectLocked}
                        onSelect={() => onAction(action)}
                    >
                        {label}
                    </Button>
                ))}
            </div>
            <div>{t("PO2.Texturing.LibraryMaterial.Title")}</div>
            <div className={selectedStyles.row}>
                <NavigationButton
                    direction="previous"
                    label={t("PO2.Texturing.LibraryMaterial.Previous.Action")}
                    disabled={selectedObjectLocked || libraryMaterialCount === 0}
                    onSelect={() => onAction("previousLibraryMaterial")}
                />
                <span className={selectedStyles.name}>{selectedMaterialName}</span>
                <NavigationButton
                    direction="next"
                    label={t("PO2.Texturing.LibraryMaterial.Next.Action")}
                    disabled={selectedObjectLocked || libraryMaterialCount === 0}
                    onSelect={() => onAction("nextLibraryMaterial")}
                />
            </div>
            <TwoColumnRow>
                <Button
                    disabled={selectedObjectLocked}
                    onSelect={() => onAction("openMaterialLibraryView")}
                >
                    {t("PO2.Texturing.LibraryMaterial.Choose.Action")}
                </Button>
                <Button
                    selected={textureSettingsOpen}
                    onSelect={() => setTextureSettingsOpen((open) => !open)}
                >
                    {t("PO2.Texturing.MaterialSettings.Toggle.Action")}
                </Button>
            </TwoColumnRow>
            {textureSettingsOpen && (
                <>
                    {selectedLibraryMaterialIndex >= 0 && (
                        <>
                            <TwoColumnRow>
                                {scalar(t("PO2.Texturing.MaterialSettings.Metallic.Label"), materialMetallic, "Metallic")}
                                {scalar(t("PO2.Texturing.MaterialSettings.Smoothness.Label"), materialSmoothness, "Smoothness")}
                            </TwoColumnRow>
                            <TwoColumnRow>
                                {scalar(t("PO2.Texturing.MaterialSettings.Coat.Label"), materialCoatStrength, "CoatStrength")}
                                <Button
                                    disabled={selectedObjectLocked}
                                    onSelect={() => onAction("resetMaterialProperties")}
                                >
                                    {t("PO2.Texturing.MaterialSettings.Reset.Action")}
                                </Button>
                            </TwoColumnRow>
                        </>
                    )}
                    <TwoColumnRow>
                        <Button
                            selected={mappingMode !== 0}
                            disabled={selectedObjectLocked}
                            onSelect={() => onAction("cycleMappingMode")}
                        >{t("PO2.Texturing.Mapping.Cycle.Label", { mode: mappingMode === 1 ? t("PO2.Texturing.Mapping.Choice.Repeat") : mappingMode === 2 ? t("PO2.Texturing.Mapping.Choice.Stretch") : t("PO2.Texturing.Choice.Original") })}</Button>
                    </TwoColumnRow>
                    {mappingMode === 1 && (
                        <>
                            <TwoColumnRow>
                                {mappingField(
                                    t("PO2.Texturing.Mapping.Width.Label"),
                                    mappingTextureWidth,
                                    "setMappingTextureWidth",
                                    0.001,
                                )}
                                {mappingField(
                                    t("PO2.Texturing.Mapping.Height.Label"),
                                    mappingTextureHeight,
                                    "setMappingTextureHeight",
                                    0.001,
                                )}
                            </TwoColumnRow>
                            <TwoColumnRow>
                                {mappingField(
                                    t("PO2.Texturing.Mapping.OffsetU.Label"),
                                    mappingOffsetU,
                                    "setMappingOffsetU",
                                )}
                                {mappingField(
                                    t("PO2.Texturing.Mapping.OffsetV.Label"),
                                    mappingOffsetV,
                                    "setMappingOffsetV",
                                )}
                            </TwoColumnRow>
                        </>
                    )}
                    {mappingMode !== 0 && (
                        <TwoColumnRow>
                            {mappingField(t("PO2.Texturing.Mapping.Rotation.Label"), mappingRotation, "setMappingRotation", undefined, 1)}
                            <Button
                                disabled={selectedObjectLocked}
                                onSelect={() => onAction("resetMappingTransform")}
                            >
                                {t("PO2.Texturing.Mapping.Reset.Action")}
                            </Button>
                        </TwoColumnRow>
                    )}
                    <div className={layout.divider} />
                </>
            )}
            <div>{t("PO2.Common.Reset")}</div>
            <div className={layout.actionRow}>
                <Button
                    disabled={selectedObjectLocked || !surfaceAreaHasOriginalAppearance}
                    onSelect={() => onAction("restoreSurfaceAreaOriginalAppearance")}
                >
                    {t("PO2.Texturing.Reset.Area.Action")}
                </Button>
                <Button
                    disabled={selectedObjectLocked || !selectedObjectHasOriginalAppearance}
                    onSelect={() => onAction("restoreSelectedObjectOriginalAppearance")}
                >
                    {t("PO2.Texturing.Reset.Object.Action")}
                </Button>
            </div>
            <div className={layout.divider} />
            <div>{t("PO2.Texturing.Projection.Title")}</div>
            <TwoColumnRow>
                <Button
                    selected={selectedObjectReceivesDecals}
                    disabled={selectedObjectLocked}
                    onSelect={() => onAction("toggleSelectedReceivesDecals")}
                >
                    {selectedObjectReceivesDecals ? t("PO2.Texturing.Projection.State.Enabled") : t("PO2.Texturing.Projection.State.Disabled")}
                </Button>
            </TwoColumnRow>
            <div>{t("PO2.Texturing.ColorMasks.Title")}</div>
            <div className={layout.actionRow}>
                <Button
                    selected={colorMaskMode === 1}
                    disabled={selectedObjectLocked || !colorMaskHasSource}
                    onSelect={() => onAction("setColorMaskSource")}
                >
                    {t("PO2.Texturing.Choice.Original")}
                </Button>
                <Button
                    selected={colorMaskMode === 2}
                    disabled={selectedObjectLocked}
                    onSelect={() => onAction("setColorMaskOverride")}
                >
                    {t("PO2.Texturing.ColorMasks.Choice.Override")}
                </Button>
                <Button
                    selected={colorMaskMode === 0}
                    disabled={selectedObjectLocked}
                    onSelect={() => onAction("setColorMaskOff")}
                >
                    {t("PO2.Common.Off")}
                </Button>
            </div>
            {colorMaskMode === 2 && (
                <>
                    <div>
                        {t("PO2.Texturing.ColorMasks.MultiplicationNotice")}
                    </div>
                    {masks.map((mask, index) => (
                        <InputLabel key={index} className={styles.colorMaskInputRow}>
                            <span>{t("PO2.Texturing.ColorMasks.Mask.Label", { index: index + 1 })}</span>
                            <TextInput
                                className={controls.maskInput}
                                value={mask.value}
                                disabled={selectedObjectLocked}
                                maxLength={7}
                                onChange={(event) => mask.set(event.currentTarget.value)}
                                onBlur={() =>
                                    commitColorMaskHex(index + 1, mask.value, mask.source, mask.set)
                                }
                                onKeyDown={(event) => {
                                    if (event.key === "Enter") event.currentTarget.blur();
                                    else if (event.key === "Escape") {
                                        mask.set(mask.source);
                                        event.preventDefault();
                                    }
                                }}
                                aria-label={t("PO2.Texturing.ColorMasks.HexColor.AccessibleLabel", { index: index + 1 })}
                            />
                        </InputLabel>
                    ))}
                </>
            )}
        </div>
    );
};
