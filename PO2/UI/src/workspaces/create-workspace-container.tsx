import type { ComponentProps } from "react";
import { group } from "@/bindings/commands";
import {
    archCreationCurveSegments$,
    archCreationDepth$,
    archCreationHeight$,
    archCreationOpeningHeight$,
    archCreationOpeningWidth$,
    archCreationWidth$,
    boxCreationDepth$,
    boxCreationHeight$,
    boxCreationWidth$,
    coneCreationBottomDiameter$,
    coneCreationHeight$,
    coneCreationSegments$,
    coneCreationTopDiameter$,
    cylinderCreationHeight$,
    cylinderCreationRadius$,
    cylinderCreationSegments$,
    domeCreationHeight$,
    domeCreationRadius$,
    domeCreationSegments$,
    domeCreationVerticalSegments$,
    planeCreationDepth$,
    planeCreationWidth$,
    selectedCreationPrimitiveType$,
    sphereCreationRadius$,
    sphereCreationSegments$,
    sphereCreationVerticalSegments$,
    stairsCreationSteps$,
    stairsCreationTreadDepth$,
    stairsCreationWidth$,
    wedgeCreationDepth$,
    wedgeCreationHeight$,
    wedgeCreationWidth$,} from "@/bindings/creation";
import { useScopedValue } from "@/bindings/scoped-value";
import { CreateWorkspace, type PrimitiveCreationParameters } from "@/workspaces/create-workspace";
import { trigger } from "cs2/api";

export const useCreationWorkspace = (creationVisible: boolean) => {
    const selectedCreationPrimitiveType = useScopedValue(selectedCreationPrimitiveType$, creationVisible);
    const sphereCreationRadius = useScopedValue(sphereCreationRadius$, creationVisible);
    const sphereCreationSegments = useScopedValue(sphereCreationSegments$, creationVisible);
    const sphereCreationVerticalSegments = useScopedValue(sphereCreationVerticalSegments$, creationVisible);
    const domeCreationRadius = useScopedValue(domeCreationRadius$, creationVisible);
    const domeCreationHeight = useScopedValue(domeCreationHeight$, creationVisible);
    const domeCreationSegments = useScopedValue(domeCreationSegments$, creationVisible);
    const domeCreationVerticalSegments = useScopedValue(domeCreationVerticalSegments$, creationVisible);
    const boxCreationWidth = useScopedValue(boxCreationWidth$, creationVisible);
    const boxCreationHeight = useScopedValue(boxCreationHeight$, creationVisible);
    const boxCreationDepth = useScopedValue(boxCreationDepth$, creationVisible);
    const planeCreationWidth = useScopedValue(planeCreationWidth$, creationVisible);
    const planeCreationDepth = useScopedValue(planeCreationDepth$, creationVisible);
    const wedgeCreationWidth = useScopedValue(wedgeCreationWidth$, creationVisible);
    const wedgeCreationHeight = useScopedValue(wedgeCreationHeight$, creationVisible);
    const wedgeCreationDepth = useScopedValue(wedgeCreationDepth$, creationVisible);
    const cylinderCreationRadius = useScopedValue(cylinderCreationRadius$, creationVisible);
    const cylinderCreationHeight = useScopedValue(cylinderCreationHeight$, creationVisible);
    const cylinderCreationSegments = useScopedValue(cylinderCreationSegments$, creationVisible);
    const coneCreationBottomDiameter = useScopedValue(coneCreationBottomDiameter$, creationVisible);
    const coneCreationTopDiameter = useScopedValue(coneCreationTopDiameter$, creationVisible);
    const coneCreationHeight = useScopedValue(coneCreationHeight$, creationVisible);
    const coneCreationSegments = useScopedValue(coneCreationSegments$, creationVisible);
    const archCreationWidth = useScopedValue(archCreationWidth$, creationVisible);
    const archCreationHeight = useScopedValue(archCreationHeight$, creationVisible);
    const archCreationDepth = useScopedValue(archCreationDepth$, creationVisible);
    const archCreationOpeningWidth = useScopedValue(archCreationOpeningWidth$, creationVisible);
    const archCreationOpeningHeight = useScopedValue(archCreationOpeningHeight$, creationVisible);
    const archCreationCurveSegments = useScopedValue(archCreationCurveSegments$, creationVisible);
    const stairsCreationWidth = useScopedValue(stairsCreationWidth$, creationVisible);
    const stairsCreationTreadDepth = useScopedValue(stairsCreationTreadDepth$, creationVisible);
    const stairsCreationSteps = useScopedValue(stairsCreationSteps$, creationVisible);
    const primitiveCreationParameters: PrimitiveCreationParameters = {
        sphereRadius: sphereCreationRadius,
        sphereSegments: sphereCreationSegments,
        sphereVerticalSegments: sphereCreationVerticalSegments,
        domeRadius: domeCreationRadius,
        domeHeight: domeCreationHeight,
        domeSegments: domeCreationSegments,
        domeVerticalSegments: domeCreationVerticalSegments,
        boxWidth: boxCreationWidth,
        boxHeight: boxCreationHeight,
        boxDepth: boxCreationDepth,
        planeWidth: planeCreationWidth,
        planeDepth: planeCreationDepth,
        wedgeWidth: wedgeCreationWidth,
        wedgeHeight: wedgeCreationHeight,
        wedgeDepth: wedgeCreationDepth,
        cylinderRadius: cylinderCreationRadius,
        cylinderHeight: cylinderCreationHeight,
        cylinderSegments: cylinderCreationSegments,
        coneBottomDiameter: coneCreationBottomDiameter,
        coneTopDiameter: coneCreationTopDiameter,
        coneHeight: coneCreationHeight,
        coneSegments: coneCreationSegments,
        archWidth: archCreationWidth,
        archHeight: archCreationHeight,
        archDepth: archCreationDepth,
        archOpeningWidth: archCreationOpeningWidth,
        archOpeningHeight: archCreationOpeningHeight,
        archCurveSegments: archCreationCurveSegments,
        stairsWidth: stairsCreationWidth,
        stairsTreadDepth: stairsCreationTreadDepth,
        stairsSteps: stairsCreationSteps,
    };

    return {
        selectedCreationPrimitiveType,
        primitiveCreationParameters,
    };
};

type CreateWorkspaceContainerProps = {
    state: ReturnType<typeof useCreationWorkspace>;
    placingPrimitiveType: ComponentProps<typeof CreateWorkspace>["placingPrimitiveType"];
};

export const CreateWorkspaceContainer = ({
    state,
    placingPrimitiveType,
}: CreateWorkspaceContainerProps) => {
    const {
        selectedCreationPrimitiveType,
        primitiveCreationParameters,
    } = state;

    return (
        <CreateWorkspace
            placingPrimitiveType={placingPrimitiveType}
            selectedPrimitiveType={selectedCreationPrimitiveType}
            parameters={primitiveCreationParameters}
            onAction={(action, ...args) => trigger(
                group,
                action,
                ...args,
            )}
        />
    );
};
