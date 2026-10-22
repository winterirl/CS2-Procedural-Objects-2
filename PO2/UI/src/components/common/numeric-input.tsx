import { TextInput } from "@/components/common/text-input";
import { useEffect, useRef, useState } from "react";
import { usePO2NumberFormat } from "@/l10n/number-format";
import { parseNumber, type NumberFormat } from "@/utils/number-format";
import styles from "./input.module.scss";

type DraftContext = {
    text: string;
    numberFormat: NumberFormat;
    resetKey: string;
    digits: number;
    value: number;
};

const useNumericDraft = (value: number, digits: number, resetKey: string, disabled: boolean) => {
    const { numberFormat, formatNumber } = usePO2NumberFormat();
    const [draft, setDraft] = useState(() => formatNumber(value, digits));
    const edited = useRef<DraftContext | null>(null);
    const reset = () => {
        edited.current = null;
        setDraft(formatNumber(value, digits));
    };
    useEffect(reset, [digits, disabled, resetKey, value, numberFormat]);

    const change = (text: string) => {
        edited.current = { text, numberFormat, resetKey, digits, value };
        setDraft(text);
    };
    const read = () => {
        const pending = edited.current;
        edited.current = null;
        // A focus change must not round an untouched value or repeat a delta.
        // Never apply a draft to a different object, language or transform frame.
        if (disabled || !pending || pending.numberFormat !== numberFormat || pending.resetKey !== resetKey ||
            pending.digits !== digits || pending.value !== value) return null;
        return parseNumber(pending.text, pending.numberFormat);
    };
    return { draft, change, read, reset, show: (next: number) => setDraft(formatNumber(next, digits)) };
};

type NumericInputProps = {
    value: number;
    digits: number;
    resetKey: string;
    ariaLabel: string;
    className?: string;
    disabled?: boolean;
    minimum?: number;
    maximum?: number;
    normalizeValue?: (value: number) => number;
    onCommit: (value: number) => void;
    onValidityChange?: (valid: boolean) => void;
};

export const NumericInput = ({
    value,
    digits,
    resetKey,
    ariaLabel,
    className = "",
    disabled = false,
    minimum,
    maximum,
    normalizeValue,
    onCommit,
    onValidityChange,
}: NumericInputProps) => {
    const input = useNumericDraft(value, digits, resetKey, disabled);
    const { numberFormat } = usePO2NumberFormat();
    const validity = useRef(onValidityChange);
    validity.current = onValidityChange;
    useEffect(() => {
        validity.current?.(true);
        return () => validity.current?.(true);
    }, [value, digits, resetKey, disabled, numberFormat]);
    const commit = () => {
        validity.current?.(true);
        const parsed = input.read();
        if (parsed === null) {
            input.reset();
            return;
        }

        const minimumClamped = minimum === undefined
            ? parsed
            : Math.max(minimum, parsed);
        const bounded = maximum === undefined
            ? minimumClamped
            : Math.min(maximum, minimumClamped);
        const next = normalizeValue ? normalizeValue(bounded) : bounded;
        if (!Number.isFinite(next)) {
            input.reset();
            return;
        }
        onCommit(next);
        input.show(next);
    };

    return (
        <TextInput
            className={`${styles.numeric} ${className}`}
            type="text"
            inputMode="decimal"
            value={input.draft}
            disabled={disabled}
            onChange={(event) => {
                const text = event.currentTarget.value;
                input.change(text);
                const parsed = parseNumber(text, numberFormat);
                validity.current?.(parsed !== null && Number.isFinite(parsed) &&
                    (minimum === undefined || parsed >= minimum) && (maximum === undefined || parsed <= maximum));
            }}
            onBlur={commit}
            onKeyDown={(event) => {
                if (event.key === "Enter") {
                    event.currentTarget.blur();
                }
                else if (event.key === "Escape") {
                    input.reset();
                    validity.current?.(true);
                    event.preventDefault();
                }
            }}
            aria-label={ariaLabel}
        />
    );
};

type NumericDeltaInputProps = {
    digits: number;
    resetKey: string;
    ariaLabel: string;
    className?: string;
    disabled?: boolean;
    onCommit: (value: number) => void;
};

export const NumericDeltaInput = ({
    digits,
    resetKey,
    ariaLabel,
    className = "",
    disabled = false,
    onCommit,
}: NumericDeltaInputProps) => {
    const input = useNumericDraft(0, digits, resetKey, disabled);
    const commit = () => {
        const parsed = input.read();
        input.reset();
        if (parsed !== null && parsed !== 0) onCommit(parsed);
    };

    return (
        <TextInput
            className={`${styles.numeric} ${className}`}
            type="text"
            inputMode="decimal"
            value={input.draft}
            disabled={disabled}
            onChange={(event) => input.change(event.currentTarget.value)}
            onBlur={commit}
            onKeyDown={(event) => {
                if (event.key === "Enter") {
                    event.currentTarget.blur();
                }
                else if (event.key === "Escape") {
                    input.reset();
                    event.preventDefault();
                }
            }}
            aria-label={ariaLabel}
        />
    );
};
