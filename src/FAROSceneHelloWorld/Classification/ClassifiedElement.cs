using System;
using System.Collections.Generic;
using System.Numerics;
using FAROSceneHelloWorld.Geometry;

namespace FAROSceneHelloWorld.Classification
{
    public enum StructuralElementType
    {
        Floor,
        Ceiling,
        Wall,
        Column,
        Pipe,
        Unclassified
    }

    /// <summary>
    /// Represents an extracted and classified architectural or MEP element with parametric geometry.
    /// </summary>
    public class ClassifiedElement
    {
        public string Name { get; set; }
        public StructuralElementType Type { get; set; }
        public string ClusterName { get; set; }

        public Vector3 Center { get; set; }
        public Vector3 Orientation { get; set; } // Normal for planes, Axis for cylinders
        public float PrimaryDimension { get; set; } // Width for planes, Radius for cylinders
        public float SecondaryDimension { get; set; } // Length for planes, Length for cylinders

        public int InlierCount => InlierPoints.Count;
        public List<Vector3> InlierPoints { get; } = new List<Vector3>();

        public DetectedPlane PlaneGeometry { get; set; }
        public DetectedCylinder CylinderGeometry { get; set; }

        public string DimensionSummary
        {
            get
            {
                if (Type == StructuralElementType.Floor || Type == StructuralElementType.Ceiling || Type == StructuralElementType.Wall)
                {
                    return $"Plane: {PrimaryDimension:F2}m x {SecondaryDimension:F2}m (Normal: [{Orientation.X:F2}, {Orientation.Y:F2}, {Orientation.Z:F2}])";
                }
                else if (Type == StructuralElementType.Column)
                {
                    return $"Column: Radius={PrimaryDimension:F3}m (Dia={PrimaryDimension * 2f:F2}m), Length={SecondaryDimension:F2}m";
                }
                else if (Type == StructuralElementType.Pipe)
                {
                    return $"Pipe: Radius={PrimaryDimension * 1000f:F1}mm (Dia={PrimaryDimension * 2000f:F1}mm), Length={SecondaryDimension:F2}m";
                }
                return "Unknown geometry";
            }
        }
    }
}
