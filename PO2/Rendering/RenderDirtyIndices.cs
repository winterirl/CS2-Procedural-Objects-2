using System;
using System.Collections.Generic;

namespace PO2.Rendering
{
    // Keep the set between frames. Clearing it only visits words we touched.
    internal sealed class RenderDirtyIndices
    {
        internal readonly uint[] Bits;
        internal readonly int[] Words;
        private int m_WordCount;
        private bool m_Sorted = true;
        internal readonly int Length;
        internal int Count { get; private set; }
        internal long Bytes => 8L * Bits.Length;

        internal RenderDirtyIndices(int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            Length = length;
            int words = (int)(((long)length + 31) / 32);
            Bits = new uint[words];
            Words = new int[words];
        }

        internal void Add(int index)
        {
            if ((uint)index >= (uint)Length) throw new ArgumentOutOfRangeException(nameof(index));
            int word = index >> 5;
            uint bit = 1u << (index & 31);
            if ((Bits[word] & bit) != 0) return;
            if (Bits[word] == 0)
            {
                Words[m_WordCount++] = word;
                m_Sorted = false;
            }

            Bits[word] |= bit;
            Count++;
        }

        internal unsafe RenderGeometryKernels.Set Describe(uint* bits, int* words) =>
            new RenderGeometryKernels.Set { Bits = bits, Words = words, Length = Length, WordCount = m_WordCount, Count = Count };

        internal unsafe void Accept(RenderGeometryKernels.Set set)
        {
            m_WordCount = set.WordCount;
            Count = set.Count;
            m_Sorted = false;
        }

        internal unsafe bool Union(IReadOnlyList<int> indices)
        {
            if (indices is int[] array)
            {
                fixed (int* input = array)
                fixed (uint* bits = Bits)
                fixed (int* words = Words)
                {
                    var context = new RenderGeometryKernels.NoticeContext
                    {
                        Indices = input, Count = array.Length, Dirty = Describe(bits, words)
                    };
                    RenderGeometryKernels.Collect(ref context);
                    Accept(context.Dirty);
                    return context.Error == 0;
                }
            }

            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                if ((uint)index >= (uint)Length) return false;
                Add(index);
            }

            return true;
        }

        internal int SortedWordCount
        {
            get
            {
                if (!m_Sorted)
                {
                    Array.Sort(Words, 0, m_WordCount);
                    m_Sorted = true;
                }

                return m_WordCount;
            }
        }

        // Compare the complete owned union. Word insertion order is irrelevant.
        internal bool SameMembers(RenderDirtyIndices other)
        {
            if (Length != other.Length || Count != other.Count || m_WordCount != other.m_WordCount) return false;
            for (int i = 0; i < m_WordCount; i++)
            {
                int word = Words[i];
                if (Bits[word] != other.Bits[word]) return false;
            }

            return true;
        }

        internal void CopyFrom(RenderDirtyIndices other)
        {
            if (Length != other.Length) throw new InvalidOperationException("Renderer work length changed.");
            Clear();
            for (int i = 0; i < other.m_WordCount; i++)
            {
                int word = other.Words[i];
                Words[i] = word;
                Bits[word] = other.Bits[word];
            }

            // Bulk cutoff counts can exceed the stored bit population.
            m_WordCount = other.m_WordCount;
            Count = other.Count;
            m_Sorted = other.m_Sorted;
        }

        internal void Clear()
        {
            for (int i = 0; i < m_WordCount; i++) Bits[Words[i]] = 0;
            m_WordCount = 0;
            Count = 0;
            m_Sorted = true;
        }

        public Enumerator GetEnumerator()
        {
            if (!m_Sorted)
            {
                Array.Sort(Words, 0, m_WordCount);
                m_Sorted = true;
            }

            return new Enumerator(this);
        }

        internal struct Enumerator
        {
            private readonly RenderDirtyIndices m_Set;
            private int m_Word, m_Base;
            private uint m_Remaining;
            public int Current { get; private set; }

