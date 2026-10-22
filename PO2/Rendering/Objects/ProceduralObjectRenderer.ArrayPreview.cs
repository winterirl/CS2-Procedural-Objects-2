using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using PO2.Core;
using PO2.Tools.Arrays;

namespace PO2.Rendering.Objects
{
    public sealed partial class ProceduralObjectRenderer
    {
        // Queue<T> is exposed twice by the game's references. Cleanup order
        // does not matter, so use the same list pattern as retired render parts.
        private readonly List<ArrayPreview> m_RetiredArrayPreviews = new List<ArrayPreview>();
        internal ArrayPreview CreateArrayPreview(ProceduralObject obj, int index) => new ArrayPreview(this, obj, index);

        private void PollArrayPreviewCleanup(bool drain)
        {
            long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 1000;
            while (m_RetiredArrayPreviews.Count > 0)
            {
                int last = m_RetiredArrayPreviews.Count - 1;
                if (m_RetiredArrayPreviews[last].RetireOne()) m_RetiredArrayPreviews.RemoveAt(last);
                if (!drain && Stopwatch.GetTimestamp() >= deadline) return;
            }
        }

        internal sealed class ArrayPreview : IDisposable
        {
            private sealed class Batch
            {
                internal Mesh Mesh;
                internal Material[] Materials;
                internal MaterialPropertyBlock[] Properties;
                internal MaterialPropertyBlock GlobalProperties;
                internal uint Layers;
            }

            private readonly ProceduralObjectRenderer m_Owner;
            private readonly ProceduralObject m_Object;
            private readonly RenderInstance m_Instance;
            private readonly RenderPart m_Source;
            private readonly int m_Index;
            private readonly List<Batch> m_Batches = new List<Batch>();
            private readonly List<GameObject> m_Copies = new List<GameObject>();
            private readonly List<int> m_CopyRevisions = new List<int>();
            private GameObject m_Root, m_SourcePreview;
            private readonly Vector3 m_Pivot;
            private readonly bool m_SourceVisible;
            private Quaternion m_Rotation = Quaternion.identity;
            private PartArrayLayout m_Layout;
            private PartArrayPlacements m_Placements;
            private GameObject m_Building;
            private MeshRenderer m_BuildRenderer;
            private int m_BuildBatch, m_BuildProperty;
            private int m_SourceRevision = -1;
            private int Count => m_Placements?.Count ?? m_Layout.Count;
            private int m_Revision, m_AppliedRevision = -1, m_Cursor, m_Visible, m_DirtyCopies;
            private bool m_Disposed, m_GrowFirst;
            internal Bounds Bounds { get; }
            internal bool Ready => !m_Disposed && m_Visible == Count && m_AppliedRevision == m_Revision;

            internal ArrayPreview(ProceduralObjectRenderer owner, ProceduralObject obj, int index)
            {
                m_Owner = owner;
                m_Object = obj;
                m_Index = index;
                if (!owner.m_Instances.TryGetValue(obj.Id, out m_Instance) || index < 0 || index >= m_Instance.Parts.Count)
                    throw new InvalidOperationException("Wait for the selected part to finish rendering.");
                m_Source = m_Instance.Parts[index];
                m_Pivot = obj.Transform.TransformPoint(obj.Parts[index].LocalPivot);
                m_SourceVisible = m_Source.GameObject.activeSelf;
                if (m_Source.GeometryVersion != obj.Parts[index].GeometryVersion || m_Source.TopologyVersion != obj.Parts[index].TopologyVersion)
                    throw new InvalidOperationException("Wait for the selected part to finish rendering.");
                bool hasBounds = false;
                Bounds bounds = default;
                if (m_Source.HasMainRendererGeometry) Capture(m_Source.Mesh, m_Source.Renderer, ref bounds, ref hasBounds);
                foreach (var area in m_Source.Areas)
                    if (area.IsolatedRenderer)
                        Capture(area.IsolatedMesh, area.IsolatedRendererComponent, ref bounds, ref hasBounds);
                if (!hasBounds) throw new InvalidOperationException("The selected part has no rendered geometry.");
                Bounds = bounds;
                m_Root = new GameObject("PO2 Array Preview");
            }

            private void Capture(Mesh mesh, MeshRenderer renderer, ref Bounds bounds, ref bool hasBounds)
            {
                if (mesh == null || mesh.vertexCount == 0) return;
                if (!hasBounds)
                {
                    bounds = mesh.bounds;
                    hasBounds = true;
                }
                else bounds.Encapsulate(mesh.bounds);

                var materials = renderer.sharedMaterials;
                var properties = new MaterialPropertyBlock[materials.Length];
                for (int i = 0; i < properties.Length; i++)
                {
                    properties[i] = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties[i], i);
                }

