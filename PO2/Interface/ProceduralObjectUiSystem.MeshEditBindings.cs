using Colossal.UI.Binding;
using PO2.Interface.Bindings;
using PO2.Localization;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private void RegisterTopologyStateBindings()
        {
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "topologyEditBusy", () => m_ProceduralObjectToolSystem.TopologyEditBusy));
            AddUpdateBinding(new GetterValueBinding<LocalizedText>(kGroup, "meshTranslationPreparationStatus", () => m_ProceduralObjectToolSystem.MeshTranslationPreparationStatus, LocalizedTextWriter.s_Instance));
            AddUpdateBinding(new GetterValueBinding<LocalizedText>(kGroup, "topologyEditStatus", () => m_ProceduralObjectToolSystem.TopologyEditStatus, LocalizedTextWriter.s_Instance));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canDeleteVertices", () => m_ProceduralObjectToolSystem.CanDeleteSelectedVertices));
            AddUpdateBinding(new GetterValueBinding<LocalizedText>(kGroup, "deleteVerticesUnavailableReason", () => m_ProceduralObjectToolSystem.DeleteVerticesUnavailableReason, LocalizedTextWriter.s_Instance));
            AddBinding(CommandBinding("deleteVertices", GuardHistory(m_ProceduralObjectToolSystem.DeleteSelectedVertices)));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canDissolveVertices", () => m_ProceduralObjectToolSystem.CanDissolveSelectedVertices));
            AddUpdateBinding(new GetterValueBinding<LocalizedText>(kGroup, "dissolveVerticesUnavailableReason", () => m_ProceduralObjectToolSystem.DissolveVerticesUnavailableReason, LocalizedTextWriter.s_Instance));
            AddBinding(CommandBinding("dissolveVertices", GuardHistory(m_ProceduralObjectToolSystem.DissolveSelectedVertices)));
        }

        private void RegisterMeshEditStateBindings()
        {
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "selectedVertexCount", () => m_ProceduralObjectToolSystem.MeshEditSelectedVertexCount));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "meshEditSelectionVersion", () => m_ProceduralObjectToolSystem.MeshEditSelectionVersion));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "meshTransformMode", () => (int)m_ProceduralObjectToolSystem.MeshTransformMode));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "meshTransformPivot", () => (int)m_ProceduralObjectToolSystem.MeshTransformPivot));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canApplyMeshNumericMove", () => m_ProceduralObjectToolSystem.CanApplyMeshNumericMove));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canSetMeshPivot", () => m_ProceduralObjectToolSystem.CanSetMeshPivot));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "meshPivotUsesSelection", () => m_ProceduralObjectToolSystem.MeshPivotUsesSelection));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "selectedSurfaceCount", () => m_ProceduralObjectToolSystem.MeshEditSelectedSurfaceCount));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "meshEditSelectionMode", () => (int)m_ProceduralObjectToolSystem.MeshEditSelectionMode));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "boxSelectionVisible", () => m_ProceduralObjectToolSystem.IsBoxSelectionVisible));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "boxSelectionLeft", () => m_ProceduralObjectToolSystem.BoxSelectionNormalizedRect.x));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "boxSelectionTop", () => m_ProceduralObjectToolSystem.BoxSelectionNormalizedRect.y));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "boxSelectionWidth", () => m_ProceduralObjectToolSystem.BoxSelectionNormalizedRect.width));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "boxSelectionHeight", () => m_ProceduralObjectToolSystem.BoxSelectionNormalizedRect.height));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "meshEditVisibilityMode", () => (int)m_ProceduralObjectToolSystem.MeshEditVisibility));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "meshEditVertexScope", () => (int)m_ProceduralObjectToolSystem.MeshEditVertexScopeMode));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "vertexAlignmentAxis", () => (int)m_ProceduralObjectToolSystem.MeshEditAlignmentAxis));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "vertexSlideTargeting", () => m_ProceduralObjectToolSystem.IsMeshEditVertexSlideTargeting));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "vertexSlideDragging", () => m_ProceduralObjectToolSystem.IsMeshEditVertexSlideDragging));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canLoopCut", () => m_ProceduralObjectToolSystem.CanMeshLoopCut));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "loopCutTargeting", () => m_ProceduralObjectToolSystem.IsMeshEditLoopCutTargeting));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "loopCutPositioning", () => m_ProceduralObjectToolSystem.IsMeshEditLoopCutPositioning));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "meshCutCount", () => m_ProceduralObjectToolSystem.MeshCutCount));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canSurfaceCut", () => m_ProceduralObjectToolSystem.CanMeshSurfaceCut));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "surfaceCutTargeting", () => m_ProceduralObjectToolSystem.IsMeshEditSurfaceCutTargeting));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "surfaceCutPositioning", () => m_ProceduralObjectToolSystem.IsMeshEditSurfaceCutPositioning));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canExtrudeFaces", () => m_ProceduralObjectToolSystem.CanBeginMeshFaceExtrude));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canInsetFaces", () => m_ProceduralObjectToolSystem.CanBeginMeshFaceInset));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "faceSelectionActive", () => m_ProceduralObjectToolSystem.IsMeshEditFaceSelectionActive));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "faceExtrudeSelecting", () => m_ProceduralObjectToolSystem.IsMeshEditFaceExtrudeSelecting));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "faceExtrudePositioning", () => m_ProceduralObjectToolSystem.IsMeshEditFaceExtrudePositioning));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canContinueFaceExtrude", () => m_ProceduralObjectToolSystem.CanContinueMeshFaceExtrude));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canCommitFaceExtrude", () => m_ProceduralObjectToolSystem.CanCommitMeshFaceExtrude));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "faceExtrudeDistance", () => m_ProceduralObjectToolSystem.MeshFaceExtrudeDistance));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "faceInsetSelecting", () => m_ProceduralObjectToolSystem.IsMeshEditFaceInsetSelecting));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "faceInsetPositioning", () => m_ProceduralObjectToolSystem.IsMeshEditFaceInsetPositioning));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canContinueFaceInset", () => m_ProceduralObjectToolSystem.CanContinueMeshFaceInset));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canCommitFaceInset", () => m_ProceduralObjectToolSystem.CanCommitMeshFaceInset));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "faceInsetAmount", () => m_ProceduralObjectToolSystem.MeshFaceInsetAmount));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "selectedFaceCount", () => m_ProceduralObjectToolSystem.MeshEditSelectedFaceCount));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canSubdivideEdges", () => m_ProceduralObjectToolSystem.CanSubdivideSelectedEdges));

            AddUpdateBinding(new GetterValueBinding<LocalizedText>(kGroup, "subdivideEdgesUnavailableReason", () => m_ProceduralObjectToolSystem.SubdivideEdgesUnavailableReason, LocalizedTextWriter.s_Instance));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canDissolveLoop", () => m_ProceduralObjectToolSystem.CanDissolveSelectedLoop));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canDissolveSurfaceCut", () => m_ProceduralObjectToolSystem.CanDissolveSelectedSurfaceCut));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canMergeVertices", () => m_ProceduralObjectToolSystem.CanMergeSelectedVertices));
        }

        private void RegisterMeshEditActionBindings()
        {
            AddBinding(CommandBinding("setVertexSelectionMode", GuardMeshView(m_ProceduralObjectToolSystem.SetMeshVertexSelectionMode)));

            AddBinding(CommandBinding("setEdgeSelectionMode", GuardMeshView(m_ProceduralObjectToolSystem.SetMeshEdgeSelectionMode)));

            AddBinding(CommandBinding("setMeshVisibilityXRay", GuardMeshView(m_ProceduralObjectToolSystem.SetMeshVisibilityXRay)));

            AddBinding(CommandBinding("setMeshVisibilitySolid", GuardMeshView(m_ProceduralObjectToolSystem.SetMeshVisibilitySolid)));

            AddBinding(CommandBinding("setMeshVertexScopeCurrentPart", GuardMeshView(m_ProceduralObjectToolSystem.SetMeshVertexScopeCurrentPart)));

            AddBinding(CommandBinding("setMeshVertexScopeAllParts", GuardMeshView(m_ProceduralObjectToolSystem.SetMeshVertexScopeAllParts)));

            AddBinding(CommandBinding("selectAllMeshElements", GuardHistory(m_ProceduralObjectToolSystem.SelectAllMeshElements)));

            AddBinding(CommandBinding("invertMeshSelection", GuardHistory(m_ProceduralObjectToolSystem.InvertMeshSelection)));

            AddBinding(CommandBinding("selectConnectedMeshElements", GuardHistory(m_ProceduralObjectToolSystem.SelectConnectedMeshElements)));

            AddBinding(CommandBinding("setMeshPivot", GuardHistory(m_ProceduralObjectToolSystem.SetMeshPivot)));

            AddBinding(CommandBinding("alignVerticesX", GuardHistory(m_ProceduralObjectToolSystem.BeginMeshVertexAlignmentX)));

            AddBinding(CommandBinding("alignVerticesY", GuardHistory(m_ProceduralObjectToolSystem.BeginMeshVertexAlignmentY)));

            AddBinding(CommandBinding("alignVerticesZ", GuardHistory(m_ProceduralObjectToolSystem.BeginMeshVertexAlignmentZ)));

            AddBinding(new TriggerBinding(kGroup, "cancelVertexAlignment", GuardHistory(m_ProceduralObjectToolSystem.CancelMeshVertexAlignment)));

            AddBinding(CommandBinding("beginVertexSlide", GuardHistory(m_ProceduralObjectToolSystem.BeginMeshVertexSlide)));

            AddBinding(new TriggerBinding(kGroup, "cancelVertexSlide", GuardHistory(m_ProceduralObjectToolSystem.CancelMeshVertexSlide)));

            AddBinding(CommandBinding("beginLoopCut", GuardHistory(m_ProceduralObjectToolSystem.BeginMeshLoopCut)));

            AddBinding(new TriggerBinding(kGroup, "cancelLoopCut", GuardHistory(m_ProceduralObjectToolSystem.CancelMeshLoopCut)));

            AddBinding(CommandBinding("beginSurfaceCut", GuardHistory(m_ProceduralObjectToolSystem.BeginMeshSurfaceCut)));

            AddBinding(new TriggerBinding(kGroup, "cancelSurfaceCut", GuardHistory(m_ProceduralObjectToolSystem.CancelMeshSurfaceCut)));

            AddBinding(CommandBinding("beginFaceExtrude", GuardHistory(m_ProceduralObjectToolSystem.BeginMeshFaceExtrude)));

            AddBinding(CommandBinding("beginFaceInset", GuardHistory(m_ProceduralObjectToolSystem.BeginMeshFaceInset)));

            AddBinding(new TriggerBinding(kGroup, "cancelFaceOperation", GuardHistory(m_ProceduralObjectToolSystem.CancelMeshFaceOperation)));

            AddBinding(CommandBinding("continueFaceExtrude", GuardHistory(m_ProceduralObjectToolSystem.ContinueMeshFaceExtrude)));

            AddBinding(new TriggerBinding(kGroup, "backFaceExtrude", GuardHistory(m_ProceduralObjectToolSystem.BackMeshFaceExtrude)));

            AddBinding(CommandBinding("confirmFaceExtrude", GuardHistory(m_ProceduralObjectToolSystem.ConfirmMeshFaceExtrude)));

            AddBinding(new TriggerBinding<float>(kGroup, "setFaceExtrudeDistance", GuardHistory<float>(m_ProceduralObjectToolSystem.SetMeshFaceExtrudeDistance)));

            AddBinding(CommandBinding("continueFaceInset", GuardHistory(m_ProceduralObjectToolSystem.ContinueMeshFaceInset)));

            AddBinding(new TriggerBinding(kGroup, "backFaceInset", GuardHistory(m_ProceduralObjectToolSystem.BackMeshFaceInset)));

            AddBinding(CommandBinding("confirmFaceInset", GuardHistory(m_ProceduralObjectToolSystem.ConfirmMeshFaceInset)));

            AddBinding(new TriggerBinding<float>(kGroup, "setFaceInsetAmount", GuardHistory<float>(m_ProceduralObjectToolSystem.SetMeshFaceInsetAmount)));

            AddBinding(CommandBinding("subdivideEdges", GuardHistory(m_ProceduralObjectToolSystem.SubdivideSelectedEdges)));

            AddBinding(CommandBinding("dissolveLoop", GuardHistory(m_ProceduralObjectToolSystem.DissolveSelectedLoop)));

            AddBinding(CommandBinding("dissolveSurfaceCut", GuardHistory(m_ProceduralObjectToolSystem.DissolveSelectedSurfaceCut)));

            AddBinding(CommandBinding("mergeVertices", GuardHistory(m_ProceduralObjectToolSystem.MergeSelectedVertices)));
        }
    }
}