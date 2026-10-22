import { Scrollable } from "cs2/ui";
import type { ComponentProps } from "react";
import styles from "./scroll-area.module.scss";

type ScrollAreaProps = Pick<ComponentProps<typeof Scrollable>, "children" | "className" | "trackVisibility">;

// Lists share one native viewport. Keep the inner content at its natural height.
export const ScrollArea = ({
    children,
    className = "",
    trackVisibility = "always",
}: ScrollAreaProps) => (
    <Scrollable
        className={`${styles.viewport} ${className}`}
        vertical
        horizontal={false}
        trackVisibility={trackVisibility}
    >
        <div className={styles.content}>{children}</div>
    </Scrollable>
);
