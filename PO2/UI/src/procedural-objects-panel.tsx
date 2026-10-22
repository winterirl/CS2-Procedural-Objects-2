import { LodWorkspace } from "@/workspaces/lod-workspace";
import { hasClipboard$, clipboardBusy$, clipboardStatus$ } from "@/bindings/clipboard";
import { ClipboardWorkspace } from "@/workspaces/clipboard-workspace";
import { group, run } from "@/bindings/commands";
import { assetConversionBusy$, assetConversionPhase$, assetConversionStatus$ } from "@/bindings/conversion";
import { placingPrimitiveType$ } from "@/bindings/creation";
import { busy$ as historyBusy$, historyFailure$ } from "@/bindings/history";
import { materialManagementView$ } from "@/bindings/material-authoring";
import {
    meshTranslationPreparationStatus$,
    topologyEditBusy$,
    topologyEditStatus$,} from "@/bindings/mesh-edit";
import {
    activeInteraction$,
    activeToolMode$,
    activeWorkspace$,
    helpOpen$,
    helpView,
    helpView$,
    performanceDiagnostics$,
    toolActive$,
    toolMode,
    workspace,} from "@/bindings/navigation";
import { useScopedValue } from "@/bindings/scoped-value";
import { deformationCommitBusy$, deformationCommitStatus$ } from "@/bindings/transforms";
import { RecoveryPanel } from "@/components/save-tile/recovery-panel";
import { ArrayPartControls } from "@/components/selected-object/array-part-controls";
import { AxisPickingPanel } from "@/components/selected-object/axis-picking-panel";
import { ObjectNameRow } from "@/components/selected-object/object-name-row";
import { PartManagementControls } from "@/components/selected-object/part-management-controls";
import { PartNavigation } from "@/components/selected-object/part-navigation";
import { ContextToolbarTools, type ContextToolbarMode } from "@/components/top-toolbar/context-toolbar-tools";
import type { ToolbarWorkspaceItem } from "@/components/top-toolbar/toolbar-types";
import { WorkspacePanel } from "@/components/workspace-panel/workspace-panel";
import { resolveHelpTopics } from "@/help/help-context";
import { HelpContextProvider } from "@/help/help-local-context";
import { HelpPanel } from "@/help/help-panel";
import proceduralObjectsIcon from "@/images/procedural-objects.svg";
import { HistoryHotkeyController } from "@/input/history-hotkey-controller";
import { usePO2Localization } from "@/l10n/localization";
import { DeleteCursor } from "@/components/common/delete-cursor";
import { Toolbars } from "@/shell/toolbars";
import toolbarShell from "@/shell/toolbars.module.scss";
import styles from "@/styles/shell.module.scss";
import { ConvertWorkspace } from "@/workspaces/convert-workspace";
import { CreateWorkspaceContainer, useCreationWorkspace } from "@/workspaces/create-workspace-container";
import { EditWorkspaceContainer, useMeshEditWorkspace } from "@/workspaces/edit-workspace-container";
import {
    MaterialLibraryWorkspaceContainer,
    useMaterialLibraryWorkspace,} from "@/workspaces/material-library-workspace-container";
import { ObjectListWorkspace } from "@/workspaces/object-list-workspace";
import { ObjectWorkspace } from "@/workspaces/object-workspace";
import { PerformanceWorkspace } from "@/workspaces/performance-workspace";
import { SaveManagerWorkspace } from "@/workspaces/save-manager-workspace";
import { SelectWorkspace } from "@/workspaces/select-workspace";
import {
    TexturingWorkspaceContainer,
    useTexturingWorkspace,} from "@/workspaces/texturing-workspace-container";
import { useObjectScale } from "@/workspaces/use-object-scale";
import { useObjectState } from "@/workspaces/use-object-state";
import { useRecoveryState } from "@/workspaces/use-recovery-state";
import { useTopologyAvailability } from "@/workspaces/use-topology-availability";
import { useTransformState } from "@/workspaces/use-transform-state";
import { trigger, useValue } from "cs2/api";
import { FloatingButton, Portal } from "cs2/ui";

