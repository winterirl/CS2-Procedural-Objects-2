import type { ComponentProps } from "react";
import typography from "@/styles/typography.module.scss";

export const InputLabel = ({ className = "", ...props }: ComponentProps<"label">) => (
    <label {...props} className={`${typography.label} ${className}`} />
);
