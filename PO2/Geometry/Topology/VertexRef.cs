using System;

namespace PO2.Geometry.Topology
{
    // Vertex ids are only unique inside a part. Passing bare ints through the
    // mesh editor worked right up until editing all parts became a thing.
    public readonly struct VertexRef : IEquatable<VertexRef>
    {
        public int PartIndex { get; }
        public int VertexId { get; }

        public VertexRef(int partIndex, int vertexId)
        {
            PartIndex = partIndex;
            VertexId = vertexId;
        }

        public bool Equals(VertexRef other)
        {
            return PartIndex == other.PartIndex && VertexId == other.VertexId;
        }

        public override bool Equals(object obj)
        {
            return obj is VertexRef other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (PartIndex * 397) ^ VertexId;
            }
        }

        public static bool operator ==(VertexRef left, VertexRef right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VertexRef left, VertexRef right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return $"Part {PartIndex}, Vertex {VertexId}";
        }
    }
}