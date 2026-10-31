// Frozen v1.0.2 preparation for original-UV layout equivalence.
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using PO2.Conversion;
using PO2.Core.Operations;
using PO2.Geometry;
using PO2.Geometry.Topology;
using PO2.Materials;
using PO2.Persistence.Model;
using PO2.Rendering.Mapping;
using MeshTopology = PO2.Geometry.Topology.MeshTopology;

namespace PO2.Rendering
{
    internal sealed class ReferenceRendererWork : ITopologyRenderWork, IArrayRenderWork
    {
        internal readonly Dictionary<int, TopologyRenderAreaWork> Areas = new Dictionary<int, TopologyRenderAreaWork>();
        internal Vector3 Scale;
        internal TopologyReplayProof ReplayProof;
        internal TopologyRenderCaptureMode CaptureMode;
        private bool ArrayCopy => CaptureMode == TopologyRenderCaptureMode.Array;
        private bool Restoring => CaptureMode == TopologyRenderCaptureMode.Restore;

        public void PrepareArray(MeshPart source, Matrix4x4 transform, CancellationToken token)
        {
            foreach (var settings in Areas.Values)
            {
                token.ThrowIfCancellationRequested();
                if (settings.CompleteCapture != null)
                {
                    settings.Current = settings.CompleteCapture();
                    settings.CompleteCapture = null;
                }

                if (settings.CompleteSeed != null)
                {
                    settings.Seed = settings.CompleteSeed();
                    settings.CompleteSeed = null;
                }

                if (source != null && settings.Seed != null)
                {
                    foreach (var area in source.SurfaceAreas)
                        if (area.Id == settings.Id)
                            area.MappingSeed = settings.Seed;
                }

                if (settings.Current != null && !transform.Equals(Matrix4x4.identity))
                    settings.Current = ArrayKernels.CopyTransformed(settings.Current, transform, token);
            }
        }

        internal static bool GeneratesTangents(TopologyRenderAreaWork settings, bool generatedUVs, bool hasTangents, bool combinedFallback) =>
            settings.RequiresTangents && (generatedUVs || !hasTangents || !settings.Isolated && combinedFallback);

        internal static bool ForcesCombinedTangents(TopologyRenderAreaWork settings, TextureMappingMode mode, bool hasTangents) =>
            !settings.Isolated && GeneratesTangents(settings, mode != TextureMappingMode.Original, hasTangents, false);

        public ITopologyRenderData Build(MeshPart candidate, PersistencePartData before, int[] oldToNew, ITopologyAppearanceCheck appearance, CancellationToken token) =>
            Build(candidate, before, oldToNew, appearance, token, Matrix4x4.identity);

        public ITopologyRenderData BuildArray(MeshPart part, PersistencePartData before, Matrix4x4 transform, CancellationToken token) => Build(part, before, null, null, token, transform);

