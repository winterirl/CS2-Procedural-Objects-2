import { useScopedValue } from "@/bindings/scoped-value";
import { scaleX$, scaleY$, scaleZ$, transformRequiresBake$ } from "@/bindings/transforms";

export const useObjectScale = (transformControlsVisible: boolean) => {
    const scaleX = useScopedValue(scaleX$, transformControlsVisible);
    const scaleY = useScopedValue(scaleY$, transformControlsVisible);
    const scaleZ = useScopedValue(scaleZ$, transformControlsVisible);
    const transformRequiresBake = useScopedValue(transformRequiresBake$, transformControlsVisible);

    return {
        scaleX,
        scaleY,
        scaleZ,
        transformRequiresBake,
    };
};
