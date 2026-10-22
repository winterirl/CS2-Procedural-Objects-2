using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using PO2.Core;
using PO2.Geometry;
using PO2.Geometry.Editing;
using PO2.Tools.ObjectTool;

namespace PO2.Tools.MeshEdit
{
    internal sealed class MeshEditGpuRequest
    {
        internal int Serial, Revision, SelectionVersion, GeometryRevision;
        internal MeshEditVisibilityMode Visibility;
        internal bool Box, Submitted, ReadbackFailed;
        internal volatile bool Invalid;
        internal int ReadbackBytes;
        internal volatile bool Complete;
        internal Vector2 Pointer;
        internal Rect Rectangle;
        internal uint[] Data;
        internal ComputeBuffer Buffer;
        internal MeshEditGpuGeometry Geometry;
        internal double Started;
    }

    // One camera, one geometry generation, and at most three owned readbacks.
    // The CPU never waits for a visibility array in the frame loop.
    internal sealed partial class MeshEditGpuSession : IDisposable
    {
        private sealed class Buffers : IDisposable
        {
            internal ComputeBuffer Candidates, Normals, Nodes, Triangles, Results, Visible, Args;
            internal ComputeBuffer SourcePositions, SourceNormals, CandidateSources, FacingSources, TriangleSources;
            internal ComputeBuffer CandidateWork, FacingWork, TriangleWork, NodeWork;
            internal ComputeBuffer Selected, PickValues, PickGroups, Minimum, Winner, Scratch, Patches, UpdateFault;
            internal bool Submitted, Unsafe, RetirementQueued, RetirementFailed;
            internal volatile bool RetirementComplete;
            internal double RetiredAt;

            public void Dispose()
            {
                foreach (var b in new[]
                         {
                             Candidates, Normals, Nodes, Triangles, Results, Visible, Args,
                             Selected, PickValues, PickGroups, Minimum, Winner, Scratch, Patches, UpdateFault,
                             SourcePositions, SourceNormals, CandidateSources, FacingSources, TriangleSources,
                             CandidateWork, FacingWork, TriangleWork, NodeWork
                         }) b?.Dispose();
            }
        }

        private sealed class SolidPass : CustomPass
        {
            internal MeshEditGpuSession Owner;

            protected override void Execute(CustomPassContext ctx)
            {
                Owner?.Render(ctx);
            }
        }

        private readonly MeshEditController m_Controller;
        private readonly ProceduralObjectToolSystem m_Tool;
        private readonly ComputeShader m_Compute;
        private readonly Material m_Material;
        private readonly int m_Classify, m_ClassifyXRay, m_Distances, m_Minimum, m_Winners, m_Resolve, m_Box, m_Patch, m_UpdateCandidates, m_UpdateFacing, m_UpdateTriangles, m_RefitNodes;
        private GameObject m_Host;
        private CustomPassVolume m_Volume;
        private Buffers m_Buffers;
        private readonly List<Buffers> m_Retired = new List<Buffers>();
        private readonly List<MeshEditGpuRequest> m_InFlight = new List<MeshEditGpuRequest>();
        private readonly List<ComputeBuffer> m_FreeReplies = new List<ComputeBuffer>(3);
        private MeshEditGpuRequest m_Action, m_Hover;
        private MeshEditGpuGeometry m_Geometry;
        private Camera m_Camera;
        private MeshEditVisibilityMode m_Visibility;
        private bool NeedsOcclusion => m_Visibility == MeshEditVisibilityMode.Solid;
        private Matrix4x4 m_View, m_Projection;
        private Rect m_Viewport;
        private ObjectTransform m_Transform;
        private float m_Near, m_Far;
        private bool m_Ortho, m_Active, m_Failed, m_Disposed;
        private int m_Part, m_Revision, m_Serial, m_ClassifiedRevision = -1, m_SelectionVersion = -1;
        private int m_HoverSlot = -1, m_HoverRevision = -1;
        private int m_SubmittedGeometryRevision = -1, m_ClassifiedGeometryRevision = -1;
        private Vector2 m_Pointer, m_HoverPointer;
        private double m_LastPass, m_ActivatedAt;
        private uint[] m_SelectionBits;

