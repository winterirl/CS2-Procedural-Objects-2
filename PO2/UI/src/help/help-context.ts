import type { HelpTopic } from "./help-content";

export type HelpContext = {
    workspace: number;
    interaction: number;
    materialView: number;
    materialCandidateCount: number;
    primitiveType: number;
    gizmoMode: number;
    transformTarget: number;
    deformMode: boolean;
    mirrorMode: boolean;
    deformationPivot: number;
    selectedHasDeformation: boolean;
    meshEditRequiresBake: boolean;
    meshTransformMode: number;
    meshEditSelectionMode: number;
    meshEditVertexScope: number;
    canConfirmAxisPicking: boolean;
    faceExtrudeSelecting: boolean;
    faceExtrudePositioning: boolean;
    faceInsetSelecting: boolean;
    faceInsetPositioning: boolean;
    selectedSurfaceAreaId: number;
    selectedObjectLocked: boolean;
    recoveryVisible: boolean;
    materialBusy: boolean;
    historyBusy: boolean;
    deformationBusy: boolean;
    topologyBusy: boolean;
};

const workspaces: HelpTopic[] = ["select", "create", "materials", "object", "edit", "texturing", "select", "convert", "saveManager", "delete", "copy", "paste", "lod"];
const primitives: HelpTopic[] = ["primitiveBox", "primitivePlane", "primitiveWedge", "primitiveCylinder", "primitiveCone", "primitiveArch", "primitiveStairs", "primitiveSphere", "primitiveDome"];
const interactions: Partial<Record<number, HelpTopic>> = {
    1: "gizmoDrag", 2: "boxSelection", 3: "primitivePlacement",
    4: "primitivePartPlacement", 5: "convertTarget", 6: "combineSource",
    7: "alignTarget", 8: "slideTarget", 9: "slideDrag",
    10: "loopCutTarget", 11: "loopCutPosition",
    12: "surfaceCutTarget", 13: "surfaceCutPosition",
    16: "partArray",
};

// Numeric IDs follow the existing C# enums, not the visible workspace labels.
export const resolveHelpTopics = (c: HelpContext): HelpTopic[] => {
    const topics: HelpTopic[] = [];
    if (c.materialView !== 0) {
        topics.push(c.materialView === 3 ? "materialEdit" : c.materialView === 2
            ? (c.materialCandidateCount > 0 ? "materialImportForm" : "materialImport")
            : "materials");
    } else if (c.interaction === 14) {
        topics.push("axisPick");
        if (c.canConfirmAxisPicking) topics.push("axisPickReady");
    } else if (c.interaction === 16) {
        topics.push("partArray");
    } else {
        const workspace = workspaces[c.workspace] ?? "select";
        topics.push(workspace);
        if (workspace === "object") {
            topics.push(c.transformTarget === 0 ? "objectScope" : "partScope");
            if (c.mirrorMode) {
                topics.push("objectMirror");
            } else if (c.deformMode) {
                topics.push(c.gizmoMode === 2 ? "deformScale" : "deformMove");
                if (c.gizmoMode === 2) topics.push(c.deformationPivot === 0 ? "deformPivotSelection" : "deformPivotSaved");
            } else {
                topics.push((["objectMove", "objectRotate", "objectScale"] as HelpTopic[])[c.gizmoMode] ?? "objectMove");
            }
            if (c.selectedHasDeformation) topics.push("unappliedDeformation");
        }
        if (workspace === "edit") {
            topics.push(c.meshEditVertexScope === 0 ? "partScope" : "objectScope");
            if (c.meshEditRequiresBake) topics.push("editRequiresDeform");
            else {
                topics.push(c.meshEditSelectionMode === 0 ? "editVertices" : "editEdges");
                topics.push((["editMove", "editRotate", "editScale"] as HelpTopic[])[c.meshTransformMode] ?? "editMove", "meshPivot");
            }
        }
        if (workspace === "texturing") {
            topics.push(c.selectedSurfaceAreaId === 0 ? "surfaceBase" : "surfaceCustom", "projections", "colorMasks");
        }
        if (workspace === "select" && c.recoveryVisible) topics.push("recovery");
        if (!(workspace === "edit" && c.meshEditRequiresBake)) {
            const interaction = interactions[c.interaction];
            if (interaction) topics.push(interaction);
            if (c.faceExtrudeSelecting) topics.push("extrudeSelect");
            else if (c.faceExtrudePositioning) topics.push("extrudePosition");
            else if (c.faceInsetSelecting) topics.push("insetSelect");
            else if (c.faceInsetPositioning) topics.push("insetPosition");
        }
        if ((c.interaction === 3 || c.interaction === 4) && primitives[c.primitiveType]) {
            topics.push(primitives[c.primitiveType]);
        }
    }
    if (c.selectedObjectLocked) topics.push("lockedObject");
    if (c.materialBusy) topics.push("materialBusy");
    if (c.historyBusy) topics.push("historyBusy");
    if (c.deformationBusy) topics.push("deformationBusy");
    if (c.topologyBusy) topics.push("topologyBusy");
    return topics;
};
