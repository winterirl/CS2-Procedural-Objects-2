using PO2.Tools.ObjectTool;
using System;
using System.Linq;
using PO2;
using UnityEngine;
using PO2.Interface.Models;
using PO2.Conversion;
using PO2.Core;
using PO2.Geometry.Editing;
using PO2.Geometry.Topology;
using PO2.History;
using PO2.Localization;
using PO2.Tools.Snapping;

internal static partial class Program {
    private static readonly string[] s_StateMessages = {
        "", "Some different wording", "cancelled", "The worker was not cancelled.",
        "Click a placed game object to convert.", "Found 12 material sets."
    };

    private static void OperationStateTests() {
        foreach (string message in s_StateMessages) {
            var conversion = new AssetConversionStatus();
            conversion.SetMessage(LocalizedText.Diagnostic(message));
            Check(conversion.GetPhase(true, false, false) == AssetConversionPhase.Ready, "Prompt phase ignores wording");
            conversion.Fail(LocalizedText.Diagnostic(message));
            Check(conversion.GetPhase(true, false, false) == AssetConversionPhase.Failed, "Failure phase ignores wording");
            Check(conversion.GetPhase(true, true, false) == AssetConversionPhase.Running, "Task determines running phase");
            Check(conversion.GetPhase(true, true, true) == AssetConversionPhase.Cancelling, "Abandoned task stays cancelling until drained");
            Check(conversion.GetPhase(false, true, true) == AssetConversionPhase.Inactive, "Leaving conversion hides its panel");
            conversion.Clear();
            Check(conversion.GetPhase(true, false, false) == AssetConversionPhase.Ready, "Reset clears the prior failure");
            foreach (MaterialImportMessageKind kind in Enum.GetValues(typeof(MaterialImportMessageKind))) {
                var notice = new MaterialImportNotice(kind, LocalizedText.Diagnostic(message));
                Check(notice.Kind == kind && notice.Text.Equals(LocalizedText.Diagnostic(message)), "Material notice keeps its kind independently of text");
            }
            SnapFailureState(message);
            MirrorOutcomeState(message, false);
            TopologyFailureState(message);
        }
        MirrorOutcomeState("", true);
        var empty = new AssetConversionStatus();
        empty.Fail(LocalizedText.Empty);
        Check(empty.Message.IsEmpty && empty.GetPhase(true, false, false) == AssetConversionPhase.Failed,
            "A missing message cannot turn a failure into a prompt");
    }

    private static void SnapFailureState(string message) {
        var obj = SnapObject(new[] { Vector3.zero, Vector3.one });
        var vertices = new[] { new VertexRef(0, 0) };
        var edges = Array.Empty<StructuralEdgeRef>();
        using (var preparation = new MeshTranslationSnapPreparation()) {
            preparation.BeginGesture(obj, 1, MeshSelectionMode.Vertex, MeshEditVertexScope.CurrentPart, 0, 1, 0);
            preparation.Fail(message);
            Check(preparation.HasFailed && !preparation.ValidateActive(obj), "Snap failure blocks attachment even with no text");
            preparation.Refresh(obj, 1, MeshSelectionMode.Vertex, MeshEditVertexScope.CurrentPart,
                0, 1, vertices, 0, edges, true);
            Check(preparation.HasFailed && preparation.Ready == null && preparation.EstimatedResidentBytes == 0,
                "Unchanged snap request stays failed and releases its budget");
            preparation.BeginGesture(obj, 1, MeshSelectionMode.Vertex, MeshEditVertexScope.CurrentPart, 0, 1, 0);
            Check(!preparation.HasFailed && preparation.ValidateActive(obj), "A new gesture can retry a failed snap request");
            preparation.Invalidate();
            Check(!preparation.HasFailed && preparation.Stamp == null, "Invalidation clears failure state");
        }
    }