        internal bool Allowed => !m_Tool.IsMeshEditFaceSelectionActive;
        internal bool Active => m_Active && !m_Failed && !m_Disposed;
        internal bool ActionPending => m_Action != null && !m_Action.Invalid;
        internal MeshEditGpuGeometry Geometry => m_Geometry;
        internal int HoverSlot => Ready && m_Geometry.Mode == m_Controller.SelectionMode && m_HoverRevision == m_Revision && m_HoverPointer.Equals(m_Pointer) ? m_HoverSlot : -1;

        internal MeshEditGpuSession(MeshEditController controller, ProceduralObjectToolSystem tool, VertexMarkerRenderer assets)
        {
            m_Controller = controller;
            m_Tool = tool;
            try
            {
                if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                    throw new InvalidOperationException("Compute or async readback is unsupported.");
                if (Marshal.SizeOf<SolidCandidate>() != 48 || Marshal.SizeOf<SolidNormal>() != 64 || Marshal.SizeOf<SolidNode>() != 48 || Marshal.SizeOf<SolidTriangle>() != 48 || Marshal.SizeOf<SolidSourcePatch>() != 16 || Marshal.SizeOf<SolidSourceRefs>() != 16 || Marshal.SizeOf<SolidFacingRefs>() != 32)
                    throw new InvalidOperationException("Solid GPU buffer layout mismatch.");
                m_Compute = assets.LoadMeshEditAsset<ComputeShader>("assets/po2solid.compute");
                Shader shader = assets.LoadMeshEditAsset<Shader>("assets/po2solid.shader");
                if (m_Compute == null || shader == null || !shader.isSupported)
                    throw new InvalidOperationException("Mesh marker assets are missing or unsupported. Rebuild the shader bundle.");
                m_Patch = Kernel("ApplySourcePatches");
                m_UpdateCandidates = Kernel("UpdateCandidates");
                m_UpdateFacing = Kernel("UpdateFacing");
                m_UpdateTriangles = Kernel("UpdateTriangles");
                m_RefitNodes = Kernel("RefitNodes");
                m_Classify = Kernel("Classify");
                m_ClassifyXRay = Kernel("ClassifyXRay");
                m_Distances = Kernel("PickDistances");
                m_Minimum = Kernel("PickMinimum");
                m_Winners = Kernel("PickWinners");
                m_Resolve = Kernel("PickResolve");
                m_Box = Kernel("BoxQuery");
                m_Material = new Material(shader) { name = "PO2 mesh markers", hideFlags = HideFlags.HideAndDontSave };
                m_Host = new GameObject("PO2 mesh edit") { hideFlags = HideFlags.HideAndDontSave };
                m_Volume = m_Host.AddComponent<CustomPassVolume>();
                m_Volume.isGlobal = true;
                m_Volume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
                m_Volume.customPasses.Add(new SolidPass { Owner = this, name = "PO2 mesh edit" });
                m_Volume.enabled = false;
                for (int i = 0; i < 3; i++) m_FreeReplies.Add(new ComputeBuffer(4, 4));
            }
            catch (Exception e)
            {
                Fail(e.Message);
            }
        }

        private int Kernel(string name)
        {
            int kernel = m_Compute.FindKernel(name);
            if (kernel < 0 || !m_Compute.IsSupported(kernel)) throw new InvalidOperationException("Unsupported mesh marker kernel: " + name);
            return kernel;
        }

