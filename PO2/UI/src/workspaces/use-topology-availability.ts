import {
    canDeleteVertices$,
    canDissolveVertices$,
    deleteVerticesUnavailableReason$,
    dissolveVerticesUnavailableReason$,} from "@/bindings/mesh-edit";
import { useScopedValue } from "@/bindings/scoped-value";
import type { usePO2Localization } from "@/l10n/localization";

export const useTopologyAvailability = (
    meshControlsVisible: boolean,
    message: ReturnType<typeof usePO2Localization>["message"],
) => {
    const canDissolveVertices = useScopedValue(canDissolveVertices$, meshControlsVisible);
    const dissolveVerticesUnavailableReason = message(useScopedValue(dissolveVerticesUnavailableReason$, meshControlsVisible));
    const canDeleteVertices = useScopedValue(canDeleteVertices$, meshControlsVisible);
    const deleteVerticesUnavailableReason = message(useScopedValue(deleteVerticesUnavailableReason$, meshControlsVisible));

    return {
        canDissolveVertices,
        dissolveVerticesUnavailableReason,
        canDeleteVertices,
        deleteVerticesUnavailableReason,
    };
};
