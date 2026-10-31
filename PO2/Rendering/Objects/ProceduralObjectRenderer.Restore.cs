using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using PO2.Core;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.Persistence;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        private sealed class RestorePartWork
        {
            internal ProceduralObject Object;
            internal int PartIndex;
            internal MeshPart Part;
            internal TopologyRendererWork Work;
            internal Exception CaptureError;
        }

        private sealed class RestorePartResult : IDisposable
        {
            internal TopologyRendererPreparation Data;
            internal Exception Error;
            public void Dispose() { Data?.Dispose(); Data = null; }
        }

        internal void RestoreObjects(IReadOnlyList<ProceduralObject> objects, Action<ProceduralObject> published, Action<ProceduralObject, Exception> failed)
        {
            var jobs = new List<RestorePartWork>();
            foreach (var obj in objects)
            {
                // Capture all engine/material requirements on the main thread.
                // A sentinel also permits an empty, but valid, object to publish.
                for (int index = 0; index < Math.Max(1, obj.Parts.Count); index++)
                {
                    var job = new RestorePartWork { Object = obj, PartIndex = index };
                    jobs.Add(job);
                    if (obj.Parts.Count == 0) continue;
                    job.Part = obj.Parts[index];
                    try
                    {
                        job.Work = new TopologyRendererWork { Scale = obj.Transform.Scale, CaptureMode = TopologyRenderCaptureMode.Restore };
                        foreach (var area in job.Part.SurfaceAreas)
                            job.Work.Areas.Add(area.Id, new TopologyRenderAreaWork
                            {
                                Id = area.Id, Isolated = RequiresIsolatedRenderer(area.Material),
                                RequiresTangents = RequiresTangentSpace(area.Material), TileSize = area.Material.GetEffectiveTileSizeMeters()
                            });
                    }
                    catch (Exception exception) { job.CaptureError = exception; }
                }
            }

            RestoreObjectStaging staging = null;
            ProceduralObject current = null;
            bool rejected = false;
            try
            {
                LoadingBatch.Run(jobs.Count, i =>
                {
                    var job = jobs[i];
                    if (job.CaptureError != null) return new RestorePartResult { Error = job.CaptureError };
                    try
                    {
                        return new RestorePartResult { Data = job.Part == null ? null : (TopologyRendererPreparation)job.Work.Build(job.Part, null, null, null, CancellationToken.None) };
                    }
                    catch (Exception exception) { return new RestorePartResult { Error = exception }; }
                }, (i, result) =>
                {
                    var job = jobs[i];
                    if (!ReferenceEquals(current, job.Object))
                    {
                        staging?.Dispose();
                        staging = null;
                        current = job.Object;
                        rejected = false;
                    }
                    if (rejected) return;
                    try
                    {
                        if (result.Error != null) throw result.Error;
                        if (staging == null) staging = new RestoreObjectStaging(this, current);
                        if (job.Part != null) staging.Append(job.PartIndex, result.Data);
                        if (job.PartIndex + 1 >= current.Parts.Count)
                        {
                            staging.Publish();
                            published(current);
                        }
                    }
                    catch (Exception exception)
                    {
                        rejected = true;
                        staging?.Dispose();
                        staging = null;
                        Destroy(current.Id);
                        PollTopologyCleanup(true);
                        failed(current, exception);
                    }
                }, result => result.Dispose());
            }
            finally
            {
                staging?.Dispose();
                PollTopologyCleanup(true);
            }
        }

        // Unity publication is synchronous during loading. It reuses the same
        // validated upload, version adoption, and rollback as topology/history.
        private sealed class RestoreObjectStaging : IDisposable
        {
            private readonly ProceduralObjectRenderer m_Owner;
            private readonly ProceduralObject m_Object;
            private readonly RenderInstance m_Instance;
            private bool m_Published, m_Disposed;

            internal RestoreObjectStaging(ProceduralObjectRenderer owner, ProceduralObject obj)
            {
                m_Owner = owner;
                m_Object = obj;
                m_Instance = new RenderInstance { ObjectId = obj.Id, Source = obj, Root = new GameObject($"PO2 Object {obj.Id}") };
                try
                {
                    m_Instance.Root.SetActive(false);
                    m_Instance.Parts.Capacity = obj.Parts.Count;
                    ApplyTransform(m_Instance, obj.Transform);
                }
                catch
                {
                    UnityEngine.Object.Destroy(m_Instance.Root);
                    throw;
                }
            }

            internal void Append(int index, TopologyRendererPreparation data)
            {
                using (var upload = new TopologyPublication(m_Owner, m_Instance, m_Object, index, m_Object.Parts[index], data, append: true, loading: true))
                {
                    while (!upload.Poll(CancellationToken.None)) { }
                    upload.Adopt();
                    upload.Commit();
                }
            }

            internal void Publish()
            {
                if (m_Instance.Parts.Count != m_Object.Parts.Count || m_Published) throw new InvalidOperationException("Incomplete restored renderer.");
                m_Owner.m_Instances.Add(m_Object.Id, m_Instance);
                try
                {
                    m_Instance.Root.SetActive(true);
                    m_Object.ClearDirty(ProceduralObjectDirtyFlags.Transform | ProceduralObjectDirtyFlags.Geometry | ProceduralObjectDirtyFlags.Topology | ProceduralObjectDirtyFlags.Material);
                    m_Owner.m_PickSceneVersion++;
                    m_Published = true;
                }
                catch
                {
                    m_Owner.m_Instances.Remove(m_Object.Id);
                    throw;
                }
            }

            public void Dispose()
            {
                if (m_Disposed) return;
                m_Disposed = true;
                if (m_Published) return;
                m_Instance.Root.SetActive(false);
                m_Owner.m_RetiredTopologyParts.AddRange(m_Instance.Parts);
                m_Instance.Parts.Clear();
                UnityEngine.Object.Destroy(m_Instance.Root);
            }
        }
    }
}
