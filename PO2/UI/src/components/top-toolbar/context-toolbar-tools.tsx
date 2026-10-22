import { MeshEditToolbarControls } from "@/components/mesh-edit/mesh-edit-toolbar-controls";
import { TexturingToolbarControls } from "@/components/texturing/texturing-toolbar-controls";
import { TransformToolbarControls } from "@/components/transform/transform-toolbar-controls";
import type { CustomOrientation } from "@/utils/ui-payloads";
import toolbar from "@/shell/main-toolbar.module.scss";
import styles from "@/shell/context-toolbar.module.scss";

export type ContextToolbarMode =
    | "transform"
    | "mesh-edit"
    | "texturing";

type ContextToolbarToolsProps = {
    mode: ContextToolbarMode;
    selectedObjectId: number;
    canBeginAxisPicking: boolean;
    selectedObjectLocked: boolean;
    selectedPartIndex: number;
    selectedPartCount: number;
    selectedSurfaceAreaCount: number;
    transformTarget: number;
    transformOrientation: number;
    customOrientations: CustomOrientation[];
    activeCustomOrientation: CustomOrientation | null;
    snapMagnetEnabled: boolean;
    snapIncrementEnabled: boolean;
    snapPOVerticesEnabled: boolean;
    snapPOEdgesEnabled: boolean;
    snapGameSurfacesEnabled: boolean;
    moveSnapIncrement: number;
    precisionMoveSnapIncrement: number;
    rotationSnapIncrement: number;
    precisionRotationSnapIncrement: number;
    scaleSnapIncrement: number;
    precisionScaleSnapIncrement: number;
    vertexAlignmentAxis: number;
    vertexSlideActive: boolean;
    topologyInteractionActive: boolean;
    vertexSelectionMode: boolean;
    edgeSelectionMode: boolean;
    meshEditVertexScope: number;
    meshEditVisibilityMode: number;
    onAction: (action: string, ...args: Array<string | number>) => void;
};

export const ContextToolbarTools = ({
    mode,
    selectedObjectId,
    canBeginAxisPicking,
    selectedObjectLocked,
    selectedPartIndex,
    selectedPartCount,
    selectedSurfaceAreaCount,
    transformTarget,
    transformOrientation,
    customOrientations,
    activeCustomOrientation,
    snapMagnetEnabled,
    snapIncrementEnabled,
    snapPOVerticesEnabled,
    snapPOEdgesEnabled,
    snapGameSurfacesEnabled,
    moveSnapIncrement,
    precisionMoveSnapIncrement,
    rotationSnapIncrement,
    precisionRotationSnapIncrement,
    scaleSnapIncrement,
    precisionScaleSnapIncrement,
    vertexAlignmentAxis,
    vertexSlideActive,
    topologyInteractionActive,
    vertexSelectionMode,
    edgeSelectionMode,
    meshEditVertexScope,
    meshEditVisibilityMode,
    onAction,
}: ContextToolbarToolsProps) => {
    const transformControls = (showTargetControls: boolean) => (
        <TransformToolbarControls
            selectedObjectId={selectedObjectId}
            canBeginAxisPicking={canBeginAxisPicking}
            showTargetControls={showTargetControls}
            selectedObjectLocked={selectedObjectLocked}
            selectedPartIndex={selectedPartIndex}
            transformTarget={transformTarget}
            transformOrientation={transformOrientation}
            customOrientations={customOrientations}
            activeCustomOrientation={activeCustomOrientation}
            snapMagnetEnabled={snapMagnetEnabled}
            snapIncrementEnabled={snapIncrementEnabled}
            snapPOVerticesEnabled={snapPOVerticesEnabled}
            snapPOEdgesEnabled={snapPOEdgesEnabled}
            snapGameSurfacesEnabled={snapGameSurfacesEnabled}
            moveSnapIncrement={moveSnapIncrement}
            precisionMoveSnapIncrement={precisionMoveSnapIncrement}
            rotationSnapIncrement={rotationSnapIncrement}
            precisionRotationSnapIncrement={precisionRotationSnapIncrement}
            scaleSnapIncrement={scaleSnapIncrement}
            precisionScaleSnapIncrement={precisionScaleSnapIncrement}
            onAction={onAction}
        />
    );

    if (mode === "transform") {
        return (
            <div className={`${toolbar.control} ${styles.tools}`}>
                {transformControls(true)}
            </div>
        );
    }

    if (mode === "mesh-edit") {
        const interactionBlocked =
            vertexAlignmentAxis >= 0 ||
            vertexSlideActive ||
            topologyInteractionActive;

        return (
            <div className={`${toolbar.control} ${styles.tools}`}>
                <MeshEditToolbarControls
                    vertexSelectionMode={vertexSelectionMode}
                    edgeSelectionMode={edgeSelectionMode}
                    vertexScope={meshEditVertexScope}
                    visibilityMode={meshEditVisibilityMode}
                    interactionBlocked={interactionBlocked}
                    onAction={onAction}
                    transformControls={transformControls(false)}
                />
            </div>
        );
    }

    return (
        <div className={`${toolbar.control} ${styles.tools}`}>
            <TexturingToolbarControls
                selectedPartCount={selectedPartCount}
                selectedSurfaceAreaCount={selectedSurfaceAreaCount}
                onAction={onAction}
            />
        </div>
    );
};
