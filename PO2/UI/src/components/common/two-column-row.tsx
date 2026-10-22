import { Children, type ReactElement } from "react";
import styles from "./two-column-row.module.scss";

// Keep both slots even when a row has only one action, as in Create's last row.
export const TwoColumnRow = ({ children }: { children: ReactElement | [ReactElement, ReactElement] }) => {
    const items = Children.toArray(children);

    return (
        <div className={styles.row}>
            {[0, 1].map((index) => (
                <div key={index} className={styles.cell} aria-hidden={items[index] == null ? true : undefined}>
                    {items[index]}
                </div>
            ))}
        </div>
    );
};
