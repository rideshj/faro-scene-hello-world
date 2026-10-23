using System;
using System.Numerics;

namespace FAROSceneSDCardWorkflow.Registration
{
    public enum TargetType
    {
        Sphere140mm,       // Standard 140mm reference sphere (r = 0.070m)
        Sphere200mm,       // 200mm large survey sphere (r = 0.100m)
        CheckerboardA4,    // Planar A4 black & white contrast target
        CheckerboardA3     // Planar A3 black & white contrast target
    }

    /// <summary>
    /// Configuration and tolerances for automated target-based registration.
    /// </summary>
    public class TargetRegistrationSettings
    {
        public TargetType PrimaryTargetType { get; set; } = TargetType.Sphere140mm;
        public TargetType TargetType { get => PrimaryTargetType; set => PrimaryTargetType = value; }
        public float TargetRadius => PrimaryTargetType == TargetType.Sphere140mm ? 0.070f : 0.100f;
        public float RadiusTolerance { get; set; } = 0.006f; // ±6mm radius fitting tolerance
        public float MaxDistanceTolerance { get; set; } = 0.008f; // 8mm inter-target distance matching threshold
        public float MaxAllowedMeanErrorMm { get; set; } = 4.0f; // 4.0mm maximum acceptable mean error
        public int MinRequiredTargets { get; set; } = 3; // Minimum 3 non-collinear targets for 6-DOF lock
    }
}
