import { bindValue, useValue } from "cs2/api";
import { useMemo } from "react";
import { englishNumberFormat, formatNumber, formatInteger } from "@/utils/number-format";
import type { NumberFormat } from "@/utils/number-format";
import { formatBytes, formatSaveBytes } from "@/utils/format";

const numberFormat$ = bindValue<NumberFormat>("po2", "numberFormat", englishNumberFormat);

export const usePO2NumberFormat = () => {
    const numberFormat = useValue(numberFormat$);
    return useMemo(() => ({
        numberFormat,
        formatNumber: (value: number, digits = 1) => formatNumber(value, digits, numberFormat),
        formatInteger: (value: number) => formatInteger(value, numberFormat),
        formatBytes: (value: number) => formatBytes(value, numberFormat),
        formatSaveBytes: (value: number) => formatSaveBytes(value, numberFormat),
    }), [numberFormat]);
};
