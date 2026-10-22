import type { RuntimeText } from "@/l10n/localization";
import { bindValue } from "cs2/api";
import { group } from "./commands";

export const hasClipboard$ = bindValue<boolean>(group, "hasClipboard", false);
export const clipboardBusy$ = bindValue<boolean>(group, "clipboardBusy", false);
export const clipboardStatus$ = bindValue<RuntimeText>(group, "clipboardStatus", null);
