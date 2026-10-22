using System;
using System.Collections.Generic;
using System.Threading;
using PO2.Geometry;
using PO2.Persistence.Model;
using UnityEngine;

namespace PO2.Core.Operations
{
    internal sealed class ObjectClipboard
    {
        private readonly PersistenceObjectData m_Data;
        internal string Name => m_Data.Name;

        internal ObjectClipboard(PersistenceObjectData data)
        {
            m_Data = data;
        }

        internal ProceduralObject CreateObject(int id, string name, Vector3 position, CancellationToken token)
        {
            var parts = new List<MeshPart>(m_Data.Parts.Length);
            foreach (var part in m_Data.Parts)
            {
                token.ThrowIfCancellationRequested();
                parts.Add(part.Restore());
            }

            var orientations = new List<CustomOrientation>(m_Data.CustomOrientations.Length);
            foreach (var orientation in m_Data.CustomOrientations) orientations.Add(orientation.Restore());
            var transform = m_Data.Transform;
            transform.Position = position;
            var obj = new ProceduralObject(id, Guid.NewGuid(), name, transform, parts, orientations, localPivot: m_Data.LocalPivot);
            obj.SetReceivesDecals(m_Data.ReceivesDecals);
            obj.SetLodProfile(m_Data.LodProfile);
            foreach (var cage in m_Data.Deformations)
            {
                token.ThrowIfCancellationRequested();
                obj.SetDeformation(cage.PartIndex, cage.Copy(obj, true));
            }

            token.ThrowIfCancellationRequested();
            return obj;
        }
    }
}