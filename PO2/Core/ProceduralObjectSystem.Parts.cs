using System;
using System.Collections.Generic;
using Game;
using Game.Tools;
using UnityEngine;
using PO2.Core.Primitives;
using PO2.Geometry;
using PO2.Geometry.Transforms;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Tools.ObjectTool;

namespace PO2.Core
{
    public partial class ProceduralObjectSystem
    {
        public bool CanRenameSelectedPart =>
            CanRenameSelectedPartInternal(out ProceduralObject _);

        public bool CanDuplicateSelectedPart =>
            CanManageSelectedPart(out ProceduralObject selected) && selected.Parts.Count < ProceduralObject.kMaxPartCount;

        public bool CanAddPrimitivePart =>
            CanManageSelectedPart(out ProceduralObject selected) && selected.Parts.Count < ProceduralObject.kMaxPartCount;

        public bool CanCombineSelected
        {
            get
            {
                if (m_ProceduralObjectToolSystem != null && m_ProceduralObjectToolSystem.IsCombineSourceActive)
                {
                    return true;
                }

                if (!CanManageSelectedPart(out ProceduralObject selected) || selected.Parts.Count >= ProceduralObject.kMaxPartCount || m_Objects.Count <= 1)
                {
                    return false;
                }

                return true;
            }
        }

        public bool CanDeleteSelectedPart =>
            CanManageSelectedPart(out ProceduralObject selected) && selected.Parts.Count > 1;

        public bool CanExtractSelectedPart =>
            CanDeleteSelectedPart;

        internal bool TryAddPrimitivePart(int parentObjectId, ProceduralObject expectedParent, PrimitiveParameters parameters, Matrix4x4 parentFromPrimitive, out int addedPartIndex, out MeshPart addedPart, out string failureReason)
        {
            addedPartIndex = -1;
            addedPart = null;
            failureReason = string.Empty;

            if (expectedParent == null || !m_Objects.TryGetValue(parentObjectId, out ProceduralObject parent) || !ReferenceEquals(parent, expectedParent))
            {
                failureReason = "The selected parent PO is no longer available.";
                return false;
            }

            if (m_SelectedObjectId != parentObjectId)
            {
                failureReason = "The selected parent PO changed during placement.";
                return false;
            }

            if (parent.IsLocked)
            {
                failureReason = "The selected parent PO is locked.";
                return false;
            }

            if (!parent.CanEditGeometry(-1, out LocalizedText deformationReason))
            {
                failureReason = deformationReason.ToString();
                return false;
            }

            if (parent.Parts.Count >= ProceduralObject.kMaxPartCount)
            {
                failureReason = $"The selected parent PO already contains the maximum " + $"{ProceduralObject.kMaxPartCount} parts.";
                return false;
            }

            MeshPart primitivePart = PrimitiveMeshFactory.CreatePrimitivePart(parameters);

            if (primitivePart == null)
            {
                failureReason = "The primitive parameters could not produce a mesh part.";
                return false;
            }

            string partName = parent.GetUniquePartName(primitivePart.Name);

            if (!PartGeometryTransform.TryCloneTransformed(primitivePart, parentFromPrimitive, partName, out MeshPart transformedPart, out failureReason))
            {
                return false;
            }

            using (var history = BeginLifetimeHistory(LocalizedText.For("PO2.History.Action.Part.AddPrimitive"), new LifetimeAction(parent, transformedPart, parent.Parts.Count, false, LocalizedText.For("PO2.History.Action.Part.AddPrimitive"))))
            {
                if (history == null)
                {
                    failureReason = "Action history cannot reserve this edit.";
                    return false;
                }

                if (!parent.TryAddPart(transformedPart, out addedPartIndex, out failureReason))
                {
                    return false;
                }

                addedPart = transformedPart;
                ApplySelectedPart(parent, addedPartIndex);
                if (history.Complete()) return true;
                addedPart = null;
                addedPartIndex = -1;
                failureReason = "The edit could not be retained in action history and was rolled back.";
                return false;
            }
        }

