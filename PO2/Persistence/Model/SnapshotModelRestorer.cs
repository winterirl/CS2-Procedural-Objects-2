using System;
using System.Collections.Generic;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Persistence.PreparedTopology;
using PO2.Persistence.Format;

namespace PO2.Persistence.Model
{
    // Own the detached candidates until every part and object is ready.
    // The caller decides when to replace its current runtime and publish them.
    internal static class SnapshotModelRestorer
    {
        internal static List<ProceduralObject> Restore(OwnedSnapshotObjects objects)
        {
            if (objects == null) throw new ArgumentNullException(nameof(objects));
            using (objects)
            {
                var identities = new HashSet<Guid>();
                var data = new PersistenceObjectData[objects.Count];
                var parts = new MeshPart[objects.Count][];
                var work = new List<KeyValuePair<int, int>>();
                for (int i = 0; i < objects.Count; i++)
                {
                    data[i] = objects.Take(i);
                    if (!identities.Add(data[i].PersistentId)) throw new InvalidOperationException("Duplicate persisted PO identity.");
                    parts[i] = new MeshPart[data[i].Parts.Length];
                    for (int j = 0; j < parts[i].Length; j++) work.Add(new KeyValuePair<int, int>(i, j));
                }

                // Every worker writes one assigned cell. Object assembly waits for
                // the whole batch, including any workers draining after failure.
                var retained = new EncodedPartTopology[work.Count];
                LoadingBatch.RunDetached(work.Count, i =>
                {
                    var key = work[i];
                    var source = data[key.Key].Parts[key.Value];
                    var encoded = objects.TakePrepared(key.Key, key.Value);
                    ValidatedPartTopology prepared = null;
                    if (encoded != null)
                    {
                        try { prepared = PreparedTopologyValidator.Validate(encoded, source); }
                        catch (Exception ex) when (PreparedTopologyExtension.IsOptionalError(ex)) { }
                    }
                    parts[key.Key][key.Value] = source.RestoreOwned(prepared);
                    if (prepared != null && prepared.CanonicalMaps) retained[i] = encoded;
                });

                var restored = new List<ProceduralObject>(data.Length);
                for (int i = 0; i < data.Length; i++)
                {
                    restored.Add(data[i].RestoreOwned(i + 1, parts[i]));
                    data[i] = null;
                    parts[i] = null;
                }
                if (objects.Session != null)
                {
                    for (int i = 0; i < work.Count; i++)
                    {
                        if (retained[i] == null) continue;
                        var key = work[i];
                        objects.Session.Stage(restored[key.Key].Parts[key.Value].CapturePersistenceTopology(), retained[i]);
                    }
                }
                return restored;
            }
        }
    }
}
