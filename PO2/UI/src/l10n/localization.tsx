import { LocalizedString, useLocalization } from "cs2/l10n";
import { useCallback } from "react";
import type { LocReactNode } from "cs2/l10n";
import english from "../../../Localization/en-US.json";
import { usePO2NumberFormat } from "./number-format";
import { englishNumberFormat, formatNumber, formatNumericValue } from "@/utils/number-format";
import type { NumberFormat } from "@/utils/number-format";

export type LocaleKey = keyof typeof english;
export type TextArguments = Readonly<Record<string, string | number>>;
export type Translate = (key: LocaleKey, args?: TextArguments) => string;

export type RuntimeText = null | {
    key: LocaleKey;
    args?: Readonly<Record<string, string | number | { number: number; digits: number } | RuntimeText>>;
} | { literal: string } | { lines: readonly RuntimeText[] };

export const resolveRuntimeText = (t: Translate, message: RuntimeText, format = englishNumberFormat): string => {
    if (!message) return "";
    if ("literal" in message) return message.literal;
    if ("lines" in message) return message.lines.map(line => resolveRuntimeText(t, line, format)).join("\n");
    const args: Record<string, string | number> = {};
    for (const [name, value] of Object.entries(message.args ?? {})) {
        args[name] = value && typeof value === "object" && "number" in value
            ? formatNumber(value.number, value.digits, format)
            : typeof value === "object" ? resolveRuntimeText(t, value, format) : value;
    }
    return t(message.key, args);
};

const tokenPattern = /\{([A-Za-z][A-Za-z0-9_]*)\}/g;

export const formatText = (text: string, args?: TextArguments, format = englishNumberFormat): string =>
    args ? text.replace(tokenPattern, (token, name: string) =>
        Object.prototype.hasOwnProperty.call(args, name)
            ? typeof args[name] === "number" ? formatNumericValue(args[name] as number, format) : String(args[name])
            : token) : text;

export const resolveText = (translate: (key: string, fallback?: string | null) => string | null,
    key: LocaleKey, args?: TextArguments, format: NumberFormat = englishNumberFormat): string => {
    const fallback = english[key];
    const translated = translate(key, fallback);
    return formatText(translated && translated.trim() && translated !== key ? translated : fallback, args, format);
};

export const usePO2Localization = () => {
    const { translate } = useLocalization();
    const numbers = usePO2NumberFormat();
    const t: Translate = useCallback((key: LocaleKey, args?: TextArguments) =>
        resolveText(translate, key, args, numbers.numberFormat), [translate, numbers.numberFormat]);
    const message = useCallback((value: RuntimeText) => resolveRuntimeText(t, value, numbers.numberFormat), [t, numbers.numberFormat]);
    return { t, message, ...numbers };
};

export const LocalizedText = ({ id, args }: { id: LocaleKey; args?: Record<string, LocReactNode> }) =>
    <LocalizedString id={id} fallback={english[id]} args={args} />;
