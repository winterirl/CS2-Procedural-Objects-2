using System;
using UnityEngine;
using PO2.Core;
using PO2.Core.Lod;
using PO2.History;
using PO2.Localization;

namespace PO2.History.Actions
{
    internal enum ObjectValueKind
    {
        Transform,
        Name,
        Locked,
        Orientations,
        ReceivesDecals,
        Pivot,
        LodProfile,
    }

    internal sealed class ObjectValueState
    {
        internal ObjectTransform Transform;
        internal string Name;
        internal bool Locked;
        internal bool ReceivesDecals;
        internal Vector3 Pivot;
        internal LodProfile LodProfile;
        internal CustomOrientation[] Orientations;

        internal static ObjectValueState Capture(ProceduralObject obj, ObjectValueKind kind)
        {
            var state = new ObjectValueState();
            switch (kind)
            {
                case ObjectValueKind.Transform: state.Transform = obj.Transform; break;
                case ObjectValueKind.Name: state.Name = obj.Name; break;
                case ObjectValueKind.Locked: state.Locked = obj.IsLocked; break;
                case ObjectValueKind.ReceivesDecals:
                    state.ReceivesDecals = obj.ReceivesDecals;
                    break;
                case ObjectValueKind.LodProfile:
                    state.LodProfile = obj.LodProfile;
                    break;
                case ObjectValueKind.Pivot:
                    state.Pivot = obj.LocalPivot;
                    break;
                case ObjectValueKind.Orientations:
                    state.Orientations = new CustomOrientation[obj.CustomOrientations.Count];
                    for (int i = 0; i < state.Orientations.Length; i++) state.Orientations[i] = obj.CustomOrientations[i].Clone();
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }

            return state;
        }

        internal ulong Fingerprint(ObjectValueKind kind)
        {
            var hash = HistoryHash.Start;
            switch (kind)
            {
                case ObjectValueKind.Transform:
                    hash.Add(Transform.Position);
                    hash.Add(Transform.Rotation);
                    hash.Add(Transform.Scale);
                    break;
                case ObjectValueKind.Name: hash.Add(Name); break;
                case ObjectValueKind.Locked: hash.Add(Locked); break;
                case ObjectValueKind.ReceivesDecals:
                    hash.Add(ReceivesDecals);
                    break;
                case ObjectValueKind.LodProfile:
                    hash.Add(LodProfile);
                    break;
                case ObjectValueKind.Pivot:
                    hash.Add(Pivot);
                    break;
                case ObjectValueKind.Orientations:
                    hash.Add(Orientations.Length);
                    foreach (var orientation in Orientations)
                    {
                        hash.Add(orientation.Name);
                        hash.Add(orientation.LocalRotation);
                    }

                    break;
            }

            return hash.Value;
        }

        internal long EstimatedBytes
        {
            get
            {
                long size = 96 + (Name == null ? 0 : 24 + Name.Length * 2L);
                if (LodProfile != null) size += 192;
                if (Orientations != null)
                {
                    size += 24 + Orientations.Length * 8L;
                    foreach (var orientation in Orientations) size += 64 + orientation.Name.Length * 2L;
                }

                return size;
            }
        }
    }

    internal sealed class ObjectValueAction : HistorySwapAction<ObjectValueState>
    {
        private readonly HistoryObjectKey m_Key;
        private readonly ObjectValueKind m_Kind;

        internal ObjectValueAction(ProceduralObject obj, ObjectValueKind kind, LocalizedText description) : base(description, ObjectValueState.Capture(obj, kind))
        {
            m_Key = new HistoryObjectKey(obj.Id, obj.PersistentId);
            m_Kind = kind;
        }

        public override HistoryFootprint Footprint => new HistoryFootprint(96 + (Opposite?.EstimatedBytes ?? 0), 0, 0, 96 + (Opposite?.EstimatedBytes ?? 0));

        protected override ObjectValueState Capture(HistoryContext context)
        {
            if (!context.TryResolve(m_Key, out var obj)) throw new InvalidOperationException("Missing history object " + m_Key);
            return ObjectValueState.Capture(obj, m_Kind);
        }

        protected override ulong Fingerprint(ObjectValueState state) => state.Fingerprint(m_Kind);

        protected override void Apply(HistoryContext context, ObjectValueState state)
        {
            if (!context.TryResolve(m_Key, out var obj)) throw new InvalidOperationException("Missing history object " + m_Key);
            obj.ApplyHistoryValue(m_Kind, state);
        }
    }
}