        private ITopologyRenderData Build(MeshPart candidate, PersistencePartData before, int[] oldToNew, ITopologyAppearanceCheck appearance, CancellationToken token, Matrix4x4 arrayTransform)
        {
            if (CaptureMode == TopologyRenderCaptureMode.Mirror && appearance != null)
                throw new InvalidOperationException("Mirror renderer capture cannot be used for topology appearance checks.");
            foreach (var settings in Areas.Values)
            {
                token.ThrowIfCancellationRequested();
                if (settings.CompleteCapture != null)
                {
                    settings.Current = settings.CompleteCapture();
                    settings.CompleteCapture = null;
                }

                if (settings.CurrentRequired && settings.Current == null)
                    throw new InvalidOperationException("Missing renderer data required for validation.");
            }

            var groups = GroupTriangles(candidate.TriangleSurfaceAreaIds, token);
            var proof = ArrayCopy || Restoring ? new TopologyReplayProof { Scale = Scale } : ValidateReplayRestoration(before, token);
            bool combinedFallback = false;
            foreach (var area in candidate.SurfaceAreas)
            {
                if (groups.ContainsKey(area.Id) && Areas.TryGetValue(area.Id, out var settings) && ForcesCombinedTangents(settings, area.Material.Mapping.Mode, candidate.Mesh.Tangents != null)) combinedFallback = true;
            }

            var prepared = new List<TopologyRenderAreaPreparation>(groups.Count);
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector4>();
            var tangents = new List<Vector4>();
            var colors = new List<Color32>();
            var tex1 = new List<Vector4>();
            var tex3 = new List<Vector4>();
            var submeshes = new List<int[]>();
            bool hasColors = false, hasTex1 = false, hasTex3 = false, requiresTangents = false;
            int dimension = 2;
            foreach (var area in candidate.SurfaceAreas)
            {
                token.ThrowIfCancellationRequested();
                if (!groups.TryGetValue(area.Id, out var triangleIds)) continue;
                if (!Areas.TryGetValue(area.Id, out var settings)) throw new InvalidOperationException("Missing renderer requirements for a Surface Area.");
                ConvertedRenderPreparationBuilder.BuildAreaSourceMesh(candidate, area, triangleIds, out var mesh, out var sourceIndices, token);
                var item = new TopologyRenderAreaPreparation
                {
                    Settings = settings, Source = mesh, SourceVertexIndices = sourceIndices,
                    SourceTriangleIds = triangleIds, UV0Dimension = (area.SourceVertexAttributes & VertexAttributeFlags.UV0FourComponent) != 0 ? 4 : 2
                };
                BuildRuntime(item, area, token);
                bool generated = item.Mapping != null;
                item.RecalculateTangents = Restoring && settings.Isolated && GeneratesTangents(settings, generated, mesh.Tangents != null, false);
                if (ArrayCopy && settings.Current != null)
                {
                    // Keep captured charts and UVs. Line copies share one rotated
                    // basis; curve copies each rotate the original captured basis.
                    var current = settings.Current;
                    if (item.Runtime.Vertices.Length != current.Vertices.Length || item.Runtime.Triangles.Length != current.Triangles.Length)
                        throw new InvalidOperationException("The array source render layout changed.");
                    for (int i = 0; i < current.Triangles.Length; i++)
                        if (item.Runtime.Triangles[i] != current.Triangles[i])
                            throw new InvalidOperationException("The array source render order changed.");
                    var uvCopy = current.UV0 == null ? null : (Vector4[])current.UV0.Clone();
                    if (item.Mapping != null && uvCopy != null)
                    {
                        for (int i = 0; i < uvCopy.Length; i++) item.Mapping.UVs[i] = new Vector2(uvCopy[i].x, uvCopy[i].y);
                    }

                    item.Runtime = new MeshData(item.Runtime.Vertices, item.Runtime.Normals, ArrayKernels.CopyTangents(current, arrayTransform, token), uvCopy, item.Runtime.VertexColors, item.Runtime.TexCoord1, item.Runtime.TexCoord3, item.Runtime.Triangles);
                }
                else if (!Restoring && GeneratesTangents(settings, generated, mesh.Tangents != null, combinedFallback))
                {
                    // The managed tangent builder is used only after matching it
                    // against the actual currently rendered Unity tangent stream.
                    bool restoredProof = ReplayProof != null && ReplayProof.Scale.Equals(Scale) && ReplayProof.RestoredTangents.TryGetValue(settings.Id, out var tile) && tile.Equals(settings.TileSize);
                    if (!restoredProof)
                    {
                        if (settings.Current == null || settings.UsesSourceTangents)
                            throw new InvalidOperationException("The restored area's tangent preparation is not validated for this renderer state.");
                        ValidateTangentCompatibility(settings, token);
                    }

                    var runtime = item.Runtime;
                    item.Runtime = new MeshData(runtime.Vertices, runtime.Normals, null, runtime.UV0, runtime.VertexColors, runtime.TexCoord1, runtime.TexCoord3, runtime.Triangles);
                    item.Runtime.RecalculateTangents();
                    if (item.Runtime.Tangents == null) throw new InvalidOperationException("This shader needs tangent data that cannot be prepared safely.");
                }

                token.ThrowIfCancellationRequested();
                prepared.Add(item);
                if (settings.Isolated) item.Upload = new TopologyMeshUpload(item.Runtime, new[] { item.Runtime.Triangles }, item.UV0Dimension, settings.RequiresTangents, token);
                else
                {
                    item.CombinedOffset = vertices.Count;
                    item.MaterialIndex = submeshes.Count;
                    var runtime = item.Runtime;
                    ConvertedRenderPreparationBuilder.AppendAreaVertices(vertices, normals, uv, tangents, colors, tex1, tex3, runtime.Vertices, runtime.Normals, runtime.UV0, runtime.Tangents, runtime.VertexColors, runtime.TexCoord1, runtime.TexCoord3);
                    int[] adjusted = new int[runtime.Triangles.Length];
                    for (int i = 0; i < adjusted.Length; i++) adjusted[i] = runtime.Triangles[i] + item.CombinedOffset;
                    submeshes.Add(adjusted);
                    dimension = Math.Max(dimension, item.UV0Dimension);
                    hasColors |= runtime.VertexColors != null;
                    hasTex1 |= runtime.TexCoord1 != null;
                    hasTex3 |= runtime.TexCoord3 != null;
                    requiresTangents |= settings.RequiresTangents;
                }
            }

            token.ThrowIfCancellationRequested();
            var result = new TopologyRendererPreparation { ReplayProof = proof, Areas = prepared.ToArray(), Submeshes = submeshes.ToArray(), UV0Dimension = dimension, RequiresTangents = requiresTangents };
            result.RecalculateCombinedTangents = Restoring && combinedFallback;
            int indexCount = 0;
            foreach (var indices in result.Submeshes) indexCount = checked(indexCount + indices.Length);
            int[] combinedIndices = new int[indexCount];
            int cursor = 0;
            foreach (var indices in result.Submeshes)
            {
                Array.Copy(indices, 0, combinedIndices, cursor, indices.Length);
                cursor += indices.Length;
            }

            result.Combined = new MeshData(vertices.ToArray(), normals.ToArray(), requiresTangents ? tangents.ToArray() : null, uv.ToArray(), hasColors ? colors.ToArray() : null, hasTex1 ? tex1.ToArray() : null, hasTex3 ? tex3.ToArray() : null, combinedIndices);
            appearance?.Validate(before, oldToNew, result, token);
            result.Upload = new TopologyMeshUpload(result.Combined, result.Submeshes, dimension, requiresTangents, token);
            long bytes = MeshBytes(result.Combined) + UploadBytes(result.Upload);
            foreach (var item in result.Areas) bytes = checked(bytes + MeshBytes(item.Source) + MeshBytes(item.Runtime) + MeshBytes(item.Settings.Current) + UploadBytes(item.Upload) + 4L * (item.SourceVertexIndices.Length + (long)item.SourceTriangleIds.Length + item.RuntimeToPart.Length));
            var maps = new int[result.Areas.Length][];
            var isolated = new Vector3[maps.Length][];
            for (int i = 0; i < maps.Length; i++)
            {
                maps[i] = result.Areas[i].SourceVertexIndices;
                if (result.Areas[i].Settings.Isolated) isolated[i] = result.Areas[i].Runtime.Vertices;
            }

            result.GeometryUpdates = RenderGeometryPreparation.TryBuild(candidate.Mesh.Vertices.Length, maps, result.Submeshes.Length == 0 ? null : result.Combined.Vertices, isolated, token);
            result.PreparedBytes = checked(bytes + (result.GeometryUpdates?.Bytes ?? 0));
            return result;
        }