    private static void MirrorOutcomeState(string message, bool cancel) {
        var obj = new ProceduralObject(9510, Guid.NewGuid(), MirrorFixture());
        obj.Transform = ObjectTransform.IdentityAt(Vector3.zero);
        var system = new ProceduralObjectSystem { MirrorTestSelected = obj,
            RejectTopologyPublication = !cancel, TopologyRejectionMessage = message };
        system.Objects.Add(obj.Id, obj);
        system.m_HistoryContext = new HistoryContext(system);
        using (system.ActionHistory = new ActionHistory(system.m_HistoryContext)) {
            var before = TopologyState.Capture(obj.Parts[0]);
            Check(system.TryMirrorSelected(-1, Vector3.right, LocalizedText.Diagnostic("Mirror"), out var reason), "Outcome test starts mirror");
            if (cancel) system.CancelMirrorEdit();
            var outcome = OperationOutcome.Pending;
            WaitDelete(() => {
                if (outcome == OperationOutcome.Pending) outcome = system.PollMirrorEdit(out reason);
                return outcome != OperationOutcome.Pending && !system.MirrorEditBusy;
            }, "Mirror outcome drains");
            Check(outcome == (cancel ? OperationOutcome.Cancelled : OperationOutcome.Failed),
                "Mirror classifies the actual outcome, including failures containing cancelled");
            Check(!system.ActionHistory.IsBusy && system.ActionHistory.Count == 0, "Failed/cancelled mirror releases history");
            MirrorSame(before, obj.Parts[0], "Outcome test preserves mirror source");
        }
    }

    private static void TopologyFailureState(string message) {
        var part = ConvertedQuad(PO2.Geometry.Topology.StructuralTopologySource.ConvertedInferred);
        var obj = new ProceduralObject(9511, Guid.NewGuid(), part);
        obj.Transform.Scale = Vector3.one;
        var system = new ProceduralObjectSystem { RejectTopologyPublication = true, TopologyRejectionMessage = message };
        system.Objects.Add(obj.Id, obj);
        var tool = new ProceduralObjectToolSystem { TestObject = obj, m_ProceduralObjectSystem = system };
        var controller = tool.m_MeshEditController;
        var selection = AllEdges(part).Select(id => new StructuralEdgeRef(0, id)).ToArray();
        controller.ReplaceEdgeSelection(obj, selection);
        system.m_HistoryContext = new HistoryContext(system, null, controller.Capture, controller.Restore,
            controller.AdoptPreparedTopologySelection);
        using (system.ActionHistory = new ActionHistory(system.m_HistoryContext)) {
            WaitDelete(() => { tool.TickVertexDelete(); return tool.CanSubdivideSelectedEdges; }, "State test analyzes selection");
            tool.TestChangeTopologyMessages(message);
            Check(tool.CanSubdivideSelectedEdges, "Changing availability text does not disable a valid selection");
            var before = TopologyState.Capture(part);
            tool.SubdivideSelectedEdges();
            WaitDelete(() => { tool.TickVertexDelete(); return !tool.TopologyEditBusy && tool.TestTopologyDrained; }, "State test drains failure");
            Check(!tool.CanSubdivideSelectedEdges, "Failure blocks the unchanged selection even with empty text");
            tool.TestChangeTopologyMessages("");
            tool.TickVertexDelete();
            Check(!tool.CanSubdivideSelectedEdges, "Clearing failure text cannot re-enable the operation");
            tool.SubdivideSelectedEdges();
            Check(!tool.TopologyEditBusy && system.ActionHistory.Count == 0, "Failed selection cannot restart or enter history");
            Same(before, TopologyState.Capture(part), "Failed topology preserves geometry");
            controller.ReplaceEdgeSelection(obj, selection);
            WaitDelete(() => { tool.TickVertexDelete(); return tool.CanSubdivideSelectedEdges; }, "New selection clears failure latch");
        }
        tool.StopVertexDelete();
    }
}

namespace PO2.Tools.ObjectTool {
    public partial class ProceduralObjectToolSystem {
        internal bool TestTopologyAnalyzed => m_TopologyAnalyzed;
        internal bool TestTopologyDrained => m_TopologyEdit.Drain.IsCompleted;
        internal void TestChangeTopologyMessages(string message) {
            m_VertexDeleteReason = m_VertexDissolveReason = m_SubdivideEdgesUnavailableReason = LocalizedText.Diagnostic(message);
        }
    }
}
