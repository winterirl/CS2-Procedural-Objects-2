using System;
using System.Collections.Generic;
using Colossal.UI.Binding;
using PO2.Core;
using PO2.Interface.Models;

namespace PO2.Interface
{
    public partial class ProceduralObjectUiSystem
    {
        private ObjectListEntry[] m_ObjectListEntries = Array.Empty<ObjectListEntry>();

        private ulong m_ObjectListVersion = ulong.MaxValue;

        private ProceduralObject m_SelectedPartsObject;

        private uint m_SelectedPartsVersion;

        private NamedIndex[] m_SelectedParts = Array.Empty<NamedIndex>();

        private void RegisterObjectStateBindings()
        {
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "showObjectMarkers", () => m_ProceduralObjectToolSystem.ShowObjectMarkers));

            AddUpdateBinding(new GetterValueBinding<ObjectListEntry[]>(kGroup, "objectListEntries", GetObjectListEntries, new ArrayWriter<ObjectListEntry>(new ValueWriter<ObjectListEntry>())));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "selectedObjectId", () => m_ProceduralObjectSystem.SelectedObjectId ?? -1));

            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "selectedObjectName", () => m_ProceduralObjectSystem.SelectedObjectName));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "selectedObjectLocked", () => m_ProceduralObjectSystem.SelectedObjectLocked));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "selectedObjectReceivesDecals", () => m_ProceduralObjectSystem.SelectedObjectReceivesDecals));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canDuplicateSelected", () => m_ProceduralObjectSystem.CanDuplicateSelected));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canRenameSelectedPart", () => m_ProceduralObjectSystem.CanRenameSelectedPart));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canDuplicateSelectedPart", () => m_ProceduralObjectSystem.CanDuplicateSelectedPart));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canAddPrimitivePart", () => m_ProceduralObjectSystem.CanAddPrimitivePart));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canCombineSelected", () => m_ProceduralObjectSystem.CanCombineSelected));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canDeleteSelectedPart", () => m_ProceduralObjectSystem.CanDeleteSelectedPart));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "canExtractSelectedPart", () => m_ProceduralObjectSystem.CanExtractSelectedPart));

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "selectedObjectHasOriginalAppearance", () => m_ProceduralObjectSystem.SelectedObjectHasOriginalAppearance));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "selectedPartIndex", () => m_ProceduralObjectSystem.SelectedPartIndex));

            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "selectedPartCount", () => m_ProceduralObjectSystem.SelectedPartCount));

            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "selectedPartName", () => m_ProceduralObjectSystem.SelectedPartName));

            AddUpdateBinding(new GetterValueBinding<NamedIndex[]>(kGroup, "selectedParts", GetSelectedParts, new ArrayWriter<NamedIndex>(new ValueWriter<NamedIndex>())));
        }

        private void RegisterObjectCreationBindings()
        {
            AddBinding(CommandBinding("addPrimitivePart", GuardHistory(m_ProceduralObjectToolSystem.BeginPrimitivePartPlacement)));

            AddBinding(CommandBinding("combineSelected", GuardHistory(m_ProceduralObjectToolSystem.BeginCombineSourceTargeting)));

            AddBinding(CommandBinding("toggleObjectMarkers", GuardHistory(m_ProceduralObjectToolSystem.ToggleObjectMarkers)));
        }

        private void RegisterObjectListSelectionBinding()
        {
            AddBinding(new TriggerBinding<int>(kGroup, "selectObjectFromList", GuardMeshView<int>(m_ProceduralObjectToolSystem.SelectObjectFromList)));
        }

        private void RegisterDeselectBinding()
        {
            AddBinding(new TriggerBinding(kGroup, "deselect", GuardNavigation(() => NavigateWorkspace(m_ProceduralObjectToolSystem.NavigateToSelectWorkspace))));
        }

        private void RegisterPartSelectionBindings()
        {
            AddBinding(CommandBinding("previousSelectedPart", GuardMeshView(m_ProceduralObjectSystem.PreviousSelectedPart)));

            AddBinding(CommandBinding("nextSelectedPart", GuardMeshView(m_ProceduralObjectSystem.NextSelectedPart)));

            AddBinding(new TriggerBinding<int>(kGroup, "setSelectedPart", GuardMeshView<int>(partIndex => m_ProceduralObjectSystem.SetSelectedPart(partIndex))));
        }

        private void RegisterObjectActionBindings()
        {
            AddBinding(CommandBinding("duplicateSelected", GuardHistory(m_ProceduralObjectSystem.DuplicateSelected)));

            AddBinding(new TriggerBinding<string>(kGroup, "renameSelected", GuardHistory<string>(m_ProceduralObjectSystem.RenameSelected)));

            AddBinding(new TriggerBinding<string>(kGroup, "renameSelectedPart", GuardHistory<string>(m_ProceduralObjectSystem.RenameSelectedPart)));

            AddBinding(CommandBinding("duplicateSelectedPart", GuardHistory(m_ProceduralObjectSystem.DuplicateSelectedPart)));

            AddBinding(CommandBinding("deleteSelectedPart", GuardHistory(m_ProceduralObjectSystem.DeleteSelectedPart)));

            AddBinding(CommandBinding("extractSelectedPart", GuardHistory(m_ProceduralObjectSystem.ExtractSelectedPart)));

            AddBinding(CommandBinding("toggleSelectedLocked", GuardHistory(m_ProceduralObjectSystem.ToggleSelectedLocked)));

            AddBinding(CommandBinding("toggleSelectedReceivesDecals", GuardHistory(m_ProceduralObjectSystem.ToggleSelectedReceivesDecals)));
        }

        private void RegisterDeleteBinding()
        {
            AddBinding(CommandBinding("deleteSelected", GuardHistory(m_ProceduralObjectSystem.DeleteSelected)));
        }

        private void OpenObjectListView()
        {
            if (m_MaterialImportBusy)
            {
                return;
            }

            // Object names can change while this view is closed.
            // Rebuild only when opening it so the normal UI update path stays allocation-free.
            RebuildObjectListEntries();
            NavigateWorkspace(m_ProceduralObjectToolSystem.NavigateToObjectListWorkspace);
        }

        private ObjectListEntry[] GetObjectListEntries()
        {
            if (m_ObjectListVersion != m_ProceduralObjectSystem.ObjectListVersion)
            {
                RebuildObjectListEntries();
            }

            return m_ObjectListEntries;
        }

        private void RebuildObjectListEntries()
        {
            var objects = new List<ProceduralObject>(m_ProceduralObjectSystem.Objects);
            objects.Sort(CompareObjectListEntries);
            var entries = new ObjectListEntry[objects.Count];
            for (int i = 0; i < objects.Count; i++)
            {
                entries[i] = new ObjectListEntry(objects[i].Id, objects[i].Name);
            }

            m_ObjectListEntries = entries;
            m_ObjectListVersion = m_ProceduralObjectSystem.ObjectListVersion;
        }

        private static int CompareObjectListEntries(ProceduralObject left, ProceduralObject right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int nameComparison = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return nameComparison != 0 ? nameComparison : left.Id.CompareTo(right.Id);
        }

        private NamedIndex[] GetSelectedParts()
        {
            ProceduralObject selected = m_ProceduralObjectSystem.SelectedObject;
            if (selected == null)
            {
                m_SelectedPartsObject = null;
                m_SelectedParts = Array.Empty<NamedIndex>();
                return m_SelectedParts;
            }

            if (ReferenceEquals(m_SelectedPartsObject, selected) && m_SelectedPartsVersion == selected.PartVersion)
            {
                return m_SelectedParts;
            }

            var entries = new NamedIndex[selected.Parts.Count];
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = new NamedIndex(i, selected.Parts[i].Name);
            }

            m_SelectedPartsObject = selected;
            m_SelectedPartsVersion = selected.PartVersion;
            m_SelectedParts = entries;
            return m_SelectedParts;
        }
    }
}