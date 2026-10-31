using System;
using System.Collections.Generic;
using Game;
using Game.Tools;
using UnityEngine;
using PO2.Conversion;
using PO2.Core.Primitives;
using PO2.Geometry;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Tools.ObjectTool;

namespace PO2.Core
{
    public partial class ProceduralObjectSystem
    {
        private int m_NextId = 1;

        public ProceduralObject CreatePrimitive(PrimitiveKind primitiveType, Vector3 position)
        {
            if (!PrimitiveDefinitions.TryGet(primitiveType, out PrimitiveDefinition definition))
            {
                Mod.log.Warn($"[PO] Unsupported primitive type {primitiveType}.");
                return null;
            }

            return CreatePrimitive(definition.DefaultParameters, position);
        }

        public ProceduralObject CreatePrimitive(PrimitiveParameters parameters, Vector3 position)
        {
            if (parameters == null)
            {
                Mod.log.Warn("[PO] Cannot create a primitive with null parameters.");
                return null;
            }

            PrimitiveKind primitiveType = parameters.PrimitiveType;

            if (!PrimitiveDefinitions.TryGet(primitiveType, out PrimitiveDefinition definition))
            {
                Mod.log.Warn($"[PO] Unsupported primitive type {primitiveType}.");
                return null;
            }

            MeshPart part = PrimitiveMeshFactory.CreatePrimitivePart(parameters);

            if (part == null)
            {
                Mod.log.Warn($"[PO] No part factory exists for primitive parameters " + $"of type {primitiveType}.");
                return null;
            }

            int id = m_NextId;

            ProceduralObject proceduralObject = new ProceduralObject(id, GetUniqueObjectName(definition.DisplayName), ObjectTransform.IdentityAt(position), new[] { part });

            using (var history = BeginLifetimeHistory(LocalizedText.For("PO2.History.Action.Object.Create"), new LifetimeAction(proceduralObject, false, LocalizedText.For("PO2.History.Action.Object.Create"))))
            {
                if (history == null) return null;
                if (!TryRegisterNewObject(proceduralObject, true, out string failureReason))
                {
                    Mod.log.Error($"[PO] Failed to create runtime renderer for object {id}: " + failureReason);

                    return null;
                }

                if (!history.Complete()) return null;

                return proceduralObject;
            }
        }

        internal ProceduralObject CreateConvertedObject(AssetConversionResult conversion, Func<bool> commitSourceReplacement)
        {
            if (conversion == null || conversion.Parts == null || conversion.Parts.Length == 0)
            {
                Mod.log.Warn("[PO Conversion] Cannot create a converted object from null conversion data.");
                return null;
            }

            if (commitSourceReplacement == null)
            {
                Mod.log.Warn("[PO Conversion] Cannot create a converted object without " + "a source-replacement transaction.");
                return null;
            }

            ConversionProfile conversionProfile = conversion.Profile;
            int id = m_NextId;
            string objectName = GetUniqueObjectName(conversion.RequestedName);
            Bounds? initialLocalBounds = conversion.LocalBounds.HasBounds ? conversion.LocalBounds.ToBounds() : (Bounds?)null;
            ProceduralObject proceduralObject = new ProceduralObject(id, Guid.NewGuid(), objectName, conversion.Transform, conversion.Parts, null, initialLocalBounds);

            if (!TryRegisterNewObject(proceduralObject, true, commitSourceReplacement, conversion.RenderPreparation, out string failureReason))
            {
                Mod.log.Error($"[PO Conversion] Failed to create and commit converted " + $"object {id} '{objectName}': {failureReason}");
                conversionProfile?.LogCompleted(false, failureReason);
                return null;
            }

            conversionProfile?.LogCompleted(true);

            return proceduralObject;
        }

        private string GetUniqueDuplicateName(string sourceName)
        {
            string baseName = GetObjectNameFamilyBase(sourceName);
            return GetFirstAvailableSuffixedName(baseName, null);
        }

        private string GetUniqueObjectName(string requestedName, int? excludeObjectId = null)
        {
            string normalized = requestedName == null ? string.Empty : requestedName.Trim();

            if (string.IsNullOrEmpty(normalized))
            {
                normalized = "Procedural Object";
            }

            if (!IsObjectNameInUse(normalized, excludeObjectId))
            {
                return normalized;
            }

            string baseName = GetObjectNameFamilyBase(normalized);
            return GetFirstAvailableSuffixedName(baseName, excludeObjectId);
        }

        private string GetFirstAvailableSuffixedName(string baseName, int? excludeObjectId)
        {
            int suffix = 1;
            while (true)
            {
                string candidate = $"{baseName} ({suffix})";
                if (!IsObjectNameInUse(candidate, excludeObjectId))
                {
                    return candidate;
                }

                suffix++;
            }
        }

