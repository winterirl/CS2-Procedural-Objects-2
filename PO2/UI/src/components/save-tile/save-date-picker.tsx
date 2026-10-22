import { usePO2Localization } from "@/l10n/localization";
import { Dropdown, DropdownItem, DropdownToggle } from "cs2/ui";
import { useState } from "react";
import { NavigationButton } from "@/components/common/navigation-button";
import { getNativeDropdownTheme } from "@/components/common/native-dropdown-theme";
import { calendarWeeks, formatCalendarDate, shiftCalendarMonth, validCleanupDate } from "@/utils/save-manager";
import materialStyles from "@/styles/materials.module.scss";
import styles from "./save-date-picker.module.scss";

const months = ["PO2.Calendar.January", "PO2.Calendar.February", "PO2.Calendar.March", "PO2.Calendar.April", "PO2.Calendar.May", "PO2.Calendar.June", "PO2.Calendar.July", "PO2.Calendar.August", "PO2.Calendar.September", "PO2.Calendar.October", "PO2.Calendar.November", "PO2.Calendar.December"] as const;
const weekdays = ["PO2.Calendar.Sun", "PO2.Calendar.Mon", "PO2.Calendar.Tue", "PO2.Calendar.Wed", "PO2.Calendar.Thu", "PO2.Calendar.Fri", "PO2.Calendar.Sat"] as const;

type SaveDatePickerProps = {
    value: string;
    disabled: boolean;
    onChange: (value: string) => void;
};

const displayedMonth = (value: string) => {
    if (validCleanupDate(value)) {
        const [year, month] = value.split("-").map(Number);
        return { year, month: month - 1 };
    }
    const today = new Date();
    return { year: today.getFullYear(), month: today.getMonth() };
};

// Cohtml does not provide the browser's date input popup, so use a native dropdown.
export const SaveDatePicker = ({ value, disabled, onChange }: SaveDatePickerProps) => {
    const { t } = usePO2Localization();
    const [view, setView] = useState(() => displayedMonth(value));
    const theme = getNativeDropdownTheme();
    const weeks = calendarWeeks(view.year, view.month);
    const moveMonth = (offset: number) => setView(current => shiftCalendarMonth(current.year, current.month, offset));

    return <div className={`${materialStyles.categoryDropdown} ${styles.picker}`}>
        <Dropdown alignment="left" onToggle={open => { if (open) setView(displayedMonth(value)); }}
            theme={{ ...theme, dropdownPopup: `${theme.dropdownPopup || ""} po2-ui ${materialStyles.categoryDropdownPopup}` }}
            content={<div className={styles.calendar} aria-label={t("PO2.SaveManager.DatePicker.AccessibleLabel")}>
                <div className={styles.heading}>
                    <NavigationButton direction="previous" label={t("PO2.SaveManager.DatePicker.PreviousMonth.Action")} disabled={disabled || (view.year === 1 && view.month === 0)} onSelect={() => moveMonth(-1)} />
                    <span aria-live="polite">{`${t(months[view.month])} ${view.year}`}</span>
                    <NavigationButton direction="next" label={t("PO2.SaveManager.DatePicker.NextMonth.Action")} disabled={disabled || (view.year === 9999 && view.month === 11)} onSelect={() => moveMonth(1)} />
                </div>
                <div className={styles.week} aria-hidden="true">
                    {weekdays.map(day => <span key={day} className={styles.weekday}>{t(day)}</span>)}
                </div>
                {weeks.map((week, index) => <div className={styles.week} key={index}>
                    {week.map((day, column) => {
                        if (day === null) return <span key={column} className={styles.empty} />;
                        const date = formatCalendarDate(view.year, view.month, day);
                        return <DropdownItem key={column} value={date} selected={value === date}
                            disabled={disabled} className={styles.day} closeOnSelect
                            onChange={onChange} onToggleSelected={onChange}>
                            {day}
                        </DropdownItem>;
                    })}
                </div>)}
            </div>}>
            <DropdownToggle disabled={disabled} aria-label={t("PO2.SaveManager.DatePicker.Toggle.AccessibleLabel")}>
                {value || t("PO2.SaveManager.DatePicker.Placeholder")}
            </DropdownToggle>
        </Dropdown>
    </div>;
};
