using System;
using PO2.Geometry;
using PO2.Geometry.Topology;

namespace PO2.Core.Primitives
{
    // Factory result containing both renderable triangles and structural faces. 
    // The latter preserve modeling intent for selection and topology operations.
    // IE. one quad represented by two triangles
    public sealed class PrimitiveMesh
    {
        public MeshData Mesh { get; }
        public StructuralFace[] StructuralFaces { get; }

        public PrimitiveMesh(MeshData mesh, StructuralFace[] structuralFaces)
        {
            Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
            StructuralFaces = structuralFaces ?? Array.Empty<StructuralFace>();
        }
    }
}