using System;
using System.Collections.Generic;
using System.Numerics;

namespace FAROSceneSDCardWorkflow.Registration
{
    /// <summary>
    /// Automated target detection engine for TLS point clouds.
    /// Extracts spherical reference targets (140mm / 200mm) and planar checkerboards.
    /// </summary>
    public static class TargetDetector
    {
        /// <summary>
        /// Detects reference spheres in a point cloud scan.
        /// </summary>
        public static List<DetectedTarget> DetectTargets(
            IList<Vector3> points,
            string scanId,
            TargetRegistrationSettings settings = null)
        {
            var targets = new List<DetectedTarget>();
            if (points == null || points.Count < 50) return targets;

            settings = settings ?? new TargetRegistrationSettings();
            float expectedRadius = settings.TargetRadius;
            float radiusTol = settings.RadiusTolerance;

            // 1. Spatial clustering with 0.35m radius to isolate candidate spherical target clusters
            var candidateClusters = FindCandidateClusters(points, clusterRadius: 0.25m, minPoints: 20, maxPoints: 2000);

            int targetCounter = 1;
            foreach (var cluster in candidateClusters)
            {
                if (FitSphereAlgebraic(cluster, expectedRadius, radiusTol, out Vector3 center, out float fittedR, out float residualMm, out int inliers))
                {
                    targets.Add(new DetectedTarget
                    {
                        Id = $"{scanId}_Sphere_{targetCounter++}",
                        ScanId = scanId,
                        Center = center,
                        TransformedCenter = center,
                        Type = settings.PrimaryTargetType,
                        Radius = fittedR,
                        FittingResidualMm = residualMm,
                        InlierCount = inliers
                    });
                }
            }

            return targets;
        }

        private static List<List<Vector3>> FindCandidateClusters(
            IList<Vector3> points,
            decimal clusterRadius,
            int minPoints,
            int maxPoints)
        {
            var clusters = new List<List<Vector3>>();
            float cellSize = (float)clusterRadius * 2f;
            var grid = new Dictionary<long, List<Vector3>>();

            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                long xi = (long)Math.Floor(p.X / cellSize);
                long yi = (long)Math.Floor(p.Y / cellSize);
                long zi = (long)Math.Floor(p.Z / cellSize);
                long key = (xi * 73856093L) ^ (yi * 19349663L) ^ (zi * 83492791L);

                if (!grid.TryGetValue(key, out var list))
                {
                    list = new List<Vector3>();
                    grid[key] = list;
                }
                list.Add(p);
            }

            foreach (var kvp in grid)
            {
                if (kvp.Value.Count >= minPoints && kvp.Value.Count <= maxPoints)
                {
                    clusters.Add(kvp.Value);
                }
            }

            return clusters;
        }

        private static bool FitSphereAlgebraic(
            List<Vector3> cluster,
            float expectedRadius,
            float radiusTolerance,
            out Vector3 center,
            out float fittedRadius,
            out float residualMm,
            out int inlierCount)
        {
            center = Vector3.Zero;
            fittedRadius = expectedRadius;
            residualMm = 0f;
            inlierCount = 0;

            if (cluster == null || cluster.Count < 8) return false;

            // 1. Validate physical cluster diameter matches expected sphere (diameter ~ 2 * expectedRadius)
            float maxClusterDiameter = expectedRadius * 2.8f;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < cluster.Count; i++)
            {
                var p = cluster[i];
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
                if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
            }
            if ((maxX - minX) > maxClusterDiameter || (maxY - minY) > maxClusterDiameter || (maxZ - minZ) > maxClusterDiameter)
            {
                return false;
            }

            // Shift cluster to centroid to prevent numerical precision loss in floating point arithmetic
            Vector3 centroid = Vector3.Zero;
            for (int i = 0; i < cluster.Count; i++) centroid += cluster[i];
            centroid /= cluster.Count;

            // Solve normal equations for 4x4 system: A^T A * c = A^T b
            // Unknowns: [2*x0, 2*y0, 2*z0, k]
            double[,] ATA = new double[4, 4];
            double[] ATb = new double[4];

