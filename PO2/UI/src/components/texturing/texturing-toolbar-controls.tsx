import { usePO2Localization } from "@/l10n/localization";
import { Icon } from "cs2/ui";
import dividerIcon from "@/images/navigation/divider-vertical.svg";
import context from "@/shell/context-toolbar.module.scss";
import { ToolbarButton as Button } from "@/components/common/toolbar-controls";
import toolbar from "@/shell/main-toolbar.module.scss";

type TexturingToolbarControlsProps = {
    selectedPartCount: number;
    selectedSurfaceAreaCount: number;
    onAction: (action: string) => void;
};

export const TexturingToolbarControls = ({
    selectedPartCount,
    selectedSurfaceAreaCount,
    onAction,
}: TexturingToolbarControlsProps) => {
    const { t } = usePO2Localization();
    const partNavigationDisabled = selectedPartCount <= 1;
    const areaNavigationDisabled = selectedSurfaceAreaCount <= 1;

    return (
        <>
            <Button
                variant="menu"
                onSelect={() => onAction("previousSelectedPart")}
                disabled={partNavigationDisabled}
                className={toolbar.textButton}
            >
                {t("PO2.Texturing.Toolbar.PreviousPart.Action")}
            </Button>
            <Button
                variant="menu"
                onSelect={() => onAction("nextSelectedPart")}
                disabled={partNavigationDisabled}
                className={toolbar.textButton}
            >
                {t("PO2.Texturing.Toolbar.NextPart.Action")}
            </Button>
            <span className={context.divider} aria-hidden="true">
                <Icon src={dividerIcon} tinted={false} className={toolbar.dividerImage} />
            </span>
            <Button
                variant="menu"
                onSelect={() => onAction("previousSelectedSurfaceArea")}
                disabled={areaNavigationDisabled}
                className={toolbar.textButton}
            >
                {t("PO2.Texturing.Toolbar.PreviousArea.Action")}
            </Button>
            <Button
                variant="menu"
                onSelect={() => onAction("nextSelectedSurfaceArea")}
                disabled={areaNavigationDisabled}
                className={toolbar.textButton}
            >
                {t("PO2.Texturing.Toolbar.NextArea.Action")}
            </Button>
        </>
    );
};
