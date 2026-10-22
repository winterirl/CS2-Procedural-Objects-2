import { usePO2Localization } from "@/l10n/localization";
import { Button } from "cs2/ui";
import { getToolButtonTheme } from "./native-tool-button-theme";
import { useEffect, useRef, type ComponentProps } from "react";
import { NumericInput } from "./numeric-input";
import styles from "./numeric-stepper.module.scss";

type NumericStepperProps = ComponentProps<typeof NumericInput> & {
    onIncrease: () => void;
    onDecrease: () => void;
};

export const NumericStepper = ({
    onIncrease,
    onDecrease,
    className = "",
    ...input
}: NumericStepperProps) => {
    const { t } = usePO2Localization();
    const stepperRef = useRef<HTMLDivElement>(null);
    const decreaseDisabled = input.disabled || (input.minimum !== undefined && input.value <= input.minimum);
    const increaseDisabled = input.disabled || (input.maximum !== undefined && input.value >= input.maximum);

    useEffect(() => {
        const element = stepperRef.current;
        if (!element) {
            return;
        }

        const onWheel = (event: WheelEvent) => {
            if (input.disabled || event.deltaY === 0) {
                return;
            }

            // Adjust the hovered control without also scrolling its panel.
            event.preventDefault();
            event.stopPropagation();
            if (event.deltaY < 0 && !increaseDisabled) {
                onIncrease();
            }
            else if (event.deltaY > 0 && !decreaseDisabled) {
                onDecrease();
            }
        };

        // React's delegated wheel listener can be passive, which prevents canceling scroll.
        element.addEventListener("wheel", onWheel, { passive: false });
        return () => element.removeEventListener("wheel", onWheel);
    }, [input.disabled, decreaseDisabled, increaseDisabled, onDecrease, onIncrease]);

    return (
        <div ref={stepperRef} className={styles.stepper}>
            <Button
                variant="icon"
                theme={getToolButtonTheme()}
                src="Media/Glyphs/ThickStrokeArrowDown.svg"
                className={`${styles.caret} ${styles.start}`}
                aria-label={t("PO2.NumericStepper.Decrease.AccessibleLabel", { label: input.ariaLabel })}
                disabled={decreaseDisabled}
                onSelect={onDecrease}
            />
            <div className={styles.numberField}>
                <NumericInput {...input} className={`${styles.value} ${className}`} />
            </div>
            <Button
                variant="icon"
                theme={getToolButtonTheme()}
                src="Media/Glyphs/ThickStrokeArrowUp.svg"
                className={`${styles.caret} ${styles.end}`}
                aria-label={t("PO2.NumericStepper.Increase.AccessibleLabel", { label: input.ariaLabel })}
                disabled={increaseDisabled}
                onSelect={onIncrease}
            />
        </div>
    );
};