                var globalProperties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(globalProperties);
                m_Batches.Add(new Batch
                {
                    Mesh = mesh, Materials = materials, Properties = properties,
                    GlobalProperties = globalProperties, Layers = renderer.renderingLayerMask
                });
            }

            internal void SetLayout(PartArrayLayout layout, Quaternion? sourceRotation = null)
            {
                m_Placements = null;
                m_Layout = layout;
                BeginRevision();
                m_Rotation = sourceRotation ?? (layout.Rotation.Equals(default(Quaternion)) ? Quaternion.identity : layout.Rotation);
            }

            internal void SetPlacements(PartArrayPlacements placements)
            {
                m_Placements = placements;
                m_Layout = default;
                m_Rotation = placements?.Source.Rotation ?? Quaternion.identity;
                BeginRevision();
            }

            private void BeginRevision()
            {
                m_Revision++;
                m_DirtyCopies = Math.Min(Count, m_Copies.Count);
                // Continue where the previous frame stopped. Restarting at zero
                // on every mouse move leaves the end of a large array frozen.
                if (m_DirtyCopies > 0) m_Cursor %= m_DirtyCopies;
                else m_Cursor = 0;
            }

            internal void Poll()
            {
                if (m_Disposed) return;
                if (!m_Owner.m_Instances.TryGetValue(m_Object.Id, out var current) || !ReferenceEquals(current, m_Instance) || m_Index >= current.Parts.Count || !ReferenceEquals(current.Parts[m_Index], m_Source))
                    throw new InvalidOperationException("The array source renderer changed.");
                if (m_AppliedRevision == m_Revision) return;
                long started = Stopwatch.GetTimestamp();
                long deadline = started + Stopwatch.Frequency / 1000;
                if (m_SourceRevision != m_Revision)
                {
                    // Keep the original visible until its complete replacement is
                    // ready. No model mutation is needed for source rotation.
                    bool rotated = !m_Rotation.Equals(Quaternion.identity);
                    if (rotated)
                    {
                        if (m_SourcePreview == null && !TryCreateCopy(deadline, out m_SourcePreview)) return;
                        var source = m_Placements?.Source ?? new PartArrayPlacement(m_Pivot, m_Rotation);
                        Place(m_SourcePreview, source.PreviewPosition(m_Object.Transform, m_Pivot), source.Rotation);
                        SetVisible(m_SourcePreview, m_SourceVisible);
                    }
                    else if (m_SourcePreview != null) SetVisible(m_SourcePreview, false);

                    SetVisible(m_Source.GameObject, !rotated && m_SourceVisible);
                    m_SourceRevision = m_Revision;
                }

                while (m_Visible > Count && Stopwatch.GetTimestamp() < deadline)
                    SetVisible(m_Copies[--m_Visible], false);

                // Reserve part of this poll for growing the preview too. Existing
                // copies must not consume every frame while the count increases.
                if (Count > m_Copies.Count && (m_GrowFirst = !m_GrowFirst))
                    GrowCopies(started + Stopwatch.Frequency / 4000);
                long moveDeadline = Count > m_Copies.Count ? started + Stopwatch.Frequency * 3 / 4000 : deadline;
                int existing = Math.Min(Count, m_Copies.Count);
                while (m_DirtyCopies > 0 && Stopwatch.GetTimestamp() < moveDeadline)
                {
                    if (m_Cursor >= existing) m_Cursor = 0;
                    int index = m_Cursor++;
                    if (m_CopyRevisions[index] == m_Revision) continue;
                    PlaceCopy(index);
                    m_CopyRevisions[index] = m_Revision;
                    m_DirtyCopies--;
                }

                GrowCopies(deadline);
                if (m_DirtyCopies == 0 && m_Visible == Count) m_AppliedRevision = m_Revision;
            }

            private void GrowCopies(long deadline)
            {
                while (m_Copies.Count < Count && Stopwatch.GetTimestamp() < deadline)
                {
                    if (!TryCreateCopy(deadline, out var copy)) return;
                    int index = m_Copies.Count;
                    m_Copies.Add(copy);
                    m_CopyRevisions.Add(m_Revision);
                    PlaceCopy(index);
                }
            }

