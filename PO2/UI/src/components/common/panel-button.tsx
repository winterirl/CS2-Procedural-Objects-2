import { Button } from "cs2/ui";
import type { ComponentProps } from "react";
import styles from "@/styles/controls.module.scss";

type PanelButtonProps = ComponentProps<typeof Button>;

export const PanelButton = ({
    className = "",
    selected = false,
    disabled = false,
    variant = "default",
    ...props
}: PanelButtonProps) => (
    <Button
        {...props}
        variant={variant === "text" ? "text" : "default"}
        selected={selected}
        disabled={disabled}
        className={`${styles.button} ${selected ? styles.selected : ""} ${disabled ? styles.disabled : ""} ${className}`}
    />
);