        internal bool TryCombineObjects(int parentObjectId, ProceduralObject expectedParent, int sourceObjectId, ProceduralObject expectedSource, out int firstPartIndex, out int addedPartCount, out string failureReason)
        {
            firstPartIndex = -1;
            addedPartCount = 0;
            failureReason = string.Empty;

            if (expectedParent == null || !m_Objects.TryGetValue(parentObjectId, out ProceduralObject parent) || !ReferenceEquals(parent, expectedParent))
            {
                failureReason = "The combine parent is no longer available.";
                return false;
            }

            if (expectedSource == null || !m_Objects.TryGetValue(sourceObjectId, out ProceduralObject source) || !ReferenceEquals(source, expectedSource))
            {
                failureReason = "The combine source is no longer available.";
                return false;
            }

            if (ReferenceEquals(parent, source) || parentObjectId == sourceObjectId)
            {
                failureReason = "A PO cannot be combined into itself.";
                return false;
            }

            if (m_SelectedObjectId != parentObjectId)
            {
                failureReason = "The selected combine parent changed.";
                return false;
            }

            if (parent.IsLocked)
            {
                failureReason = "The combine parent is locked.";
                return false;
            }

            if (source.IsLocked)
            {
                failureReason = "The combine source is locked.";
                return false;
            }

            if (!parent.CanEditGeometry(-1, out LocalizedText deformationReason) || !source.CanEditGeometry(-1, out deformationReason))
            {
                failureReason = deformationReason.ToString();
                return false;
            }

            if (source.Parts.Count > ProceduralObject.kMaxPartCount - parent.Parts.Count)
            {
                failureReason = $"Combining these objects would exceed the maximum " + $"{ProceduralObject.kMaxPartCount} parts.";
                return false;
            }

            ObjectTransform parentTransform = parent.Transform;
            ObjectTransform sourceTransform = source.Transform;
            Matrix4x4 parentLocalToWorld = Matrix4x4.TRS(parentTransform.Position, parentTransform.Rotation, parentTransform.Scale);
            Matrix4x4 sourceLocalToWorld = Matrix4x4.TRS(sourceTransform.Position, sourceTransform.Rotation, sourceTransform.Scale);
            Matrix4x4 parentFromSource = parentLocalToWorld.inverse * sourceLocalToWorld;

            HashSet<string> reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < parent.Parts.Count; i++)
            {
                reservedNames.Add(parent.Parts[i].Name);
            }

            List<MeshPart> preparedParts = new List<MeshPart>(source.Parts.Count);

            for (int i = 0; i < source.Parts.Count; i++)
            {
                MeshPart sourcePart = source.Parts[i];
                string finalName = ProceduralObject.GetUniquePartName(sourcePart.Name, reservedNames);
                reservedNames.Add(finalName);

                if (!PartGeometryTransform.TryCloneTransformed(sourcePart, parentFromSource, finalName, out MeshPart preparedPart, out string transformFailureReason))
                {
                    failureReason = $"Source part {i} could not be prepared: " + transformFailureReason;
                    return false;
                }

                preparedParts.Add(preparedPart);
            }

            // Recheck identities and locks at the commit edge.
            // Everything that's expensive above was done against detached clones.
            if (!m_Objects.TryGetValue(parentObjectId, out ProceduralObject liveParent) || !ReferenceEquals(liveParent, parent) || !m_Objects.TryGetValue(sourceObjectId, out ProceduralObject liveSource) || !ReferenceEquals(liveSource, source) || m_SelectedObjectId != parentObjectId || parent.IsLocked || source.IsLocked || source.Parts.Count != preparedParts.Count || preparedParts.Count > ProceduralObject.kMaxPartCount - parent.Parts.Count)
            {
                failureReason = "The combine objects changed while the operation was being prepared.";
                return false;
            }

            var lifetimeActions = new List<HistoryCaptureAction>();
            for (int i = 0; i < preparedParts.Count; i++) lifetimeActions.Add(new LifetimeAction(parent, preparedParts[i], parent.Parts.Count + i, false, LocalizedText.For("PO2.History.Action.Object.Combine")));
            lifetimeActions.Add(new LifetimeAction(source, true, LocalizedText.For("PO2.History.Action.Object.Combine")));

            using (var history = BeginLifetimeHistory(LocalizedText.For("PO2.History.Action.Object.Combine"), lifetimeActions.ToArray()))
            {
                if (history == null)
                {
                    failureReason = "Action history cannot reserve this edit.";
                    return false;
                }

                try
                {
                    if (!parent.TryAddParts(preparedParts, out firstPartIndex, out failureReason))
                    {
                        return false;
                    }
                }
                catch (Exception exception)
                {
                    if (firstPartIndex >= 0)
                    {
                        parent.TryRollbackAddedParts(firstPartIndex, preparedParts);
                    }

                    firstPartIndex = -1;
                    failureReason = $"The parent PO could not commit the prepared parts: " + exception.Message;
                    return false;
                }

                try
                {
                    if (m_HoveredObjectId == sourceObjectId)
                    {
                        ClearHoveredObject();
                    }

                    m_Renderer.Destroy(sourceObjectId);
                    if (!m_Objects.Remove(sourceObjectId))
                    {
                        throw new InvalidOperationException("The source PO disappeared during the combine commit.");
                    }

                    UntrackObjectName(source);
                    NotifyLodObjectRemoved(sourceObjectId);
                }
                catch (Exception exception)
                {
                    bool rolledBack = parent.TryRollbackAddedParts(firstPartIndex, preparedParts);
                    source.MarkDirty(ProceduralObjectDirtyFlags.All);
                    firstPartIndex = -1;

                    failureReason = rolledBack ? $"The source PO could not be removed: {exception.Message}" : $"The source PO could not be removed and the parent " + $"rollback failed: {exception.Message}";
                    return false;
                }

                addedPartCount = preparedParts.Count;
                ApplySelectedPart(parent, firstPartIndex);
                if (history.Complete()) return true;
                firstPartIndex = -1;
                addedPartCount = 0;
                failureReason = "The edit could not be retained in action history and was rolled back.";
                return false;
            }
        }