            private void PlaceCopy(int index)
            {
                Vector3 position;
                Quaternion rotation;
                if (m_Placements != null)
                {
                    var pose = m_Placements[index];
                    position = pose.PreviewPosition(m_Object.Transform, m_Pivot);
                    rotation = pose.Rotation;
                }
                else
                {
                    position = m_Layout.PreviewPosition(m_Object.Transform, index);
                    rotation = m_Layout.Rotation;
                }

                Place(m_Copies[index], position, rotation);
                SetVisible(m_Copies[index], true);
                m_Visible = Math.Max(m_Visible, index + 1);
            }

            private void Place(GameObject copy, Vector3 position, Quaternion rotation)
            {
                copy.transform.SetPositionAndRotation(position, rotation * m_Object.Transform.Rotation);
            }

            private static void SetVisible(GameObject copy, bool visible)
            {
                if (copy.activeSelf != visible) copy.SetActive(visible);
            }

            private bool TryCreateCopy(long deadline, out GameObject copy)
            {
                copy = null;
                if (m_Building == null)
                {
                    m_Building = new GameObject("Array Copy");
                    m_Building.SetActive(false);
                    m_Building.transform.SetParent(m_Root.transform, false);
                    m_Building.transform.localScale = m_Object.Transform.Scale;
                    m_BuildBatch = 0;
                    m_BuildProperty = 0;
                }

                // Slice by render batch and property block, not whole part. A
                // large converted part can contain hundreds of isolated areas.
                while (Stopwatch.GetTimestamp() < deadline)
                {
                    if (m_BuildBatch == m_Batches.Count)
                    {
                        copy = m_Building;
                        m_Building = null;
                        return true;
                    }

                    var batch = m_Batches[m_BuildBatch];
                    if (m_BuildRenderer == null)
                    {
                        var child = new GameObject("Preview Surface");
                        child.transform.SetParent(m_Building.transform, false);
                        child.AddComponent<MeshFilter>().sharedMesh = batch.Mesh;
                        m_BuildRenderer = child.AddComponent<MeshRenderer>();
                        m_BuildRenderer.sharedMaterials = batch.Materials;
                        m_BuildRenderer.renderingLayerMask = batch.Layers;
                        m_BuildRenderer.shadowCastingMode = ShadowCastingMode.Off;
                        m_BuildRenderer.SetPropertyBlock(batch.GlobalProperties);
                    }
                    else if (m_BuildProperty < batch.Properties.Length)
                    {
                        int index = m_BuildProperty++;
                        if (!batch.Properties[index].isEmpty) m_BuildRenderer.SetPropertyBlock(batch.Properties[index], index);
                    }
                    else
                    {
                        m_BuildBatch++;
                        m_BuildProperty = 0;
                        m_BuildRenderer = null;
                    }
                }

                return false;
            }

            public void Dispose()
            {
                if (m_Disposed) return;
                m_Disposed = true;
                m_Root?.SetActive(false);
                if (m_Owner.m_Instances.TryGetValue(m_Object.Id, out var current) && ReferenceEquals(current, m_Instance) && m_Index < current.Parts.Count && ReferenceEquals(current.Parts[m_Index], m_Source))
                    m_Source.GameObject.SetActive(m_SourceVisible);
                m_Batches.Clear();
                m_Owner.m_RetiredArrayPreviews.Add(this);
            }

            internal bool RetireOne()
            {
                if (m_Building != null)
                {
                    if (RetireCopy(m_Building))
                    {
                        m_Building = null;
                        m_BuildRenderer = null;
                    }

                    return false;
                }

                if (m_SourcePreview != null)
                {
                    if (RetireCopy(m_SourcePreview)) m_SourcePreview = null;
                    return false;
                }

                if (m_Copies.Count > 0)
                {
                    int last = m_Copies.Count - 1;
                    if (RetireCopy(m_Copies[last]))
                    {
                        m_Copies.RemoveAt(last);
                        m_CopyRevisions.RemoveAt(last);
                    }

                    return false;
                }

                if (m_Root != null)
                {
                    UnityEngine.Object.Destroy(m_Root);
                    m_Root = null;
                }

                return true;
            }

            private static bool RetireCopy(GameObject copy)
            {
                if (copy.transform.childCount == 0)
                {
                    UnityEngine.Object.Destroy(copy);
                    return true;
                }

                var child = copy.transform.GetChild(copy.transform.childCount - 1);
                child.gameObject.SetActive(false);
                child.SetParent(null, false);
                UnityEngine.Object.Destroy(child.gameObject);
                return false;
            }
        }
    }
}