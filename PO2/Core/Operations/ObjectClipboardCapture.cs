using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PO2.Core.Deformation;
using PO2.Core.Lod;
using PO2.History;
using PO2.Persistence.Model;
using UnityEngine;

namespace PO2.Core.Operations
{
    // Copy live streams in short slices
    // Pasting only reads the finished snapshot
    internal sealed class ObjectClipboardCapture
    {
        private readonly ProceduralObject m_Source;
        private readonly string m_Name;
        private readonly ObjectTransform m_Transform;
        private readonly Vector3 m_Pivot;
        private readonly bool m_Decals;
        private readonly LodProfile m_LodProfile;
        private readonly uint m_LodSettingsVersion;
        private readonly uint m_OrientationsVersion, m_DeformationVersion;
        private readonly PersistenceCustomOrientationData[] m_Orientations;
        private readonly PersistencePartData[] m_Parts;
        private readonly TopologyTargetStamp[] m_Stamps;
        private readonly BudgetedCopy m_CageCopy = new BudgetedCopy();
        private readonly CageCapture[] m_Cages;
        private TopologySnapshotCapture m_Part;
        private int m_Index;
        internal Task Drain => Task.WhenAll(m_CageCopy.Allocation, m_Part?.Copy.Allocation ?? Task.CompletedTask);

        private sealed class BaselineCapture
        {
            internal int PartIndex;
            internal BudgetedCopy.Stream<Vector3> Positions, Normals;
            internal BudgetedCopy.Stream<Vector4> Tangents;
        }

        private sealed class CageCapture
        {
            internal Guid Id;
            internal int PartIndex;
            internal Matrix4x4 Reference;
            internal Vector3[] Corners;
            internal BaselineCapture[] Parts;
        }

        internal ObjectClipboardCapture(ProceduralObject source, CancellationToken token)
        {
            m_Source = source;
            m_Name = source.Name;
            m_Transform = source.Transform;
            m_Pivot = source.LocalPivot;
            m_Decals = source.ReceivesDecals;
            m_LodProfile = source.LodProfile;
            m_LodSettingsVersion = source.LodSettingsVersion;
            m_OrientationsVersion = source.CustomOrientationVersion;
            m_DeformationVersion = source.DeformationVersion;
            m_Orientations = new PersistenceCustomOrientationData[source.CustomOrientations.Count];
            for (int i = 0; i < m_Orientations.Length; i++) m_Orientations[i] = PersistenceCustomOrientationData.Capture(source.CustomOrientations[i]);
            m_Parts = new PersistencePartData[source.Parts.Count];
            m_Stamps = new TopologyTargetStamp[m_Parts.Length];
            for (int i = 0; i < m_Stamps.Length; i++) m_Stamps[i] = new TopologyTargetStamp(source, i);
            m_Cages = new CageCapture[source.Deformations.Count];
            for (int i = 0; i < m_Cages.Length; i++)
            {
                var cage = source.Deformations[i];
                var copy = new CageCapture
                {
                    Id = cage.Id, PartIndex = cage.PartIndex, Reference = cage.LocalFromReference,
                    Corners = (Vector3[])cage.Corners.Clone(), Parts = new BaselineCapture[cage.Parts.Length]
                };
                for (int p = 0; p < copy.Parts.Length; p++)
                {
                    var part = cage.Parts[p];
                    copy.Parts[p] = new BaselineCapture
                    {
                        PartIndex = part.PartIndex,
                        Positions = m_CageCopy.Add(part.Positions, 12), Normals = m_CageCopy.Add(part.Normals, 12),
                        Tangents = m_CageCopy.Add(part.Tangents, 16)
                    };
                }

                m_Cages[i] = copy;
            }

            m_CageCopy.Start(token);
        }

        private bool Matches()
        {
            if (m_Source.Name != m_Name || !m_Source.Transform.Equals(m_Transform) || !m_Source.LocalPivot.Equals(m_Pivot) || m_Source.ReceivesDecals != m_Decals || m_Source.LodSettingsVersion != m_LodSettingsVersion || m_Source.CustomOrientationVersion != m_OrientationsVersion || m_Source.DeformationVersion != m_DeformationVersion || m_Source.Parts.Count != m_Stamps.Length) return false;
            for (int i = 0; i < m_Stamps.Length; i++)
                if (!m_Stamps[i].Matches(m_Source, i))
                    return false;
            return true;
        }

        internal bool Poll(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!Matches()) throw new InvalidOperationException("The PO changed while copying.");
            long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 500;
            do
            {
                if (m_Index == m_Parts.Length) return m_CageCopy.Poll(Matches, token, deadlineTimestamp: deadline);
                if (m_Part == null)
                {
                    m_Part = new TopologySnapshotCapture(m_Source, m_Index);
                    m_Part.Start(token);
                }

                if (!m_Part.Poll(token, deadline)) return false;
                m_Parts[m_Index++] = m_Part.Finish();
                m_Part = null;
            } while (Stopwatch.GetTimestamp() < deadline);

            return false;
        }

        internal ObjectClipboard Finish()
        {
            if (m_Index != m_Parts.Length || !m_CageCopy.IsComplete || !Matches()) throw new InvalidOperationException("The clipboard capture is incomplete.");
            var data = PersistenceObjectData.FromDecoded(Guid.NewGuid(), m_Name, false, m_Decals, m_Transform, m_Orientations, m_Parts, m_Pivot, m_LodProfile);
            data.Deformations = new DeformationCage[m_Cages.Length];
            for (int i = 0; i < m_Cages.Length; i++)
            {
                var cage = m_Cages[i];
                var parts = new DeformationBaseline[cage.Parts.Length];
                for (int p = 0; p < parts.Length; p++)
                {
                    var part = cage.Parts[p];
                    parts[p] = new DeformationBaseline(part.PartIndex, Guid.Empty, part.Positions.Data, part.Normals?.Data, part.Tangents?.Data);
                }

                data.Deformations[i] = new DeformationCage(cage.Id, cage.PartIndex, cage.Reference, cage.Corners, parts);
            }

            return new ObjectClipboard(data);
        }
    }
}