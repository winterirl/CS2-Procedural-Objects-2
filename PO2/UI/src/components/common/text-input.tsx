import type { ComponentProps } from "react";
import styles from "./input.module.scss";

type TextInputProps = ComponentProps<"input">;

export const TextInput = ({ className = "", ...props }: TextInputProps) => (
    <input {...props} className={`${styles.field} ${className}`} />
);
