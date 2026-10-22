using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using PO2.Core;
using PO2.Geometry;
using PO2.Rendering;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        private const long kUpdatePartBytes = RenderGeometryPreparation.kPartLimit;
        private const long kUpdateTotalBytes = 128L * 1024 * 1024;
        private long m_GeometryUpdateBytes;

        internal void NoticeGeometryChange(ProceduralObject obj, GeometryChange change)
        {
            if (obj == null || !m_Instances.TryGetValue(obj.Id, out var instance) || !ReferenceEquals(instance.Source, obj)) return;
            if ((uint)change.PartIndex >= (uint)instance.Parts.Count || (uint)change.PartIndex >= (uint)obj.Parts.Count || !ReferenceEquals(obj.Parts[change.PartIndex], change.Part))
            {
                InvalidateGeometryChanges(obj);
                return;
            }

            var state = instance.Parts[change.PartIndex].GeometryUpdates;
            if (state == null) return;
            if (!state.Notice(change))
            {
            }
        }

        internal void InvalidateGeometryChanges(ProceduralObject obj)
        {
            if (obj == null || !m_Instances.TryGetValue(obj.Id, out var instance) || !ReferenceEquals(instance.Source, obj)) return;
            foreach (var part in instance.Parts) part.GeometryUpdates?.Invalidate();
        }

        private void ReleaseGeometryUpdates(RenderPart part)
        {
            if (part.GeometryUpdates == null) return;
            m_GeometryUpdateBytes -= part.GeometryUpdates.Bytes;
            part.GeometryUpdates.Dispose();
            part.GeometryUpdates = null;
        }

        private void PrepareGeometryUpdates(RenderPart part, MeshPart source, RenderGeometryPreparation prepared = null)
        {
            ReleaseGeometryUpdates(part);
            long estimate = RenderGeometryState.Estimate(part, source);
            if (estimate > kUpdatePartBytes || estimate > kUpdateTotalBytes - m_GeometryUpdateBytes)
            {
                prepared?.Dispose();
                return;
            }

            var state = RenderGeometryState.TryCreate(part, source, prepared);
            if (state == null)
            {
                return;
            }

            part.GeometryUpdates = state;
            m_GeometryUpdateBytes += state.Bytes;
        }

        private void AdoptGeometryUpdates(RenderPart part, MeshPart source)
        {
            if (part.GeometryUpdates != null && !part.GeometryUpdates.AdoptSource(part, source))
                PrepareGeometryUpdates(part, source);
        }

        private void CompleteGeometryUpdate(RenderPart part, MeshPart source, bool precise)
        {
            var state = part.GeometryUpdates;
            if (state == null) return; // An explicit budget fallback stays a fallback until the next layout.
            if (!state.Matches(part, source))
            {
                PrepareGeometryUpdates(part, source);
                return;
            }

            state.Complete(source.GeometryVersion, precise);
        }

        private bool TryRefreshPreciseGeometry(RenderPart part, MeshPart source, Vector3 scale)
        {
            var state = part.GeometryUpdates;
            if (state == null || !state.Ready(part, source)) return false;
            // Resolve all area/layout requirements before touching the cached values.
            for (int i = 0; i < state.Areas.Length; i++)
            {
                var area = state.Areas[i].Area;
                if (!source.TryGetSurfaceArea(area.SurfaceAreaId, out var settings) || settings.MappingVersion != area.MappingVersion || settings.MaterialSourceVersion != area.MaterialSourceVersion)
                    return false;
            }

            state.PrepareValues();
            try
            {
                bool broadMain = false;
                for (int i = 0; i < state.Areas.Length; i++)
                {
                    var update = state.Areas[i];
                    var area = update.Area;
                    if (update.PositionCount == 0 && update.NormalCount == 0) continue;
                    if (area.GeneratedMapping != null)
                    {
                        source.TryGetSurfaceArea(area.SurfaceAreaId, out var settings);
                        bool changed = area.GeneratedMapping.Update(area.AreaMesh, area.AreaTopology, scale, settings.Material.Mapping, settings.Material.GetEffectiveTileSizeMeters(), false);
                        if (changed || area.GeneratedMapping.Vertices.Length != area.CombinedVertexCount) return false;
                        if (area.IsolatedRenderer)
                        {
                            WriteIsolatedAreaStreams(area, area.GeneratedMapping.Vertices, area.GeneratedMapping.Normals, ConvertUV2ToUV4(area.GeneratedMapping.UVs), null, null, null, null, RequiresTangentSpace(settings.Material), false);
                            update.Target.RefreshBounds();
                        }
                        else
                        {
                            CopyAreaStreams(part, area, area.GeneratedMapping.Vertices, area.GeneratedMapping.Normals, area.GeneratedMapping.UVs);
                            broadMain = true;
                        }

                        continue;
                    }
                    // Original-mapped caches and destination sets were filled together by the kernel.
                }

                // All source and destination values are ready before the first ordinary upload.
                // A failure returns through the existing full refresh, using current source data.
                foreach (var target in state.Targets)
                {
                    if (target.Main && broadMain)
                    {
                        WriteIncrementalUnityMeshStreams(part);
                        target.RefreshBounds();
                    }
                    else if (target.DirtyPositions.Count != 0 || target.DirtyNormals.Count != 0)
                    {
                        if (!target.RangesReady)
                        {
                            // Native tangent generation may have repacked a mesh. Keep its
                            // compatible broad writer instead of resetting the layout every frame.
                            if (target.Main) WriteSourceChanges(part.Mesh, part.CombinedVertices, part.CombinedNormals, part.CombinedTangents, target.DirtyPositions.Count != 0, target.DirtyNormals.Count != 0, part.HasTangentStream, part.UsesSourceTangents, true);
                            else
                            {
                                var area = target.Area;
                                source.TryGetSurfaceArea(area.SurfaceAreaId, out var settings);
                                WriteSourceChanges(area.IsolatedMesh, area.AreaMesh.Vertices, area.AreaMesh.Normals, area.AreaMesh.Tangents, target.DirtyPositions.Count != 0, target.DirtyNormals.Count != 0, RequiresTangentSpace(settings.Material), area.UsesSourceTangents, true);
                            }

                            target.RefreshBounds();
                        }
                        else target.Upload();
                    }
                }
            }
            catch (Exception)
            {
                state.Invalidate();
                return false;
            }

            return true;
        }

        private sealed class RenderAreaUpdate
        {
            internal readonly RenderArea Area;
            internal readonly int[] Mapping;
            internal readonly Vector3[] PositionsReference, NormalsReference;
            internal readonly RenderMeshUpdate Target;
            internal int PositionCount, NormalCount, TargetIndex;

            internal RenderAreaUpdate(RenderArea area, RenderMeshUpdate target)
            {
                Area = area;
                Target = target;
                Mapping = area.SourceVertexIndices;
                PositionsReference = area.AreaMesh.Vertices;
                NormalsReference = area.AreaMesh.Normals;
            }

            internal bool Matches() => ReferenceEquals(Mapping, Area.SourceVertexIndices) && ReferenceEquals(PositionsReference, Area.AreaMesh.Vertices) && ReferenceEquals(NormalsReference, Area.AreaMesh.Normals);

            internal void Clear()
            {
                PositionCount = NormalCount = 0;
            }
        }

        private sealed class RenderMeshUpdate
        {
            internal readonly RenderPart Part;
            internal readonly RenderArea Area;
            internal bool Main => Area == null;
            internal Mesh Mesh => Main ? Part.Mesh : Area.IsolatedMesh;
            internal Vector3[] Positions => Main ? Part.CombinedVertices : Area.GeneratedMapping != null ? Area.GeneratedMapping.Vertices : Area.AreaMesh.Vertices;
            internal Vector3[] Normals => Main ? Part.CombinedNormals : Area.GeneratedMapping != null ? Area.GeneratedMapping.Normals : Area.AreaMesh.Normals;
            private readonly Vector3[] m_Positions, m_Normals;
            internal readonly RenderDirtyIndices DirtyPositions, DirtyNormals;
            internal readonly RenderUploadPlan Plan = new RenderUploadPlan();
            internal RenderUploadPlan PositionPlan, NormalPlan;
            internal int BulkPositionCause, BulkNormalCause;
            internal readonly RenderBounds Bounds;
            private readonly Mesh m_Mesh;
            private int m_NormalStream;
            internal bool BulkPositions, BulkNormals, RangesReady;
            internal int MappedCopies, BulkThreshold, FragmentedThreshold;
            internal long Bytes => DirtyPositions.Bytes + DirtyNormals.Bytes + Bounds.Bytes + 512;

            internal RenderMeshUpdate(RenderPart part, RenderArea area, RenderBounds bounds)
            {
                Part = part;
                Area = area;
                m_Mesh = Mesh;
                m_Positions = Positions;
                m_Normals = Normals;
                DirtyPositions = new RenderDirtyIndices(Positions.Length);
                DirtyNormals = new RenderDirtyIndices(Positions.Length);
                Bounds = bounds ?? new RenderBounds(Positions);
            }

            internal bool Matches() => ReferenceEquals(Mesh, m_Mesh) && ReferenceEquals(Positions, m_Positions) && ReferenceEquals(Normals, m_Normals);

            internal bool CanUploadRanges()
            {
                if (!Matches() || Mesh.vertexCount != Positions.Length || !RenderVertexStreams.DynamicStream(Mesh, VertexAttribute.Position, 0, 3)) return false;
                m_NormalStream = Mesh.HasVertexAttribute(VertexAttribute.Normal) ? Mesh.GetVertexAttributeStream(VertexAttribute.Normal) : -1;
                return DirtyNormals.Count == 0 || (Normals != null && m_NormalStream >= 0 && RenderVertexStreams.DynamicStream(Mesh, VertexAttribute.Normal, m_NormalStream, 3));
            }

            internal void RefreshBounds()
            {
                Bounds.Rebuild(Positions);
            }

            internal void Upload()
            {
                UploadStream(Positions, DirtyPositions, 0, RenderVertexStreams.Role.Position, BulkPositions);
                if (DirtyNormals.Count != 0) UploadStream(Normals, DirtyNormals, m_NormalStream, RenderVertexStreams.Role.Normal, BulkNormals);
                bool tangentRecalculation = Main ? Part.HasTangentStream && !Part.UsesSourceTangents : Mesh.HasVertexAttribute(VertexAttribute.Tangent) && !Area.UsesSourceTangents;
                if (tangentRecalculation) RenderVertexStreams.RecalculateTangents(Mesh);
                if (DirtyPositions.Count != 0)
                {
                    Bounds.UpdatePrepared(Positions, BulkPositions);
                    RenderVertexStreams.PublishBounds(Mesh, Bounds.Value);
                }
                else RenderVertexStreams.Notify(Mesh);
            }

            private void UploadStream(Vector3[] values, RenderDirtyIndices dirty, int stream, RenderVertexStreams.Role role, bool bulk)
            {
                if (dirty.Count == 0) return;
                var plan = role == RenderVertexStreams.Role.Position ? PositionPlan : NormalPlan;
                if (plan == null)
                {
                    Plan.Build(dirty, 12, bulk);
                    plan = Plan;
                }

                for (int i = 0; i < plan.RangeCount; i++)
                {
                    var range = plan.Ranges[i];
                    RenderVertexStreams.Submit(Mesh, values, range.Start, range.Start, range.Count, stream, 12, role);
                }
            }

            internal void Clear()
            {
                DirtyPositions.Clear();
                DirtyNormals.Clear();
                Bounds.Pending.Clear();
                BulkPositions = BulkNormals = false;
                BulkPositionCause = BulkNormalCause = 0;
                PositionPlan = NormalPlan = null;
            }
        }

        private sealed partial class RenderGeometryState : IDisposable
        {
            private readonly RenderGeometryPreparation.Reservation m_Budget;
            internal readonly int[] Offsets;
            internal readonly RenderGeometryPreparation.Copy[] Copies;
            internal readonly RenderAreaUpdate[] Areas;
            internal readonly RenderMeshUpdate[] Targets;
            internal readonly RenderDirtyIndices Positions, Normals;
            internal readonly long Bytes;
            private MeshPart m_Source;
            private MeshData m_Mesh;
            private readonly Vector3[] m_Positions, m_Normals;
            private readonly Vector4[] m_Tangents, m_UV, m_TC1, m_TC3;
            private readonly Color32[] m_Colors;
            private uint m_Topology, m_Membership, m_Full;
            private uint m_Covered;
            private bool m_Valid = true;

            internal static long Estimate(RenderPart part, MeshPart source)
            {
                long sourceCount = source.Mesh.Vertices.Length, copies = 0, rendered = part.CombinedVertices.Length;
                foreach (var area in part.Areas)
                {
                    copies += area.SourceVertexIndices.Length;
                    if (area.IsolatedRenderer) rendered += area.CombinedVertexCount;
                }

                // Include the temporary CSR cursors and padded bounds trees before allocating.
                return RenderGeometryPreparation.Estimate((int)sourceCount, copies, rendered, part.Areas.Count);
            }

            internal static RenderGeometryState TryCreate(RenderPart part, MeshPart source, RenderGeometryPreparation prepared)
            {
                var maps = new int[part.Areas.Count][];
                var isolated = new Vector3[maps.Length][];
                for (int i = 0; i < maps.Length; i++)
                {
                    var area = part.Areas[i];
                    maps[i] = area.SourceVertexIndices;
                    if (maps[i].Length != area.AreaMesh.Vertices.Length) throw new InvalidOperationException("Invalid renderer area mapping.");
                    if (area.IsolatedRenderer) isolated[i] = area.GeneratedMapping?.Vertices ?? area.AreaMesh.Vertices;
                }

                var mainPositions = part.HasMainRendererGeometry ? part.CombinedVertices : null;
                bool preparedMatches = prepared != null && prepared.Budget != null && prepared.SourceCount == source.Mesh.Vertices.Length && prepared.Maps.Length == maps.Length && ReferenceEquals(prepared.MainPositions, mainPositions);
                for (int i = 0; preparedMatches && i < maps.Length; i++)
                    preparedMatches = ReferenceEquals(prepared.Maps[i], maps[i]) && ReferenceEquals(prepared.IsolatedPositions[i], isolated[i]);
                if (!preparedMatches)
                {
                    prepared?.Dispose();
                    prepared = RenderGeometryPreparation.TryBuild(source.Mesh.Vertices.Length, maps, mainPositions, isolated);
                }

                if (prepared == null) return null;
                try
                {
                    return new RenderGeometryState(part, source, prepared);
                }
                catch
                {
                    prepared.Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                m_WorkPlan?.Dispose();
                m_WorkPlan = null;
                m_Budget.Dispose();
            }

            internal RenderGeometryState(RenderPart part, MeshPart source, RenderGeometryPreparation prepared)
            {
                m_Source = source;
                m_Mesh = source.Mesh;
                m_Positions = m_Mesh.Vertices;
                m_Normals = m_Mesh.Normals;
                m_Tangents = m_Mesh.Tangents;
                m_UV = m_Mesh.UV0;
                m_TC1 = m_Mesh.TexCoord1;
                m_TC3 = m_Mesh.TexCoord3;
                m_Colors = m_Mesh.VertexColors;
                m_Topology = source.TopologyVersion;
                m_Membership = source.SurfaceAreaMembershipVersion;
                m_Full = source.FullGeometryVersion;
                m_Covered = source.GeometryVersion;
                Positions = new RenderDirtyIndices(m_Positions.Length);
                Normals = new RenderDirtyIndices(m_Positions.Length);
                m_Budget = prepared.Budget;
                Offsets = prepared.Offsets;
                Copies = prepared.Copies;
                var targets = new List<RenderMeshUpdate>();
                RenderMeshUpdate main = null;
                if (part.HasMainRendererGeometry)
                {
                    main = new RenderMeshUpdate(part, null, prepared.MainBounds);
                    targets.Add(main);
                }

                Areas = new RenderAreaUpdate[part.Areas.Count];
                for (int i = 0; i < Areas.Length; i++)
                {
                    var area = part.Areas[i];
                    var target = main;
                    if (area.IsolatedRenderer)
                    {
                        target = new RenderMeshUpdate(part, area, prepared.IsolatedBounds[i]);
                        targets.Add(target);
                    }

                    if (target == null) throw new InvalidOperationException("Invalid renderer area target.");
                    Areas[i] = new RenderAreaUpdate(area, target);
                }

                Targets = targets.ToArray();
                InitializeKernels();
                Bytes = Offsets.Length * 4L + Copies.Length * 8L + Positions.Bytes + Normals.Bytes + KernelBytes + 512;
                Bytes += Areas.Length * 128L;
                foreach (var target in Targets) Bytes += target.Bytes;
                InitializeWorkPlan();
                Bytes += m_WorkPlan?.Bytes ?? 0;
                prepared.Budget = null; // The live state now owns the reservation.
            }

            internal bool AdoptSource(RenderPart part, MeshPart source)
            {
                var mesh = source.Mesh;
                if (!ReferenceEquals(m_Positions, mesh.Vertices) || !ReferenceEquals(m_Normals, mesh.Normals) || !ReferenceEquals(m_Tangents, mesh.Tangents) || !ReferenceEquals(m_UV, mesh.UV0) || !ReferenceEquals(m_TC1, mesh.TexCoord1) || !ReferenceEquals(m_TC3, mesh.TexCoord3) || !ReferenceEquals(m_Colors, mesh.VertexColors)) return false;
                if (m_WorkPlan != null) m_WorkPlan.Valid = false;
                m_Source = source;
                m_Mesh = mesh;
                m_Topology = source.TopologyVersion;
                m_Membership = source.SurfaceAreaMembershipVersion;
                m_Full = source.FullGeometryVersion;
                if (!Matches(part, source)) return false;
                Complete(source.GeometryVersion, true);
                return true;
            }

            internal bool Matches(RenderPart part, MeshPart source)
            {
                if (!ReferenceEquals(source, m_Source) || !ReferenceEquals(source.Mesh, m_Mesh) || source.TopologyVersion != m_Topology || source.SurfaceAreaMembershipVersion != m_Membership || source.FullGeometryVersion != m_Full || part.Areas.Count != Areas.Length || !ReferenceEquals(m_Positions, m_Mesh.Vertices) || !ReferenceEquals(m_Normals, m_Mesh.Normals) || !ReferenceEquals(m_Tangents, m_Mesh.Tangents) || !ReferenceEquals(m_UV, m_Mesh.UV0) || !ReferenceEquals(m_TC1, m_Mesh.TexCoord1) || !ReferenceEquals(m_TC3, m_Mesh.TexCoord3) || !ReferenceEquals(m_Colors, m_Mesh.VertexColors)) return false;
                for (int i = 0; i < Areas.Length; i++)
                    if (!ReferenceEquals(part.Areas[i], Areas[i].Area) || !Areas[i].Matches())
                        return false;
                foreach (var target in Targets)
                    if (!target.Matches())
                        return false;
                return true;
            }

            internal bool Ready(RenderPart part, MeshPart source)
            {
                bool matches = Matches(part, source);
                bool versions = m_Valid && m_Covered == source.GeometryVersion;
                return matches && versions;
            }

            internal bool Notice(GeometryChange change)
            {
                if (!m_Valid || !ReferenceEquals(change.Part, m_Source) || change.Topology != m_Topology || change.Before != m_Covered || change.After == change.Before || change.Positions == null || change.Normals == null)
                {
                    Invalidate();
                    return false;
                }

                if ((change.Normals.Count != 0 && m_Normals == null) || !Positions.Union(change.Positions) || !Normals.Union(change.Normals))
                {
                    Invalidate();
                    Positions.Clear();
                    Normals.Clear();
                    return false;
                }

                m_Covered = change.After;
                return true;
            }

            internal void Invalidate()
            {
                m_Valid = false;
                if (m_WorkPlan != null) m_WorkPlan.Valid = false;
            }

            internal void Complete(uint version, bool precise)
            {
                // Bounds repair can fail. Complete the whole generation before
                // advancing coverage or discarding its pending footprint.
                if (!precise)
                {
                    if (m_WorkPlan != null) m_WorkPlan.Valid = false;
                    foreach (var target in Targets) target.RefreshBounds();
                }

                m_Covered = version;
                m_Valid = true;
                Positions.Clear();
                Normals.Clear();
                foreach (var area in Areas) area.Clear();
                foreach (var target in Targets) target.Clear();
            }
        }
    }
}