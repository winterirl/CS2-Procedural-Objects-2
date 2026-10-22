import type { RuntimeText } from "@/l10n/localization";
import { bindValue, trigger } from "cs2/api";

export const canUndo$ = bindValue<boolean>("po2", "canUndo", false);
export const canRedo$ = bindValue<boolean>("po2", "canRedo", false);
export const busy$ = bindValue<boolean>("po2", "historyBusy", false);
export const undoDescription$ = bindValue<RuntimeText>("po2", "undoDescription", null);
export const redoDescription$ = bindValue<RuntimeText>("po2", "redoDescription", null);

export const runHistoryCommand = (command: "undo" | "redo") => trigger("po2", command);

export const historyFailure$ = bindValue<boolean>("po2", "historyFailure", false);
