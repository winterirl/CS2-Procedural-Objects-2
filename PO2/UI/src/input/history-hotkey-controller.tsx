import { trigger } from "cs2/api";
import { useEffect } from "react";
import { group } from "@/bindings/commands";
import { historyHotkey, isHistoryTextTarget } from "@/components/history/history-hotkeys";

// Native actions dispatch history now. Keep browser undo out of the game UI.
export const HistoryHotkeyController = () => {
    useEffect(() => {
        let composing = false;
        let focused = false;
        const updateFocus = () => {
            const next = composing || isHistoryTextTarget(document.activeElement) ||
                (document.activeElement instanceof Element && document.activeElement.closest("[role='dialog']") !== null);
            if (next !== focused) {
                focused = next;
                trigger(group, "setHotkeyUiFocused", focused);
            }
        };
        const onCompositionStart = () => { composing = true; updateFocus(); };
        const onCompositionEnd = () => { composing = false; updateFocus(); };
        const onKeyDown = (event: KeyboardEvent) => {
            updateFocus();
            if (focused || event.defaultPrevented || isHistoryTextTarget(event.target)) return;
            if (historyHotkey(event.key, event.ctrlKey, event.shiftKey, event.altKey, event.isComposing || event.keyCode === 229)) {
                event.preventDefault();
                event.stopPropagation();
            }
        };
        updateFocus();
        document.addEventListener("focusin", updateFocus);
        document.addEventListener("focusout", updateFocus);
        document.addEventListener("compositionstart", onCompositionStart);
        document.addEventListener("compositionend", onCompositionEnd);
        document.addEventListener("keydown", onKeyDown, true);
        return () => {
            document.removeEventListener("focusin", updateFocus);
            document.removeEventListener("focusout", updateFocus);
            document.removeEventListener("compositionstart", onCompositionStart);
            document.removeEventListener("compositionend", onCompositionEnd);
            document.removeEventListener("keydown", onKeyDown, true);
            trigger(group, "setHotkeyUiFocused", false);
        };
    }, []);

    return null;
};
