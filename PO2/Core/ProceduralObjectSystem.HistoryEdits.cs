using System;
using System.Collections.Generic;
using PO2.Core.Operations;
using PO2.Geometry.Editing;
using PO2.History;
using PO2.History.Actions;
using PO2.Localization;
using PO2.Tools.MeshEdit;

namespace PO2.Core
{
    public partial class ProceduralObjectSystem
    {
        internal void RestorePreparedTopologySelection(ProceduralObject obj, TopologyPreparedSelection selection) =>
            m_HistoryContext.RestorePreparedTopologySelection(obj, selection);

        internal TopologyAction CreatePreparedTopologyHistory(ProceduralObject obj, int index, ReversibleTopologyDelta delta, TopologySelection before, TopologySelection after, LocalizedText description, TopologyReplayProof proof, TopologySize size)
        {
            if (!obj.CanEditGeometry(index, out LocalizedText reason)) throw new LocalizedTextException(reason);
            return new TopologyAction(obj, index, delta, before, after, m_HistoryContext, description, proof, size);
        }

        internal HistoryEdit BeginTopologyHistory(ProceduralObject obj, int index, MeshMutationResult mutation, LocalizedText description)
        {
            if (!obj.CanEditGeometry(index, out LocalizedText reason))
            {
                Mod.log.Warn(reason.ToString());
                return null;
            }

            return BeginHistoryEdit(description, () => new List<HistoryCaptureAction>
            {
                new TopologyAction(obj, index, mutation, description, m_HistoryContext)
            });
        }

        internal HistoryEdit BeginLifetimeHistory(LocalizedText description, params HistoryCaptureAction[] actions)
        {
            return BeginHistoryEdit(description, () => new List<HistoryCaptureAction>(actions), true);
        }

        internal HistoryEdit BeginPartTransformHistory(PartTransformAction action) =>
            BeginHistoryEdit(action.Description, () => new List<HistoryCaptureAction> { action });

        internal HistoryEdit BeginPartPivotHistory(ProceduralObject obj, int partIndex, LocalizedText description)
        {
            return BeginHistoryEdit(description, () => new List<HistoryCaptureAction>
            {
                new PartPivotAction(obj, partIndex, description),
            });
        }

        internal HistoryEdit BeginObjectDeformationHistory(LocalizedText description, IReadOnlyList<PartDeformationDeltaAction> actions, DeformationAction cageAction = null)
        {
            if (actions == null || actions.Count == 0)
            {
                return null;
            }

            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i] == null)
                {
                    return null;
                }
            }

            return BeginHistoryEdit(description, () =>
            {
                var captures = new List<HistoryCaptureAction>(actions.Count);

                for (int i = 0; i < actions.Count; i++)
                {
                    captures.Add(actions[i]);
                }

                if (cageAction != null) captures.Add(cageAction);
                return captures;
            });
        }

        internal VertexHistoryEdit BeginVertexHistory(ProceduralObject obj, Dictionary<int, int[]> indices, LocalizedText description)
        {
            foreach (int index in indices.Keys)
            {
                if (!obj.CanEditGeometry(index, out LocalizedText reason))
                {
                    Mod.log.Warn(reason.ToString());
                    return null;
                }
            }

            var parts = new Dictionary<int, VertexPatchAction>();
            var edit = BeginHistoryEdit(description, () =>
            {
                var actions = new List<HistoryCaptureAction>();
                try
                {
                    foreach (var pair in indices)
                    {
                        var action = new VertexPatchAction(obj, pair.Key, pair.Value, description, layoutCache: m_HistoryContext.Layouts);
                        parts.Add(pair.Key, action);
                        actions.Add(action);
                    }

                    return actions;
                }
                catch
                {
                    foreach (var action in actions) action.Dispose();
                    parts.Clear();
                    throw;
                }
            });
            return edit == null ? null : new VertexHistoryEdit(edit, parts);
        }

        internal VertexHistoryEdit BeginVertexHistory(ProceduralObject obj, LocalizedText description, Func<HistoryLayoutCache, MeshVertexEditPreparation.Acquisition> prepare)
        {
            var parts = new Dictionary<int, VertexPatchAction>();
            var edit = BeginHistoryEdit(description, () =>
            {
                var actions = new List<HistoryCaptureAction>();
                try
                {
                    using (var acquired = prepare(m_HistoryContext.Layouts))
                    {
                        foreach (var pair in acquired.Layouts)
                        {
                            if (!obj.CanEditGeometry(pair.Key, out LocalizedText reason)) throw new LocalizedTextException(reason);
                            var action = new VertexPatchAction(obj, pair.Key, pair.Value.Selected, description, pair.Value);
                            actions.Add(action);
                            parts.Add(pair.Key, action);
                        }
                    }

                    return actions;
                }
                catch
                {
                    foreach (var action in actions) action.Dispose();
                    parts.Clear();
                    throw;
                }
            });
            return edit == null ? null : new VertexHistoryEdit(edit, parts);
        }

        internal HistoryEdit BeginObjectHistory(ProceduralObject obj, ObjectValueKind kind, LocalizedText description)
        {
            return BeginHistoryEdit(description, () => new List<HistoryCaptureAction>
            {
                new ObjectValueAction(obj, kind, description)
            });
        }

        internal HistoryEdit BeginAppearanceHistory(ProceduralObject obj, int partIndex, int areaId, LocalizedText description)
        {
            return BeginHistoryEdit(description, () =>
            {
                if (!obj.Parts[partIndex].TryGetSurfaceArea(areaId, out var area)) throw new InvalidOperationException("Missing Surface Area.");
                return new List<HistoryCaptureAction> { new AppearanceAction(obj, partIndex, area, description) };
            });
        }

        internal HistoryEdit BeginOriginalAppearanceHistory(ProceduralObject obj)
        {
            LocalizedText description = LocalizedText.For("PO2.History.Action.Material.RestoreOriginal");
            return BeginHistoryEdit(description, () =>
            {
                var actions = new List<HistoryCaptureAction>();
                for (int partIndex = 0; partIndex < obj.Parts.Count; partIndex++)
                {
                    foreach (var area in obj.Parts[partIndex].SurfaceAreas)
                    {
                        if (area.HasOriginalState) actions.Add(new AppearanceAction(obj, partIndex, area, description));
                    }
                }

                return actions;
            });
        }

        internal HistoryEdit BeginSurfaceAreaHistory(ProceduralObject obj, int partIndex, LocalizedText description)
        {
            return BeginHistoryEdit(description, () => new List<HistoryCaptureAction>
            {
                new SurfaceAreaAction(obj, partIndex, description)
            });
        }

        // The factory runs immediately. No closure is retained by an action.
        private HistoryEdit BeginHistoryEdit(LocalizedText description, Func<List<HistoryCaptureAction>> capture, bool restoreFocus = false)
        {
            var transaction = ActionHistory.TryBegin();
            if (transaction == null) return null;
            try
            {
                return new HistoryEdit(m_HistoryContext, transaction, description, capture(), message => Mod.log.Error(message), restoreFocus);
            }
            catch (Exception exception)
            {
                transaction.Dispose();
                Mod.log.Error("[PO History] Could not capture " + description + ": " + exception.Message);
                return null;
            }
        }
    }
}