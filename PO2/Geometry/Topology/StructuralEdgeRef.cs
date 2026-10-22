using System;

namespace PO2.Geometry.Topology
{
    // Same deal as VertexRef: structural edge ids restart for every
    // part, so the part index is part of the identity.
    public readonly struct StructuralEdgeRef : IEquatable<StructuralEdgeRef>
    {
        public int PartIndex { get; }
        public int EdgeId { get; }

        public StructuralEdgeRef(int partIndex, int edgeId)
        {
            PartIndex = partIndex;
            EdgeId = edgeId;
        }

        public bool Equals(StructuralEdgeRef other)
        {
            return PartIndex == other.PartIndex && EdgeId == other.EdgeId;
        }

        public override bool Equals(object obj)
        {
            return obj is StructuralEdgeRef other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (PartIndex * 397) ^ EdgeId;
            }
        }

        // I don't remeber the last time I had a legit reason to overload an operator
        // I'm weirdly happy to do this
        public static bool operator ==(StructuralEdgeRef left, StructuralEdgeRef right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(StructuralEdgeRef left, StructuralEdgeRef right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return $"Part {PartIndex}, Structural Edge {EdgeId}";
        }
    }
}