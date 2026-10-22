export const historyHotkey = (key: string, ctrl: boolean, shift: boolean, alt: boolean, composing: boolean): "undo" | "redo" | null => {
    if (!ctrl || alt || composing) return null;
    const normalized = key.toLowerCase();
    if (normalized === "z") return shift ? "redo" : "undo";
    if (normalized === "y" && !shift) return "redo";
    return null;
};

export const isHistoryTextTarget = (target: EventTarget | null): boolean =>
    target instanceof Element && target.closest("input, textarea, select, [contenteditable]:not([contenteditable='false']), [role='textbox']") !== null;
