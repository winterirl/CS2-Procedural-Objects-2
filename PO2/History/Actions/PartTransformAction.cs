using System;
using UnityEngine;
using PO2.Core;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.History;
using PO2.Localization;

namespace PO2.History.Actions
{
    internal sealed class PartTransformAction : HistoryCaptureAction, IHistoryPayloadOwner
    {
        private readonly HistoryPartKey m_Key;
        private readonly ulong m_Layout;
        private readonly bool m_PositionsOnly;
        private GeometryStreams m_Start;
        private Matrix4x4 m_Forward = Matrix4x4.identity;
        private Matrix4x4 m_Inverse;
        private HistoryResidentPayload m_Payload;
        private bool m_Correction;
        private ulong m_Before, m_After;
        private readonly Vector3 m_StartPivot;
        private Vector3 m_AfterPivot;
        internal bool UsesCorrection => m_Correction;

        internal PartTransformAction(ProceduralObject obj, int index, PartGeometrySnapshot snapshot, LocalizedText description) : base(description)
        {
            var part = obj.Parts[index];
            m_Key = new HistoryPartKey(new HistoryObjectKey(obj.Id, obj.PersistentId), part.PartHistoryId, index);
            m_Layout = GeometryHistory.Layout(part.Mesh);
            m_PositionsOnly = snapshot.PositionsOnly;
            // Share the existing cancellation arrays until Seal. The action
            // releases them before the drag drops its preview work buffers.
            m_Start = new GeometryStreams(snapshot.Positions, snapshot.Normals, snapshot.Tangents);
            m_StartPivot = part.LocalPivot;
            m_Before = HashState(m_Start, m_StartPivot);
        }

        internal void SetRecipe(Matrix4x4 matrix)
        {
            m_Forward = matrix;
        }

        public System.Collections.Generic.IEnumerable<HistoryResidentPayload> Payloads
        {
            get
            {
                if (m_Payload != null) yield return m_Payload;
            }
        }

        public void CoolManagedPayload()
        {
        }

        public override HistoryFootprint Footprint => new HistoryFootprint(256 + (m_Start?.RawBytes ?? 0), m_Payload?.Footprint.EncodedBytes ?? 0, m_Payload?.Footprint.ColdBytes ?? 0, 256 + (m_Start?.RawBytes ?? 0) + (m_Payload?.LongLength ?? 0));

        private MeshPart Resolve(HistoryContext context)
        {
            if (!context.TryResolve(m_Key, out var part, out _) || GeometryHistory.Layout(part.Mesh) != m_Layout)
                throw new InvalidOperationException("Part transform history target or layout changed.");
            return part;
        }

        internal override void Seal(HistoryContext context)
        {
            MeshPart part = Resolve(context);
            var current = GeometryStreams.Capture(part, m_PositionsOnly);
            m_AfterPivot = part.LocalPivot;
            m_After = HashState(current, m_AfterPivot);
            HasChanges = m_After != m_Before;
            if (!HasChanges)
            {
                m_Start = null;
                return;
            }

            byte[] opposite = m_Start.Encode();
            m_Inverse = m_Forward.inverse;
            try
            {
                var predicted = current.Transform(m_Inverse, m_PositionsOnly);
                byte[] correction = m_Start.Encode(predicted);
                // Verify both recipes before retaining a command-only redo.
                if (correction.Length < opposite.Length && HashState(predicted.Decode(correction, true), m_StartPivot) == m_Before && HashState(m_Start.Transform(m_Forward, m_PositionsOnly), m_AfterPivot) == m_After)
                {
                    m_Payload = new HistoryResidentPayload(correction, m_Start.RawBytes);
                    m_Correction = true;
                }
            }
            catch (InvalidOperationException)
            {
            }

            if (!m_Correction) m_Payload = new HistoryResidentPayload(opposite, m_Start.RawBytes);
            m_Start = null;
        }

        internal override void RestoreCapture(HistoryContext context)
        {
            var part = Resolve(context);
            if (m_Start != null)
            {
                if (HashState(GeometryStreams.Capture(part, m_PositionsOnly), part.LocalPivot) != m_Before)
                {
                    Apply(context, part, m_Start, m_StartPivot);
                }
            }
            else if (HasChanges)
            {
                var result = Replay(HistoryDirection.Undo, context);
                if (!result.Success) throw new InvalidOperationException(result.Reason);
            }
        }

        public override HistoryResult Replay(HistoryDirection direction, HistoryContext context)
        {
            var part = Resolve(context);
            var current = GeometryStreams.Capture(part, m_PositionsOnly);
            bool undo = direction == HistoryDirection.Undo;
            if (HashState(current, part.LocalPivot) != (undo ? m_After : m_Before))
            {
                return new HistoryResult(false, "Part transform values no longer match.", true);
            }

            GeometryStreams restored;
            byte[] displaced = null;
            if (m_Correction) restored = undo ? current.Transform(m_Inverse, m_PositionsOnly).Decode(m_Payload.Bytes, true) : current.Transform(m_Forward, m_PositionsOnly);
            else
            {
                restored = current.Decode(m_Payload.Bytes, false);
                displaced = current.Encode();
            }

            Vector3 restoredPivot = undo ? m_StartPivot : m_AfterPivot;
            if (HashState(restored, restoredPivot) != (undo ? m_Before : m_After))
            {
                return new HistoryResult(false, "Part transform payload failed verification.", true);
            }

            var replacement = displaced == null ? null : new HistoryResidentPayload(displaced, restored.RawBytes);
            Apply(context, part, restored, restoredPivot);
            if (!m_Correction)
            {
                m_Payload.Dispose();
                m_Payload = replacement;
            }

            return HistoryResult.Ok;
        }

        private void Apply(HistoryContext context, MeshPart part, GeometryStreams streams, Vector3 localPivot)
        {
            if (!context.TryResolve(m_Key.Owner, out var obj)) throw new InvalidOperationException("Missing part history owner.");
            streams.Publish(part);
            if (!part.SetLocalPivot(localPivot) && !part.LocalPivot.Equals(localPivot))
            {
                throw new InvalidOperationException("Part transform pivot could not be restored.");
            }

            if (m_PositionsOnly) part.MarkPositionsChanged();
            else part.MarkGeometryChanged();
            obj.MarkDirty(ProceduralObjectDirtyFlags.Geometry);
            context.GeometryChanged(obj);
        }

        private static ulong HashState(GeometryStreams streams, Vector3 localPivot)
        {
            HistoryHash hash = HistoryHash.Start;
            hash.Add(streams.Hash());
            hash.Add(localPivot);
            return hash.Value;
        }

        public override void Dispose()
        {
            m_Start = null;
            m_Payload?.Dispose();
            m_Payload = null;
        }
    }
}