        private static long MeshBytes(MeshData mesh) => mesh == null ? 0 : checked(12L * mesh.Vertices.Length + 12L * (mesh.Normals?.Length ?? 0) + 16L * (mesh.Tangents?.Length ?? 0) + 16L * (mesh.UV0?.Length ?? 0) + 4L * (mesh.VertexColors?.Length ?? 0) + 16L * (mesh.TexCoord1?.Length ?? 0) + 16L * (mesh.TexCoord3?.Length ?? 0) + 4L * mesh.Triangles.Length);

        // Packed managed data, readable Unity staging, and the native upload.
        private static long UploadBytes(TopologyMeshUpload upload) => upload == null ? 0 : checked(3L * (upload.VertexBytes + 4L * upload.Indices.Length));

        private void BuildRuntime(TopologyRenderAreaPreparation item, SurfaceArea area, CancellationToken token)
        {
            var mesh = item.Source;
            var sourceIndices = item.SourceVertexIndices;
            var settings = item.Settings;
            bool generated = area.Material.Mapping.Mode != TextureMappingMode.Original;
            if (generated)
            {
                item.Topology = MeshTopology.Build(mesh);
                item.Mapping = new GeneratedMapping(mesh, item.Topology, area.SourceVertexAttributes, area.MappingSeed, sourceIndices);
                item.Mapping.Update(mesh, item.Topology, Scale, area.Material.Mapping, settings.TileSize, true);
                token.ThrowIfCancellationRequested();
                var uvs = new Vector4[item.Mapping.UVs.Length];
                for (int i = 0; i < uvs.Length; i++) uvs[i] = new Vector4(item.Mapping.UVs[i].x, item.Mapping.UVs[i].y, 0, 0);
                item.Runtime = new MeshData(item.Mapping.Vertices, item.Mapping.Normals, null, uvs, item.Mapping.Triangles);
                item.RuntimeToPart = new int[item.Mapping.SourceVertexIndices.Length];
                for (int i = 0; i < item.RuntimeToPart.Length; i++) item.RuntimeToPart[i] = sourceIndices[item.Mapping.SourceVertexIndices[i]];
                item.UV0Dimension = 2;
            }
            else
            {
                item.Runtime = mesh;
                item.RuntimeToPart = sourceIndices;
            }
        }

