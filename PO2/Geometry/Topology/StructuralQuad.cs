namespace PO2.Geometry.Topology
{
    // Quad-specific editing code benefits from a type that cannot contain three corners.
    // The value converts to the shared face record when stored.
    public readonly struct StructuralQuad
    {
        private readonly StructuralFace m_Face;

        public int Id => m_Face.Id;
        public int CornerCount => m_Face.CornerCount;

        public StructuralQuad(int id, int vertex0, int vertex1, int vertex2, int vertex3)
        {
            m_Face = new StructuralFace(id, vertex0, vertex1, vertex2, vertex3);
        }

        private StructuralQuad(StructuralFace face)
        {
            m_Face = face;
        }

        public int GetRenderVertexIndex(int cornerIndex)
        {
            return m_Face.GetRenderVertexIndex(cornerIndex);
        }

        public StructuralQuad CloneWithId(int id)
        {
            return new StructuralQuad(m_Face.CloneWithId(id));
        }

        public StructuralQuad Clone()
        {
            return this;
        }

        public static implicit operator StructuralFace(StructuralQuad quad)
        {
            return quad.m_Face;
        }
    }
}