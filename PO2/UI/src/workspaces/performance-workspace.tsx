import { usePO2Localization } from "@/l10n/localization";
import { PanelButton as Button } from "@/components/common/panel-button";
import type {
    PerformanceDiagnostics,
    PerformanceObjectSummary,
} from "@/utils/ui-payloads";
import { formatTime } from "@/utils/format";
import controls from "@/styles/controls.module.scss";
import styles from "@/styles/content.module.scss";

type PerformanceWorkspaceProps = {
    diagnostics: PerformanceDiagnostics;
    onRefresh: () => void;
    refreshDisabled?: boolean;
};

type MetricRowProps = {
    label: string;
    value: string;
};

const MetricRow = ({ label, value }: MetricRowProps) => (
    <div className={styles.valueRow}>
        <span>{label}</span>
        <span className={styles.value}>{value}</span>
    </div>
);

type ObjectLodRowProps = {
    label: string;
    triangles: number;
    targetTriangles: number;
    budgetMet: boolean;
    relativeError: number;
    absoluteError: number;
    method: string;
    prunedTriangles?: number;
};

const ObjectLodRow = ({
    label,
    triangles,
    targetTriangles,
    budgetMet,
    relativeError,
    absoluteError,
    method,
    prunedTriangles,
}: ObjectLodRowProps) => {
    const { t, formatNumber, formatInteger } = usePO2Localization();
    return (
        <div className={styles.performanceObjectLodRow}>
            <span className={styles.performanceObjectLodLabel}>{label}</span>
            <span>{t("PO2.Performance.LOD.TriangleBudget.Summary", { triangles: formatInteger(triangles), target: formatInteger(targetTriangles) })}</span>
            <span>|</span>
            <span>{t("PO2.Performance.LOD.Error.Summary", { relativeError: formatNumber(relativeError * 100, 3), absoluteError: formatNumber(
                    absoluteError,
                    3,
                ) })}</span>
            <span>|</span>
            <span>{method}</span>
            <span>|</span>
            <span>{budgetMet ? t("PO2.Performance.LOD.Budget.Met") : t("PO2.Performance.LOD.Budget.Missed")}</span>
            {prunedTriangles !== undefined && (
                <>
                    <span>|</span>
                    <span>{t("PO2.Performance.LOD.PrunedTriangles.Summary", { count: formatInteger(prunedTriangles) })}</span>
                </>
            )}
        </div>
    );
};

const ObjectLodDiagnostics = ({
    object,
}: {
    object: PerformanceObjectSummary;
}) => {
    const { t, formatNumber } = usePO2Localization();
    if (!object.hasLodResult) {
        return (
            <div className={styles.performanceObjectLodPending}>
                {t("PO2.Performance.LOD.Status.ResultUnavailable")}
            </div>
        );
    }

    return (
        <div className={styles.performanceObjectLodDiagnostics}>
            <ObjectLodRow
                label="LOD1"
                triangles={object.lod1TriangleCount}
                targetTriangles={object.lod1TargetTriangleCount}
                budgetMet={object.lod1BudgetMet}
                relativeError={object.lod1MaximumRelativeError}
                absoluteError={object.lod1MaximumAbsoluteError}
                method={object.lod1UsedPositionFocusedSimplification
                    ? t("PO2.Performance.LOD.Method.PositionUVEdge")
                    : t("PO2.Performance.LOD.PassThrough.Label")}
            />
            <ObjectLodRow
                label="LOD2"
                triangles={object.lod2TriangleCount}
                targetTriangles={object.lod2TargetTriangleCount}
                budgetMet={object.lod2BudgetMet}
                relativeError={object.lod2MaximumRelativeError}
                absoluteError={object.lod2MaximumAbsoluteError}
                method={object.lod2UsedAggressiveSimplification
                    ? t("PO2.Performance.LOD.Method.Aggressive")
                    : t("PO2.Performance.LOD.PassThrough.Label")}
                prunedTriangles={object.lod2PrunedTriangleCount}
            />
            <div className={styles.performanceObjectLodResult}>
                {object.lodWorthPublishing ? t("PO2.Performance.LOD.Result.Publishable") : t("PO2.Performance.LOD.PassThrough.Label")}
                <span>|</span>
                <span>{t("PO2.Performance.LOD.GenerationTime.Value", { milliseconds: formatNumber(object.lodGenerationMilliseconds, 2) })}</span>
            </div>
        </div>
    );
};

