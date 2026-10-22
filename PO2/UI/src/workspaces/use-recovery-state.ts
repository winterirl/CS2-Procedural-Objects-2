import {
    persistenceBusy$,
    recoveryCandidates$,
    recoveryResultPending$,
    recoveryStatus$,} from "@/bindings/recovery";
import { useScopedValue } from "@/bindings/scoped-value";
import type { usePO2Localization } from "@/l10n/localization";
import { useValue } from "cs2/api";

export const useRecoveryState = (
    recoveryVisible: boolean,
    message: ReturnType<typeof usePO2Localization>["message"],
) => {
    const persistenceBusy = useValue(persistenceBusy$);
    const recoveryCandidates = useScopedValue(recoveryCandidates$, recoveryVisible);
    const recoveryStatus = message(useScopedValue(recoveryStatus$, recoveryVisible));
    const recoveryResultPending = useScopedValue(recoveryResultPending$, recoveryVisible);

    return {
        persistenceBusy,
        recoveryCandidates,
        recoveryStatus,
        recoveryResultPending,
    };
};
