using System;
using UnityEngine;

namespace PO2.Tools.Snapping
{
    // Object movement can use a persistent local-space index while Mesh Edit
    // uses a prepared selection tree with separate per-drag query state.
    internal interface ITranslationSnapSource : IDisposable
    {
        bool TryFindNearest(Vector3 target, out int primitiveIndex, out Vector3 closestPoint, out float distanceSquared);

        bool TryGetClosestPoint(int primitiveIndex, Vector3 target, out Vector3 closestPoint, out float distanceSquared);
    }
}