        private TopologyReplayProof ValidateReplayRestoration(PersistencePartData before, CancellationToken token)
        {
            var proof = new TopologyReplayProof { Scale = Scale };
            Dictionary<int, int[]> groups = null;
            MeshData source = null;
            foreach (var saved in before.SurfaceAreas)
            {
                if (!Areas.TryGetValue(saved.Id, out var settings) || settings.Current == null || !settings.RequiresTangents || settings.UsesSourceTangents) continue;
                token.ThrowIfCancellationRequested();
                if (groups == null) groups = GroupTriangles(before.TriangleSurfaceAreaIds, token);
                if (!groups.TryGetValue(saved.Id, out var triangles)) continue;
                if (source == null) source = new MeshData(before.Vertices, before.Normals, before.Tangents, before.UV0, before.VertexColors, before.TexCoord1, before.TexCoord3, before.Triangles);
                var area = saved.Restore();
                ConvertedRenderPreparationBuilder.BuildAreaSourceMesh(source, area, triangles, out var mesh, out var indices, token);
                var fresh = new TopologyRenderAreaPreparation
                {
                    Settings = settings, Source = mesh, SourceVertexIndices = indices,
                    UV0Dimension = (area.SourceVertexAttributes & VertexAttributeFlags.UV0FourComponent) != 0 ? 4 : 2
                };
                BuildRuntime(fresh, area, token);
                fresh.Runtime.RecalculateTangents();
                TopologyRendererValidation.ValidateRestoredArea(settings.Current, fresh.Runtime, token);
                proof.RestoredTangents.Add(saved.Id, settings.TileSize);
            }

            return proof;
        }

        internal static Dictionary<int, int[]> GroupTriangles(IReadOnlyList<int> memberships, CancellationToken token)
        {
            var counts = new Dictionary<int, int>();
            for (int i = 0; i < memberships.Count; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                counts.TryGetValue(memberships[i], out int count);
                counts[memberships[i]] = count + 1;
            }

            var groups = new Dictionary<int, int[]>(counts.Count);
            var cursors = new Dictionary<int, int>(counts.Count);
            foreach (var pair in counts)
            {
                groups.Add(pair.Key, new int[pair.Value]);
                cursors.Add(pair.Key, 0);
            }

            for (int i = 0; i < memberships.Count; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                int id = memberships[i], cursor = cursors[id];
                groups[id][cursor] = i;
                cursors[id] = cursor + 1;
            }

            return groups;
        }

        private static void ValidateTangentCompatibility(TopologyRenderAreaWork area, CancellationToken token)
        {
            var current = area.Current;
            if (current == null || current.Tangents == null) throw new InvalidOperationException("This shader's tangent fallback has not been validated for topology edits.");
            var derived = new MeshData(current.Vertices, current.Normals, null, current.UV0, current.Triangles);
            derived.RecalculateTangents();
            if (derived.Tangents == null || derived.Tangents.Length != current.Tangents.Length)
                throw new InvalidOperationException("The renderer tangent layout is incompatible with topology edits.");
            for (int i = 0; i < derived.Tangents.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                if (!TopologyRendererValidation.SameTangent(derived.Tangents[i], current.Tangents[i]))
                    throw new InvalidOperationException("This mesh uses a Unity tangent fallback that cannot yet be preserved by topology edits.");
            }
        }
    }

}
