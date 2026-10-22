import { findModule, getModule } from "cs2/modding";

type ToolButtonStyles = {
    button: string;
    iconButton?: string;
    "icon-button"?: string;
};

let nativeTheme: { button: string } | undefined;
let themeChecked = false;

export const getToolButtonTheme = () => {
    if (themeChecked) {
        return nativeTheme;
    }

    themeChecked = true;
    try {
        // Resolve the game's stylesheet once, without keeping its generated class names.
        const modules = findModule(/\/tool-button\.module\.scss$/);
        if (modules.length === 1) {
            const [path, ...exports] = modules[0];
            for (const name of exports) {
                const nativeStyles = getModule(path, name) as ToolButtonStyles | undefined;
                if (nativeStyles && typeof nativeStyles.button === "string") {
                    const icon = nativeStyles.iconButton || nativeStyles["icon-button"];
                    nativeTheme = { button: `${nativeStyles.button}${icon ? ` ${icon}` : ""}` };
                    return nativeTheme;
                }
            }
        }
        console.warn("[PO2] Native tool button theme was not found.");
    } catch (error) {
        console.warn("[PO2] Could not load the native tool button theme:", error);
    }
    return undefined;
};