export const ProceduralObjectsButton = () => {
    const { t } = usePO2Localization();
    const toolActive = useValue(toolActive$);

    return (
        <div className="po2-ui">
            <div className={styles.buttonRoot}>
                <FloatingButton
                    src={proceduralObjectsIcon}
                    tooltipLabel={t("PO2.Navigation.OpenPanel.Tooltip")}
                    selected={toolActive}
                    className={styles.launcherButton}
                    onSelect={() => trigger(group, "toggleTool")}
                />
            </div>
        </div>
    );
};

export const ProceduralObjectsPanel = () => {
    const toolActive = useValue(toolActive$);
    return toolActive ? <OpenProceduralObjectsPanel /> : null;
};

const OpenProceduralObjectsPanel = () => {
    const { t, message } = usePO2Localization();
    // Keep these hooks unconditional. Visibility only controls their subscriptions.
    const activeWorkspace = useValue(activeWorkspace$);
    const materialManagementView = useValue(materialManagementView$);
    const materialImportViewActive = materialManagementView === 2;
    const materialEditViewActive = materialManagementView === 3;
    const materialLibraryViewActive = materialManagementView === 1;
    const normalWorkspace = materialManagementView === 0;
    const creationVisible = normalWorkspace && activeWorkspace === workspace.create;
    const objectListVisible = normalWorkspace && (activeWorkspace === workspace.select || activeWorkspace === workspace.objectList);
    const recoveryVisible = normalWorkspace && activeWorkspace === workspace.select;
    const materialManagementVisible = materialManagementView !== 0;
    const materialAuthoringVisible = materialImportViewActive || materialEditViewActive;
    const meshEditActive = activeWorkspace === workspace.meshEdit;
    const lodActive = activeWorkspace === workspace.lod;
    const texturingActive = activeWorkspace === workspace.texturing;
    const editorActive = meshEditActive || texturingActive;
    const meshControlsVisible = normalWorkspace && meshEditActive;
    const texturingVisible = normalWorkspace && texturingActive;
    const transformControlsVisible = normalWorkspace && (activeWorkspace === workspace.transform || meshEditActive);
    const partControlsVisible = normalWorkspace && (activeWorkspace === workspace.transform || editorActive);
    const activeToolMode = useValue(activeToolMode$);
    const activeInteraction = useValue(activeInteraction$);
    const assetConversionBusy = useValue(assetConversionBusy$);
    const assetConversionPhase = useValue(assetConversionPhase$);
    const assetConversionStatus = message(useValue(assetConversionStatus$));
    const historyBusy = useValue(historyBusy$);
    const hasClipboard = useValue(hasClipboard$);
    const clipboardBusy = useValue(clipboardBusy$);
    const clipboardStatus = message(useValue(clipboardStatus$));
    const topologyEditBusy = useValue(topologyEditBusy$);
    const topologyEditStatus = message(useValue(topologyEditStatus$));
    const meshTranslationPreparationStatus = message(useValue(meshTranslationPreparationStatus$));
    const {
        canDissolveVertices,
        dissolveVerticesUnavailableReason,
        canDeleteVertices,
        deleteVerticesUnavailableReason,
    } = useTopologyAvailability(meshControlsVisible, message);
    const deformationCommitBusy = useValue(deformationCommitBusy$);
    const deformationCommitStatus = message(useValue(deformationCommitStatus$));
    const historyFailure = useValue(historyFailure$);
    const helpOpen = useValue(helpOpen$);
    const activeHelpView = useValue(helpView$);
    const performanceDiagnostics = useScopedValue(performanceDiagnostics$, helpOpen && activeHelpView === helpView.performance);
    const placingPrimitiveType = useValue(placingPrimitiveType$);
    const placingPrimitiveAsPart = activeToolMode === toolMode.placePrimitivePart;
    const combineSourceActive = activeToolMode === toolMode.combineSource;
    const createWorkspaceState = useCreationWorkspace(creationVisible);

    const {
        persistenceBusy,
        recoveryCandidates,
        recoveryStatus,
        recoveryResultPending,
    } = useRecoveryState(recoveryVisible, message);
    const {
        showObjectMarkers,
        objectListEntries,
        selectedObjectId,
        selectedObjectName,
        selectedObjectLocked,
        selectedObjectReceivesDecals,
        canDuplicateSelected,
        canRenameSelectedPart,
        canDuplicateSelectedPart,
        canAddPrimitivePart,
        canCombineSelected,
        canDeleteSelectedPart,
        canExtractSelectedPart,
        selectedObjectHasOriginalAppearance,
        selectedPartIndex,
        selectedPartCount,
        selectedPartName,
        selectedParts,
    } = useObjectState(objectListVisible, partControlsVisible, materialManagementVisible);
    const {
        mirrorMode,
        canMirrorSelected,
        mirrorRequiresBake,
        mirrorStatus,
        arrayActive,
        canArraySelectedPart,
        arrayStatus,
        deformMode,
        deformationPivot,
        deformationSelectionCount,
        deformationStatus,
        meshEditRequiresBake,
        canBakeMeshDeformation,
        canBakeDeformation,
        selectedHasDeformation,
        gizmoMode,
        transformTarget,
        transformOrientation,
        snapMagnetEnabled,
        snapIncrementEnabled,
        canSnapToTerrain,
        snapPOVerticesEnabled,
        snapPOEdgesEnabled,
        snapGameSurfacesEnabled,
        moveSnapIncrement,
        precisionMoveSnapIncrement,
        rotationSnapIncrement,
        precisionRotationSnapIncrement,
        scaleSnapIncrement,
        precisionScaleSnapIncrement,
        customOrientations,
        activeCustomOrientationIndex,
        canBeginAxisPicking,
        canConfirmAxisPicking,
    } = useTransformState(transformControlsVisible, message);
    const axisPicking = activeInteraction === 14;
    const editWorkspaceState = useMeshEditWorkspace(meshControlsVisible, normalWorkspace, editorActive, message);
    const {
        meshEditSelectionVersion,
        meshTransformMode,
        meshTransformPivot,
        selectedSurfaceCount,
        meshEditSelectionMode,
        vertexSelectionMode,
        edgeSelectionMode,
        boxSelectionVisible,
        boxSelectionLeft,
        boxSelectionTop,
        boxSelectionWidth,
        boxSelectionHeight,
        meshEditVisibilityMode,
        meshEditVertexScope,
        vertexAlignmentAxis,
        topologyInteractionActive,
        vertexSlideActive,        faceExtrudeSelecting,
        faceExtrudePositioning,
        faceInsetSelecting,
        faceInsetPositioning,
    } = editWorkspaceState;
    const {
        scaleX,
        scaleY,
        scaleZ,
        transformRequiresBake,
    } = useObjectScale(transformControlsVisible);
    const texturingWorkspaceState = useTexturingWorkspace(texturingVisible, materialManagementVisible);
    const {
        selectedSurfaceAreaId,
        selectedSurfaceAreaCount,    } = texturingWorkspaceState;
    const materialLibraryWorkspaceState = useMaterialLibraryWorkspace(materialImportViewActive, materialAuthoringVisible, materialManagementVisible, message, t);
    const {
        materialImportCandidateCount,
        materialImportBusy,    } = materialLibraryWorkspaceState;
    const activeCustomOrientation = transformOrientation === 2
        ? customOrientations.find(
            (orientation) => orientation.index === activeCustomOrientationIndex,
        ) ?? null
        : null;

    const hasSelection = selectedObjectId >= 0;
    const selectedObjectDraftKey = `${selectedObjectId}`;
    const numericDeltaDraftKey =
        `${selectedObjectId}:${transformTarget}:${selectedPartIndex}:` +
        `${transformOrientation}:${activeCustomOrientationIndex}:` +
        (meshEditActive
            ? `mesh:${meshEditSelectionVersion}:${meshEditSelectionMode}:${meshEditVertexScope}:${meshTransformMode}:${meshTransformPivot}`
            : "object");
    const selectedAreaDraftKey =
        `${selectedObjectId}:${selectedPartIndex}:${selectedSurfaceAreaId}`;

    const navigateWorkspace = (triggerName: string) => {
        trigger(group, triggerName);
    };

    const workspaceTitle = lodActive ? t("PO2.Lod.Title") : activeWorkspace === workspace.copy ? t("PO2.Clipboard.Copy.Mode.Label")
        : activeWorkspace === workspace.paste ? t("PO2.Clipboard.Paste.Mode.Label")
        : activeWorkspace === workspace.delete
            ? t("PO2.Delete.Mode.Label")
            : materialEditViewActive
            ? t("PO2.MaterialEditor.Edit.Title")
            : materialImportViewActive
                ? t("PO2.MaterialEditor.Import.Title")
                : activeWorkspace === workspace.create
                    ? t("PO2.Common.Create")
                    : activeWorkspace === workspace.convert
                        ? t("PO2.Common.Convert")
                        : activeWorkspace === workspace.materialLibrary
                            ? t("PO2.Navigation.Materials.Title")
                            : activeWorkspace === workspace.objectList
                                ? t("PO2.Navigation.ObjectList.Title")
                                : activeWorkspace === workspace.transform
                                    ? t("PO2.Common.Object")
                                    : activeWorkspace === workspace.meshEdit
                                        ? t("PO2.Common.Edit")
                                        : activeWorkspace === workspace.texturing
                                            ? t("PO2.Common.Texturing")
                                            : t("PO2.Common.Select");

    const toolbarWorkspaceItems: ToolbarWorkspaceItem[] = [
        {
            id: "select",
            label: t("PO2.Common.Select"),
            tooltip: t("PO2.Navigation.Select.Tooltip"),
            active: activeWorkspace === workspace.select,
            disabled: materialImportBusy,
            disabledReason: t("PO2.Navigation.Disabled.MaterialOperationPending"),
            onSelect: () => navigateWorkspace("navigateSelectWorkspace"),
        },
        {
            id: "create",
            label: t("PO2.Common.Create"),
            tooltip: t("PO2.Navigation.Create.Tooltip"),
            active: activeWorkspace === workspace.create,
            disabled: materialImportBusy,
            disabledReason: t("PO2.Navigation.Disabled.MaterialOperationPending"),
            onSelect: () => navigateWorkspace("navigateCreateWorkspace"),
        },
        {
            id: "convert",
            label: t("PO2.Common.Convert"),
            tooltip: t("PO2.Navigation.Convert.Tooltip"),
            active: activeWorkspace === workspace.convert,
            disabled: materialImportBusy || assetConversionBusy,
            disabledReason: materialImportBusy
                ? t("PO2.Navigation.Disabled.MaterialOperationPending")
                : t("PO2.Navigation.Disabled.ConversionPending"),
            onSelect: () => navigateWorkspace("navigateConvertWorkspace"),
        },
        ...(hasSelection ? [
            {
                id: "transform",
                label: t("PO2.Common.Object"),
                active: activeWorkspace === workspace.transform,
                disabled: materialImportBusy,
                disabledReason: t("PO2.Navigation.Disabled.MaterialOperationPending"),
                onSelect: () => navigateWorkspace("navigateTransformWorkspace"),
            },
            {
                id: "mesh-edit",
                label: t("PO2.Common.Edit"),
                active: activeWorkspace === workspace.meshEdit,
                disabled: selectedObjectLocked || materialImportBusy,
                disabledReason: materialImportBusy
                    ? t("PO2.Navigation.Disabled.MaterialOperationPending")
                    : t("PO2.Navigation.Disabled.ObjectLocked"),
                onSelect: () => navigateWorkspace("navigateMeshEditWorkspace"),
            },
            {
                id: "texturing",
                label: t("PO2.Common.Texturing"),
                active: activeWorkspace === workspace.texturing,
                disabled: materialImportBusy,
                disabledReason: t("PO2.Navigation.Disabled.MaterialOperationPending"),
                onSelect: () => navigateWorkspace("navigateTexturingWorkspace"),
            },
            {
                id: "lod",
                label: t("PO2.Lod.Title"),
                active: lodActive,
                disabled: materialImportBusy,
                disabledReason: t("PO2.Navigation.Disabled.MaterialOperationPending"),
                onSelect: () => navigateWorkspace("navigateLodWorkspace"),
            },
        ] : []),
        {
            id: "materials",
            label: t("PO2.Common.Materials"),
            tooltip: t("PO2.Navigation.Materials.Tooltip"),
            active: activeWorkspace === workspace.materialLibrary,
            disabled: materialImportBusy,
            disabledReason: t("PO2.Navigation.Disabled.MaterialOperationPending"),
            onSelect: run("openMaterialLibraryView"),
        },
    ];

    const toolbarContextMode: ContextToolbarMode | null = activeWorkspace === workspace.transform
            ? "transform"
            : activeWorkspace === workspace.meshEdit
                ? "mesh-edit"
                : activeWorkspace === workspace.texturing
                    ? "texturing"
                    : null;

    const toolbarContextTools = toolbarContextMode === null || axisPicking || clipboardBusy
        ? null
        : (
            <ContextToolbarTools
                selectedObjectId={selectedObjectId}
                canBeginAxisPicking={canBeginAxisPicking}
                mode={toolbarContextMode}
                selectedObjectLocked={selectedObjectLocked}
                selectedPartIndex={selectedPartIndex}
                selectedPartCount={selectedPartCount}
                selectedSurfaceAreaCount={selectedSurfaceAreaCount}
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
                vertexAlignmentAxis={vertexAlignmentAxis}
                vertexSlideActive={vertexSlideActive}
                topologyInteractionActive={topologyInteractionActive}
                vertexSelectionMode={vertexSelectionMode}
                edgeSelectionMode={edgeSelectionMode}
                meshEditVertexScope={meshEditVertexScope}
                meshEditVisibilityMode={meshEditVisibilityMode}
                onAction={(action, ...args) => trigger(group, action, ...args)}
            />
        );

    let workspaceContext = null;
    if (hasSelection && (
        activeWorkspace === workspace.transform ||
        activeWorkspace === workspace.meshEdit ||
        activeWorkspace === workspace.texturing || lodActive
    )) {
        workspaceContext = (
            <ObjectNameRow
                selectedObjectId={selectedObjectId}
                selectedObjectName={selectedObjectName}
                selectedObjectLocked={selectedObjectLocked}
                combineSourceActive={combineSourceActive || axisPicking || lodActive}
                onRename={(name) => trigger(group, "renameSelected", name)}
            />
        );
    }
    else if (activeWorkspace === workspace.materialLibrary && hasSelection) {
        workspaceContext = (
            <div
                className={
                    `${styles.contextSummary} ${styles.contextSummaryInline}`
                }
            >
                <div className={styles.contextPrimary}>{t("PO2.Materials.ApplyTarget.Label")}</div>
                <div className={styles.contextMeta}>
                    {t("PO2.Materials.ApplyTarget.Summary", { objectName: selectedObjectName, partName: selectedPartName, areaId: selectedSurfaceAreaId })}
                </div>
            </div>
        );
    }

    const historyStatus = (historyBusy || historyFailure) && (
        <div role="status" aria-live="polite" className={styles.historyStatus}>
            {historyBusy
                ? t("PO2.History.Status.Loading")
                : t("PO2.History.Status.OperationFailed")}
        </div>
    );

    const helpTopicKey = resolveHelpTopics({
        workspace: activeWorkspace,
        interaction: activeInteraction,
        materialView: materialManagementView,
        materialCandidateCount: materialImportCandidateCount,
        primitiveType: placingPrimitiveType,
        gizmoMode, transformTarget, deformMode, mirrorMode, deformationPivot,
        selectedHasDeformation, meshEditRequiresBake, meshTransformMode,
        meshEditSelectionMode, meshEditVertexScope, canConfirmAxisPicking,
        faceExtrudeSelecting, faceExtrudePositioning, faceInsetSelecting, faceInsetPositioning,
        selectedSurfaceAreaId, selectedObjectLocked,
        recoveryVisible: recoveryStatus.length > 0,
        materialBusy: materialImportBusy,
        historyBusy,
        deformationBusy: deformationCommitBusy,
        topologyBusy: topologyEditBusy,
    }).join("|");

    // The box-selection overlay stays outside the panel shell.
    return (
        <Portal>
            <HelpContextProvider>
            <div className="po2-ui">
                <HistoryHotkeyController />
                {activeWorkspace === workspace.delete && <DeleteCursor />}
                {boxSelectionVisible && (
                    <div
                        className={styles.boxSelectionRect}
                        style={{
                            left: `${boxSelectionLeft * 100}%`,
                            top: `${boxSelectionTop * 100}%`,
                            width: `${boxSelectionWidth * 100}%`,
                            height: `${boxSelectionHeight * 100}%`,
                        }}
                    />
                )}

                <div
                    className={`po2-ui ${toolbarShell.root}`}
                    aria-busy={historyBusy || deformationCommitBusy || topologyEditBusy || clipboardBusy}
                >
                    <Toolbars
                        workspaceItems={toolbarWorkspaceItems.map(item => ({ ...item, disabled: item.disabled || clipboardBusy }))}
                        deleteActive={activeWorkspace === workspace.delete}
                        copyActive={activeWorkspace === workspace.copy}
                        pasteActive={activeWorkspace === workspace.paste}
                        copyDisabled={materialImportBusy || historyBusy || topologyEditBusy || deformationCommitBusy || clipboardBusy}
                        pasteDisabled={!hasClipboard || materialImportBusy || historyBusy || topologyEditBusy || deformationCommitBusy || clipboardBusy}
                        onCopy={run("toggleCopyMode")}
                        onPaste={run("togglePasteMode")}
                        deleteDisabled={materialImportBusy || historyBusy || topologyEditBusy || deformationCommitBusy || clipboardBusy}
                        onDelete={run("toggleDeleteMode")}
                        contextTools={toolbarContextTools}
                        saveManagerActive={activeWorkspace === workspace.saveManager}
                        saveManagerDisabled={materialImportBusy || historyBusy || topologyEditBusy || deformationCommitBusy || persistenceBusy || clipboardBusy}
                        onSaveManager={run("openSaveManager")}
                        helpActive={helpOpen}
                        helpDisabled={false}
                        backDisabled={materialImportBusy}
                        closeDisabled={materialImportBusy}
                        onBack={run("navigateBack")}
                        onHelp={run("toggleHelpView")}
                        onClose={() => trigger(group, "closeTool")}
                    />
                </div>

                <div
                    className={`po2-ui ${styles.root}`}
                    aria-busy={historyBusy || deformationCommitBusy || topologyEditBusy || clipboardBusy}
                    style={historyBusy || deformationCommitBusy || topologyEditBusy || (clipboardBusy && activeWorkspace !== workspace.copy)
                        ? { pointerEvents: "none" }
                        : undefined}
                >
                    <div className={styles["panel-slot"]}>
                        <MaterialLibraryWorkspaceContainer
                            state={materialLibraryWorkspaceState}
                            materialManagementView={materialManagementView}
                            workspaceContext={workspaceContext}
                            historyStatus={historyStatus}
                            hasSelection={hasSelection}
                            selectedObjectLocked={selectedObjectLocked}
                            selectedSurfaceAreaId={selectedSurfaceAreaId}
                        />

                        {!materialImportViewActive && !materialEditViewActive && !materialLibraryViewActive && (
                            activeWorkspace === workspace.saveManager ? <SaveManagerWorkspace /> :
                            activeWorkspace === workspace.select && recoveryStatus.length > 0 ? (
                                <RecoveryPanel
                                    status={recoveryStatus}
                                    resultPending={recoveryResultPending}
                                    onDismiss={() => trigger(group, "dismissRecoveryResult")}
                                    candidates={recoveryCandidates}
                                    disabled={historyBusy || topologyEditBusy || deformationCommitBusy || persistenceBusy}
                                    onRecover={(fileKey) => trigger(group, "recoverSnapshotCandidate", fileKey)}
                                />
                            ) : <WorkspacePanel
                                id="PO2Panel"
                                scrollable={hasSelection && (activeWorkspace === workspace.transform || activeWorkspace === workspace.meshEdit || activeWorkspace === workspace.texturing || lodActive)}
                                title={workspaceTitle}
                                context={workspaceContext}
                                status={<>{historyStatus}{clipboardStatus && <div role="status" aria-live="polite">{clipboardStatus}</div>}{arrayStatus && <div role="status" aria-live="polite">{arrayStatus}</div>}{meshTranslationPreparationStatus && (
                                    <div role="status" aria-live="polite">{meshTranslationPreparationStatus}</div>
                                )}{((topologyEditBusy && !mirrorMode) || deformationCommitBusy) && (
                                    <div role="status" aria-live="polite">
                                        {topologyEditBusy && !mirrorMode ? topologyEditStatus : deformationCommitStatus || t("PO2.Object.Deformation.Status.Finishing")}
                                    </div>
                                )}</>}
                            >
                                <div className={styles.content}>
                                    {(activeWorkspace === workspace.copy || activeWorkspace === workspace.paste) && (
                                        <ClipboardWorkspace copying={activeWorkspace === workspace.copy}
                                            canClear={hasClipboard || clipboardBusy} onClear={run("clearClipboard")} />
                                    )}
                                    {activeWorkspace === workspace.delete && (
                                        <div>{t("PO2.Delete.Target.Instruction")}</div>
                                    )}
                                    {activeWorkspace === workspace.select && (
                                        <SelectWorkspace
                                            showObjectMarkers={showObjectMarkers}
                                            onToggleMarkers={() => trigger(
                                                group,
                                                "toggleObjectMarkers",
                                            )}
                                        />
                                    )}

                                    {activeWorkspace === workspace.create && (
                                        <CreateWorkspaceContainer
                                            state={createWorkspaceState}
                                            placingPrimitiveType={placingPrimitiveType}
                                        />
                                    )}

                                    {activeWorkspace === workspace.convert && (
                                        <ConvertWorkspace
                                            conversionPhase={assetConversionPhase}
                                            conversionStatus={assetConversionStatus}
                                        />
                                    )}

                                    {(activeWorkspace === workspace.select || activeWorkspace === workspace.objectList) && (
                                        <ObjectListWorkspace
                                            entries={objectListEntries}
                                            selectedObjectId={selectedObjectId}
                                            onSelect={(objectId) => trigger(
                                                group,
                                                "selectObjectFromList",
                                                objectId,
                                            )}
                                        />
                                    )}

                                    {hasSelection && (
                                        activeWorkspace === workspace.transform ||
                                        activeWorkspace === workspace.meshEdit ||
                                        activeWorkspace === workspace.texturing || lodActive
                                    ) ? (
                                        <>
                                            {!lodActive && !axisPicking && (!meshEditActive || meshEditVertexScope === 0) && <PartNavigation
                                                showDivider={meshEditActive}
                                                objectId={selectedObjectId} index={selectedPartIndex} name={selectedPartName}
                                                parts={selectedParts} canRename={canRenameSelectedPart}
                                                disabled={arrayActive || combineSourceActive || vertexAlignmentAxis >= 0 || vertexSlideActive || topologyInteractionActive}
                                                onAction={(action, ...args) => trigger(group, action, ...args)}
                                            />}
                                            {!lodActive && !arrayActive && !axisPicking && !editorActive && <PartManagementControls
                                                canAdd={canAddPrimitivePart} canCombine={canCombineSelected}
                                                canDuplicate={canDuplicateSelectedPart} canExtract={canExtractSelectedPart}
                                                canArray={canArraySelectedPart}
                                                canDelete={canDeleteSelectedPart} combining={combineSourceActive} placing={placingPrimitiveAsPart}
                                                onAction={(action) => trigger(group, action)}
                                            />}

                                            {lodActive ? (
                                                <LodWorkspace />
                                            ) : arrayActive ? (
                                                <ArrayPartControls objectId={selectedObjectId} />
                                            ) : axisPicking ? (
                                                <AxisPickingPanel canConfirm={canConfirmAxisPicking} onAction={(action) => trigger(group, action)} />
                                            ) : texturingActive ? (
                                                <TexturingWorkspaceContainer
                                                    state={texturingWorkspaceState}
                                                    selectedObjectLocked={selectedObjectLocked}
                                                    selectedObjectReceivesDecals={selectedObjectReceivesDecals}
                                                    selectedObjectHasOriginalAppearance={selectedObjectHasOriginalAppearance}
                                                    selectedSurfaceCount={selectedSurfaceCount}
                                                    selectedAreaDraftKey={selectedAreaDraftKey}
                                                />
                                            ) : meshEditActive ? (
                                                <EditWorkspaceContainer
                                                    state={editWorkspaceState}
                                                    canDissolveVertices={canDissolveVertices}
                                                    dissolveVerticesUnavailableReason={dissolveVerticesUnavailableReason}
                                                    canDeleteVertices={canDeleteVertices}
                                                    deleteVerticesUnavailableReason={deleteVerticesUnavailableReason}
                                                    topologyEditStatus={topologyEditStatus}
                                                    meshEditRequiresBake={meshEditRequiresBake}
                                                    canBakeMeshDeformation={canBakeMeshDeformation}
                                                    deformationStatus={deformationStatus}
                                                    numericDeltaDraftKey={numericDeltaDraftKey}
                                                />
                                            ) : (
                                                <ObjectWorkspace
                                                    canDuplicateSelected={canDuplicateSelected}
                                                    selectedObjectLocked={selectedObjectLocked}
                                                    combineSourceActive={combineSourceActive}
                                                    canSnapToTerrain={canSnapToTerrain}
                                                    mirrorMode={mirrorMode}
                                                    canMirrorSelected={canMirrorSelected}
                                                    mirrorRequiresBake={mirrorRequiresBake}
                                                    mirrorStatus={mirrorStatus}
                                                    deformMode={deformMode}
                                                    deformationPivot={deformationPivot}
                                                    deformationSelectionCount={deformationSelectionCount}
                                                    deformationStatus={deformationStatus}
                                                    canBakeDeformation={canBakeDeformation}
                                                    selectedHasDeformation={selectedHasDeformation}
                                                    transformRequiresBake={transformRequiresBake}
                                                    gizmoMode={gizmoMode}
                                                    transformTarget={transformTarget}
                                                    numericDeltaDraftKey={`${numericDeltaDraftKey}:${deformMode}:${deformationPivot}`}
                                                    selectedObjectDraftKey={selectedObjectDraftKey}
                                                    scaleX={scaleX}
                                                    scaleY={scaleY}
                                                    scaleZ={scaleZ}
                                                    onAction={(action, ...args) => trigger(
                                                        group,
                                                        action,
                                                        ...args,
                                                    )}
                                                />
                                            )}
                                        </>
                                    ) : activeWorkspace !== workspace.create &&
                                        activeWorkspace !== workspace.convert &&
                                        activeWorkspace !== workspace.select &&
                                        activeWorkspace !== workspace.objectList &&
                                        activeWorkspace !== workspace.delete &&
                                        activeWorkspace !== workspace.copy &&
                                        activeWorkspace !== workspace.paste ? (
                                        <div className={styles.emptyState}>
                                            {t("PO2.Navigation.EmptySelection.Instruction")}
                                        </div>
                                    ) : null}
                                </div>
                            </WorkspacePanel>
                        )}
                    </div>
                </div>
                {helpOpen && (
                    <div className={styles.helpSlot}>
                        <HelpPanel
                            topicKey={helpTopicKey}
                            performance={activeHelpView === helpView.performance}
                            captureDisabled={historyBusy || deformationCommitBusy || topologyEditBusy || materialImportBusy || axisPicking}
                            onClose={run("closeHelpView")}
                            onContext={run("showContextHelp")}
                            onPerformance={run("openPerformanceView")}
                            diagnostics={<PerformanceWorkspace diagnostics={performanceDiagnostics} refreshDisabled={historyBusy || deformationCommitBusy || topologyEditBusy || materialImportBusy || axisPicking} onRefresh={run("refreshPerformanceDiagnostics")} />}
                        />
                    </div>
                )}
            </div>
            </HelpContextProvider>
        </Portal>
    );
};
