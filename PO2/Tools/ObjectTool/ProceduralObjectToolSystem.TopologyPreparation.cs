using System;
using System.Threading;
using System.Threading.Tasks;
using PO2.Core;
using PO2.Core.Operations;
using PO2.Geometry.Editing;
using PO2.History;
using PO2.Localization;
using PO2.Persistence.Model;
using PO2.Tools.MeshEdit;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private sealed class TopologyAnalysis
        {
            internal VertexDeletePlan Delete;
            internal VertexDissolvePlan Dissolve;
            internal EdgeSubdivisionPlan Subdivision;
            internal int[] SelectedVertices;
            internal bool CanDelete, CanDissolve, CanSubdivide;
            internal LocalizedText DeleteReason, DissolveReason, SubdivisionReason;
        }

        private readonly TopologyEditSession m_TopologyEdit = new TopologyEditSession();
        private TopologySnapshotCapture m_TopologyCapture;
        private TopologySelectionCapture m_TopologySelectionCapture;
        private PersistencePartData m_TopologySource;
        private TopologyEditIndex m_TopologyIndex;
        private Task<TopologyEditIndex> m_TopologyIndexWork;
        private Task<TopologyAnalysis> m_TopologyAnalysisWork;
        private CancellationTokenSource m_TopologyCaptureCancellation, m_TopologyAnalysisCancellation, m_TopologySelectionCancellation;
        private Task m_TopologyRetiredCapture = Task.CompletedTask;
        private VertexDeletePlan m_VertexDeletePlan;
        private VertexDissolvePlan m_VertexDissolvePlan;
        private EdgeSubdivisionPlan m_SubdivisionPlan;
        private TopologyTargetStamp m_TopologyEditStamp;
        private ProceduralObject m_TopologyObject;
        private int m_TopologyEditPart = -1, m_TopologySelectionVersion = -1, m_TopologyActivePart = -1;
        private MeshEditVertexScope m_TopologyScope;
        private MeshSelectionMode m_TopologyMode;
        private uint m_TopologyPartVersion, m_TopologyGeometryVersion;
        private TopologySelectionElement[] m_TopologyElements;
        private int[] m_TopologySelected;
        private bool m_TopologyAnalyzed;
        private bool m_CanDeleteVertices, m_CanDissolveVertices, m_CanSubdivideEdges;
        private int m_TopologyOperation;
        private LocalizedText m_VertexDeleteReason = LocalizedText.For("PO2.Edit.Selection.Required.VerticesInPart");
        private LocalizedText m_VertexDissolveReason = LocalizedText.For("PO2.Edit.Selection.Required.VerticesInPart");
        private LocalizedText m_SubdivideEdgesUnavailableReason = LocalizedText.For("PO2.Edit.Selection.Required.EdgesInPart");
        private LocalizedText m_TopologyEditStatus = LocalizedText.Empty;

        internal void AdoptPreparedTopologySelection(ProceduralObject obj, TopologyPreparedSelection prepared) =>
            m_MeshEditController.AdoptPreparedTopologySelection(obj, prepared);

        public bool TopologyEditBusy => m_TopologyEdit.IsBusy || m_ProceduralObjectSystem?.MirrorEditBusy == true;
        private LocalizedText MarkerPreparationStatus => m_MeshEditController.VisibilityMode == MeshEditVisibilityMode.XRay ? LocalizedText.For("PO2.Edit.Status.PreparingXRayMarkers") : LocalizedText.For("PO2.Edit.Status.PreparingSolidMarkers");
        internal bool MarkerPreparationPending => m_MeshEditController?.MarkerPreparationPending == true;
        internal bool MeshNavigationBusy => DeformationSnapshotBusy || TopologyEditBusy || GpuInteractionPending || ArrayBusy || m_ProceduralObjectSystem?.ClipboardBusy == true;
        public bool MeshMutationBusy => MarkerPreparationPending || MeshNavigationBusy || m_IsGizmoDragging || m_IsVertexSlideDragging || ArrayActive;
        public LocalizedText TopologyEditStatus => MarkerPreparationPending ? MarkerPreparationStatus : GpuInteractionPending ? LocalizedText.For("PO2.Edit.Status.SelectionPending") : m_ProceduralObjectSystem?.MirrorEditBusy == true ? m_ProceduralObjectSystem.MirrorEditStatus : TopologyEditBusy ? m_TopologyEdit.Status : m_TopologyEditStatus;
        public bool CanDeleteSelectedVertices => TopologyCacheCurrent(MeshSelectionMode.Vertex) && m_VertexDeletePlan != null && m_CanDeleteVertices;
        public bool CanDissolveSelectedVertices => TopologyCacheCurrent(MeshSelectionMode.Vertex) && m_VertexDissolvePlan != null && m_CanDissolveVertices;
        public bool CanSubdivideSelectedEdges => TopologyCacheCurrent(MeshSelectionMode.Edge) && m_SubdivisionPlan != null && m_CanSubdivideEdges;
        public LocalizedText DeleteVerticesUnavailableReason => TopologyUnavailable(MeshSelectionMode.Vertex, m_VertexDeleteReason);
        public LocalizedText DissolveVerticesUnavailableReason => TopologyUnavailable(MeshSelectionMode.Vertex, m_VertexDissolveReason);
        public LocalizedText SubdivideEdgesUnavailableReason => TopologyUnavailable(MeshSelectionMode.Edge, m_SubdivideEdgesUnavailableReason);

        private LocalizedText TopologyUnavailable(MeshSelectionMode mode, LocalizedText reason)
        {
            if (m_MeshEditController == null || m_MeshEditController.SelectionMode != mode)
                return mode == MeshSelectionMode.Edge ? LocalizedText.For("PO2.Edit.Selection.Required.EdgesInPart") : LocalizedText.For("PO2.Edit.Selection.Required.VerticesInPart");
            return TryGetTopologyBlock(out _, out LocalizedText blocked) ? blocked : (TopologySelectionCurrent() ? reason : LocalizedText.For("PO2.Edit.Status.AnalyzingSelection"));
        }

        private bool TryGetTopologyBlock(out ProceduralObject obj, out LocalizedText reason)
        {
            reason = LocalizedText.Empty;
            if (!TryGetMeshEditObject(out obj) || m_MeshEditController == null || (m_MeshEditController.SelectionMode != MeshSelectionMode.Vertex && m_MeshEditController.SelectionMode != MeshSelectionMode.Edge) || (m_MeshEditController.SelectionMode == MeshSelectionMode.Edge ? m_MeshEditController.EdgeSelectionCount : m_MeshEditController.SelectionCount) == 0)
                reason = LocalizedText.For("PO2.Edit.Selection.Required.ElementsInPart");
            else if (MarkerPreparationPending) reason = MarkerPreparationStatus;
            else if (!m_TopologyEdit.Drain.IsCompleted) reason = LocalizedText.For("PO2.Edit.Status.FinishingCancellation");
            else if (obj.IsLocked) reason = LocalizedText.For("PO2.Edit.Disabled.ObjectLocked");
            else if (TopologyEditBusy || m_ProceduralObjectSystem.ActionHistory.IsBusy || AssetConversionBusy || DeformationSnapshotBusy || DeformationCommitBusy || m_IsGizmoDragging || m_IsVertexSlideDragging || m_BoxSelection.IsGestureActive || IsMeshTopologyInteractionActive || m_MeshEditController.IsAlignmentTargeting || m_MeshEditController.IsVertexSlideTargeting)
                reason = LocalizedText.For("PO2.Edit.Disabled.OperationPending");
            else if (MeshEditRequiresBake) reason = LocalizedText.For("PO2.Edit.Disabled.DeformationPending");
            else return false;
            return true;
        }

        private bool TopologySelectionCurrent() => TryGetMeshEditObject(out var obj) && ReferenceEquals(obj, m_TopologyObject) && m_TopologySelectionVersion == m_MeshEditController.SelectionVersion && m_TopologyPartVersion == obj.PartVersion && m_TopologyGeometryVersion == obj.GeometryVersion && m_TopologyMode == m_MeshEditController.SelectionMode && m_TopologyActivePart == m_ProceduralObjectSystem.SelectedPartIndex && m_TopologyScope == m_MeshEditController.VertexScope;
        private bool TopologyCacheCurrent(MeshSelectionMode mode) => m_MeshEditController.SelectionMode == mode && !TryGetTopologyBlock(out var obj, out _) && TopologySelectionCurrent() && m_TopologyEditStamp != null && m_TopologyEditStamp.Matches(obj, m_TopologyEditPart);

        private void SetTopologyReasons(LocalizedText reason)
        {
            m_CanDeleteVertices = m_CanDissolveVertices = m_CanSubdivideEdges = false;
            m_VertexDeleteReason = m_VertexDissolveReason = m_SubdivideEdgesUnavailableReason = reason;
        }

        private void ClearTopologySelection()
        {
            m_TopologyEditStatus = LocalizedText.Empty;
            m_TopologyAnalysisCancellation?.Cancel();
            m_TopologySelectionCancellation?.Cancel();
            if (m_TopologySelectionCapture != null)
            {
                m_TopologyRetiredCapture = Task.WhenAll(m_TopologyRetiredCapture, m_TopologySelectionCapture.Drain);
                m_TopologySelectionCapture.Dispose();
                m_TopologySelectionCapture = null;
            }

            m_TopologySelectionCancellation?.Dispose();
            m_TopologySelectionCancellation = null;
            m_VertexDeletePlan = null;
            m_VertexDissolvePlan = null;
            m_SubdivisionPlan = null;
            m_CanDeleteVertices = m_CanDissolveVertices = m_CanSubdivideEdges = false;
            m_TopologySelected = null;
            m_TopologyElements = null;
            m_TopologyAnalyzed = false;
            m_TopologySelectionVersion = -1;
        }

        private void InvalidateTopologySource()
        {
            m_TopologyCaptureCancellation?.Cancel();
            if (m_TopologyCapture != null) m_TopologyRetiredCapture = Task.WhenAll(m_TopologyRetiredCapture, m_TopologyCapture.Copy.Allocation);
            m_TopologyCapture = null;
            m_TopologySource = null;
            m_TopologyIndex = null;
            m_TopologyEditStamp = null;
        }

        private void PollTopologyWorkers()
        {
            if (m_TopologyIndexWork != null && m_TopologyIndexWork.IsCompleted)
            {
                try
                {
                    var index = m_TopologyIndexWork.GetAwaiter().GetResult();
                    if (!m_TopologyCaptureCancellation.IsCancellationRequested) m_TopologyIndex = index;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    if (!m_TopologyCaptureCancellation.IsCancellationRequested)
                    {
                        SetTopologyReasons(LocalizedText.FromException(e));
                        m_TopologyAnalyzed = true;
                    }
                }

                m_TopologyIndexWork = null;
            }

            if (m_TopologyAnalysisWork != null && m_TopologyAnalysisWork.IsCompleted)
            {
                try
                {
                    var result = m_TopologyAnalysisWork.GetAwaiter().GetResult();
                    if (!m_TopologyAnalysisCancellation.IsCancellationRequested)
                    {
                        m_VertexDeletePlan = result.Delete;
                        m_VertexDissolvePlan = result.Dissolve;
                        m_SubdivisionPlan = result.Subdivision;
                        m_CanDeleteVertices = result.CanDelete;
                        m_CanDissolveVertices = result.CanDissolve;
                        m_CanSubdivideEdges = result.CanSubdivide;
                        m_VertexDeleteReason = result.DeleteReason;
                        m_VertexDissolveReason = result.DissolveReason;
                        m_SubdivideEdgesUnavailableReason = result.SubdivisionReason;
                        m_TopologySelected = result.SelectedVertices;
                        m_TopologyAnalyzed = true;
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    if (!m_TopologyAnalysisCancellation.IsCancellationRequested)
                    {
                        SetTopologyReasons(LocalizedText.FromException(e));
                        m_TopologyAnalyzed = true;
                    }
                }

                m_TopologyAnalysisWork = null;
                m_TopologyAnalysisCancellation.Dispose();
                m_TopologyAnalysisCancellation = null;
            }

            if (m_TopologyRetiredCapture.IsCompleted)
            {
                var ignored = m_TopologyRetiredCapture.Exception;
            }
        }

        private void RefreshTopologyAvailability()
        {
            PollTopologyWorkers();
            if (!m_TopologyEdit.Drain.IsCompleted) return;
            bool isBlocked = TryGetTopologyBlock(out var obj, out LocalizedText blocked);
            if (isBlocked)
            {
                ClearTopologySelection();
                InvalidateTopologySource();
                SetTopologyReasons(blocked);
                return;
            }

            if (m_TopologyEditStamp != null && !m_TopologyEditStamp.Matches(obj, m_TopologyEditPart))
            {
                ClearTopologySelection();
                InvalidateTopologySource();
            }

            try
            {
                if (!TopologySelectionCurrent())
                {
                    ClearTopologySelection();
                    SetTopologyReasons(LocalizedText.For("PO2.Edit.Status.AnalyzingSelection"));
                    int partIndex = m_MeshEditController.FirstSelectedPart;
                    if (!ReferenceEquals(obj, m_TopologyObject) || partIndex != m_TopologyEditPart) InvalidateTopologySource();
                    m_TopologyObject = obj;
                    m_TopologyEditPart = partIndex;
                    m_TopologySelectionVersion = m_MeshEditController.SelectionVersion;
                    m_TopologyPartVersion = obj.PartVersion;
                    m_TopologyGeometryVersion = obj.GeometryVersion;
                    m_TopologyActivePart = m_ProceduralObjectSystem.SelectedPartIndex;
                    m_TopologyScope = m_MeshEditController.VertexScope;
                    m_TopologyMode = m_MeshEditController.SelectionMode;
                    if (partIndex < 0 || partIndex >= obj.Parts.Count || !m_MeshEditController.IsPartInCurrentScope(partIndex))
                        throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Selection.Error.OutsideScope"));
                }

                if (m_TopologyAnalyzed || m_TopologyAnalysisWork != null || m_TopologyIndexWork != null || !m_TopologyRetiredCapture.IsCompleted) return;
                if (m_TopologyElements == null)
                {
                    if (m_TopologySelectionCapture == null)
                    {
                        m_TopologySelectionCancellation = new CancellationTokenSource();
                        m_TopologySelectionCapture = m_MeshEditController.CaptureTopologySelection(obj);
                        m_TopologySelectionCapture.Start(m_TopologySelectionCancellation.Token);
                    }

                    if (!m_TopologySelectionCapture.Poll(TopologySelectionCurrent, m_TopologySelectionCancellation.Token)) return;
                    m_TopologyElements = m_TopologySelectionCapture.Items;
                    m_TopologySelectionCapture.Dispose();
                    m_TopologySelectionCapture = null;
                }

                if (m_TopologySource == null)
                {
                    if (m_TopologyCapture == null)
                    {
                        m_TopologyCaptureCancellation?.Dispose();
                        m_TopologyCaptureCancellation = new CancellationTokenSource();
                        m_TopologyCapture = new TopologySnapshotCapture(obj, m_TopologyEditPart);
                        m_TopologyEditStamp = m_TopologyCapture.Stamp;
                        m_TopologyCapture.Start(m_TopologyCaptureCancellation.Token);
                    }

                    if (!m_TopologyCapture.Poll(m_TopologyCaptureCancellation.Token)) return;
                    m_TopologySource = m_TopologyCapture.Finish();
                    m_TopologyCapture = null;
                }

                if (m_TopologyIndex == null)
                {
                    var source = m_TopologySource;
                    var token = m_TopologyCaptureCancellation.Token;
                    m_TopologyIndexWork = Task.Run(() => new TopologyEditIndex(source, token), token);
                    return;
                }

                m_TopologyAnalysisCancellation = new CancellationTokenSource();
                var cancellation = m_TopologyAnalysisCancellation.Token;
                var index = m_TopologyIndex;
                var elements = m_TopologyElements;
                var mode = m_TopologyMode;
                int part = m_TopologyEditPart;
                m_TopologyAnalysisWork = Task.Run(() => AnalyzeTopology(index, elements, mode, part, cancellation), cancellation);
            }
            catch (OperationCanceledException)
            {
                ClearTopologySelection();
                InvalidateTopologySource();
            }
            catch (Exception e)
            {
                SetTopologyReasons(LocalizedText.FromException(e));
                m_TopologyAnalyzed = true;
            }
        }

        private static TopologyAnalysis AnalyzeTopology(TopologyEditIndex index, TopologySelectionElement[] elements, MeshSelectionMode mode, int part, CancellationToken token)
        {
            var result = new TopologyAnalysis
            {
                DeleteReason = LocalizedText.For("PO2.Edit.Selection.Required.VerticesInPart"),
                DissolveReason = LocalizedText.For("PO2.Edit.Selection.Required.VerticesInPart"), SubdivisionReason = LocalizedText.For("PO2.Edit.Selection.Required.EdgesInPart")
            };
            var vertices = mode == MeshSelectionMode.Vertex ? new int[elements.Length] : null;
            var edges = mode == MeshSelectionMode.Edge ? new SubdivisionEdge[elements.Length] : null;
            for (int i = 0; i < elements.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                var element = elements[i];
                if (element.Part != part) throw new LocalizedTextException(LocalizedText.For("PO2.Edit.Selection.Error.MultipleParts"));
                if (vertices != null) vertices[i] = element.Id;
                else edges[i] = new SubdivisionEdge(element.Id, element.A, element.B);
            }

            if (vertices != null)
            {
                result.SelectedVertices = vertices;
                result.CanDelete = TopologyEditor.TryAnalyzeVertexDeletion(index.Source, vertices, token, out result.Delete, out result.DeleteReason, index);
                result.CanDissolve = TopologyEditor.TryAnalyzeVertexDissolution(index, vertices, token, out result.Dissolve, out result.DissolveReason);
            }
            else result.CanSubdivide = TopologyEditor.TryAnalyzeEdgeSubdivision(index, edges, token, out result.Subdivision, out result.SubdivisionReason);

            return result;
        }

        private bool ProcessPendingTopologyEdit()
        {
            if (ProcessPendingMirrorEdit()) return true;
            if (!TopologyEditBusy)
            {
                RefreshTopologyAvailability();
                return false;
            }

            bool hasTarget = TryGetMeshEditObject(out var obj);
            if (!hasTarget || !TopologySelectionCurrent()) m_TopologyEdit.Cancel();
            OperationOutcome outcome = m_TopologyEdit.PollFinish(obj, m_ProceduralObjectSystem, out LocalizedText reason);
            if (outcome == OperationOutcome.Pending) return true;
            m_TopologyEditStatus = reason;
            if (outcome == OperationOutcome.Succeeded)
            {
                ClearTopologySelection();
                InvalidateTopologySource();
                m_MeshEditController.ClearHover();
                ClearGizmoHover();
            }
            else if (outcome == OperationOutcome.Failed && hasTarget && TopologySelectionCurrent() && m_TopologyEditStamp != null && m_TopologyEditStamp.Matches(obj, m_TopologyEditPart))
            {
                if (m_TopologyOperation == 2)
                {
                    m_CanSubdivideEdges = false;
                    m_SubdivideEdgesUnavailableReason = reason;
                }
                else if (m_TopologyOperation == 1)
                {
                    m_CanDissolveVertices = false;
                    m_VertexDissolveReason = reason;
                }
                else
                {
                    m_CanDeleteVertices = false;
                    m_VertexDeleteReason = reason;
                }

                // Keep this operation blocked until the selection changes.
                m_TopologyEditStatus = LocalizedText.Empty;
            }

            return true;
        }

        private void ResetTopologyEdit()
        {
            m_ProceduralObjectSystem?.ResetMirrorEdit();
            m_TopologyEdit.Reset();
            m_TopologyEditStatus = LocalizedText.Empty;
            ClearTopologySelection();
            InvalidateTopologySource();
            m_TopologyObject = null;
            var capture = m_TopologyCaptureCancellation;
            var analysis = m_TopologyAnalysisCancellation;
            var indexWork = m_TopologyIndexWork;
            var analysisWork = m_TopologyAnalysisWork;
            m_TopologyCaptureCancellation = null;
            m_TopologyAnalysisCancellation = null;
            m_TopologyIndexWork = null;
            m_TopologyAnalysisWork = null;
            m_TopologyRetiredCapture = Task.WhenAll(m_TopologyRetiredCapture, (Task)indexWork ?? Task.CompletedTask, (Task)analysisWork ?? Task.CompletedTask).ContinueWith(t =>
            {
                var ignored = t.Exception;
                capture?.Dispose();
                analysis?.Dispose();
            }, TaskScheduler.Default);
        }
    }
}