        internal bool Prepare(ProceduralObject obj, Camera camera, Vector2 pointer, SolidVisibilityBvh bvh)
        {
            Poll();
            if (m_Disposed || m_Failed) return false;
            try
            {
                if (!m_Active)
                {
                    m_Active = true;
                    m_ActivatedAt = Time.realtimeSinceStartupAsDouble;
                    m_Revision++;
                }

                if (m_Visibility != m_Controller.VisibilityMode)
                {
                    m_Visibility = m_Controller.VisibilityMode;
                    InvalidateGeometry();
                    if (!NeedsOcclusion) m_Geometry?.EnterXRay();
                }

                m_Pointer = pointer;
                if (m_Geometry == null || !m_Geometry.MatchesLayout(obj, m_Controller.SelectionMode) || (NeedsOcclusion && (!m_Geometry.OcclusionCurrent || m_Geometry.TreeSerial != bvh.BuildSerial)))
                {
                    PrepareGeneration(obj, bvh);
                    if (!Ready) return true;
                }
                else if (!m_Geometry.Matches(obj, m_Controller.SelectionMode))
                {
                    InvalidateGeometry();
                    m_Geometry.Update(NeedsOcclusion);
                }

                if (Time.realtimeSinceStartupAsDouble - Math.Max(m_LastPass, m_ActivatedAt) > 5)
                    throw new InvalidOperationException("The editing camera's HDRP pass stopped executing.");
                SetCamera(camera, obj.Transform);
                int part = m_Controller.VertexScope == MeshEditVertexScope.CurrentPart ? m_Controller.ActivePartIndex : -1;
                if (part != m_Part)
                {
                    m_Part = part;
                    m_Revision++;
                }

                m_Volume.targetCamera = camera;
                m_Volume.enabled = true;
                if (m_SelectionVersion != m_Controller.SelectionVersion) UploadSelection();
                if (m_Hover != null && Obsolete(m_Hover))
                {
                    m_Hover.Invalid = true;
                    m_Hover = null;
                }

                if (m_Hover != null && m_Hover.Complete)
                {
                    if (Valid(m_Hover) && m_Hover.Pointer.Equals(m_Pointer))
                    {
                        m_HoverSlot = (int)m_Hover.Data[1] - 1;
                        m_HoverPointer = m_Pointer;
                        m_HoverRevision = m_Revision;
                    }

                    m_Hover = null;
                }

                return true;
            }
            catch (Exception e)
            {
                Fail(e.Message);
                return false;
            }
        }

        internal void Stop()
        {
            CancelPreparation();
            if (m_Active || m_Geometry != null || m_Buffers != null) DetachGeneration();
            m_Active = false;
            m_HoverRevision = -1;
            CancelAction();
            if (m_Hover != null) m_Hover.Invalid = true;
            m_Hover = null;
            if (m_Volume != null) m_Volume.enabled = false;
            Poll();
        }

        internal void Invalidate()
        {
            CancelPreparation();
            DetachGeneration();
        }

        internal void InvalidateGeometry()
        {
            m_Revision++;
            m_ClassifiedRevision = -1;
            m_HoverRevision = -1;
            CancelAction();
        }

        internal void Notice(ProceduralObject obj, GeometryChange change)
        {
            if (!Active || m_Geometry == null || !ReferenceEquals(m_Geometry.Object, obj)) return;
            InvalidateGeometry();
            m_Geometry.Notice(change, m_Controller.VisibilityMode == MeshEditVisibilityMode.Solid);
        }

        internal void CancelAction()
        {
            if (m_Action != null) m_Action.Invalid = true;
            m_Action = null;
        }

        internal MeshEditGpuRequest Request(Vector2 pointer, Rect rectangle, bool box)
        {
            CancelAction();
            if (!Ready) return null;
            m_Action = NewRequest(pointer, rectangle, box);
            if (m_Geometry != null && m_Geometry.Candidates.Length == 0)
            {
                m_Action.Data = new uint[] { (uint)m_Action.Serial, 0, 0, 3 };
                m_Action.Complete = true;
            }

            return m_Action;
        }

        private MeshEditGpuRequest NewRequest(Vector2 pointer, Rect rectangle, bool box) => new MeshEditGpuRequest
        {
            Serial = ++m_Serial, Revision = m_Revision, SelectionVersion = m_Controller.SelectionVersion,
            Geometry = m_Geometry, GeometryRevision = m_Geometry?.GeometryRevision ?? -1, Pointer = pointer, Rectangle = rectangle, Box = box,
            Started = Time.realtimeSinceStartupAsDouble, Visibility = m_Visibility,
        };

