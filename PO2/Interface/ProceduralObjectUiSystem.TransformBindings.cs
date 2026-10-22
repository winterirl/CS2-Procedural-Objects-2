using System;
using Colossal.UI.Binding;
using UnityEngine;
using PO2.Core;
using PO2.Interface.Bindings;
using PO2.Interface.Models;
using PO2.Localization;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private ProceduralObject m_CustomOrientationCacheObject;

        private uint m_CustomOrientationCacheVersion;

        private NamedIndex[] m_CustomOrientations = Array.Empty<NamedIndex>();

        private void RegisterDeformationStateBindings()
        {
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "mirrorMode", () => m_ProceduralObjectToolSystem.MirrorMode));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canMirrorSelected", () => m_ProceduralObjectToolSystem.CanMirrorSelected));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "mirrorRequiresBake", () => m_ProceduralObjectToolSystem.MirrorRequiresBake));
            AddUpdateBinding(new GetterValueBinding<LocalizedText>(kGroup, "mirrorStatus", () => m_ProceduralObjectToolSystem.MirrorStatus, LocalizedTextWriter.s_Instance));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "deformMode", () => m_ProceduralObjectToolSystem.DeformMode));
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "deformationPivot", () => m_ProceduralObjectToolSystem.DeformationPivot));
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "deformationSelectionCount", () => m_ProceduralObjectToolSystem.DeformationSelectionCount));
            AddUpdateBinding(new GetterValueBinding<LocalizedText>(kGroup, "deformationStatus", () => m_ProceduralObjectToolSystem.DeformationStatus, LocalizedTextWriter.s_Instance));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canBakeDeformation", () => m_ProceduralObjectToolSystem.CanBakeDeformation));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "meshEditRequiresBake", () => m_ProceduralObjectToolSystem.MeshEditRequiresBake));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canBakeMeshDeformation", () => m_ProceduralObjectToolSystem.CanBakeMeshDeformation));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "selectedHasDeformation", () => m_ProceduralObjectToolSystem.SelectedHasDeformation));
        }

        private void RegisterTransformStateBindings()
        {
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "gizmoMode", () => (int)m_ProceduralObjectToolSystem.GizmoMode));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "transformTarget", () => (int)m_ProceduralObjectToolSystem.TransformTarget));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "transformOrientation", () => (int)m_ProceduralObjectToolSystem.TransformOrientation));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "snapMagnetEnabled", () => m_ProceduralObjectToolSystem.SnapMagnetEnabled));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "snapIncrementEnabled", () => m_ProceduralObjectToolSystem.SnapIncrementEnabled));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canSnapToTerrain", () => m_ProceduralObjectToolSystem.CanSnapToTerrain));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "snapPOVerticesEnabled", () => m_ProceduralObjectToolSystem.SnapPOVerticesEnabled));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "snapPOEdgesEnabled", () => m_ProceduralObjectToolSystem.SnapPOEdgesEnabled));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "snapGameSurfacesEnabled", () => m_ProceduralObjectToolSystem.SnapGameSurfacesEnabled));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "moveSnapIncrement", () => m_ProceduralObjectToolSystem.MoveSnapIncrement));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "precisionMoveSnapIncrement", () => m_ProceduralObjectToolSystem.PrecisionMoveSnapIncrement));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "rotationSnapIncrement", () => m_ProceduralObjectToolSystem.RotationSnapIncrement));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "precisionRotationSnapIncrement", () => m_ProceduralObjectToolSystem.PrecisionRotationSnapIncrement));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "scaleSnapIncrement", () => m_ProceduralObjectToolSystem.ScaleSnapIncrement));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "precisionScaleSnapIncrement", () => m_ProceduralObjectToolSystem.PrecisionScaleSnapIncrement));

            AddUpdateBinding(new GetterValueBinding<NamedIndex[]>(kGroup, "customOrientations", GetSelectedCustomOrientations, new ArrayWriter<NamedIndex>(new ValueWriter<NamedIndex>())));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "activeCustomOrientationIndex", () => m_ProceduralObjectToolSystem.ActiveCustomOrientationIndex));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canBeginAxisPicking", () => m_ProceduralObjectToolSystem.CanBeginAxisPicking));
        }

        private void RegisterScaleBindings()
        {
            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "scaleX", () => GetSelectedScale().x));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "scaleY", () => GetSelectedScale().y));

            AddUpdateBinding(new GetterValueBinding<float>(kGroup, "scaleZ", () => GetSelectedScale().z));
        }

        private void RegisterDeformationResetBindings()
        {
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "transformRequiresBake", () => m_ProceduralObjectToolSystem.TransformRequiresBake));
            AddBinding(CommandBinding("resetMeshDeformation", GuardHistory(m_ProceduralObjectToolSystem.ResetMeshDeformation)));
        }

        private void RegisterTransformModeBindings()
        {
            AddBinding(CommandBinding("setMeshTransformMode", GuardHistory<int>(m_ProceduralObjectToolSystem.SetMeshTransformMode)));

            AddBinding(CommandBinding("setMeshTransformPivot", GuardHistory<int>(m_ProceduralObjectToolSystem.SetMeshTransformPivot)));

            AddBinding(new TriggerBinding<float>(kGroup, "scaleMeshSelectionUniform", GuardHistory<float>(m_ProceduralObjectToolSystem.ScaleMeshSelectionUniform)));

            AddBinding(CommandBinding("setTranslateMode", GuardHistory(m_ProceduralObjectToolSystem.SetTranslateMode)));

            AddBinding(CommandBinding("setRotateMode", GuardHistory(m_ProceduralObjectToolSystem.SetRotateMode)));

            AddBinding(CommandBinding("setScaleMode", GuardHistory(m_ProceduralObjectToolSystem.SetScaleMode)));

            AddBinding(CommandBinding("setMirrorMode", GuardHistory(m_ProceduralObjectToolSystem.SetMirrorMode)));
            AddBinding(CommandBinding("mirrorSelected", GuardHistory<int>(m_ProceduralObjectToolSystem.MirrorSelected)));
            AddBinding(CommandBinding("setDeformMode", GuardHistory(m_ProceduralObjectToolSystem.SetDeformMode)));
            AddBinding(CommandBinding("setDeformationMoveMode", GuardHistory(m_ProceduralObjectToolSystem.SetDeformationMoveMode)));
            AddBinding(CommandBinding("setDeformationScaleMode", GuardHistory(m_ProceduralObjectToolSystem.SetDeformationScaleMode)));
            AddBinding(CommandBinding("bakeMeshDeformation", GuardHistory(m_ProceduralObjectToolSystem.BakeMeshDeformation)));
            AddBinding(CommandBinding("bakeDeformation", GuardHistory(m_ProceduralObjectToolSystem.BakeDeformation)));
            AddBinding(CommandBinding("resetDeformation", GuardHistory(m_ProceduralObjectToolSystem.ResetDeformation)));
            AddBinding(CommandBinding("editExistingDeformation", GuardHistory(m_ProceduralObjectToolSystem.EditExistingDeformation)));
            AddBinding(CommandBinding("setDeformationPivot", GuardHistory<int>(m_ProceduralObjectToolSystem.SetDeformationPivot)));
        }

        private void RegisterSnappingActionBindings()
        {
            // Snap settings change tool preferences, so array previews and drags can keep using them.
            AddBinding(CommandBinding("toggleSnapMagnet", GuardMeshView(m_ProceduralObjectToolSystem.ToggleSnapMagnet)));

            AddBinding(CommandBinding("toggleSnapIncrement", GuardMeshView(m_ProceduralObjectToolSystem.ToggleSnapIncrement)));

            AddBinding(CommandBinding("snapToTerrain", GuardHistory(m_ProceduralObjectToolSystem.SnapToTerrain)));

            AddBinding(CommandBinding("toggleSnapPOVertices", GuardMeshView(m_ProceduralObjectToolSystem.ToggleSnapPOVertices)));

            AddBinding(CommandBinding("toggleSnapPOEdges", GuardMeshView(m_ProceduralObjectToolSystem.ToggleSnapPOEdges)));

            AddBinding(CommandBinding("toggleSnapGameSurfaces", GuardMeshView(m_ProceduralObjectToolSystem.ToggleSnapGameSurfaces)));

            AddBinding(new TriggerBinding<float>(kGroup, "setMoveSnapIncrement", GuardMeshView<float>(m_ProceduralObjectToolSystem.SetMoveSnapIncrement)));

            AddBinding(new TriggerBinding<float>(kGroup, "setPrecisionMoveSnapIncrement", GuardMeshView<float>(m_ProceduralObjectToolSystem.SetPrecisionMoveSnapIncrement)));

            AddBinding(new TriggerBinding<float>(kGroup, "setRotationSnapIncrement", GuardMeshView<float>(m_ProceduralObjectToolSystem.SetRotationSnapIncrement)));

            AddBinding(new TriggerBinding<float>(kGroup, "setPrecisionRotationSnapIncrement", GuardMeshView<float>(m_ProceduralObjectToolSystem.SetPrecisionRotationSnapIncrement)));

            AddBinding(new TriggerBinding<float>(kGroup, "setScaleSnapIncrement", GuardMeshView<float>(m_ProceduralObjectToolSystem.SetScaleSnapIncrement)));

            AddBinding(new TriggerBinding<float>(kGroup, "setPrecisionScaleSnapIncrement", GuardMeshView<float>(m_ProceduralObjectToolSystem.SetPrecisionScaleSnapIncrement)));

            AddBinding(new TriggerBinding(kGroup, "resetSnapIncrements", GuardMeshView(m_ProceduralObjectToolSystem.ResetSnapIncrements)));
        }

        private void RegisterTransformActionBindings()
        {
            AddBinding(CommandBinding("setGlobalTransformOrientation", GuardHistory(m_ProceduralObjectToolSystem.SetGlobalTransformOrientation)));

            AddBinding(CommandBinding("setLocalTransformOrientation", GuardHistory(m_ProceduralObjectToolSystem.SetLocalTransformOrientation)));

            AddBinding(CommandBinding("setObjectTransformTarget", GuardHistory(m_ProceduralObjectToolSystem.SetObjectTransformTarget)));

            AddBinding(CommandBinding("setActivePartTransformTarget", GuardHistory(m_ProceduralObjectToolSystem.SetActivePartTransformTarget)));

            AddBinding(new TriggerBinding<int>(kGroup, "setCustomTransformOrientation", GuardHistory<int>(m_ProceduralObjectToolSystem.SetCustomTransformOrientation)));

            AddBinding(CommandBinding("beginAxisPicking", GuardHistory(m_ProceduralObjectToolSystem.BeginAxisPicking)));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canConfirmAxisPicking", () => m_ProceduralObjectToolSystem.CanConfirmAxisPicking));
            AddBinding(CommandBinding("confirmAxisPicking", GuardPicker(m_ProceduralObjectToolSystem.ConfirmAxisPicking)));
            AddBinding(new TriggerBinding(kGroup, "cancelAxisPicking", GuardPicker(m_ProceduralObjectToolSystem.CancelAxisPicking)));

            AddBinding(new TriggerBinding<string>(kGroup, "renameActiveCustomOrientation", GuardHistory<string>(m_ProceduralObjectToolSystem.RenameActiveCustomOrientation)));

            AddBinding(new TriggerBinding(kGroup, "deleteActiveCustomOrientation", GuardHistory(m_ProceduralObjectToolSystem.DeleteActiveCustomOrientation)));

            AddBinding(new TriggerBinding<float>(kGroup, "moveSelectedX", GuardHistory<float>(m_ProceduralObjectToolSystem.MoveSelectedX)));

            AddBinding(new TriggerBinding<float>(kGroup, "moveSelectedY", GuardHistory<float>(m_ProceduralObjectToolSystem.MoveSelectedY)));

            AddBinding(new TriggerBinding<float>(kGroup, "moveSelectedZ", GuardHistory<float>(m_ProceduralObjectToolSystem.MoveSelectedZ)));

            AddBinding(new TriggerBinding<float>(kGroup, "rotateSelectedX", GuardHistory<float>(m_ProceduralObjectToolSystem.RotateSelectedX)));

            AddBinding(new TriggerBinding<float>(kGroup, "rotateSelectedY", GuardHistory<float>(m_ProceduralObjectToolSystem.RotateSelectedY)));

            AddBinding(new TriggerBinding<float>(kGroup, "rotateSelectedZ", GuardHistory<float>(m_ProceduralObjectToolSystem.RotateSelectedZ)));

            AddBinding(new TriggerBinding<float>(kGroup, "setScaleX", GuardHistory<float>(m_ProceduralObjectToolSystem.SetSelectedScaleX)));

            AddBinding(new TriggerBinding<float>(kGroup, "setScaleY", GuardHistory<float>(m_ProceduralObjectToolSystem.SetSelectedScaleY)));

            AddBinding(new TriggerBinding<float>(kGroup, "setScaleZ", GuardHistory<float>(m_ProceduralObjectToolSystem.SetSelectedScaleZ)));
        }

        private NamedIndex[] GetSelectedCustomOrientations()
        {
            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;
            if (selected == null)
            {
                m_CustomOrientationCacheObject = null;
                m_CustomOrientations = Array.Empty<NamedIndex>();
                return m_CustomOrientations;
            }

            if (ReferenceEquals(m_CustomOrientationCacheObject, selected) && m_CustomOrientationCacheVersion == selected.CustomOrientationVersion)
            {
                return m_CustomOrientations;
            }

            var entries = new NamedIndex[selected.CustomOrientations.Count];
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = new NamedIndex(i, selected.CustomOrientations[i].Name);
            }

            m_CustomOrientationCacheObject = selected;
            m_CustomOrientationCacheVersion = selected.CustomOrientationVersion;
            m_CustomOrientations = entries;
            return m_CustomOrientations;
        }

        private Vector3 GetSelectedScale()
        {
            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;
            return selected != null ? selected.Transform.Scale : Vector3.one;
        }
    }
}