            internal Enumerator(RenderDirtyIndices set)
            {
                m_Set = set;
                m_Word = 0;
                m_Base = 0;
                m_Remaining = 0;
                Current = -1;
            }

            public bool MoveNext()
            {
                if (m_Remaining == 0)
                {
                    if (m_Word == m_Set.m_WordCount) return false;
                    int word = m_Set.Words[m_Word++];
                    m_Base = word << 5;
                    m_Remaining = m_Set.Bits[word];
                }

                uint bits = m_Remaining;
                int shift = 0;
                if ((bits & 0xffffu) == 0)
                {
                    shift += 16;
                    bits >>= 16;
                }

                if ((bits & 0xffu) == 0)
                {
                    shift += 8;
                    bits >>= 8;
                }

                if ((bits & 0xfu) == 0)
                {
                    shift += 4;
                    bits >>= 4;
                }

                if ((bits & 3u) == 0)
                {
                    shift += 2;
                    bits >>= 2;
                }

                if ((bits & 1u) == 0) shift++;
                Current = m_Base + shift;
                m_Remaining &= m_Remaining - 1;
                return true;
            }
        }
    }

    internal sealed class RenderUploadPlan
    {
        internal enum DenseCause
        {
            None,
            Density,
            Fragmentation,
            CpuBulk
        }

        internal DenseCause Cause { get; private set; }
        internal const int kMaximumRanges = 32;
        internal const int kMaximumGapBytes = 256;

        internal struct Range
        {
            internal int Start, Count;

            internal Range(int start, int count)
            {
                Start = start;
                Count = count;
            }
        }

        internal readonly Range[] Ranges = new Range[kMaximumRanges];
        internal int RangeCount { get; private set; }
        internal int UploadedVertices { get; private set; }
        internal bool Dense { get; private set; }

        internal void Build(RenderDirtyIndices dirty, int stride, bool bulk = false)
        {
            if (stride <= 0) throw new ArgumentOutOfRangeException(nameof(stride));
            RangeCount = 0;
            UploadedVertices = 0;
            Dense = false;
            Cause = DenseCause.None;
            if (dirty.Count == 0) return;
            if (bulk)
            {
                Full(dirty.Length, DenseCause.CpuBulk);
                return;
            }

            if (dirty.Count * 2L >= dirty.Length)
            {
                Full(dirty.Length, DenseCause.Density);
                return;
            }

            int start = -1, last = -1;
            int wordCount = dirty.SortedWordCount;
            for (int word = 0; word < wordCount; word++)
            {
                int wordIndex = dirty.Words[word];
                uint bits = dirty.Bits[wordIndex];
                while (bits != 0)
                {
                    int first = RenderGeometryKernels.First(bits);
                    uint remaining = ~(bits >> first);
                    int length = remaining == 0 ? 32 : RenderGeometryKernels.First(remaining);
                    uint mask = length == 32 ? uint.MaxValue : ((1u << length) - 1) << first;
                    bits &= ~mask;
                    int index = wordIndex * 32 + first, end = index + length - 1;
                    if (start < 0)
                    {
                        start = index;
                        last = end;
                        continue;
                    }

                    if ((index - last - 1L) * stride <= kMaximumGapBytes)
                    {
                        last = end;
                        continue;
                    }

                    if (!Add(start, last - start + 1))
                    {
                        Full(dirty.Length, DenseCause.Fragmentation);
                        return;
                    }

                    start = index;
                    last = end;
                }
            }

            if (!Add(start, last - start + 1)) Full(dirty.Length, DenseCause.Fragmentation);
            else if (UploadedVertices * 2L >= dirty.Length) Full(dirty.Length, DenseCause.Density);
        }

        private bool Add(int start, int count)
        {
            if (RangeCount == kMaximumRanges) return false;
            Ranges[RangeCount++] = new Range(start, count);
            UploadedVertices += count;
            return true;
        }

        private void Full(int count, DenseCause cause)
        {
            Cause = cause;
            Dense = true;
            RangeCount = 1;
            UploadedVertices = count;
            Ranges[0] = new Range(0, count);
        }
    }
}