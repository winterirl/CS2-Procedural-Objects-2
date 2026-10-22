using PO2.Geometry;
using PO2.Materials;

namespace PO2.Core.Primitives
{
    // Generates local mesh data and default parts for built-in primitives.
    // Primitives and converted POs all use the same render/logical/structural topology model
    // lol a factory, microsoft java
    public static partial class PrimitiveMeshFactory
    {
        public static PrimitiveMesh CreatePrimitive(PrimitiveKind primitiveType)
        {
            if (!PrimitiveDefinitions.TryGet(primitiveType, out PrimitiveDefinition definition))
            {
                return null;
            }

            return CreatePrimitive(definition.DefaultParameters);
        }

        public static PrimitiveMesh CreatePrimitive(PrimitiveParameters parameters)
        {
            if (parameters == null)
            {
                return null;
            }

            switch (parameters.PrimitiveType)
            {
                case PrimitiveKind.Box:
                {
                    BoxParameters box = parameters as BoxParameters;
                    return box != null ? new PrimitiveMesh(CreateBox(box.Width, box.Height, box.Depth), CreateBoxStructuralFaces()) : null;
                }

                case PrimitiveKind.Plane:
                {
                    PlaneParameters plane = parameters as PlaneParameters;
                    return plane != null ? CreatePlanePrimitive(plane.Width, plane.Depth) : null;
                }

                case PrimitiveKind.Wedge:
                {
                    WedgeParameters wedge = parameters as WedgeParameters;
                    return wedge != null ? CreateWedgePrimitive(wedge.Width, wedge.Height, wedge.Depth) : null;
                }

                case PrimitiveKind.Cylinder:
                {
                    CylinderParameters cylinder = parameters as CylinderParameters;
                    return cylinder != null ? CreateCylinderPrimitive(cylinder.Radius, cylinder.Height, cylinder.Segments) : null;
                }

                case PrimitiveKind.Cone:
                {
                    ConeParameters cone = parameters as ConeParameters;
                    return cone != null ? CreateConePrimitive(cone.BottomDiameter, cone.TopDiameter, cone.Height, cone.Segments) : null;
                }

                case PrimitiveKind.Arch:
                {
                    ArchParameters arch = parameters as ArchParameters;
                    return arch != null ? CreateArchPrimitive(arch.Width, arch.Height, arch.Depth, arch.OpeningWidth, arch.OpeningHeight, arch.CurveSegments) : null;
                }

                case PrimitiveKind.Stairs:
                {
                    StairsParameters stairs = parameters as StairsParameters;
                    return stairs != null ? CreateStairsPrimitive(stairs.Width, stairs.TreadDepth, stairs.Steps) : null;
                }

                case PrimitiveKind.Sphere:
                {
                    SphereParameters sphere = parameters as SphereParameters;
                    return sphere != null ? CreateSpherePrimitive(sphere.Radius, sphere.Segments, sphere.VerticalSegments) : null;
                }

                case PrimitiveKind.Dome:
                {
                    DomeParameters dome = parameters as DomeParameters;
                    return dome != null ? CreateDomePrimitive(dome.Radius, dome.Height, dome.Segments, dome.VerticalSegments) : null;
                }

                default:
                    return null;
            }
        }

        public static MeshPart CreatePrimitivePart(PrimitiveParameters parameters)
        {
            if (parameters == null || !PrimitiveDefinitions.TryGet(parameters.PrimitiveType, out PrimitiveDefinition definition))
            {
                return null;
            }

            PrimitiveMesh primitive = CreatePrimitive(parameters);

            if (primitive == null)
            {
                return null;
            }

            var part = new MeshPart(definition.PartName, primitive.Mesh, new MaterialData(MaterialLibrary.kPrototypeNoisyGray, new TextureMappingData(TextureMappingMode.Repeat)), primitive.StructuralFaces);
            if (parameters.PrimitiveType == PrimitiveKind.Sphere || parameters.PrimitiveType == PrimitiveKind.Dome)
            {
                part.SetSurfaceAreaSourceVertexAttributes(0, VertexAttributeFlags.UV0FourComponent | VertexAttributeFlags.SurfaceUVCoordinates);
            }

            return part;
        }
    }
}