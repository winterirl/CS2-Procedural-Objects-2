import { bindScopedValue } from "@/bindings/scoped-value";
import { RuntimeText } from "@/l10n/localization";
import type { SaveTileData } from "@/utils/ui-payloads";
import { bindValue } from "cs2/api";
import { group } from "./commands";

export const recoveryCandidates$ = bindScopedValue<SaveTileData[]>(group, "recoveryCandidates", []);
export const persistenceBusy$ = bindValue<boolean>(group, "persistenceBusy", false);
export const recoveryStatus$ = bindScopedValue<RuntimeText>(group, "recoveryStatus", null);
export const recoveryResultPending$ = bindScopedValue<boolean>(group, "recoveryResultPending", false);