        private bool IsObjectNameInUse(string candidate, int? excludeObjectId)
        {
            foreach (KeyValuePair<int, ProceduralObject> entry in m_Objects)
            {
                if (excludeObjectId.HasValue && entry.Key == excludeObjectId.Value)
                {
                    continue;
                }

                if (string.Equals(entry.Value.Name, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetObjectNameFamilyBase(string name)
        {
            string normalized = name == null ? string.Empty : name.Trim();

            if (string.IsNullOrEmpty(normalized) || normalized[normalized.Length - 1] != ')')
            {
                return string.IsNullOrEmpty(normalized) ? "Procedural Object" : normalized;
            }

            int openParenIndex = normalized.LastIndexOf(" (");
            if (openParenIndex <= 0 || openParenIndex + 2 >= normalized.Length - 1)
            {
                return normalized;
            }

            long suffix = 0;
            for (int i = openParenIndex + 2; i < normalized.Length - 1; i++)
            {
                char character = normalized[i];
                if (character < '0' || character > '9')
                {
                    return normalized;
                }

                suffix = suffix * 10 + (character - '0');
                if (suffix > 100000000L)
                {
                    return normalized;
                }
            }

            if (suffix <= 0)
            {
                return normalized;
            }

            return normalized.Substring(0, openParenIndex);
        }

        public void DuplicateSelected()
        {
            if (m_ProceduralObjectToolSystem != null && (m_ProceduralObjectToolSystem.Mode == ObjectToolMode.MeshEdit || m_ProceduralObjectToolSystem.Mode == ObjectToolMode.Texturing || m_ProceduralObjectToolSystem.Mode == ObjectToolMode.CombineSource))
            {
                return;
            }

            if (!TryGetSelected(out ProceduralObject selected))
            {
                return;
            }

            if (selected.IsLocked)
            {
                return;
            }

            float offsetDistance = 1f;
            if (selected.TryGetLocalBounds(out Bounds localBounds))
            {
                offsetDistance = Mathf.Abs(localBounds.size.x * selected.Transform.Scale.x);

                if (offsetDistance < 0.001f)
                {
                    offsetDistance = 1f;
                }
            }

            Vector3 localXDirection = selected.Transform.TransformVector(Vector3.right);
            if (localXDirection.sqrMagnitude < 0.000001f)
            {
                localXDirection = selected.Transform.Rotation * Vector3.right;
            }

            Vector3 duplicatePosition = selected.Transform.Position + localXDirection.normalized * offsetDistance;

            int id = m_NextId;
            string duplicateName = GetUniqueDuplicateName(selected.Name);
            ProceduralObject duplicate = selected.Clone(id, duplicateName, duplicatePosition);

            using (var history = BeginLifetimeHistory(LocalizedText.For("PO2.History.Action.Object.Duplicate"), new LifetimeAction(duplicate, false, LocalizedText.For("PO2.History.Action.Object.Duplicate"))))
            {
                if (history == null) return;
                if (!TryRegisterNewObject(duplicate, true, out string failureReason))
                {
                    Mod.log.Error($"[PO] Failed to create runtime renderer for duplicate object {id}: " + failureReason);
                    return;
                }

                if (!history.Complete()) return;
            }
        }

        private bool TryRegisterNewObject(ProceduralObject candidate, bool select, out string failureReason)
        {
            return TryRegisterNewObject(candidate, select, null, null, out failureReason);
        }

        private bool TryRegisterNewObject(ProceduralObject candidate, bool select, Func<bool> commitAfterRegistration, out string failureReason)
        {
            return TryRegisterNewObject(candidate, select, commitAfterRegistration, null, out failureReason);
        }

        private bool TryRegisterNewObject(ProceduralObject candidate, bool select, Func<bool> commitAfterRegistration, ConvertedRenderPreparation renderPreparation, out string failureReason, PO2.Core.Operations.IHistoryObjectRenderer historyRenderer = null)
        {
            failureReason = string.Empty;
            if (candidate == null)
            {
                failureReason = "The new PO is null.";
                return false;
            }

            if (candidate.Id <= 0 || m_Objects.ContainsKey(candidate.Id))
            {
                failureReason = $"Runtime object ID {candidate.Id} is already in use.";
                return false;
            }

            try
            {
                // A source stays untouched until both the candidate PO data and its runtime renderer exist successfully.
                if (historyRenderer != null)
                {
                    if (!ReferenceEquals(historyRenderer.Object, candidate)) throw new InvalidOperationException("The prepared renderer belongs to a different object.");
                    historyRenderer.Publish();
                }
                else m_Renderer.Create(candidate, renderPreparation);
            }
            catch (System.Exception exception)
            {
                failureReason = exception.ToString();
                return false;
            }

            try
            {
                m_Objects.Add(candidate.Id, candidate);
                TrackObjectName(candidate);
            }
            catch (System.Exception exception)
            {
                string rollbackFailure = RollbackNewObjectCandidate(candidate, removeRegistration: false);
                failureReason = $"The new PO could not be registered: {exception}" + rollbackFailure;
                return false;
            }

            if (commitAfterRegistration != null)
            {
                bool committed;
                try
                {
                    committed = commitAfterRegistration();
                }
                catch (System.Exception exception)
                {
                    string rollbackFailure = RollbackNewObjectCandidate(candidate, removeRegistration: true);
                    failureReason = exception.ToString() + rollbackFailure;
                    return false;
                }

                if (!committed)
                {
                    string rollbackFailure = RollbackNewObjectCandidate(candidate, removeRegistration: true);
                    failureReason = "The source object could not commit the operation." + rollbackFailure;
                    return false;
                }
            }

            m_NextId = Math.Max(m_NextId, candidate.Id + 1);

            if (select)
            {
                Select(candidate.Id);
            }

            return true;
        }

        private string RollbackNewObjectCandidate(ProceduralObject candidate, bool removeRegistration)
        {
            if (removeRegistration)
            {
                if (m_Objects.Remove(candidate.Id)) UntrackObjectName(candidate);
                NotifyLodObjectRemoved(candidate.Id);
            }

            try
            {
                m_Renderer.Destroy(candidate.Id);
                return string.Empty;
            }
            catch (System.Exception exception)
            {
                Mod.log.Error($"[PO] Runtime renderer rollback failed for object " + $"{candidate.Id}: {exception}");
                return $" Runtime renderer rollback also failed: " + $"{exception.Message}";
            }
        }
    }
}