import styles from "./native-dropdown.module.scss";
import { getModule } from "cs2/modding";
import type { DropdownTheme } from "cs2/ui";

let cachedTheme: DropdownTheme | undefined;

export const getNativeDropdownTheme = (): DropdownTheme => {
    if (!cachedTheme) {
        // Use the registered dropdown themes, not other widgets with similar names.
        const defaultTheme = getModule(
            "game-ui/common/input/dropdown/themes/default.module.scss",
            "classes",
        ) as DropdownTheme;
        const gameTheme = getModule(
            "game-ui/game/themes/game-dropdown.module.scss",
            "classes",
        ) as Partial<DropdownTheme>;

        const nativeTheme = { ...defaultTheme, ...gameTheme };
        cachedTheme = {
            ...nativeTheme,
            dropdownToggle: `${nativeTheme.dropdownToggle} ${styles.toggle}`,
            dropdownMenu: `${nativeTheme.dropdownMenu} ${styles.menu}`,
            dropdownItem: `${nativeTheme.dropdownItem} ${styles.item}`,
            label: `${nativeTheme.label} ${styles.label}`,
        };
    }

    return cachedTheme;
};
