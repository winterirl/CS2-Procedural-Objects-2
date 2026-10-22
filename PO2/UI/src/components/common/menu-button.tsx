import stationary from "./stationary-menu.module.scss";
import { Button } from "cs2/ui";
import type { ComponentProps } from "react";
import styles from "./menu-button.module.scss";

type MenuButtonProps = Omit<ComponentProps<typeof Button>, "variant"> & {
    labelClassName?: string;
};

// The sidebar uses one button skin. Call sites only add layout classes.
export const MenuButton = ({
    children,
    className = "",
    selected = false,
    labelClassName = "",
    ...props
}: MenuButtonProps) => (
    <Button
        {...props}
        variant="menu"
        selected={selected}
        data-po2-disabled={props.disabled ? "true" : "false"}
        data-po2-selected={selected ? "true" : "false"}
        className={`${styles.menuButton} ${stationary.button} ${className}`}
    >
        <span className={`${styles.label} ${labelClassName}`}>{children}</span>
    </Button>
);