export const PerformanceWorkspace = ({
    diagnostics,
    onRefresh,
    refreshDisabled = false,
}: PerformanceWorkspaceProps) => {
    const { t, message, formatNumber, formatInteger, formatBytes } = usePO2Localization();
    if (diagnostics.state === "error") {
        return (
            <div className={styles.performanceWorkspace}>
                <div className={styles.performanceStatus} role="alert">
                    {message(diagnostics.error)}
                </div>
                <Button
                    onSelect={onRefresh}
                disabled={refreshDisabled}
                    className={controls.wideButton}
                >
                    {t("PO2.Common.Refresh")}
                </Button>
            </div>
        );
    }

    if (diagnostics.state !== "ready") {
        return (
            <div
                className={styles.performanceStatus}
                role="status"
                aria-live="polite"
            >
                {t("PO2.Performance.Snapshot.Status.Capturing")}
            </div>
        );
    }

    return (
        <div className={styles.performanceWorkspace}>
            <section className={styles.performanceSection}>
                <div className={styles.sectionHeader}>{t("PO2.Performance.Scene.Title")}</div>
                <div className={styles.card}>
                    <MetricRow
                        label={t("PO2.Performance.Scene.ProceduralObjects.Label")}
                        value={formatInteger(diagnostics.objectCount)}
                    />
                    <MetricRow
                        label={t("PO2.Performance.Scene.Parts.Label")}
                        value={formatInteger(diagnostics.partCount)}
                    />
                    <MetricRow
                        label={t("PO2.Performance.Scene.SurfaceAreas.Label")}
                        value={formatInteger(diagnostics.surfaceAreaCount)}
                    />
                    <MetricRow
                        label={t("PO2.Performance.Scene.LogicalVertices.Label")}
                        value={formatInteger(diagnostics.logicalVertexCount)}
                    />
                    <MetricRow
                        label={t("PO2.Performance.Scene.MeshVertices.Label")}
                        value={formatInteger(diagnostics.meshVertexCount)}
                    />
                    <MetricRow
                        label={t("PO2.Performance.Scene.Triangles.Label")}
                        value={formatInteger(diagnostics.triangleCount)}
                    />
                </div>
            </section>

            <section className={styles.performanceSection}>
                <div className={styles.sectionHeader}>{t("PO2.Performance.Rendering.Title")}</div>
                <div className={styles.card}>
                    <MetricRow
                        label={t("PO2.Performance.Rendering.ActiveMaterialBatches.Label")}
                        value={formatInteger(
                            diagnostics.activeMaterialBatchCount,
                        )}
                    />
                    <MetricRow
                        label={t("PO2.Performance.Rendering.ActiveMeshRenderers.Label")}
                        value={formatInteger(
                            diagnostics.activeRendererCount,
                        )}
                    />
                    <MetricRow
                        label={t("PO2.Performance.Rendering.CachedMaterialVariants.Label")}
                        value={formatInteger(
                            diagnostics.cachedMaterialVariantCount,
                        )}
                    />
                    <MetricRow
                        label={t("PO2.Performance.Rendering.LoadedLibraryTextures.Label")}
                        value={formatInteger(
                            diagnostics.loadedLibraryTextureCount,
                        )}
                    />
                </div>
            </section>

            <section className={styles.performanceSection}>
                <div className={styles.sectionHeader}>{t("PO2.Performance.LOD.Title")}</div>
                <div className={styles.card}>
                    <MetricRow
                        label={t("PO2.Performance.LOD.State.Label")}
                        value={diagnostics.lodGenerationEnabled
                            ? diagnostics.lodCaptureActive
                                ? t("PO2.Performance.LOD.State.Capturing")
                                : diagnostics.lodPendingPublicationObjectCount > 0
                                    ? t("PO2.Performance.LOD.State.Publishing")
                                : t("PO2.Performance.LOD.State.Ready")
                            : t("PO2.Performance.LOD.State.Disabled")}
                    />
                    <MetricRow
                        label={t("PO2.Performance.LOD.TrackedObjects.Label")}
                        value={formatInteger(
                            diagnostics.lodTrackedObjectCount,
                        )}
                    />
                    <MetricRow
                        label={t("PO2.Performance.LOD.OutstandingJobs.Label")}
                        value={formatInteger(
                            diagnostics.lodOutstandingJobCount,
                        )}
                    />
                    <MetricRow
                        label={t("PO2.Performance.LOD.AcceptedResults.Label")}
                        value={formatInteger(
                            diagnostics.lodAcceptedResultCount,
                        )}
                    />
                    <MetricRow
                        label={t("PO2.Performance.LOD.PublishedObjectsMeshes.Label")}
                        value={`${formatInteger(
                            diagnostics.lodPublishedObjectCount,
                        )} / ${formatInteger(
                            diagnostics.lodPublishedMeshCount,
                        )}`}
                    />
                    <MetricRow
                        label={t("PO2.Performance.LOD.PendingObjectsUploads.Label")}
                        value={`${formatInteger(
                            diagnostics.lodPendingPublicationObjectCount,
                        )} / ${formatInteger(
                            diagnostics.lodPendingMeshUploadCount,
                        )}`}
                    />
                    <MetricRow
                        label={t("PO2.Performance.LOD.PublishedDiscardedFailed.Label")}
                        value={`${formatInteger(
                            diagnostics.lodPublishedResultCount,
                        )} / ${formatInteger(
                            diagnostics.lodDiscardedPublicationResultCount,
                        )} / ${formatInteger(
                            diagnostics.lodFailedPublicationCount,
                        )}`}
                    />
                    <MetricRow
                        label={t("PO2.Performance.LOD.PublishablePassThrough.Label")}
                        value={`${formatInteger(
                            diagnostics.lodPublishableResultCount,
                        )} / ${formatInteger(
                            diagnostics.lodPassThroughResultCount,
                        )}`}
                    />
                    <MetricRow
                        label={t("PO2.Performance.LOD.ReducedAttemptedLevels.Label")}
                        value={`${formatInteger(
                            diagnostics.lodSimplifiedAreaLevelCount,
                        )} / ${formatInteger(
                            diagnostics.lodAttemptedAreaLevelCount,
                        )}`}
                    />
                    <MetricRow
                        label={t("PO2.Performance.LOD.LimitedLevels.Label")}
                        value={formatInteger(
                            diagnostics.lodLimitedAreaLevelCount,
                        )}
                    />
                    <MetricRow
                        label={t("PO2.Performance.LOD.BudgetMissedLevels.Label")}
                        value={formatInteger(
                            diagnostics.lodBudgetMissedAreaLevelCount,
                        )}
                    />
                    <MetricRow
                        label={t("PO2.Performance.LOD.AggressiveLevelsAndPrunedTriangles.Label")}
                        value={t("PO2.Performance.LOD.TriangleCounts.Value", { first: formatInteger(
                            diagnostics.lodAggressiveAreaLevelCount,
                        ), second: formatInteger(
                            diagnostics.lodPrunedTriangleCount,
                        ) })}
                    />
                    <MetricRow
                        label={t("PO2.Performance.LOD.StaleCancelled.Label")}
                        value={`${formatInteger(
                            diagnostics.lodStaleResultCount,
                        )} / ${formatInteger(
                            diagnostics.lodCancelledJobCount,
                        )}`}
                    />
                    <MetricRow
                        label={t("PO2.Performance.LOD.FailedCaptureRejected.Label")}
                        value={`${formatInteger(
                            diagnostics.lodFailedJobCount,
                        )} / ${formatInteger(
                            diagnostics.lodRejectedCaptureCount,
                        )}`}
                    />
                    <MetricRow
                        label={t("PO2.Performance.LOD.CapturedPayload.Label")}
                        value={formatBytes(
                            diagnostics.lodCapturedBytes,
                        )}
                    />
                    <MetricRow
                        label={t("PO2.Performance.LOD.MaxCaptureSetup.Label")}
                        value={`${formatNumber(
                            diagnostics.lodMaximumCaptureSetupMilliseconds,
                            3,
                        )} ms`}
                    />
                    <MetricRow
                        label={t("PO2.Performance.LOD.MaxCaptureCopyUpdate.Label")}
                        value={`${formatNumber(
                            diagnostics.lodMaximumCaptureCopyUpdateMilliseconds,
                            3,
                        )} ms`}
                    />
                    <MetricRow
                        label={t("PO2.Performance.LOD.MaxWorkerJob.Label")}
                        value={`${formatNumber(
                            diagnostics.lodMaximumWorkerJobMilliseconds,
                            3,
                        )} ms`}
                    />
                    <MetricRow
                        label={t("PO2.Performance.LOD.LastMaxPublicationStep.Label")}
                        value={t("PO2.Performance.LOD.PublicationTime.Value", { last: formatNumber(
                            diagnostics.lodLastPublicationStepMilliseconds,
                            3,
                        ), maximum: formatNumber(
                            diagnostics.lodMaximumPublicationStepMilliseconds,
                            3,
                        ) })}
                    />
                    {diagnostics.lodHasLastAcceptedResult && (
                        <>
                            <MetricRow
                                label={t("PO2.Performance.LOD.LastChain.Label")}
                                value={`${formatInteger(
                                    diagnostics.lodLastSourceTriangleCount,
                                )} -> ${formatInteger(
                                    diagnostics.lodLastLod1TriangleCount,
                                )} -> ${formatInteger(
                                    diagnostics.lodLastLod2TriangleCount,
                                )}`}
                            />
                            <MetricRow
                                label={t("PO2.Performance.LOD.LastTargets.Label")}
                                value={t("PO2.Performance.LOD.TriangleCounts.Value", { first: formatInteger(
                                    diagnostics.lodLastLod1TargetTriangleCount,
                                ), second: formatInteger(
                                    diagnostics.lodLastLod2TargetTriangleCount,
                                ) })}
                            />
                            <MetricRow
                                label={t("PO2.Performance.LOD.LastBudgets.Label")}
                                value={`${diagnostics.lodLastLod1BudgetMet
                                    ? t("PO2.Performance.LOD.Budget.Met")
                                    : t("PO2.Performance.LOD.Budget.Missed")} / ${diagnostics.lodLastLod2BudgetMet
                                    ? t("PO2.Performance.LOD.Budget.Met")
                                    : t("PO2.Performance.LOD.Budget.Missed")}`}
                            />
                            <MetricRow
                                label={t("PO2.Performance.LOD.LastMaxError.Label")}
                                value={t("PO2.Performance.LOD.LastError.Value", { lod1: formatNumber(
                                    diagnostics.lodLastLod1MaximumAbsoluteError,
                                    3,
                                ), lod2: formatNumber(
                                    diagnostics.lodLastLod2MaximumAbsoluteError,
                                    3,
                                ) })}
                            />
                            <MetricRow
                                label={t("PO2.Performance.LOD.LastResult.Label")}
                                value={diagnostics.lodLastAcceptedWorthPublishing
                                    ? t("PO2.Performance.LOD.Result.Publishable")
                                    : t("PO2.Performance.LOD.LastResult.PassThrough")}
                            />
                        </>
                    )}
                </div>
                {diagnostics.lodLastFailureReason !== "" && (
                    <div className={styles.performanceStatus} role="status">{t("PO2.Performance.LOD.Status.GenerationIssue", { message: diagnostics.lodLastFailureReason })}</div>
                )}
                {diagnostics.lodLastPublicationFailureReason !== "" && (
                    <div className={styles.performanceStatus} role="status">{t("PO2.Performance.LOD.Status.PublicationIssue", { message: diagnostics.lodLastPublicationFailureReason })}</div>
                )}
            </section>

            <section className={styles.performanceSection}>
                <div className={styles.sectionHeader}>{t("PO2.Performance.HeaviestObjects.Title")}</div>
                {diagnostics.heaviestObjects.length === 0 ? (
                    <div className={styles.performanceEmpty}>
                        {t("PO2.Performance.HeaviestObjects.Empty")}
                    </div>
                ) : (
                    <div className={styles.performanceObjectList}>
                        {diagnostics.heaviestObjects.map((object) => (
                            <div
                                className={styles.performanceObjectRow}
                                key={object.objectId}
                            >
                                <div className={styles.performanceObjectHeader}>
                                    <span
                                        className={styles.performanceObjectName}
                                        title={object.name}
                                    >
                                        {object.name}
                                    </span>
                                    <span className={styles.performanceObjectStats}>
                                        <span className={styles.performanceObjectStat}>
                                            <span>{formatInteger(object.triangleCount)}</span>
                                            <span>{t("PO2.Performance.HeaviestObjects.Triangles.Unit")}</span>
                                        </span>
                                        <span>|</span>
                                        <span className={styles.performanceObjectStat}>
                                            <span>{formatInteger(object.meshVertexCount)}</span>
                                            <span>{t("PO2.Performance.HeaviestObjects.Vertices.Unit")}</span>
                                        </span>
                                    </span>
                                </div>
                                <ObjectLodDiagnostics object={object} />
                            </div>
                        ))}
                    </div>
                )}
            </section>

            <div className={styles.performanceCapturedAt}>{t("PO2.Performance.Snapshot.CaptureTime.Summary", { time: formatTime(diagnostics.capturedAtUtc) })}</div>
            <Button
                onSelect={onRefresh}
                disabled={refreshDisabled}
                className={controls.wideButton}
            >
                {t("PO2.Common.Refresh")}
            </Button>
        </div>
    );
};
