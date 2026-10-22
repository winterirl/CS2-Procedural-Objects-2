import stationary from "./stationary-menu.module.scss";
import { Button } from "cs2/ui";
import type { ComponentProps } from "react";

export { Panel } from "cs2/ui";

// Round buttons use the hover fill for selection.
export const ToolbarButton = (props: ComponentProps<typeof Button>) => (
    <Button
        {...props}
        selected={props.variant === "round" ? false : props.selected}
        aria-pressed={props.variant === "round" ? props.selected ?? props["aria-pressed"] : props["aria-pressed"]}
        className={`${props.className || ""} ${props.variant === "menu" ? stationary.button : ""}`}
        data-po2-disabled={props.disabled ? "true" : "false"}
        data-po2-selected={props.selected ? "true" : "false"}
    />
);
