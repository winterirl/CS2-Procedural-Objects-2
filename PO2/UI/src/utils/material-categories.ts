import type { MaterialLibraryEntry } from "./ui-payloads";

export const materialCategories = (entries: MaterialLibraryEntry[]) =>
    Array.from(new Set(entries.map(entry => entry.category)))
        .sort((left, right) => left.localeCompare(right));

// null is the browser's all-categories choice. Every string is a real category.
export const filterMaterials = (entries: MaterialLibraryEntry[], category: string | null, search: string) => {
    const normalizedSearch = search.trim().toLocaleLowerCase();
    return entries.filter(entry =>
        (category === null || entry.category === category) &&
        (!normalizedSearch || entry.displayName.toLocaleLowerCase().includes(normalizedSearch) ||
            entry.category.toLocaleLowerCase().includes(normalizedSearch)));
};
