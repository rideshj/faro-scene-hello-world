using System;
using System.Numerics;

namespace FAROSceneSDCardWorkflow.Registration
{
    /// <summary>
    /// Represents an individual physical reference target detected in a TLS scan.
    /// </summary>
    public class DetectedTarget
    {
        public string Id { get; set; }
        public string GlobalLabel { get; set; }
        public string TargetId { get => GlobalLabel ?? Id ?? "Target"; set => Id = value; }
        public string ScanId { get; set; }
        public Vector3 Center { get; set; } // Local coordinate frame
        public Vector3 TransformedCenter { get; set; } // Registered global coordinate frame
        public TargetType Type { get; set; } = TargetType.Sphere140mm;
        public float Radius { get; set; }
        public float FittingResidualMm { get; set; }
        public int InlierCount { get; set; }
        public float TensionResidualMm { get; set; }

        public override string ToString()
        {
            return $"[{ScanId}] {GlobalLabel ?? Id}: ({Center.X:F3}, {Center.Y:F3}, {Center.Z:F3}) (Residual: {TensionResidualMm:F1}mm)";
        }
    }

    /// <summary>
    /// Represents a matched target correspondence link between two overlapping scans.
    /// </summary>
    public class TargetCorrespondence
    {
        public string TargetLabel { get; set; }
        public DetectedTarget TargetInScanA { get; set; }
        public DetectedTarget TargetInScanB { get; set; }
        public float DistanceErrorMm { get; set; }
    }
}
