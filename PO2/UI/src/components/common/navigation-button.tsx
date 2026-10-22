import { Button } from "cs2/ui";
import { getToolButtonTheme } from "./native-tool-button-theme";
import styles from "./navigation-button.module.scss";

type NavigationButtonProps = {
    direction: "previous" | "next";
    label: string;
    disabled?: boolean;
    onSelect: () => void;
};

export const NavigationButton = ({
    direction,
    label,
    disabled,
    onSelect,
}: NavigationButtonProps) => (
    <Button
        variant="icon"
        theme={{ ...getToolButtonTheme(), icon: `${styles.icon} ${direction === "previous" ? styles.previous : styles.next}` }}
        src="Media/Glyphs/ThickStrokeArrowDown.svg"
        className={styles.button}
        aria-label={label}
        title={label}
        disabled={disabled}
        onSelect={onSelect}
    />
);
