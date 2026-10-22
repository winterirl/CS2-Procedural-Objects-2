using System;

namespace PO2.Core.Primitives
{
    // Immutable metadata for one built-in primitive. 
    // Definitions keep UI naming and default creation parameters aligned with the mesh factory.
    public sealed class PrimitiveDefinition
    {
        public PrimitiveKind Type { get; }
        public string DisplayName { get; }
        public string PartName { get; }
        public PrimitiveParameters DefaultParameters { get; }

        public PrimitiveDefinition(PrimitiveKind type, string displayName, string partName, PrimitiveParameters defaultParameters)
        {
            if (defaultParameters == null)
            {
                throw new ArgumentNullException(nameof(defaultParameters));
            }

            if (defaultParameters.PrimitiveType != type)
            {
                throw new ArgumentException("Default parameters must match the primitive definition type.", nameof(defaultParameters));
            }

            Type = type;
            DisplayName = displayName;
            PartName = partName;
            DefaultParameters = defaultParameters;
        }
    }

    // Shared definitions are safe here~ every placement gets a fresh parameter snapshot.
    public static class PrimitiveDefinitions
    {
        private static readonly PrimitiveDefinition s_Box = new PrimitiveDefinition(PrimitiveKind.Box, "Box", "Box", new BoxParameters(10f, 10f, 10f));

        private static readonly PrimitiveDefinition s_Plane = new PrimitiveDefinition(PrimitiveKind.Plane, "Plane", "Plane", new PlaneParameters(10f, 10f));

        private static readonly PrimitiveDefinition s_Wedge = new PrimitiveDefinition(PrimitiveKind.Wedge, "Wedge", "Wedge", new WedgeParameters(10f, 10f, 10f));

        private static readonly PrimitiveDefinition s_Cylinder = new PrimitiveDefinition(PrimitiveKind.Cylinder, "Cylinder", "Cylinder", new CylinderParameters(5f, 10f, 12));

        private static readonly PrimitiveDefinition s_Cone = new PrimitiveDefinition(PrimitiveKind.Cone, "Cone", "Cone", new ConeParameters(10f, 0f, 10f, 12));

        private static readonly PrimitiveDefinition s_Arch = new PrimitiveDefinition(PrimitiveKind.Arch, "Arch", "Arch", new ArchParameters(10f, 10f, 2f, 6f, 8f, 12));

        private static readonly PrimitiveDefinition s_Stairs = new PrimitiveDefinition(PrimitiveKind.Stairs, "Stairs", "Stairs", new StairsParameters(4f, 0.3f, 10));

        private static readonly PrimitiveDefinition s_Sphere = new PrimitiveDefinition(PrimitiveKind.Sphere, "Sphere", "Sphere", new SphereParameters(5f, 24, 12));

        private static readonly PrimitiveDefinition s_Dome = new PrimitiveDefinition(PrimitiveKind.Dome, "Dome", "Dome", new DomeParameters(5f, 5f, 24, 6));

        public static string GetDisplayName(PrimitiveKind type)
        {
            if (TryGet(type, out PrimitiveDefinition definition))
            {
                return definition.DisplayName;
            }

            return type.ToString();
        }

        public static bool TryGet(PrimitiveKind type, out PrimitiveDefinition definition)
        {
            switch (type)
            {
                case PrimitiveKind.Box:
                    definition = s_Box;
                    return true;

                case PrimitiveKind.Plane:
                    definition = s_Plane;
                    return true;

                case PrimitiveKind.Wedge:
                    definition = s_Wedge;
                    return true;

                case PrimitiveKind.Cylinder:
                    definition = s_Cylinder;
                    return true;

                case PrimitiveKind.Cone:
                    definition = s_Cone;
                    return true;

                case PrimitiveKind.Arch:
                    definition = s_Arch;
                    return true;

                case PrimitiveKind.Stairs:
                    definition = s_Stairs;
                    return true;

                case PrimitiveKind.Sphere:
                    definition = s_Sphere;
                    return true;

                case PrimitiveKind.Dome:
                    definition = s_Dome;
                    return true;

                default:
                    definition = null;
                    return false;
            }
        }
    }
}