        public void RenameSelectedPart(string name)
        {
            if (!TryGetRenameableSelectedPart("rename a mesh part", out ProceduralObject selected, out MeshPart _, out int partIndex))
            {
                return;
            }

            string requestedName = name == null ? string.Empty : name.Trim();
            if (string.IsNullOrEmpty(requestedName))
            {
                Mod.log.Warn("[PO Part] Part name cannot be blank.");
                return;
            }

            using (var history = BeginLifetimeHistory(LocalizedText.For("PO2.History.Action.Part.Rename"), new PartNameAction(selected, partIndex)))
            {
                if (history == null) return;
                PartRenameOutcome outcome = selected.RenamePart(partIndex, requestedName, out string finalName, out string failureReason);
                if (outcome != PartRenameOutcome.Changed)
                {
                    if (outcome == PartRenameOutcome.Failed)
                    {
                        Mod.log.Warn($"[PO Part] Could not rename part {partIndex}: " + failureReason);
                    }

                    return;
                }

                if (!history.Complete()) return;
            }
        }

        public void DuplicateSelectedPart()
        {
            if (!TryGetManageableSelectedPart("duplicate a mesh part", out ProceduralObject selected, out MeshPart sourcePart, out int sourcePartIndex))
            {
                return;
            }

            if (selected.Parts.Count >= ProceduralObject.kMaxPartCount)
            {
                Mod.log.Warn($"[PO Part] Object {selected.Id} already contains the " + $"maximum {ProceduralObject.kMaxPartCount} parts.");
                return;
            }

            float offset = 1f;
            if (PartGeometryTransform.TryGetLogicalBounds(sourcePart, out Bounds bounds))
            {
                offset = Mathf.Max(Mathf.Abs(bounds.size.x), 1f);
            }

            string duplicateName = selected.GetUniquePartName($"{sourcePart.Name} Copy");

            if (!PartGeometryTransform.TryCloneTransformed(sourcePart, Matrix4x4.Translate(Vector3.right * offset), duplicateName, out MeshPart duplicate, out string transformFailureReason))
            {
                Mod.log.Error($"[PO Part] Failed to clone object {selected.Id} part " + $"{sourcePartIndex}: {transformFailureReason}");
                return;
            }

            using (var history = BeginLifetimeHistory(LocalizedText.For("PO2.History.Action.Part.Duplicate"), new LifetimeAction(selected, duplicate, selected.Parts.Count, false, LocalizedText.For("PO2.History.Action.Part.Duplicate"))))
            {
                if (history == null) return;
                if (!selected.TryAddPart(duplicate, out int addedPartIndex, out string failureReason))
                {
                    Mod.log.Warn($"[PO Part] Could not duplicate part " + $"{sourcePartIndex}: {failureReason}");
                    return;
                }

                ApplySelectedPart(selected, addedPartIndex);
                if (!history.Complete()) return;
            }
        }

        public void DeleteSelectedPart()
        {
            if (!TryGetManageableSelectedPart("delete a mesh part", out ProceduralObject selected, out MeshPart _, out int partIndex))
            {
                return;
            }

            using (var history = BeginLifetimeHistory(LocalizedText.For("PO2.History.Action.Part.Delete"), new LifetimeAction(selected, selected.Parts[partIndex], partIndex, true, LocalizedText.For("PO2.History.Action.Part.Delete"))))
            {
                if (history == null) return;
                if (!selected.TryRemovePart(partIndex, out MeshPart removedPart, out string failureReason))
                {
                    Mod.log.Warn($"[PO Part] Could not delete part {partIndex}: " + failureReason);
                    return;
                }

                int nextPartIndex = Mathf.Clamp(partIndex, 0, selected.Parts.Count - 1);
                ApplySelectedPart(selected, nextPartIndex);

                if (!history.Complete()) return;
            }
        }