        internal bool Valid(MeshEditGpuRequest r) => r != null && Active && !r.Invalid && r.Geometry == m_Geometry && m_Geometry != null && r.Revision == m_Revision && r.GeometryRevision == m_Geometry.GeometryRevision && r.SelectionVersion == m_Controller.SelectionVersion && m_Geometry.Matches(m_Geometry.Object, m_Controller.SelectionMode) && r.Visibility == m_Visibility && r.Visibility == m_Controller.VisibilityMode && m_Controller.IsActive && r.Complete && r.Data != null && r.Data.Length >= 4 && r.Data[0] == (uint)r.Serial && r.Data[2] == 0 && r.Data[3] == 3 && (r.Box ? r.Data[1] == (uint)m_Geometry.Candidates.Length && r.Data.Length == 4 + (m_Geometry.Candidates.Length + 31) / 32 : r.Data.Length == 4 && r.Data[1] <= (uint)m_Geometry.Candidates.Length);
        internal bool Obsolete(MeshEditGpuRequest r) => r == null || r.Invalid || !Active || r.Revision != m_Revision || r.Geometry != m_Geometry || m_Geometry == null || r.GeometryRevision != m_Geometry.GeometryRevision || !m_Controller.IsActive || r.Visibility != m_Visibility || r.Visibility != m_Controller.VisibilityMode || !m_Geometry.Matches(m_Geometry.Object, m_Controller.SelectionMode) || r.SelectionVersion != m_Controller.SelectionVersion || ((!r.Box || !r.Complete) && Time.realtimeSinceStartupAsDouble - r.Started > 5);

