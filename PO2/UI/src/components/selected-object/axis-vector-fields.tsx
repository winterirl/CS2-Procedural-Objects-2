import type { ReactNode } from "react";
import { InputLabel } from "@/components/common/input-label";
import styles from "./selected-object.module.scss";

export type VectorAxis = "X" | "Y" | "Z";
export const AxisVectorFields = ({ children }: { children: (axis: VectorAxis) => ReactNode }) => (
    <div className={styles.vector}>
        {(["X", "Y", "Z"] as VectorAxis[]).map((axis) => (
            <InputLabel key={axis}>
                <span className={styles[`axis${axis}`]}>{axis}</span>
                {children(axis)}
            </InputLabel>
        ))}
    </div>
);
