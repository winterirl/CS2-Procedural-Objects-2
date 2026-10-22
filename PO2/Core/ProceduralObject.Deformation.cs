using System;
using System.Collections.Generic;
using PO2.Core.Deformation;
using PO2.Localization;

namespace PO2.Core
{
    public sealed partial class ProceduralObject
    {
        private readonly List<DeformationCage> m_Deformations = new List<DeformationCage>();
        internal IReadOnlyList<DeformationCage> Deformations => m_Deformations;
        internal uint DeformationVersion { get; private set; }
        internal bool HasDeformation => m_Deformations.Count != 0;

        internal DeformationCage GetDeformation(int partIndex)
        {
            foreach (var cage in m_Deformations)
                if (cage.PartIndex == partIndex)
                    return cage;
            return null;
        }

        internal bool CanEditDeformationScope(int partIndex, out LocalizedText reason)
        {
            reason = LocalizedText.Empty;
            foreach (var cage in m_Deformations)
            {
                if (cage.PartIndex != partIndex && (partIndex < 0 || cage.PartIndex < 0))
                {
                    reason = LocalizedText.For("PO2.Object.Deformation.Error.OverlappingScope");
                    return false;
                }
            }

            return true;
        }

        internal DeformationCage GetGeometryDeformation(int partIndex)
        {
            foreach (var cage in m_Deformations)
            {
                if (partIndex < 0 || cage.PartIndex < 0 || cage.PartIndex == partIndex) return cage;
            }

            return null;
        }

        internal bool CanEditGeometry(int partIndex, out LocalizedText reason)
        {
            bool editable = GetGeometryDeformation(partIndex) == null;
            reason = editable ? LocalizedText.Empty : LocalizedText.For("PO2.Edit.Disabled.DeformationPending");
            return editable;
        }

        internal void SetDeformation(int partIndex, DeformationCage cage)
        {
            if (cage != null && (cage.PartIndex != partIndex || !cage.Matches(this) || !CanEditDeformationScope(partIndex, out _)))
            {
                throw new LocalizedTextException(LocalizedText.For("PO2.Object.Deformation.Error.PartMismatch"));
            }

            for (int i = 0; i < m_Deformations.Count; i++)
            {
                if (m_Deformations[i].PartIndex != partIndex) continue;
                if (ReferenceEquals(m_Deformations[i], cage)) return;
                if (cage == null) m_Deformations.RemoveAt(i);
                else m_Deformations[i] = cage;
                DeformationVersion++;
                return;
            }

            if (cage != null)
            {
                m_Deformations.Add(cage);
                DeformationVersion++;
            }
        }

        internal void RebindDeformationParts()
        {
            foreach (var cage in m_Deformations)
            {
                foreach (var part in cage.Parts) part.PartId = m_Parts[part.PartIndex].PartHistoryId;
            }
        }

        internal void CopyDeformationsTo(ProceduralObject target)
        {
            foreach (var cage in m_Deformations) target.SetDeformation(cage.PartIndex, cage.Copy(target, true));
        }
    }
}