        private static bool Same(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        private void SetCamera(Camera camera, ObjectTransform transform)
        {
            if (m_Camera != camera || !Same(m_View, camera.worldToCameraMatrix) || !Same(m_Projection, camera.projectionMatrix) || !m_Viewport.Equals(camera.pixelRect) || m_Near != camera.nearClipPlane || m_Far != camera.farClipPlane || m_Ortho != camera.orthographic || !m_Transform.Position.Equals(transform.Position) || !m_Transform.Rotation.Equals(transform.Rotation) || !m_Transform.Scale.Equals(transform.Scale))
            {
                m_Camera = camera;
                m_View = camera.worldToCameraMatrix;
                m_Projection = camera.projectionMatrix;
                for (int row = 0; row < 4; row++)
                {
                    MeshEditGpuGeometry.Finite(m_View.GetRow(row));
                    MeshEditGpuGeometry.Finite(m_Projection.GetRow(row));
                }

                MeshEditGpuGeometry.Finite(MeshEditGpuGeometry.Lane(transform.Position));
                MeshEditGpuGeometry.Finite(MeshEditGpuGeometry.Lane(transform.Scale));
                m_Viewport = camera.pixelRect;
                m_Near = camera.nearClipPlane;
                m_Far = camera.farClipPlane;
                m_Ortho = camera.orthographic;
                m_Transform = transform;
                m_Revision++;
            }
        }

        internal void RefreshVisuals()
        {
            if (Ready && m_SelectionVersion != m_Controller.SelectionVersion)
                UploadSelection();
        }

        private void UploadSelection()
        {
            Array.Clear(m_SelectionBits, 0, m_SelectionBits.Length);
            if (m_Geometry.Mode == MeshSelectionMode.Vertex)
            {
                foreach (var v in m_Controller.GetSelectionSnapshot())
                    if (m_Geometry.VertexSlots.TryGetValue(v, out int i))
                        m_SelectionBits[i >> 5] |= 1u << (i & 31);
            }
            else
                foreach (var e in m_Controller.GetEdgeSelectionSnapshot())
                    if (m_Geometry.EdgeSlots.TryGetValue(e, out int i))
                        m_SelectionBits[i >> 5] |= 1u << (i & 31);

            m_Buffers.Selected.SetData(m_SelectionBits);
            m_SelectionVersion = m_Controller.SelectionVersion;
        }

        private void Bind(CommandBuffer cmd, int k, string name, ComputeBuffer buffer) => cmd.SetComputeBufferParam(m_Compute, k, name, buffer);

        private void Constants(CommandBuffer cmd)
        {
            var t = m_Transform;
            Vector3 safe = new Vector3(MeshEditGpuGeometry.SafeScale(t.Scale.x), MeshEditGpuGeometry.SafeScale(t.Scale.y), MeshEditGpuGeometry.SafeScale(t.Scale.z));
            cmd.SetComputeMatrixParam(m_Compute, "_SolidLocalToWorld", Matrix4x4.TRS(t.Position, t.Rotation, t.Scale));
            cmd.SetComputeMatrixParam(m_Compute, "_SolidWorldToLocal", Matrix4x4.TRS(t.Position, t.Rotation, safe).inverse);
            cmd.SetComputeMatrixParam(m_Compute, "_SolidNormalToWorld", Matrix4x4.TRS(Vector3.zero, t.Rotation, new Vector3(1 / safe.x, 1 / safe.y, 1 / safe.z)));
            cmd.SetComputeMatrixParam(m_Compute, "_SolidView", m_View);
            cmd.SetComputeMatrixParam(m_Compute, "_SolidProjection", m_Projection);
            Matrix4x4 inverse = m_View.inverse;
            cmd.SetComputeVectorParam(m_Compute, "_SolidCameraPosition", inverse.GetColumn(3));
            cmd.SetComputeVectorParam(m_Compute, "_SolidCameraForward", -inverse.GetColumn(2));
            cmd.SetComputeVectorParam(m_Compute, "_SolidViewport", new Vector4(m_Viewport.x, m_Viewport.y, m_Viewport.width, m_Viewport.height));
            cmd.SetComputeFloatParam(m_Compute, "_SolidNear", m_Near);
            cmd.SetComputeFloatParam(m_Compute, "_SolidFar", m_Far);
            cmd.SetComputeIntParam(m_Compute, "_SolidOrthographic", m_Ortho ? 1 : 0);
            cmd.SetComputeIntParam(m_Compute, "_SolidCount", m_Geometry.Candidates.Length);
            cmd.SetComputeIntParam(m_Compute, "_SolidGroups", (m_Geometry.Candidates.Length + 63) / 64);
            cmd.SetComputeIntParam(m_Compute, "_SolidEdges", m_Geometry.Mode == MeshSelectionMode.Edge ? 1 : 0);
            cmd.SetComputeIntParam(m_Compute, "_SolidPart", m_Part);
            cmd.SetComputeIntParam(m_Compute, "_SolidNodeCount", m_Geometry.Nodes.Length);
            cmd.SetComputeIntParam(m_Compute, "_SolidTriangleCount", m_Geometry.Triangles.Length);
        }

        private void Pick(CommandBuffer cmd, Vector2 pointer, MeshEditGpuRequest request)
        {
            var b = m_Buffers;
            int groups = (m_Geometry.Candidates.Length + 63) / 64;
            cmd.SetComputeVectorParam(m_Compute, "_Pointer", new Vector4(pointer.x, pointer.y, 0, 0));
            Bind(cmd, m_Distances, "_SolidResults", b.Results);
            Bind(cmd, m_Distances, "_PickValues", b.PickValues);
            Bind(cmd, m_Distances, "_PickGroups", b.PickGroups);
            cmd.DispatchCompute(m_Compute, m_Distances, groups, 1, 1);
            Bind(cmd, m_Minimum, "_PickGroups", b.PickGroups);
            Bind(cmd, m_Minimum, "_PickMinimum", b.Minimum);
            cmd.DispatchCompute(m_Compute, m_Minimum, 1, 1, 1);
            Bind(cmd, m_Winners, "_PickValues", b.PickValues);
            Bind(cmd, m_Winners, "_PickMinimum", b.Minimum);
            Bind(cmd, m_Winners, "_PickGroups", b.PickGroups);
            cmd.DispatchCompute(m_Compute, m_Winners, groups, 1, 1);
            Bind(cmd, m_Resolve, "_PickGroups", b.PickGroups);
            Bind(cmd, m_Resolve, "_Winner", b.Winner);
            Bind(cmd, m_Resolve, "_Reply", request?.Buffer ?? b.Scratch);
            cmd.SetComputeIntParam(m_Compute, "_RequestSerial", request?.Serial ?? 0);
            cmd.DispatchCompute(m_Compute, m_Resolve, 1, 1, 1);
        }

        private bool StartRequest(MeshEditGpuRequest r)
        {
            if (r == null || r.Submitted || r.Complete || Obsolete(r) || m_FreeReplies.Count == 0) return false;
            int words = 4 + (r.Box ? (m_Geometry.Candidates.Length + 31) / 32 : 0);
            int free = m_FreeReplies.Count - 1;
            var buffer = m_FreeReplies[free];
            m_FreeReplies.RemoveAt(free);
            if (buffer.count < words)
            {
                buffer.Dispose();
                buffer = new ComputeBuffer(words, 4);
            }

            r.Buffer = buffer;
            r.ReadbackBytes = words * 4;
            r.Submitted = true;
            m_InFlight.Add(r);
            return true;
        }

        private void Readback(CommandBuffer cmd, MeshEditGpuRequest r)
        {
            cmd.RequestAsyncReadback(r.Buffer, r.ReadbackBytes, 0, request =>
            {
                if (m_Disposed)
                {
                    r.Invalid = true;
                    r.Complete = true;
                    return;
                }

                try
                {
                    if (request.hasError)
                    {
                        r.ReadbackFailed = true;
                        r.Invalid = true;
                    }
                    else
                    {
                        var data = request.GetData<uint>();
                        r.Data = new uint[data.Length];
                        data.CopyTo(r.Data);
                    }
                }
                catch
                {
                    r.ReadbackFailed = true;
                    r.Invalid = true;
                }
                finally
                {
                    r.Complete = true;
                }
            });
        }

        private int m_LastPickRevision = -1;
        private Vector2 m_LastPickPointer;

        private void Render(CustomPassContext context)
        {
            if (!Active || m_Geometry == null || m_Buffers == null || context.hdCamera.camera != m_Camera) return;
            if (!m_Tool.TryGetMeshVisualizationObject(out var obj) || !m_Geometry.Matches(obj, m_Controller.SelectionMode) || m_Visibility != m_Controller.VisibilityMode || m_Tool.IsMeshEditFaceSelectionActive) return;
            try
            {
                SetCamera(context.hdCamera.camera, obj.Transform);
                m_LastPass = Time.realtimeSinceStartupAsDouble;
                var cmd = context.cmd;
                var b = m_Buffers;
                if (m_Geometry.Candidates.Length == 0) return;
                b.Unsafe = true;
                if (m_SubmittedGeometryRevision != m_Geometry.GeometryRevision)
                {
                    UploadGeometryChanges(cmd);
                    m_SubmittedGeometryRevision = m_Geometry.GeometryRevision;
                }

                Constants(cmd);
                if (m_ClassifiedRevision != m_Revision || m_ClassifiedGeometryRevision != m_SubmittedGeometryRevision)
                {
                    string sample = NeedsOcclusion ? "PO2 Solid visibility" : "PO2 X-Ray projection";
                    cmd.BeginSample(sample);
                    cmd.SetBufferCounterValue(b.Visible, 0);
                    int classify = NeedsOcclusion ? m_Classify : m_ClassifyXRay;
                    Bind(cmd, classify, "_SolidCandidates", b.Candidates);
                    if (NeedsOcclusion)
                    {
                        Bind(cmd, classify, "_SolidNormals", b.Normals);
                        Bind(cmd, classify, "_SolidNodes", b.Nodes);
                        Bind(cmd, classify, "_SolidTriangles", b.Triangles);
                    }

                    Bind(cmd, classify, "_GeometryFault", b.UpdateFault);
                    Bind(cmd, classify, "_SolidResults", b.Results);
                    Bind(cmd, classify, "_Visible", b.Visible);
                    cmd.DispatchCompute(m_Compute, classify, (m_Geometry.Candidates.Length + 63) / 64, 1, 1);
                    cmd.CopyCounterValue(b.Visible, b.Args, 4);
                    cmd.EndSample(sample);
                    m_ClassifiedRevision = m_Revision;
                    m_ClassifiedGeometryRevision = m_SubmittedGeometryRevision;
                }

                if (StartRequest(m_Action))
                {
                    if (m_Action.Box)
                    {
                        // Reuse the cursor reduction to report any classification
                        // fault before the box kernel fills the detached bitset.
                        Pick(cmd, m_Pointer, m_Action);
                        var rect = m_Action.Rectangle;
                        cmd.SetComputeVectorParam(m_Compute, "_Rectangle", new Vector4(rect.xMin, rect.yMin, rect.xMax, rect.yMax));
                        cmd.SetComputeIntParam(m_Compute, "_RequestSerial", m_Action.Serial);
                        Bind(cmd, m_Box, "_SolidResults", b.Results);
                        Bind(cmd, m_Box, "_Reply", m_Action.Buffer);
                        cmd.DispatchCompute(m_Compute, m_Box, ((m_Geometry.Candidates.Length + 31) / 32 + 63) / 64, 1, 1);
                    }
                    else Pick(cmd, m_Action.Pointer, m_Action);

                    Readback(cmd, m_Action);
                    m_LastPickRevision = -1;
                }

                bool changed = m_LastPickRevision != m_Revision || !m_LastPickPointer.Equals(m_Pointer);
                if (m_Hover == null && (m_HoverRevision != m_Revision || !m_HoverPointer.Equals(m_Pointer)))
                    m_Hover = NewRequest(m_Pointer, default, false);
                bool readHover = StartRequest(m_Hover);
                if (changed || readHover)
                {
                    cmd.BeginSample("PO2 mesh cursor");
                    Pick(cmd, m_Pointer, readHover ? m_Hover : null);
                    if (readHover) Readback(cmd, m_Hover);
                    m_LastPickRevision = m_Revision;
                    m_LastPickPointer = m_Pointer;
                    cmd.EndSample("PO2 mesh cursor");
                }

                var block = context.propertyBlock;
                block.SetBuffer("_SolidResults", b.Results);
                block.SetBuffer("_Visible", b.Visible);
                block.SetBuffer("_Selected", b.Selected);
                block.SetBuffer("_Winner", b.Winner);
                block.SetMatrix("_DrawViewProjection", context.hdCamera.mainViewConstants.projMatrix * m_View);
                block.SetVector("_DrawViewport", new Vector4(m_Viewport.x, m_Viewport.y, m_Viewport.width, m_Viewport.height));
                block.SetFloat("_Near", m_Near);
                block.SetFloat("_Far", m_Far);
                var inverseView = m_View.inverse;
                block.SetVector("_DrawCameraPosition", inverseView.GetColumn(3));
                block.SetVector("_CameraRight", inverseView.GetColumn(0));
                block.SetVector("_CameraUp", inverseView.GetColumn(1));
                block.SetVector("_MarkerRadius", new Vector4(10f * Mathf.Tan(m_Camera.fieldOfView * Mathf.Deg2Rad * 0.5f) / Mathf.Max(1, m_Viewport.height), 10f * m_Camera.orthographicSize / Mathf.Max(1, m_Viewport.height), m_Ortho ? 1 : 0, 0));
                bool edges = m_Geometry.Mode == MeshSelectionMode.Edge;
                block.SetInt("_Edges", edges ? 1 : 0);
                block.SetColor("_NormalColor", (edges ? VertexRenderSystem.s_EdgeNormalColor : VertexMarkerVisualStateUtility.s_NormalColor).linear);
                block.SetColor("_HoveredColor", VertexMarkerVisualStateUtility.s_HoveredColor.linear);
                block.SetColor("_SelectedColor", VertexMarkerVisualStateUtility.s_SelectedColor.linear);
                block.SetColor("_SelectedHoveredColor", VertexMarkerVisualStateUtility.s_SelectedHoveredColor.linear);
                m_Controller.GpuTargetVisual(out int target, out Color color, out bool suppress);
                block.SetInt("_TargetSlot", target);
                block.SetColor("_TargetColor", color.linear);
                block.SetInt("_SuppressHover", suppress || m_Tool.GpuHoverSuppressed ? 1 : 0);
                cmd.BeginSample("PO2 mesh draw");
                cmd.DrawProceduralIndirect(Matrix4x4.identity, m_Material, 0, UnityEngine.MeshTopology.Triangles, b.Args, 0, block);
                cmd.EndSample("PO2 mesh draw");
                b.Submitted = true;
                b.Unsafe = false;
            }
            catch (Exception e)
            {
                Fail(e.Message);
            }
        }

        internal void Poll()
        {
            DrainPreparation();
            for (int i = m_InFlight.Count - 1; i >= 0; i--)
            {
                var r = m_InFlight[i];
                if (!r.Complete)
                {
                    if (!m_Failed && Time.realtimeSinceStartupAsDouble - r.Started > 10)
                    {
                        Fail("GPU readback timed out. Pending selection was cancelled.");
                        return;
                    }

                    continue;
                }

                m_FreeReplies.Add(r.Buffer);
                r.Buffer = null;
                m_InFlight.RemoveAt(i);
                if (!m_Failed && r.ReadbackFailed)
                {
                    Fail("GPU query readback failed. Pending selection was cancelled.");
                    return;
                }

                if (!m_Failed && !r.Invalid && r.Data != null && r.Data.Length >= 4 && (r.Data[2] != 0 || r.Data[3] != 3))
                {
                    Fail("The GPU reported an invalid visibility tree or query layout.");
                    return;
                }
            }

            for (int i = m_Retired.Count - 1; i >= 0; i--)
            {
                var b = m_Retired[i];
                if (b.Unsafe) continue;
                if (b.Submitted && !b.RetirementQueued)
                {
                    // This set is no longer drawn. Queue a four-byte completion
                    // marker after its submitted graphics work, once per retirement.
                    b.RetirementQueued = true;
                    b.RetiredAt = Time.realtimeSinceStartupAsDouble;
                    try
                    {
                        AsyncGPUReadback.Request(b.Scratch, 4, 0, request =>
                        {
                            b.RetirementFailed = request.hasError;
                            b.RetirementComplete = true;
                        });
                    }
                    catch
                    {
                        b.RetirementFailed = true;
                        b.RetirementComplete = true;
                    }
                }

                if (b.RetirementFailed || (b.RetirementQueued && !b.RetirementComplete && Time.realtimeSinceStartupAsDouble - b.RetiredAt > 10))
                {
                    // Keep uncertain resources until shutdown. Never release them
                    // early or retry a failed completion check every update.
                    b.Unsafe = true;
                    if (!m_Failed)
                    {
                        Fail("GPU buffer retirement failed or timed out.");
                        return;
                    }

                    continue;
                }

                if (b.Submitted && !b.RetirementComplete) continue;
                b.Dispose();
                m_Retired.RemoveAt(i);
            }
        }

        private void Fail(string reason)
        {
            if (!m_Failed) Mod.log.Warn("[PO Mesh GPU] Using the CPU backend for this session: " + reason);
            m_Failed = true;
            Stop();
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            Stop();
            m_Disposed = true;
            if (m_RetiredPreparation != null)
            {
                var retired = m_RetiredPreparation;
                m_RetiredPreparation = null;
                // Detached CPU work owns no Unity resources; never wait for it at shutdown.
                retired.Completion.ContinueWith(_ => retired.Release(), System.Threading.Tasks.TaskScheduler.Default);
            }

            // Shutdown only. All normal updates poll completion without waiting.
            // Drain only our buffers at teardown, never unrelated game requests.
            foreach (var r in m_InFlight)
                if (r.Buffer != null)
                {
                    if (!r.Complete) AsyncGPUReadback.Request(r.Buffer).WaitForCompletion();
                    r.Buffer.Dispose();
                }

            if (m_Buffers != null && (m_Buffers.Unsafe || (m_Buffers.Submitted && !m_Buffers.RetirementComplete)))
                AsyncGPUReadback.Request(m_Buffers.Scratch).WaitForCompletion();
            foreach (var b in m_Retired)
                if (b.Unsafe || (b.Submitted && !b.RetirementComplete))
                    AsyncGPUReadback.Request(b.Scratch).WaitForCompletion();
            m_InFlight.Clear();
            foreach (var b in m_FreeReplies) b.Dispose();
            m_FreeReplies.Clear();
            m_Buffers?.Dispose();
            foreach (var b in m_Retired) b.Dispose();
            m_Retired.Clear();
            if (m_Host != null) UnityEngine.Object.Destroy(m_Host);
            if (m_Material != null) UnityEngine.Object.Destroy(m_Material);
        }
    }
}