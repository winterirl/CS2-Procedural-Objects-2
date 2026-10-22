import { AssetConversionPhase } from "@/bindings/operation-state";
import { RuntimeText } from "@/l10n/localization";
import { bindValue } from "cs2/api";
import { group } from "./commands";

export const assetConversionBusy$ = bindValue<boolean>(group, "assetConversionBusy", false);
export const assetConversionPhase$ = bindValue<AssetConversionPhase>(group, "assetConversionPhase", AssetConversionPhase.Inactive);
export const assetConversionStatus$ = bindValue<RuntimeText>(group, "assetConversionStatus", null);
