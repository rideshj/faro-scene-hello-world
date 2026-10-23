using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace FAROSceneSDCardWorkflow.Registration
{
    /// <summary>
    /// Represents an imported scan station with raw points, detected targets, and 4x4 transformation.
    /// </summary>
    public class ScanStation
    {
        public string ScanId { get; set; }
        public string Name { get; set; }
        public string FilePath { get; set; }
        public List<Vector3> Points { get; set; } = new List<Vector3>();
        public List<DetectedTarget> Targets { get; set; } = new List<DetectedTarget>();
        public Matrix4x4 Transformation { get; set; } = Matrix4x4.Identity;
        public Vector3 ScannerPosition => new Vector3(Transformation.M41, Transformation.M42, Transformation.M43);
        public System.Drawing.Color Color { get; set; }
    }

    /// <summary>
    /// Overall registration results and metrics.
    /// </summary>
    public class RegistrationResult
    {
        public bool Success { get; set; }
        public int TotalScans { get; set; }
        public int RegisteredScans { get; set; }
        public int TotalTargetMatches { get; set; }
        public float MeanTargetErrorMm { get; set; }
        public float MaxTargetErrorMm { get; set; }
        public List<TargetCorrespondence> Correspondences { get; } = new List<TargetCorrespondence>();
        public List<ScanStation> Stations { get; } = new List<ScanStation>();
        public TimeSpan Elapsed { get; set; }
        public string QualityRating { get; set; } // "Excellent", "Good", "Review Needed"

        public override string ToString()
        {
            return $"{RegisteredScans}/{TotalScans} Scans Registered | Mean Error: {MeanTargetErrorMm:F1}mm (Max: {MaxTargetErrorMm:F1}mm) - {QualityRating}";
        }
    }

    /// <summary>
    /// Multi-scan target-based registration engine for FARO SCENE.
    /// </summary>
    public class TargetRegistrationEngine
    {
        public event Action<string, int> ProgressChanged;

        private static readonly System.Drawing.Color[] ScanColors = new System.Drawing.Color[]
        {
            System.Drawing.Color.FromArgb(0, 120, 215),   // Cyan / Blue
            System.Drawing.Color.FromArgb(232, 17, 35),   // Red
            System.Drawing.Color.FromArgb(16, 124, 65),   // Green
            System.Drawing.Color.FromArgb(255, 140, 0),   // Orange
            System.Drawing.Color.FromArgb(136, 23, 152),  // Purple
            System.Drawing.Color.FromArgb(0, 153, 188),   // Teal
            System.Drawing.Color.FromArgb(216, 59, 1)     // Dark Orange
        };

        /// <summary>
        /// Executes automated target-based registration on the provided scan stations.
        /// </summary>
        public RegistrationResult Execute(
            List<ScanStation> stations,
            TargetRegistrationSettings settings = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            settings = settings ?? new TargetRegistrationSettings();

            var result = new RegistrationResult
            {
                TotalScans = stations.Count
            };

            if (stations == null || stations.Count == 0)
            {
                result.Success = false;
                result.QualityRating = "Failed (No scans)";
                return result;
            }

            // Assign distinct color to each scan for top-view visualization
            for (int i = 0; i < stations.Count; i++)
            {
                stations[i].Color = ScanColors[i % ScanColors.Length];
                result.Stations.Add(stations[i]);
            }

            // 1. Detect targets in each scan if not already detected
            for (int i = 0; i < stations.Count; i++)
            {
                int pct = 10 + (int)((i / (float)stations.Count) * 25f);
                ReportProgress($"Detecting reference targets in {stations[i].Name}...", pct);

                if (stations[i].Targets == null || stations[i].Targets.Count == 0)
                {
                    stations[i].Targets = TargetDetector.DetectTargets(stations[i].Points, stations[i].ScanId, settings);
                }
            }

            // If only 1 scan, registration is trivial (origin)
            if (stations.Count == 1)
            {
                stations[0].Transformation = Matrix4x4.Identity;
                result.RegisteredScans = 1;
                result.Success = true;
                result.QualityRating = "Reference Scan (Origin)";
                sw.Stop();
                result.Elapsed = sw.Elapsed;
                return result;
            }

            // 2. Set Scan 0 as reference frame (Identity)
            stations[0].Transformation = Matrix4x4.Identity;
            foreach (var t in stations[0].Targets)
            {
                t.TransformedCenter = t.Center;
            }

            var registeredSet = new HashSet<int> { 0 };
            var allCorrespondences = new List<TargetCorrespondence>();
            float totalErrorMm = 0f;
            float maxErrorMm = 0f;
            int totalPairs = 0;

            // 3. Incrementally align remaining scans to the registered network
            bool progressMade = true;
            while (registeredSet.Count < stations.Count && progressMade)
            {
                progressMade = false;

                for (int candidateIdx = 0; candidateIdx < stations.Count; candidateIdx++)
                {
                    if (registeredSet.Contains(candidateIdx)) continue;

                    var candidate = stations[candidateIdx];
                    int bestRefIdx = -1;
                    List<TargetCorrespondence> bestMatches = null;

                    // Test candidate against all already-registered scans
                    foreach (int refIdx in registeredSet)
                    {
                        var refStation = stations[refIdx];
                        var matches = TargetMatcher.MatchTargets(refStation.Targets, candidate.Targets, settings.MaxDistanceTolerance);

                        if (matches.Count >= settings.MinRequiredTargets)
                        {
                            if (bestMatches == null || matches.Count > bestMatches.Count)
                            {
                                bestMatches = matches;
                                bestRefIdx = refIdx;
                            }
                        }
                    }

                    if (bestMatches != null && bestMatches.Count >= settings.MinRequiredTargets)
                    {
                        var refStation = stations[bestRefIdx];

                        // Build paired coordinate lists in registered world frame
                        var srcPoints = new List<Vector3>();
                        var tgtPoints = new List<Vector3>();

                        foreach (var m in bestMatches)
                        {
                            srcPoints.Add(m.TargetInScanB.Center); // candidate local
                            tgtPoints.Add(m.TargetInScanA.TransformedCenter); // reference registered
                        }

                        if (RigidTransformSolver.Solve(srcPoints, tgtPoints, out Matrix4x4 transform,
                            out float meanResMm, out float maxResMm, out float[] residuals))
                        {
                            candidate.Transformation = transform;

                            // Update candidate target transformed centers and residuals
                            for (int mIdx = 0; mIdx < bestMatches.Count; mIdx++)
                            {
                                var match = bestMatches[mIdx];
                                match.TargetInScanB.TransformedCenter = Vector3.Transform(match.TargetInScanB.Center, transform);
                                match.DistanceErrorMm = residuals[mIdx];
                                match.TargetInScanB.TensionResidualMm = residuals[mIdx];
                                match.TargetInScanA.TensionResidualMm = Math.Max(match.TargetInScanA.TensionResidualMm, residuals[mIdx]);

                                totalErrorMm += residuals[mIdx];
                                if (residuals[mIdx] > maxErrorMm) maxErrorMm = residuals[mIdx];
                                totalPairs++;

                                allCorrespondences.Add(match);
                            }

                            registeredSet.Add(candidateIdx);
                            progressMade = true;
                            ReportProgress($"Registered {candidate.Name} to {refStation.Name} (Residual: {meanResMm:F1}mm)...", 40 + (registeredSet.Count * 45 / stations.Count));
                            break;
                        }
                    }
                }
            }

            result.RegisteredScans = registeredSet.Count;
            result.TotalTargetMatches = totalPairs;
            result.MeanTargetErrorMm = totalPairs > 0 ? (totalErrorMm / totalPairs) : 0f;
            result.MaxTargetErrorMm = maxErrorMm;
            result.Correspondences.AddRange(allCorrespondences);

            if (result.RegisteredScans == stations.Count)
            {
                result.Success = true;
                if (result.MeanTargetErrorMm <= 2.0f) result.QualityRating = "Excellent (< 2mm)";
                else if (result.MeanTargetErrorMm <= settings.MaxAllowedMeanErrorMm) result.QualityRating = "Good (< 4mm)";
                else result.QualityRating = "Review Needed (> 4mm)";
            }
            else
            {
                result.Success = result.RegisteredScans > 1;
                result.QualityRating = $"Partial ({result.RegisteredScans}/{stations.Count} scans registered)";
            }

            sw.Stop();
            result.Elapsed = sw.Elapsed;
            ReportProgress($"Target-based registration complete: {result}", 100);

            return result;
        }

        private void ReportProgress(string msg, int pct)
        {
            ProgressChanged?.Invoke(msg, pct);
        }
    }
}
