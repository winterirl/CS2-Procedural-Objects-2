using System;
using System.Collections;
using System.Collections.Generic;

namespace PO2.Geometry.Topology
{
    // Tiny read-only view into one shared topology buffer.
    // Converted meshes can have hundreds of thousands of logical vertices,
    // so giving every vertex three separate int[] allocations is a painful amount of garbage collection.
    public readonly struct IndexSpan : IReadOnlyList<int>
    {
        private readonly int[] m_Items;
        private readonly int m_Offset;

        public int Count { get; }

        public int this[int index]
        {
            get
            {
                if (index < 0 || index >= Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return m_Items[m_Offset + index];
            }
        }

        public IndexSpan(int[] items) : this(items ?? Array.Empty<int>(), 0, items?.Length ?? 0)
        {
        }

        internal IndexSpan(int[] items, int offset, int count)
        {
            m_Items = items ?? throw new ArgumentNullException(nameof(items));

            if (offset < 0 || count < 0 || offset > items.Length - count)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "The topology span is outside its shared buffer.");
            }

            m_Offset = offset;
            Count = count;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(m_Items, m_Offset, Count);
        }

        IEnumerator<int> IEnumerable<int>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public struct Enumerator : IEnumerator<int>
        {
            private readonly int[] m_Items;
            private readonly int m_End;
            private int m_Index;

            public int Current => m_Items[m_Index];
            object IEnumerator.Current => Current;

            internal Enumerator(int[] items, int offset, int count)
            {
                m_Items = items ?? Array.Empty<int>();
                m_Index = offset - 1;
                m_End = offset + count;
            }

            public bool MoveNext()
            {
                int next = m_Index + 1;
                if (next >= m_End)
                {
                    return false;
                }

                m_Index = next;
                return true;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
            }
        }
    }
}