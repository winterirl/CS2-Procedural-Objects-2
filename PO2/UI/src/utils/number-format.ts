export type NumberFormat = Readonly<{
    locale: string;
    decimalSeparator: string;
    groupSeparator: string;
    negativeSign: string;
    positiveSign: string;
    groupSizes: readonly number[];
}>;

export const englishNumberFormat: NumberFormat = {
    locale: "en-US", decimalSeparator: ".", groupSeparator: ",",
    negativeSign: "-", positiveSign: "+", groupSizes: [3],
};

const localizeDecimal = (text: string, format: NumberFormat) => text
    .replace(".", format.decimalSeparator)
    .replace(/-/g, () => format.negativeSign)
    .replace(/\+/g, () => format.positiveSign);

// Keep editable values ungrouped so a decimal can never be read as thousands.
export const formatNumber = (value: number, digits = 1, format = englishNumberFormat) =>
    localizeDecimal((Number.isFinite(value) ? value : 0).toFixed(digits), format);

export const formatNumericValue = (value: number, format = englishNumberFormat) =>
    localizeDecimal(String(Number.isFinite(value) ? value : 0), format);

export const formatInteger = (value: number, format = englishNumberFormat) => {
    let text = Number.isFinite(value) ? String(Math.max(0, Math.round(value))) : "0";
    // JavaScript switches to scientific notation for very large numbers.
    if (text.includes("e")) return localizeDecimal(text, format);
    const groups: string[] = [];
    let index = 0, size = format.groupSizes[index] ?? 0;
    while (size > 0 && text.length > size) {
        groups.unshift(text.slice(-size));
        text = text.slice(0, -size);
        if (index < format.groupSizes.length - 1) size = format.groupSizes[++index];
    }
    groups.unshift(text);
    return groups.join(format.groupSeparator);
};

export const parseNumber = (value: string, format = englishNumberFormat): number | null => {
    let text = value.trim();
    if (format.decimalSeparator !== ".") {
        // Dot decimals remain useful when pasting values from tools and examples.
        // Mixed separators and grouped input are deliberately not guessed.
        if (text.includes(format.decimalSeparator) && text.includes(".")) return null;
        text = text.split(format.decimalSeparator).join(".");
    }
    if (format.negativeSign !== "-") text = text.split(format.negativeSign).join("-");
    if (format.positiveSign !== "+") text = text.split(format.positiveSign).join("+");
    if (!/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/.test(text)) return null;
    const result = Number(text);
    return Number.isFinite(result) ? result : null;
};
