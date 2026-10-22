import { englishNumberFormat, formatNumber } from "./number-format";
export { formatNumber, formatInteger } from "./number-format";

export const formatSaveBytes = (bytes: number, format = englishNumberFormat) =>
    `${formatNumber((Number.isFinite(bytes) && bytes >= 0 ? bytes : 0) / (1024 * 1024), 2, format)} MiB`;

export const formatBytes = (bytes: number, format = englishNumberFormat) => {
    if (!Number.isFinite(bytes) || bytes < 0) {
        return "0 B";
    }
    if (bytes >= 1024 * 1024) {
        return `${formatNumber(bytes / (1024 * 1024), 2, format)} MiB`;
    }
    if (bytes >= 1024) {
        return `${formatNumber(bytes / 1024, 1, format)} KiB`;
    }
    return `${Math.round(bytes)} B`;
};

// Cohtml can fall back to Date.toString for toLocaleString, including the timezone.
// Build the save label from local fields so the game always gets a compact value.
export const formatSaveTimestamp = (value: string, unknown = "Unknown") => {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return unknown;
    const pad = (part: number) => part.toString().padStart(2, "0");
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ` +
        `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
};

export const formatTimestamp = (value: string) => {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
};

export const formatTime = (value: string) => {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
        return value;
    }

    const hours = date.getHours().toString().padStart(2, "0");
    const minutes = date.getMinutes().toString().padStart(2, "0");
    return `${hours}:${minutes}`;
};

export const normalizeHexColor = (value: string) => {
    const trimmed = value.trim().replace(/^#/, "");
    return /^[0-9a-fA-F]{6}$/.test(trimmed)
        ? `#${trimmed.toUpperCase()}`
        : null;
};