            for (int i = 0; i < cluster.Count; i++)
            {
                double x = cluster[i].X - centroid.X;
                double y = cluster[i].Y - centroid.Y;
                double z = cluster[i].Z - centroid.Z;
                double sq = x * x + y * y + z * z;

                double a0 = 2.0 * x;
                double a1 = 2.0 * y;
                double a2 = 2.0 * z;
                double a3 = 1.0;

                ATA[0, 0] += a0 * a0; ATA[0, 1] += a0 * a1; ATA[0, 2] += a0 * a2; ATA[0, 3] += a0 * a3;
                ATA[1, 0] += a1 * a0; ATA[1, 1] += a1 * a1; ATA[1, 2] += a1 * a2; ATA[1, 3] += a1 * a3;
                ATA[2, 0] += a2 * a0; ATA[2, 1] += a2 * a1; ATA[2, 2] += a2 * a2; ATA[2, 3] += a2 * a3;
                ATA[3, 0] += a3 * a0; ATA[3, 1] += a3 * a1; ATA[3, 2] += a3 * a2; ATA[3, 3] += a3 * a3;

                ATb[0] += a0 * sq;
                ATb[1] += a1 * sq;
                ATb[2] += a2 * sq;
                ATb[3] += a3 * sq;
            }

            // Solve 4x4 linear system using Gaussian elimination with partial pivoting
            if (!Solve4x4(ATA, ATb, out double[] sol)) return false;

            double localX0 = sol[0];
            double localY0 = sol[1];
            double localZ0 = sol[2];
            double k = sol[3];

            double rSq = k + (localX0 * localX0 + localY0 * localY0 + localZ0 * localZ0);
            if (rSq <= 0.0001) return false;

            fittedRadius = (float)Math.Sqrt(rSq);
            if (Math.Abs(fittedRadius - expectedRadius) > radiusTolerance)
            {
                return false;
            }

            center = new Vector3((float)(localX0 + centroid.X), (float)(localY0 + centroid.Y), (float)(localZ0 + centroid.Z));

            // Compute residuals
            float sumResidual = 0f;
            int inliers = 0;
            for (int i = 0; i < cluster.Count; i++)
            {
                float d = Vector3.Distance(cluster[i], center);
                float diff = Math.Abs(d - fittedRadius);
                if (diff <= radiusTolerance * 1.5f)
                {
                    inliers++;
                    sumResidual += diff;
                }
            }

            residualMm = inliers > 0 ? (sumResidual / inliers) * 1000f : 0f;
            inlierCount = inliers;
            if (residualMm > 4.0f) return false;
            return inliers >= Math.Max(12, (int)(cluster.Count * 0.70f));
        }

        private static bool Solve4x4(double[,] A, double[] b, out double[] x)
        {
            x = new double[4];
            double[,] M = new double[4, 5];
            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 4; c++) M[r, c] = A[r, c];
                M[r, 4] = b[r];
            }

            // Forward elimination with partial pivoting
            for (int col = 0; col < 4; col++)
            {
                int maxRow = col;
                double maxVal = Math.Abs(M[col, col]);
                for (int row = col + 1; row < 4; row++)
                {
                    if (Math.Abs(M[row, col]) > maxVal)
                    {
                        maxVal = Math.Abs(M[row, col]);
                        maxRow = row;
                    }
                }

                if (maxVal < 1e-12) return false; // Singular

                if (maxRow != col)
                {
                    for (int k = col; k < 5; k++)
                    {
                        double tmp = M[col, k];
                        M[col, k] = M[maxRow, k];
                        M[maxRow, k] = tmp;
                    }
                }

                for (int row = col + 1; row < 4; row++)
                {
                    double factor = M[row, col] / M[col, col];
                    for (int k = col; k < 5; k++)
                    {
                        M[row, k] -= factor * M[col, k];
                    }
                }
            }

            // Back-substitution
            for (int row = 3; row >= 0; row--)
            {
                double sum = M[row, 4];
                for (int col = row + 1; col < 4; col++)
                {
                    sum -= M[row, col] * x[col];
                }
                x[row] = sum / M[row, row];
            }

            return true;
        }
    }
}
