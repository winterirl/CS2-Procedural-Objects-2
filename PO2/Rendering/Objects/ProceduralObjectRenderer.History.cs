using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using PO2.Core;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.Geometry.Picking;
using PO2.Rendering;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        internal IHistoryObjectRenderer PrepareHistoryObject(ProceduralObject obj) => new HistoryObjectRenderer(this, obj);

        private sealed class HistoryObjectRenderer : IHistoryObjectRenderer
        {
            private readonly ProceduralObjectRenderer m_Owner;
            private readonly RenderInstance m_Instance;
            private readonly CancellationTokenSource m_Cancellation = new CancellationTokenSource();
            private TopologyRendererWork m_Work;
            private Task<TopologyRendererPreparation> m_Build;
            private Task<TrianglePickCache> m_PickingBuild;
            private TopologyPublication m_Upload;
            private int m_Part, m_Area;
            private bool m_Disposed, m_Published;
            public ProceduralObject Object { get; }
            public Task Drain { get; private set; } = Task.CompletedTask;

            internal HistoryObjectRenderer(ProceduralObjectRenderer owner, ProceduralObject obj)
            {
                m_Owner = owner;
                Object = obj;
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
                    m_Cancellation.Dispose();
                    throw;
                }
            }

            public bool Poll(CancellationToken token)
            {
                if (m_Disposed) throw new ObjectDisposedException(nameof(HistoryObjectRenderer));
                token.ThrowIfCancellationRequested();
                var timer = Stopwatch.StartNew();
                do
                {
                    if (m_Part == Object.Parts.Count) return true;
                    if (m_Upload != null)
                    {
                        if (m_Upload.Poll(token))
                        {
                            if (!m_PickingBuild.IsCompleted) return false;
                            TrianglePickCache picking = null;
                            if (m_PickingBuild.IsFaulted)
                            {
                                try
                                {
                                    Mod.log.Warn("[PO Picking] Restore cache build failed: " + m_PickingBuild.Exception.GetBaseException().Message);
                                }
                                catch
                                {
                                }
                            }
                            else picking = m_PickingBuild.GetAwaiter().GetResult();

                            // The root stays hidden while finished parts are attached.
                            m_Upload.Adopt();
                            m_Upload.Commit();
                            m_Upload.Dispose();
                            m_Upload = null;
                            var part = Object.Parts[m_Part];
                            var state = m_Instance.Parts[m_Part].PickCache;
                            state.Observe(part, part.PositionVersion, part.FullGeometryVersion, part.TopologyVersion, part.SurfaceAreaMembershipVersion);
                            state.Cache = picking;
                            state.Failed = picking == null;
                            m_PickingBuild = null;
                            m_Part++;
                            m_Area = 0;
                        }
                    }
                    else if (m_Build != null)
                    {
                        if (!m_Build.IsCompleted) return false;
                        var data = m_Build.GetAwaiter().GetResult();
                        m_Build = null;
                        try
                        {
                            m_Upload = new TopologyPublication(m_Owner, m_Instance, Object, m_Part, Object.Parts[m_Part], data, true);
                        }
                        catch
                        {
                            data.Dispose();
                            throw;
                        }
                    }
                    else
                    {
                        var part = Object.Parts[m_Part];
                        if (m_Work == null) m_Work = new TopologyRendererWork { Scale = Object.Transform.Scale, CaptureMode = TopologyRenderCaptureMode.Restore };
                        if (m_Area < part.SurfaceAreas.Count)
                        {
                            var area = part.SurfaceAreas[m_Area++];
                            m_Work.Areas.Add(area.Id, new TopologyRenderAreaWork
                            {
                                Id = area.Id,
                                Isolated = m_Owner.RequiresIsolatedRenderer(area.Material),
                                RequiresTangents = RequiresTangentSpace(area.Material), TileSize = area.Material.GetEffectiveTileSizeMeters()
                            });
                        }
                        else
                        {
                            var work = m_Work;
                            m_Work = null;
                            var cancellation = m_Cancellation.Token;
                            m_Build = Task.Run(() => (TopologyRendererPreparation)work.Build(part, null, null, null, cancellation), cancellation);
                            m_PickingBuild = Task.Run(() => PreparePicking(part, cancellation), cancellation);
                            Drain = Task.WhenAll(m_Build, m_PickingBuild);
                            return false;
                        }
                    }
                } while (timer.Elapsed.TotalMilliseconds < 2);

                return m_Part == Object.Parts.Count;
            }

            private static TrianglePickCache PreparePicking(MeshPart part, CancellationToken token)
            {
                // The restored model is still detached, so workers can copy it safely.
                var areas = new System.Collections.Generic.HashSet<int>();
                foreach (var area in part.SurfaceAreas) areas.Add(area.Id);
                var capture = new ObjectPickCapture(new ObjectPickCacheState(), part.Mesh.Vertices, part.Mesh.Triangles, part.TriangleSurfaceAreaIds, areas);
                while (!capture.Step()) token.ThrowIfCancellationRequested();
                token.ThrowIfCancellationRequested();
                var result = capture.CreateBuild()();
                token.ThrowIfCancellationRequested();
                return result;
            }

            public void Publish()
            {
                if (m_Disposed || m_Published || m_Part != Object.Parts.Count || m_Owner.m_Instances.ContainsKey(Object.Id))
                    throw new InvalidOperationException("The restored renderer is not ready to publish.");
                m_Owner.m_Instances.Add(Object.Id, m_Instance);
                try
                {
                    m_Instance.Root.SetActive(true);
                    Object.ClearDirty(ProceduralObjectDirtyFlags.Transform | ProceduralObjectDirtyFlags.Geometry | ProceduralObjectDirtyFlags.Topology | ProceduralObjectDirtyFlags.Material);
                    m_Owner.m_PickSceneVersion++;
                    m_Published = true;
                }
                catch
                {
                    m_Owner.m_Instances.Remove(Object.Id);
                    m_Instance.Root.SetActive(false);
                    throw;
                }
            }

            public void Dispose()
            {
                if (m_Disposed) return;
                m_Disposed = true;
                m_Cancellation.Cancel();
                m_Upload?.Dispose();
                m_Upload = null;
                if (!m_Published)
                {
                    m_Instance.Root.SetActive(false);
                    m_Owner.m_RetiredTopologyParts.AddRange(m_Instance.Parts);
                    m_Instance.Parts.Clear();
                    UnityEngine.Object.Destroy(m_Instance.Root);
                }

                var build = m_Build;
                m_Build = null;
                var picking = m_PickingBuild;
                m_PickingBuild = null;
                if (build == null && picking == null)
                {
                    m_Cancellation.Dispose();
                    Drain = Task.CompletedTask;
                    return;
                }

                var cancellation = m_Cancellation;
                Drain = Task.WhenAll((Task)build ?? Task.CompletedTask, (Task)picking ?? Task.CompletedTask).ContinueWith(completed =>
                {
                    var ignored = completed.Exception;
                    if (build?.Status == TaskStatus.RanToCompletion) build.Result.Dispose();
                    cancellation.Dispose();
                }, TaskScheduler.Default);
            }
        }
    }
}