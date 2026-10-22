using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using PO2.Core;
using PO2.Core.Deformation;
using PO2.History;
using PO2.Localization;
using PO2.Persistence.Format;

namespace PO2.History.Actions
{
    // Normal edits retain corners only. Crossing a Bake/Reset/create boundary
    // retains one encoded baseline payload in memory.
    internal sealed class DeformationAction : HistoryCaptureAction, IHistoryPayloadOwner
    {
        private readonly HistoryObjectKey m_Key;
        private readonly int m_PartIndex;
        private DeformationCage m_Capture;
        private readonly Vector3[] m_Before;
        private Vector3[] m_After;
        private Guid m_Id;
        private ulong m_Expected;
        private bool m_Sealed;
        private HistoryResidentPayload m_Payload;
        private readonly long m_LogicalBytes;

        internal DeformationAction(ProceduralObject obj, int partIndex, DeformationCage baseline, bool transition, LocalizedText description) : base(description)
        {
            m_Key = new HistoryObjectKey(obj.Id, obj.PersistentId);
            m_PartIndex = partIndex;
            m_Capture = obj.GetDeformation(partIndex);
            m_Before = m_Capture == null ? null : (Vector3[])m_Capture.Corners.Clone();
            m_Id = baseline.Id;
            m_LogicalBytes = transition ? baseline.RawBytes : 512;
        }

        internal void SetPreparedPayload(byte[] bytes)
        {
            if (bytes == null) return;
            m_Payload = new HistoryResidentPayload(bytes, m_LogicalBytes);
        }

        public IEnumerable<HistoryResidentPayload> Payloads
        {
            get
            {
                if (m_Payload != null) yield return m_Payload;
            }
        }

        public void CoolManagedPayload()
        {
        }

        public override HistoryFootprint Footprint => new HistoryFootprint(512 + (m_Capture?.RawBytes ?? 0), 0, 0, 512).Add(m_Payload?.Footprint ?? default);

        private ProceduralObject Resolve(HistoryContext context)
        {
            if (!context.TryResolve(m_Key, out var obj)) throw new InvalidOperationException("Missing deformation history target.");
            return obj;
        }

        private static ulong Hash(DeformationCage cage)
        {
            if (cage == null) return 0;
            var hash = HistoryHash.Start;
            hash.Add(cage.Id.ToString());
            foreach (var corner in cage.Corners) hash.Add(corner);
            return hash.Value;
        }

        internal override void Seal(HistoryContext context)
        {
            var current = Resolve(context).GetDeformation(m_PartIndex);
            m_After = current == null ? null : (Vector3[])current.Corners.Clone();
            m_Expected = Hash(current);
            HasChanges = m_Expected != Hash(m_Capture);
            if (HasChanges && (m_Before == null || m_After == null) && m_Payload == null)
            {
                throw new InvalidOperationException("Missing prepared baseline history payload.");
            }

            m_Sealed = true;
            m_Capture = null;
        }

        private void Apply(ProceduralObject obj, Vector3[] corners)
        {
            DeformationCage cage = null;
            if (corners != null)
            {
                cage = obj.GetDeformation(m_PartIndex);
                if (cage == null) cage = DeformationCodec.Decode(m_Payload.Bytes, obj);
                if (cage.Id != m_Id) throw new InvalidOperationException("The deformation baseline changed.");
                cage = cage.WithCorners(corners);
            }

            obj.SetDeformation(m_PartIndex, cage);
        }

        internal override void RestoreCapture(HistoryContext context)
        {
            var obj = Resolve(context);
            if (!m_Sealed) obj.SetDeformation(m_PartIndex, m_Capture);
            else Apply(obj, m_Before);
            context.GeometryChanged(obj);
        }

        public override async Task<HistoryResult> Prepare(HistoryDirection direction, CancellationToken cancellation)
        {
            if (m_Payload != null) await m_Payload.Prepare(cancellation).ConfigureAwait(false);
            return HistoryResult.Ok;
        }

        public override HistoryResult Replay(HistoryDirection direction, HistoryContext context)
        {
            var obj = Resolve(context);
            if (Hash(obj.GetDeformation(m_PartIndex)) != m_Expected)
            {
                return new HistoryResult(false, "The deformation history state no longer matches.", true);
            }

            Apply(obj, direction == HistoryDirection.Undo ? m_Before : m_After);
            m_Expected = Hash(obj.GetDeformation(m_PartIndex));
            context.GeometryChanged(obj);
            return HistoryResult.Ok;
        }

        public override void Dispose()
        {
            m_Capture = null;
            m_Payload?.Dispose();
            m_Payload = null;
        }
    }
}