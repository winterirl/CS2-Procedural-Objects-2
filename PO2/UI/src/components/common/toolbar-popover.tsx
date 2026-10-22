import { createContext, useContext, useEffect, useRef } from "react";
import type { ReactNode } from "react";
import { createPortal } from "react-dom";
import { ToolbarButton, Panel } from "./toolbar-controls";
import toolbar from "@/shell/main-toolbar.module.scss";
import styles from "@/shell/toolbar-popover.module.scss";

// Render expanded settings in the stack slot, outside the toolbar panel content.
export const ToolbarPopoverHost = createContext<HTMLDivElement | null>(null);

type ToolbarPopoverProps = {
    label: string;
    selected: boolean;
    open: boolean;
    onToggle: (open: boolean) => void;
    className: string;
    children: ReactNode;
};

// These hold editable settings, so selecting a setting leaves the panel open.
export const ToolbarPopover = ({ label, selected, open, onToggle, className, children }: ToolbarPopoverProps) => {
    const root = useRef<HTMLDivElement>(null);
    const popup = useRef<HTMLDivElement>(null);
    const host = useContext(ToolbarPopoverHost);

    useEffect(() => {
        if (!open) return;
        const closeOutside = (event: MouseEvent) => {
            if (event.target instanceof Node &&
                !root.current?.contains(event.target) &&
                !popup.current?.contains(event.target)) onToggle(false);
        };
        const closeOnEscape = (event: KeyboardEvent) => {
            if (event.key !== "Escape") return;
            event.preventDefault();
            event.stopPropagation();
            onToggle(false);
            root.current?.querySelector<HTMLButtonElement>("button")?.focus();
        };
        document.addEventListener("mousedown", closeOutside, true);
        document.addEventListener("keydown", closeOnEscape, true);
        return () => {
            document.removeEventListener("mousedown", closeOutside, true);
            document.removeEventListener("keydown", closeOnEscape, true);
        };
    }, [open, onToggle]);

    return (
        <div ref={root} className={styles.anchor}>
            <div className={toolbar.control}>
                <ToolbarButton
                    variant="menu"
                    className={toolbar.textButton}
                    selected={selected || open}
                    aria-expanded={open}
                    onSelect={() => onToggle(!open)}
                >
                    {label}
                </ToolbarButton>
            </div>
            {open && host && createPortal(
                <div ref={popup} className={`${styles.position} ${className}`}>
                    <Panel className={styles.panel} contentClassName={styles.content}>
                        <div role="group" aria-label={label}>{children}</div>
                    </Panel>
                </div>,
                host,
            )}
        </div>
    );
};
