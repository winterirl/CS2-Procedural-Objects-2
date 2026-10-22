using System;
using System.Threading;
using UnityEngine;
using PO2.Geometry.Topology;

namespace PO2.Tools.Snapping
{
    // Each pose is immutable. A later pose can copy the tree's grouping and
    // refit its bounds without changing any source still used by a drag.
    internal sealed class TranslationSnapSourceData
    {
        internal readonly Primitive[] Primitives;
        internal readonly int PrimitiveCount;
        internal readonly int[] PrimitiveIndices;
        internal readonly Node[] Nodes;
        internal readonly VertexRef[] EffectiveVertices;
        internal readonly MeshTranslationSnapCapture.Input Input;
        private readonly MeshTranslationSnapBudget.Reservation m_Reservation;
        internal int NodeCount { get; private set; }
        internal long BoundsVisits, PartitionVisits, Partitions, PartitionFallbacks;
        internal bool Refit { get; private set; }
        internal long EstimatedResidentBytes => (long)Primitives.Length * 24 + (long)PrimitiveIndices.Length * 4 + (long)Nodes.Length * 40 + (long)EffectiveVertices.Length * 8 + (Input?.EstimatedBytes ?? 0) + 4096;

        internal void Retain() => m_Reservation?.Retain();
        internal void Release() => m_Reservation?.Dispose();

        internal TranslationSnapSourceData(Primitive[] primitives, int primitiveCount, VertexRef[] vertices, CancellationToken cancellation, MeshTranslationSnapCapture.Input input = null, TranslationSnapSourceData reuse = null, MeshTranslationSnapBudget.Reservation reservation = null)
        {
            if (primitiveCount <= 0 || primitiveCount > primitives.Length)
                throw new InvalidOperationException("The mesh selection has no usable snapping points.");
            Primitives = primitives;
            PrimitiveCount = primitiveCount;
            EffectiveVertices = vertices;
            Input = input;
            m_Reservation = reservation;
            int leaves = 1;
            while (leaves * 8 < primitiveCount) leaves *= 2;
            PrimitiveIndices = new int[primitiveCount];
            Nodes = new Node[Math.Max(1, leaves * 2 - 1)];
            if (reuse != null && reuse.PrimitiveCount == primitiveCount && input != null && input.Layout != null && ReferenceEquals(input.Layout, reuse.Input?.Layout))
            {
                Array.Copy(reuse.PrimitiveIndices, PrimitiveIndices, primitiveCount);
                Array.Copy(reuse.Nodes, Nodes, reuse.NodeCount);
                NodeCount = reuse.NodeCount;
                Refit = true;
                RefitBounds(cancellation);
            }
            else
            {
                var points = new TranslationSnapBuildKernels.BuildPoint[primitiveCount];
                for (int i = 0; i < primitiveCount; i++)
                {
                    if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                    PrimitiveIndices[i] = i;
                    points[i] = new TranslationSnapBuildKernels.BuildPoint
                    {
                        Minimum = Vector3.Min(primitives[i].Start, primitives[i].End),
                        Maximum = Vector3.Max(primitives[i].Start, primitives[i].End),
                        Center = (primitives[i].Start + primitives[i].End) * 0.5f,
                    };
                }

                Build(points, cancellation);
            }
        }

        private struct Range
        {
            internal int Start, Count, Node;
        }

        private unsafe void Build(TranslationSnapBuildKernels.BuildPoint[] points, CancellationToken token)
        {
            var stack = new Range[64];
            int count = 1;
            NodeCount = 1;
            stack[0] = new Range { Start = 0, Count = PrimitiveCount, Node = 0 };
            fixed (TranslationSnapBuildKernels.BuildPoint* pointPointer = points)
            fixed (int* orderPointer = PrimitiveIndices)
            {
                while (count > 0)
                {
                    var range = stack[--count];
                    var c = new TranslationSnapBuildKernels.Context
                    {
                        Points = pointPointer, Order = orderPointer, Start = range.Start, Count = range.Count,
                        Minimum = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue),
                        CenterMinimum = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue),
                        Maximum = new Vector3(float.MinValue, float.MinValue, float.MinValue),
                        CenterMaximum = new Vector3(float.MinValue, float.MinValue, float.MinValue),
                    };
                    do
                    {
                        token.ThrowIfCancellationRequested();
                        TranslationSnapBuildKernels.Advance(ref c);
                    } while (c.Stage != 4);

                    BoundsVisits += c.BoundsVisits;
                    PartitionVisits += c.PartitionVisits;
                    Partitions += c.Partitions;
                    PartitionFallbacks += c.Fallbacks;
                    var node = new Node { Minimum = c.Minimum, Maximum = c.Maximum, Left = -1, Right = -1 };
                    if (range.Count <= 8)
                    {
                        node.Start = range.Start;
                        node.Count = range.Count;
                    }
                    else
                    {
                        node.Left = NodeCount++;
                        node.Right = NodeCount++;
                        int middle = range.Start + range.Count / 2;
                        stack[count++] = new Range { Start = middle, Count = range.Start + range.Count - middle, Node = node.Right };
                        stack[count++] = new Range { Start = range.Start, Count = middle - range.Start, Node = node.Left };
                    }

                    Nodes[range.Node] = node;
                }
            }
        }

        private void RefitBounds(CancellationToken token)
        {
            for (int i = NodeCount - 1; i >= 0; i--)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                var node = Nodes[i];
                if (node.Count > 0)
                {
                    Vector3 minimum = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                    Vector3 maximum = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                    for (int j = 0; j < node.Count; j++)
                    {
                        var primitive = Primitives[PrimitiveIndices[node.Start + j]];
                        minimum = Vector3.Min(minimum, Vector3.Min(primitive.Start, primitive.End));
                        maximum = Vector3.Max(maximum, Vector3.Max(primitive.Start, primitive.End));
                        BoundsVisits++;
                    }

                    node.Minimum = minimum;
                    node.Maximum = maximum;
                }
                else
                {
                    node.Minimum = Vector3.Min(Nodes[node.Left].Minimum, Nodes[node.Right].Minimum);
                    node.Maximum = Vector3.Max(Nodes[node.Left].Maximum, Nodes[node.Right].Maximum);
                }

                Nodes[i] = node;
            }
        }

        internal readonly struct Primitive
        {
            public Vector3 Start { get; }
            public Vector3 End { get; }

            public Primitive(Vector3 start, Vector3 end)
            {
                Start = start;
                End = end;
            }
        }

        internal struct Node
        {
            public Vector3 Minimum, Maximum;
            public int Start, Count, Left, Right;
        }
    }
}