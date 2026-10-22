import { NumericStepper } from "@/components/common/numeric-stepper";
import layout from "@/styles/layout.module.scss";

type MaterialScalarControlProps = {
    label: string;
    value: number;
    digits: number;
    resetKey: string;
    minimum?: number;
    maximum?: number;
    disabled?: boolean;
    onDecrease: () => void;
    onCommit: (value: number) => void;
    onIncrease: () => void;
};

export const MaterialScalarControl = ({
    label,
    value,
    digits,
    resetKey,
    minimum,
    maximum,
    disabled = false,
    onDecrease,
    onCommit,
    onIncrease,
}: MaterialScalarControlProps) => (
    <div className={layout.splitRow}>
        <span>{label}</span>
        <NumericStepper
            value={value}
            digits={digits}
            resetKey={resetKey}
            ariaLabel={label}
            disabled={disabled}
            minimum={minimum}
            maximum={maximum}
            onCommit={onCommit}
            onDecrease={onDecrease}
            onIncrease={onIncrease}
        />
    </div>
);
