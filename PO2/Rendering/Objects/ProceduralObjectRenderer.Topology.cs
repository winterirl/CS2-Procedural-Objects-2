using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using PO2.Core;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.History;
using PO2.Materials;
using PO2.Rendering;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        internal ITopologyRenderCapture CaptureTopology(ProceduralObject obj, int index, MeshPart candidate, TopologyReplayProof proof = null, TopologyRenderCaptureMode mode = TopologyRenderCaptureMode.Full)
        {
            if (!m_Instances.TryGetValue(obj.Id, out var instance) || instance.Parts.Count != obj.Parts.Count)
                throw new InvalidOperationException("The renderer is not ready for topology editing.");
            return new TopologyCapture(this, instance, obj, index, candidate, proof, mode);
        }

        internal ITopologyPublication StageTopology(ProceduralObject obj, int index, MeshPart candidate, ITopologyRenderData data)
        {
            if (!(data is TopologyRendererPreparation prepared) || !m_Instances.TryGetValue(obj.Id, out var instance))
                throw new InvalidOperationException("Missing prepared topology renderer.");
            return new TopologyPublication(this, instance, obj, index, candidate, prepared);
        }

        private sealed class TopologyCapture : ITopologyRenderCapture
        {
            private readonly ProceduralObjectRenderer m_Owner;
            private readonly RenderInstance m_Instance;
            private readonly RenderPart m_Render;
            private readonly ProceduralObject m_Object;
            private readonly MeshPart m_Candidate;
            private readonly int m_Index;
            private readonly TopologyTargetStamp m_Stamp;
            private readonly BudgetedCopy m_Copy = new BudgetedCopy();
            private readonly TopologyRendererWork m_Work = new TopologyRendererWork();
            private readonly Dictionary<int, RenderArea> m_Areas = new Dictionary<int, RenderArea>();
            private readonly Dictionary<Mesh, Mesh.MeshDataArray> m_Leases = new Dictionary<Mesh, Mesh.MeshDataArray>();
            private readonly List<Action> m_Finish = new List<Action>();
            private readonly List<SurfaceArea> m_CaptureAreas = new List<SurfaceArea>();
            private int m_Area;
            private bool m_Started, m_Finished, m_RequirementsReady;
            public long CapturedBytes => m_Copy.Bytes;

            internal TopologyCapture(ProceduralObjectRenderer owner, RenderInstance instance, ProceduralObject obj, int index, MeshPart candidate, TopologyReplayProof proof, TopologyRenderCaptureMode mode)
            {
                m_Owner = owner;
                m_Instance = instance;
                m_Object = obj;
                m_Index = index;
                m_Render = instance.Parts[index];
                m_Stamp = new TopologyTargetStamp(obj, index);
                m_Work.Scale = obj.Transform.Scale;
                m_Work.ReplayProof = proof;
                m_Candidate = candidate;
                m_Work.CaptureMode = mode;
                foreach (var area in m_Render.Areas) m_Areas.Add(area.SurfaceAreaId, area);
                var ids = new HashSet<int>();
                foreach (var area in candidate.SurfaceAreas)
                {
                    m_CaptureAreas.Add(area);
                    ids.Add(area.Id);
                }

                foreach (var area in obj.Parts[index].SurfaceAreas)
                    if (ids.Add(area.Id))
                        m_CaptureAreas.Add(area);
                if (!Matches()) throw new InvalidOperationException("Wait for the current renderer update before editing topology.");
            }

            private bool Matches() => m_Stamp.Matches(m_Object, m_Index) && m_Index < m_Instance.Parts.Count && ReferenceEquals(m_Instance.Parts[m_Index], m_Render) && m_Render.TopologyVersion == m_Object.Parts[m_Index].TopologyVersion && m_Render.GeometryVersion == m_Object.Parts[m_Index].GeometryVersion && m_Render.SurfaceAreaMembershipVersion == m_Object.Parts[m_Index].SurfaceAreaMembershipVersion;

            public bool Poll(CancellationToken token, long deadlineTimestamp = 0)
            {
                token.ThrowIfCancellationRequested();
                if (!Matches()) throw new InvalidOperationException("The renderer changed during topology capture.");
                if (!m_RequirementsReady)
                {
                    if (m_Area < m_CaptureAreas.Count)
                    {
                        CollectRequirements(m_CaptureAreas[m_Area++]);
                        return false;
                    }

                    FinishRequirements();
                    m_RequirementsReady = true;
                    m_Area = 0;
                }

                if (m_Area < m_CaptureAreas.Count)
                {
                    CaptureArea(m_CaptureAreas[m_Area++]);
                    return false;
                }

                if (!m_Started)
                {
                    if (m_Copy.Bytes != 0) m_Copy.Start(token);
                    m_Started = true;
                }

                if (m_Copy.Bytes != 0 && !m_Copy.Poll(Matches, token, deadlineTimestamp: deadlineTimestamp)) return false;
                if (!m_Finished)
                {
                    foreach (var finish in m_Finish) finish();
                    m_Finish.Clear();
                    m_Finished = true;
                }

                return true;
            }

            private void CollectRequirements(SurfaceArea source)
            {
                var settings = new TopologyRenderAreaWork
                {
                    Id = source.Id,
                    Isolated = m_Owner.RequiresIsolatedRenderer(source.Material),
                    RequiresTangents = RequiresTangentSpace(source.Material),
                    UnsupportedInterpolation = source.NativeWindowKind != NativeWindowKind.None || source.Material.VanillaSpecialKind != VanillaSpecialMaterialKind.None || m_Owner.IsWindowSurface(source.Material),
                    TileSize = source.Material.GetEffectiveTileSizeMeters()
                };
                m_Work.Areas.Add(source.Id, settings);
                if (!m_Areas.TryGetValue(source.Id, out var area)) return;
                settings.UsesSourceTangents = area.UsesSourceTangents && (area.IsolatedRenderer || m_Render.UsesSourceTangents);
                settings.CurrentRequired = m_Work.CaptureMode != TopologyRenderCaptureMode.Mirror;
            }

            private void FinishRequirements()
            {
                if (m_Work.CaptureMode != TopologyRenderCaptureMode.Mirror) return;
                // Mirror keeps the active areas. Combined tangent generation can
                // also affect areas that have their own source tangent stream.
                bool combinedFallback = false;
                foreach (var area in m_Candidate.SurfaceAreas)
                    if (m_Areas.ContainsKey(area.Id) && TopologyRendererWork.ForcesCombinedTangents(m_Work.Areas[area.Id], area.Material.Mapping.Mode, m_Candidate.Mesh.Tangents != null))
                        combinedFallback = true;
                foreach (var area in m_Candidate.SurfaceAreas)
                {
                    var settings = m_Work.Areas[area.Id];
                    settings.CurrentRequired = settings.RequiresTangents && !settings.UsesSourceTangents || TopologyRendererWork.GeneratesTangents(settings, area.Material.Mapping.Mode != TextureMappingMode.Original, m_Candidate.Mesh.Tangents != null, combinedFallback);
                    // Empty areas do not have a current mesh or a replay proof.
                    if (!m_Areas.ContainsKey(area.Id)) settings.CurrentRequired = false;
                }
            }

            private void CaptureArea(SurfaceArea source)
            {
                var settings = m_Work.Areas[source.Id];
                if (!settings.CurrentRequired || !m_Areas.TryGetValue(source.Id, out var area)) return;
                var runtime = area.GeneratedMapping;
                int count = runtime != null ? runtime.Vertices.Length : area.AreaMesh.Vertices.Length;
                int[] areaSource = area.SourceVertexIndices, generatedSource = runtime?.SourceVertexIndices;
                var mapping = m_Work.CaptureMode == TopologyRenderCaptureMode.Full ? m_Copy.Add(count, 4, i => areaSource[generatedSource == null ? i : generatedSource[i]]) : null;
                if (m_Work.CaptureMode == TopologyRenderCaptureMode.Array && runtime != null && !runtime.UsesSurfaceUVs)
                {
                    if (runtime.PreservedSeed != null) settings.Seed = runtime.PreservedSeed;
                    else
                    {
                        int chartCount = runtime.ChartCount;
                        var charts = m_Copy.Add(runtime.TriangleCharts, 4);
                        var corners = m_Copy.Add(runtime.Triangles.Length, 4, i => areaSource[generatedSource[runtime.Triangles[i]]]);
                        var coordinates = m_Copy.Add(runtime.Triangles.Length, 8, i => runtime.RawCoordinate(runtime.Triangles[i], m_Work.Scale));
                        m_Finish.Add(() => settings.CompleteSeed = () => new GeneratedMappingSeed(corners.Data, charts.Data, coordinates.Data, GeneratedMappingSeed.Measure(settings.Current, m_Work.Scale, charts.Data, coordinates.Data, chartCount)));
                    }
                }

                var triangles = m_Copy.Add(runtime != null ? runtime.Triangles : area.AreaMesh.Triangles, 4);
                BudgetedCopy.Stream<Vector3> positions, normals;
                BudgetedCopy.Stream<Vector4> uv, uv1, colorspace, tangents;
                BudgetedCopy.Stream<Color32> colors;
                if (area.IsolatedRenderer)
                {
                    positions = m_Copy.Add(runtime != null ? runtime.Vertices : area.AreaMesh.Vertices, 12);
                    normals = m_Copy.Add(runtime != null ? runtime.Normals : area.AreaMesh.Normals, 12);
                    uv = runtime != null ? m_Copy.Add(count, 16, i => new Vector4(runtime.UVs[i].x, runtime.UVs[i].y, 0, 0)) : m_Copy.Add(area.AreaMesh.UV0, 16);
                    colors = m_Copy.Add(runtime == null ? area.AreaMesh.VertexColors : null, 4);
                    uv1 = m_Copy.Add(runtime == null ? area.AreaMesh.TexCoord1 : null, 16);
                    colorspace = m_Copy.Add(runtime == null ? area.AreaMesh.TexCoord3 : null, 16);
                }
                else
                {
                    int offset = area.CombinedVertexOffset;
                    var p = m_Render.CombinedVertices;
                    var n = m_Render.CombinedNormals;
                    var u = m_Render.CombinedUVs;
                    var c = m_Render.CombinedColors;
                    var t = m_Render.CombinedTexCoord3;
                    var t1 = m_Render.CombinedTexCoord1;
                    positions = m_Copy.Add(count, 12, i => p[offset + i]);
                    normals = m_Copy.Add(count, 12, i => n[offset + i]);
                    uv = m_Copy.Add(count, 16, i => u[offset + i]);
                    colors = c.Length == p.Length ? m_Copy.Add(count, 4, i => c[offset + i]) : null;
                    uv1 = area.AreaMesh.TexCoord1 != null && t1.Length == p.Length ? m_Copy.Add(count, 16, i => t1[offset + i]) : null;
                    colorspace = t.Length == p.Length ? m_Copy.Add(count, 16, i => t[offset + i]) : null;
                }

                tangents = CaptureTangents(area.IsolatedRenderer ? area.IsolatedMesh : m_Render.Mesh, area.IsolatedRenderer ? 0 : area.CombinedVertexOffset, count);
                m_Finish.Add(() =>
                {
                    var p = positions.Data;
                    var n = normals?.Data;
                    var t = tangents?.Data;
                    var u = uv?.Data;
                    var c = colors?.Data;
                    var tc1 = uv1?.Data;
                    var tc = colorspace?.Data;
                    var indices = triangles.Data;
                    settings.RuntimeToPart = mapping?.Data;
                    // Validation of the captured arrays also runs on the worker.
                    settings.CompleteCapture = () => new MeshData(p, n, t, u, c, tc1, tc, indices);
                });
            }

            private BudgetedCopy.Stream<Vector4> CaptureTangents(Mesh mesh, int offset, int count)
            {
                if (mesh == null || !mesh.HasVertexAttribute(VertexAttribute.Tangent)) return null;
                if (!m_Leases.TryGetValue(mesh, out var lease))
                {
                    lease = Mesh.AcquireReadOnlyMeshData(mesh);
                    m_Leases.Add(mesh, lease);
                }

                var data = lease[0];
                if (data.GetVertexAttributeFormat(VertexAttribute.Tangent) != VertexAttributeFormat.Float32 || data.GetVertexAttributeDimension(VertexAttribute.Tangent) != 4)
                    throw new InvalidOperationException("Unsupported renderer tangent format.");
                int stream = data.GetVertexAttributeStream(VertexAttribute.Tangent);
                int stride = data.GetVertexBufferStride(stream) / 4, start = data.GetVertexAttributeOffset(VertexAttribute.Tangent) / 4;
                var raw = data.GetVertexData<float>(stream);
                return m_Copy.Add(count, 16, i =>
                {
                    int at = (offset + i) * stride + start;
                    return new Vector4(raw[at], raw[at + 1], raw[at + 2], raw[at + 3]);
                });
            }

            public System.Threading.Tasks.Task Drain => m_Copy.Allocation;

            public ITopologyRenderWork Finish()
            {
                if (!m_Finished || !Matches()) throw new InvalidOperationException("Incomplete renderer capture.");
                return m_Work;
            }

            public void Dispose()
            {
                foreach (var lease in m_Leases.Values) lease.Dispose();
                m_Leases.Clear();
                m_Finish.Clear();
            }
        }

        private sealed class TopologyUpload
        {
            private readonly Mesh m_Mesh;
            private readonly TopologyMeshUpload m_Data;
            private int m_Step, m_Vertex, m_Index, m_Submesh;
            private bool m_RecalculateTangents;
            private const MeshUpdateFlags kFlags = RenderVertexStreams.kBatchFlags | MeshUpdateFlags.DontValidateIndices;

            internal TopologyUpload(Mesh mesh, TopologyMeshUpload data, bool tangents = false)
            {
                m_Mesh = mesh;
                m_Data = data;
                m_RecalculateTangents = tangents;
            }

            internal bool Poll()
            {
                if (m_Step == 0)
                {
                    m_Mesh.SetVertexBufferParams(m_Data.VertexCount, m_Data.Layout.Attributes);
                    m_Step++;
                    return false;
                }

                if (m_Step == 1)
                {
                    m_Mesh.SetIndexBufferParams(m_Data.Indices.Length, IndexFormat.UInt32);
                    m_Step++;
                    return false;
                }

                if (m_Vertex < m_Data.VertexCount)
                {
                    int count = Math.Min(2048, m_Data.VertexCount - m_Vertex);
                    var layout = m_Data.Layout;
                    RenderVertexStreams.Submit(m_Mesh, m_Data.Positions, m_Vertex, m_Vertex, count, 0, 12, RenderVertexStreams.Role.Position);
                    if (layout.Normal >= 0) RenderVertexStreams.Submit(m_Mesh, m_Data.Directions, m_Vertex, m_Vertex, count, layout.Normal, 12, RenderVertexStreams.Role.Normal);
                    if (layout.Tangent >= 0) RenderVertexStreams.Submit(m_Mesh, m_Data.SourceTangents, m_Vertex, m_Vertex, count, layout.Tangent, 16, RenderVertexStreams.Role.Tangent);
                    if (layout.Auxiliary >= 0) RenderVertexStreams.Submit(m_Mesh, m_Data.Auxiliary, m_Vertex * layout.AuxiliaryStride, m_Vertex * layout.AuxiliaryStride, count * layout.AuxiliaryStride, layout.Auxiliary, 1, RenderVertexStreams.Role.Auxiliary);
                    m_Vertex += count;
                    return false;
                }

                if (m_Index < m_Data.Indices.Length)
                {
                    int count = Math.Min(16384, m_Data.Indices.Length - m_Index);
                    m_Mesh.SetIndexBufferData(m_Data.Indices, m_Index, m_Index, count, kFlags);
                    m_Index += count;
                    return false;
                }

                if (m_Step == 2)
                {
                    m_Mesh.subMeshCount = m_Data.SubmeshCounts.Length;
                    m_Step++;
                    return false;
                }

                if (m_Submesh < m_Data.SubmeshCounts.Length)
                {
                    var descriptor = new SubMeshDescriptor(m_Data.SubmeshOffsets[m_Submesh], m_Data.SubmeshCounts[m_Submesh], UnityEngine.MeshTopology.Triangles)
                    {
                        bounds = m_Data.Bounds, firstVertex = 0, vertexCount = m_Data.VertexCount
                    };
                    m_Mesh.SetSubMesh(m_Submesh++, descriptor, kFlags);
                    return false;
                }

                m_Mesh.bounds = m_Data.Bounds;
                if (m_RecalculateTangents)
                {
                    RenderVertexStreams.RecalculateTangents(m_Mesh);
                    m_RecalculateTangents = false;
                    return false;
                }

                m_Mesh.MarkModified();
                return true; // LOD0 stays dynamic and readable.
            }
        }

        private sealed class TopologyPublication : ITopologyPublication
        {
            private readonly ProceduralObjectRenderer m_Owner;
            private readonly RenderInstance m_Instance;
            private readonly ProceduralObject m_Object;
            private readonly MeshPart m_Candidate;
            private TopologyRendererPreparation m_Data;
            private readonly bool m_Append;
            private readonly int m_Index;
            private readonly RenderPart m_Old;
            private RenderPart m_Staging;
            private readonly List<Material> m_Materials = new List<Material>();
            private TopologyUpload m_Upload;
            private int m_Area, m_Properties;
            private bool m_MainStarted, m_Ready, m_Adopted, m_Committed;

            internal TopologyPublication(ProceduralObjectRenderer owner, RenderInstance instance, ProceduralObject obj, int index, MeshPart candidate, TopologyRendererPreparation data, bool append = false)
            {
                m_Owner = owner;
                m_Instance = instance;
                m_Object = obj;
                m_Index = index;
                m_Candidate = candidate;
                m_Data = data;
                m_Append = append;
                m_Old = append ? null : instance.Parts[index];
                var go = new GameObject($"Part {index}: {candidate.Name}");
                Mesh mesh = null;
                try
                {
                    go.SetActive(false);
                    go.transform.SetParent(instance.Root.transform, false);
                    mesh = new Mesh { name = $"PO2 Part {index}" };
                    mesh.MarkDynamic();
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    m_Staging = new RenderPart
                    {
                        GameObject = go, Mesh = mesh, Renderer = go.AddComponent<MeshRenderer>(),
                        ReceivesDecals = obj.ReceivesDecals, PickSource = append ? candidate : obj.Parts[index]
                    };
                    ApplyRendererDecalLayers(m_Staging.Renderer, obj.ReceivesDecals);
                }
                catch
                {
                    UnityEngine.Object.Destroy(mesh);
                    UnityEngine.Object.Destroy(go);
                    throw;
                }
            }

            public bool Poll(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (m_Ready) return true;
                if (m_Upload != null)
                {
                    if (!m_Upload.Poll()) return false;
                    m_Upload = null;
                    return false;
                }

                if (m_Area < m_Data.Areas.Length)
                {
                    AddArea(m_Data.Areas[m_Area++]);
                    return false;
                }

                if (!m_MainStarted)
                {
                    var mesh = m_Data.Combined;
                    m_Staging.CombinedVertices = mesh.Vertices;
                    m_Staging.CombinedNormals = mesh.Normals;
                    m_Staging.CombinedUVs = mesh.UV0;
                    m_Staging.CombinedTangents = mesh.Tangents ?? Array.Empty<Vector4>();
                    m_Staging.CombinedColors = mesh.VertexColors ?? Array.Empty<Color32>();
                    m_Staging.CombinedTexCoord1 = mesh.TexCoord1 ?? Array.Empty<Vector4>();
                    m_Staging.CombinedTexCoord3 = mesh.TexCoord3 ?? Array.Empty<Vector4>();
                    m_Staging.CombinedUV0Dimension = m_Data.UV0Dimension;
                    m_Staging.HasTangentStream = m_Data.RequiresTangents;
                    // Generated areas retain their normal mapping refresh path.
                    bool sourceTangents = true;
                    foreach (var area in m_Staging.Areas)
                        if (!area.IsolatedRenderer && !area.UsesSourceTangents)
                            sourceTangents = false;
                    m_Staging.UsesSourceTangents = m_Data.RequiresTangents && sourceTangents;
                    m_Staging.HasMainRendererGeometry = m_Materials.Count > 0;
                    m_Staging.Renderer.sharedMaterials = m_Materials.ToArray();
                    m_Upload = new TopologyUpload(m_Staging.Mesh, m_Data.Upload, m_Data.RecalculateCombinedTangents);
                    m_MainStarted = true;
                    return false;
                }

                if (m_Properties < m_Staging.Areas.Count)
                {
                    m_Staging.DeleteHovered = m_Instance.DeleteHovered;
                    ApplyAreaPropertyBlock(m_Staging, m_Staging.Areas[m_Properties++], m_Instance.Hovered);
                    return false;
                }

                m_Owner.PrepareGeometryUpdates(m_Staging, m_Candidate, m_Data.GeometryUpdates);
                m_Data.GeometryUpdates = null;
                // Appends can stage many parts. Drop upload scratch as each one finishes.
                if (m_Append)
                {
                    m_Data.Dispose();
                    m_Data = null;
                }

                m_Ready = true;
                return true;
            }

            private void AddArea(TopologyRenderAreaPreparation data)
            {
                if (!m_Candidate.TryGetSurfaceArea(data.Settings.Id, out var source) || m_Owner.RequiresIsolatedRenderer(source.Material) != data.Settings.Isolated || RequiresTangentSpace(source.Material) != data.Settings.RequiresTangents || !source.Material.GetEffectiveTileSizeMeters().Equals(data.Settings.TileSize))
                    throw new InvalidOperationException("Material requirements changed during topology preparation.");
                var area = new RenderArea
                {
                    SurfaceAreaId = source.Id, MaterialIndex = data.MaterialIndex,
                    MaterialSourceVersion = source.MaterialSourceVersion, MaterialPropertiesVersion = source.MaterialPropertiesVersion,
                    MappingVersion = source.MappingVersion, ColorMaskVersion = source.ColorMaskVersion,
                    MaterialSourceKind = source.Material.SourceKind, DefinitionId = source.Material.DefinitionId,
                    SurfaceGuid = source.Material.SurfaceGuid, VanillaSpecialKind = source.Material.VanillaSpecialKind,
                    ResolvedSurfaceGuid = ResolveNativeSurfaceGuid(source.Material), ReceivesDecals = m_Staging.ReceivesDecals,
                    Metallic = source.Material.Metallic, Smoothness = source.Material.Smoothness, CoatStrength = source.Material.CoatStrength,
                    MappingMode = source.Material.Mapping.Mode, AreaMesh = data.Source, SourceVertexIndices = data.SourceVertexIndices,
                    AreaTopology = data.Topology, GeneratedMapping = data.Mapping, CombinedVertexOffset = data.CombinedOffset,
                    CombinedVertexCount = data.Runtime.Vertices.Length, IsolatedRenderer = data.Settings.Isolated,
                    UsesSourceTangents = data.Mapping == null && data.Source.Tangents != null, UV0Dimension = data.UV0Dimension
                };
                m_Staging.Areas.Add(area);
                area.Material = m_Owner.AcquireMaterial(source.Material, m_Staging.ReceivesDecals);
                CopyColorMaskState(area, source.ColorMask);
                if (!area.IsolatedRenderer)
                {
                    m_Materials.Add(area.Material);
                    return;
                }

                var go = new GameObject($"Isolated Surface {area.SurfaceAreaId}: {m_Candidate.Name}");
                go.transform.SetParent(m_Staging.GameObject.transform, false);
                area.IsolatedGameObject = go;
                area.IsolatedMesh = new Mesh { name = $"PO2 Isolated Surface {area.SurfaceAreaId}" };
                area.IsolatedMesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = area.IsolatedMesh;
                area.IsolatedRendererComponent = go.AddComponent<MeshRenderer>();
                area.IsolatedRendererComponent.sharedMaterial = area.Material;
                ApplyRendererDecalLayers(area.IsolatedRendererComponent, m_Staging.ReceivesDecals);
                m_Upload = new TopologyUpload(area.IsolatedMesh, data.Upload, data.RecalculateTangents);
            }

            public void Adopt()
            {
                if (!m_Ready || (m_Append ? m_Instance.Parts.Count != m_Index : !ReferenceEquals(m_Instance.Parts[m_Index], m_Old)))
                    throw new InvalidOperationException("The renderer target changed before publication.");
                SetTopologyVersions(m_Staging, m_Object.Parts[m_Index], m_Object.Transform.Scale);
                m_Owner.AdoptGeometryUpdates(m_Staging, m_Object.Parts[m_Index]);
                m_Adopted = true;
                m_Owner.m_PickSceneVersion++;
                if (m_Append) m_Instance.Parts.Add(m_Staging);
                else
                {
                    m_Old.GameObject.SetActive(false);
                    m_Instance.Parts[m_Index] = m_Staging;
                }

                m_Staging.GameObject.SetActive(true);
            }

            public void Rollback()
            {
                if (m_Committed || !m_Adopted && (m_Append || !ReferenceEquals(m_Instance.Parts[m_Index], m_Old))) return;
                // The model may have been restored even if adoption failed before
                // swapping renderers. Refresh the retained renderer's versions too.
                m_Staging.GameObject.SetActive(false);
                if (m_Append)
                {
                    m_Instance.Parts.RemoveAt(m_Index);
                    m_Owner.m_PickSceneVersion++;
                    m_Adopted = false;
                    return;
                }

                m_Instance.Parts[m_Index] = m_Old;
                SetTopologyVersions(m_Old, m_Object.Parts[m_Index], m_Object.Transform.Scale);
                m_Owner.AdoptGeometryUpdates(m_Old, m_Object.Parts[m_Index]);
                m_Old.GameObject.SetActive(true);
                m_Owner.m_PickSceneVersion++;
                m_Adopted = false;
            }

            public void Commit()
            {
                m_Committed = true;
                if (m_Old != null) m_Owner.m_RetiredTopologyParts.Add(m_Old);
                m_Staging = null;
            }

            public void Dispose()
            {
                m_Data?.Dispose();
                m_Data = null;
                if (m_Committed || m_Staging == null) return;
                if (m_Adopted) Rollback();
                if (m_Staging.GameObject != null) m_Staging.GameObject.SetActive(false);
                m_Owner.m_RetiredTopologyParts.Add(m_Staging);
                m_Staging = null;
            }
        }

        private static void SetTopologyVersions(RenderPart render, MeshPart part, Vector3 scale)
        {
            render.GeometryVersion = part.GeometryVersion;
            render.PositionVersion = part.PositionVersion;
            render.FullGeometryVersion = part.FullGeometryVersion;
            render.DirectionVersion = part.DirectionVersion;
            render.TopologyVersion = part.TopologyVersion;
            render.SurfaceAreaMembershipVersion = part.SurfaceAreaMembershipVersion;
            render.MappingScale = scale;
            render.PickSource = part;
        }

        private readonly List<RenderPart> m_RetiredTopologyParts = new List<RenderPart>();

        internal void PollTopologyCleanup(bool drain = false)
        {
            PollArrayPreviewCleanup(drain);
            var timer = Stopwatch.StartNew();
            do
            {
                if (m_RetiredTopologyParts.Count == 0) return;
                var part = m_RetiredTopologyParts[m_RetiredTopologyParts.Count - 1];
                if (part.Areas.Count > 0)
                {
                    var area = part.Areas[part.Areas.Count - 1];
                    part.Areas.RemoveAt(part.Areas.Count - 1);
                    ReleaseMaterial(area);
                    if (area.IsolatedMesh != null) UnityEngine.Object.Destroy(area.IsolatedMesh);
                    if (area.IsolatedGameObject != null) UnityEngine.Object.Destroy(area.IsolatedGameObject);
                }
                else
                {
                    ReleaseGeometryUpdates(part);
                    part.PickCache.Release();
                    if (part.Mesh != null) UnityEngine.Object.Destroy(part.Mesh);
                    if (part.GameObject != null) UnityEngine.Object.Destroy(part.GameObject);
                    m_RetiredTopologyParts.RemoveAt(m_RetiredTopologyParts.Count - 1);
                }
            } while (drain || timer.Elapsed.TotalMilliseconds < 1);
        }
    }
}