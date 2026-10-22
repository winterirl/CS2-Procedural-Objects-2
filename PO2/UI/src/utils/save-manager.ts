import type { SaveTileData } from "./ui-payloads";

export type SaveStatusFilter = "all" | "Matched" | "Orphaned" | "Unknown";
export const unknownLineage = "00000000-0000-0000-0000-000000000000";

export const filterManagedSaves = (saves: SaveTileData[], lineage: string, status: SaveStatusFilter, oldest: boolean) =>
    saves.filter(save => (!lineage || save.sessionGuid === lineage) &&
        (status === "all" || save.gameSaveStatus === status))
        .sort((a, b) => (oldest ? 1 : -1) * (Date.parse(a.savedAtUtc) - Date.parse(b.savedAtUtc)) ||
            a.fileKey.localeCompare(b.fileKey));

export const saveLineages = (saves: SaveTileData[]) => {
    const groups = new Map<string, { id: string; name: string | null; count: number }>();
    for (const save of saves) {
        const group = groups.get(save.sessionGuid);
        if (group) {
            group.count++;
            if (group.name === null && save.cityName.trim()) group.name = save.cityName.trim();
        }
        else groups.set(save.sessionGuid, { id: save.sessionGuid, name: save.cityName.trim() || null, count: 1 });
    }
    return Array.from(groups.values());
};

export const validCleanupDate = (value: string) => {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
    const [year, month, day] = value.split("-").map(Number);
    if (year < 1 || month < 1 || month > 12 || day < 1) return false;
    const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
    return day <= [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][month - 1];
};

export const formatCalendarDate = (year: number, month: number, day: number) =>
    `${String(year).padStart(4, "0")}-${String(month + 1).padStart(2, "0")}-${String(day).padStart(2, "0")}`;

export const shiftCalendarMonth = (year: number, month: number, offset: number) => {
    const index = Math.max(12, Math.min(9999 * 12 + 11, year * 12 + month + offset));
    return { year: Math.floor(index / 12), month: index % 12 };
};

export const calendarWeeks = (year: number, month: number): (number | null)[][] => {
    // Use calendar fields directly. No UTC conversion is needed for a chosen date.
    const first = new Date(0);
    first.setFullYear(year, month, 1);
    first.setHours(12, 0, 0, 0);
    const last = new Date(first);
    last.setMonth(month + 1, 0);
    const count = last.getDate();
    const offset = first.getDay();
    return Array.from({ length: Math.ceil((offset + count) / 7) }, (_, week) =>
        Array.from({ length: 7 }, (_, column) => {
            const day = week * 7 + column - offset + 1;
            return day >= 1 && day <= count ? day : null;
        }));
};