        public void ExtractSelectedPart()
        {
            if (!TryGetManageableSelectedPart("extract a mesh part", out ProceduralObject selected, out MeshPart sourcePart, out int sourcePartIndex))
            {
                return;
            }

            if (selected.Parts.Count <= 1)
            {
                Mod.log.Warn("[PO Part] A PO must retain at least one mesh part.");
                return;
            }

            ProceduralObject extracted;
            try
            {
                // Start the new PO at the part's pivot, keeping the mesh in place.
                Vector3 localOrigin = sourcePart.LocalPivot;
                ObjectTransform extractedTransform = selected.Transform;
                extractedTransform.Position = selected.Transform.TransformPoint(localOrigin);
                MeshPart extractedPart = sourcePart.Clone();
                Vector3[] vertices = extractedPart.Mesh.Vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] -= localOrigin;
                }

                extractedPart.SetLocalPivot(Vector3.zero);
                extractedPart.MarkPositionsChanged();

                extracted = new ProceduralObject(m_NextId, Guid.NewGuid(), GetUniqueObjectName(sourcePart.Name), extractedTransform, new[] { extractedPart }, selected.CustomOrientations, localPivot: Vector3.zero);
                extracted.SetReceivesDecals(selected.ReceivesDecals);
                extracted.SetLodProfile(selected.LodProfile);
            }
            catch (System.Exception exception)
            {
                Mod.log.Error($"[PO Part] Failed to prepare extracted part " + $"{sourcePartIndex}: {exception}");
                return;
            }

            using (var history = BeginLifetimeHistory(LocalizedText.For("PO2.History.Action.Part.Extract"), new LifetimeAction(extracted, false, LocalizedText.For("PO2.History.Action.Part.Extract")), new LifetimeAction(selected, sourcePart, sourcePartIndex, true, LocalizedText.For("PO2.History.Action.Part.Extract"))))
            {
                if (history == null) return;
                string removalFailureReason = string.Empty;
                bool registered = TryRegisterNewObject(extracted, true, () => selected.TryRemovePart(sourcePartIndex, out MeshPart _, out removalFailureReason), out string registrationFailureReason);

                if (!registered)
                {
                    string failureReason = string.IsNullOrEmpty(removalFailureReason) ? registrationFailureReason : removalFailureReason;
                    Mod.log.Warn($"[PO Part] Could not extract part " + $"{sourcePartIndex}: {failureReason}");
                    return;
                }

                if (!history.Complete()) return;
            }
        }

        private bool CanRenameSelectedPartInternal(out ProceduralObject selected)
        {
            return TryGetSelected(out selected) && selected.Parts.Count > 0 && m_ProceduralObjectToolSystem?.ArrayActive != true && !ArrayEditBusy && !selected.IsLocked && (m_ProceduralObjectToolSystem == null || (m_ProceduralObjectToolSystem.Mode != ObjectToolMode.CombineSource && !m_ProceduralObjectToolSystem.IsGizmoDragging));
        }

        private bool CanManageSelectedPart(out ProceduralObject selected)
        {
            return TryGetSelected(out selected) && selected.Parts.Count > 0 && !selected.IsLocked && !selected.HasDeformation && (m_ProceduralObjectToolSystem == null || (m_ProceduralObjectToolSystem.Mode != ObjectToolMode.MeshEdit && m_ProceduralObjectToolSystem.Mode != ObjectToolMode.CombineSource && !m_ProceduralObjectToolSystem.IsGizmoDragging));
        }

        private bool TryGetRenameableSelectedPart(string operation, out ProceduralObject selected, out MeshPart part, out int partIndex)
        {
            if (!TryGetSelectedPart(out selected, out part, out partIndex))
            {
                return false;
            }

            if (selected.IsLocked)
            {
                return false;
            }

            if (m_ProceduralObjectToolSystem != null && m_ProceduralObjectToolSystem.Mode == ObjectToolMode.CombineSource)
            {
                return false;
            }

            if (m_ProceduralObjectToolSystem != null && m_ProceduralObjectToolSystem.IsGizmoDragging)
            {
                return false;
            }

            return true;
        }

        private bool TryGetManageableSelectedPart(string operation, out ProceduralObject selected, out MeshPart part, out int partIndex)
        {
            if (!TryGetSelectedPart(out selected, out part, out partIndex))
            {
                return false;
            }

            if (!selected.CanEditGeometry(-1, out LocalizedText reason))
            {
                Mod.log.Warn(reason.ToString());
                return false;
            }

            if (selected.IsLocked)
            {
                return false;
            }

            if (m_ProceduralObjectToolSystem != null && (m_ProceduralObjectToolSystem.Mode == ObjectToolMode.MeshEdit || m_ProceduralObjectToolSystem.Mode == ObjectToolMode.CombineSource))
            {
                return false;
            }

            if (m_ProceduralObjectToolSystem != null && m_ProceduralObjectToolSystem.IsGizmoDragging)
            {
                return false;
            }

            return true;
        }
    }
}