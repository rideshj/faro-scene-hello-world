using System;
using System.Collections.Generic;
using System.Numerics;

namespace FAROSceneHelloWorld.Geometry
{
    /// <summary>
    /// Geometric cylinder model defined by axis direction, axis point, radius, and longitudinal extent.
    /// </summary>
    public class DetectedCylinder
    {
        public Vector3 AxisPoint { get; set; }
        public Vector3 AxisDirection { get; set; } // Unit vector
        public Vector3 Center { get; set; }
        public float Radius { get; set; }
        public float Length { get; set; }
        public List<int> InlierIndices { get; } = new List<int>();

        /// <summary>
        /// True if cylinder is vertical (|Axis . Z| >= 0.75) and radius >= 0.15m (diameter >= 0.30m).
        /// </summary>
        public bool IsColumn => Radius >= 0.15f && Math.Abs(AxisDirection.Z) >= 0.75f;

        /// <summary>
        /// True if cylinder radius < 0.15m (diameter < 0.30m, e.g. MEP piping, conduit, ductwork).
        /// </summary>
        public bool IsPipe => Radius < 0.15f;
    }

    /// <summary>
    /// RANSAC cylindrical extraction engine utilizing spatial proximity sampling and perpendicular bisector intersection.
    /// </summary>
    public static class RansacCylinderDetector
    {
        private static long GetCellKey(Vector3 pos, float cellSize)
        {
            long xi = (long)Math.Floor(pos.X / cellSize);
            long yi = (long)Math.Floor(pos.Y / cellSize);
            long zi = (long)Math.Floor(pos.Z / cellSize);
            return (xi * 73856093L) ^ (yi * 19349663L) ^ (zi * 83492791L);
        }

        /// <summary>
        /// Extracts cylindrical geometries (Columns and MEP Pipes) from non-planar points.
        /// </summary>
        public static List<DetectedCylinder> DetectCylinders(
            PointCloud cloud,
            List<int> candidateIndices,
            float distanceThreshold = 0.035f,
            float minRadius = 0.015f,  // 15mm radius (30mm pipe)
            float maxRadius = 1.2f,    // 1.2m radius (2.4m structural column)
            int maxIterations = 800,
            int minInliers = 60,
            int maxCylinders = 10)
        {
            var cylinders = new List<DetectedCylinder>();
            if (cloud == null || candidateIndices == null || candidateIndices.Count < minInliers)
            {
                return cylinders;
            }

            var active = new List<int>(candidateIndices);
            var rng = new Random(101);
            float cosAngleThresh = (float)Math.Cos(35.0 * Math.PI / 180.0);
            const float spatialCellSize = 0.6f;

            for (int cylIdx = 0; cylIdx < maxCylinders && active.Count >= minInliers; cylIdx++)
            {
                // Build fast spatial hash grid for active points to support local proximity sampling
                var spatialGrid = new Dictionary<long, List<int>>();
                for (int i = 0; i < active.Count; i++)
                {
                    long k = GetCellKey(cloud.Points[active[i]].Position, spatialCellSize);
                    if (!spatialGrid.TryGetValue(k, out var list))
                    {
                        list = new List<int>();
                        spatialGrid[k] = list;
                    }
                    list.Add(active[i]);
                }

                int bestInliers = 0;
                Vector3 bestAxisDir = Vector3.Zero;
                Vector3 bestAxisPt = Vector3.Zero;
                float bestRadius = 0f;

                for (int iter = 0; iter < maxIterations; iter++)
                {
                    // 1. Pick point 1
                    int idx1 = active[rng.Next(active.Count)];
                    var p1 = cloud.Points[idx1];

                    // 2. Pick point 2 (80% from local spatial neighborhood, 20% random)
                    int idx2 = -1;
                    if (rng.NextDouble() < 0.80)
                    {
                        long cellKey = GetCellKey(p1.Position, spatialCellSize);
                        if (spatialGrid.TryGetValue(cellKey, out var neighbors) && neighbors.Count > 1)
                        {
                            idx2 = neighbors[rng.Next(neighbors.Count)];
                        }
                    }

                    if (idx2 < 0 || idx2 == idx1)
                    {
                        idx2 = active[rng.Next(active.Count)];
                        if (idx1 == idx2) continue;
                    }

                    var p2 = cloud.Points[idx2];

                    // Surface normals must not be parallel (need cross product for cylinder axis)
                    Vector3 axis = Vector3.Cross(p1.Normal, p2.Normal);
                    float axisLen = axis.Length();
                    if (axisLen < 0.10f) continue;

                    axis /= axisLen;

                    // 3. Project to 2D plane perpendicular to axis
                    Vector3 u = Math.Abs(axis.Z) < 0.9f
                        ? Vector3.Normalize(Vector3.Cross(axis, VectorMath.UnitZ))
                        : Vector3.Normalize(Vector3.Cross(axis, VectorMath.UnitX));
                    Vector3 v = Vector3.Normalize(Vector3.Cross(axis, u));

                    float p1u = Vector3.Dot(p1.Position, u), p1v = Vector3.Dot(p1.Position, v);
                    float p2u = Vector3.Dot(p2.Position, u), p2v = Vector3.Dot(p2.Position, v);
                    float n1u = Vector3.Dot(p1.Normal, u), n1v = Vector3.Dot(p1.Normal, v);
                    float n2u = Vector3.Dot(p2.Normal, u), n2v = Vector3.Dot(p2.Normal, v);

                    float lenN1 = (float)Math.Sqrt(n1u * n1u + n1v * n1v);
                    float lenN2 = (float)Math.Sqrt(n2u * n2u + n2v * n2v);
                    if (lenN1 < 0.2f || lenN2 < 0.2f) continue;

                    n1u /= lenN1; n1v /= lenN1;
                    n2u /= lenN2; n2v /= lenN2;

                    // Perpendicular bisector intersection:
                    // d = P2 - P1 in 2D
                    float du = p2u - p1u;
                    float dv = p2v - p1v;
                    float dSq = du * du + dv * dv;
                    if (dSq < 1e-4f) continue;

                    // Normal 1 projection onto chord d
                    float dotNorm1D = n1u * du + n1v * dv;
                    if (Math.Abs(dotNorm1D) < 0.05f) continue;

                    // Distance along normal 1 to circle center on perpendicular bisector
                    float t1 = dSq / (2.0f * dotNorm1D);
                    float r = Math.Abs(t1);

                    if (r < minRadius || r > maxRadius) continue;

                    float cu = p1u + t1 * n1u;
                    float cv = p1v + t1 * n1v;

                    // Verify normal 2 is roughly consistent with center
                    float cToP2u = p2u - cu;
                    float cToP2v = p2v - cv;
                    float lenC2 = (float)Math.Sqrt(cToP2u * cToP2u + cToP2v * cToP2v);
                    if (lenC2 < 1e-4f) continue;
                    cToP2u /= lenC2; cToP2v /= lenC2;

                    if (Math.Abs(cToP2u * n2u + cToP2v * n2v) < 0.65f) continue;

                    Vector3 axisPt = cu * u + cv * v + Vector3.Dot(p1.Position, axis) * axis;

                    // 4. Inlier count
                    int inliers = 0;
                    for (int a = 0; a < active.Count; a++)
                    {
                        var pt = cloud.Points[active[a]];
                        Vector3 w = pt.Position - axisPt;
                        float h = Vector3.Dot(w, axis);
                        Vector3 dPerp = w - h * axis;
                        float distToAxis = dPerp.Length();

                        if (Math.Abs(distToAxis - r) <= distanceThreshold)
                        {
                            if (distToAxis > 1e-4f)
                            {
                                Vector3 radialDir = dPerp / distToAxis;
                                if (Math.Abs(Vector3.Dot(pt.Normal, radialDir)) >= cosAngleThresh)
                                {
                                    inliers++;
                                }
                            }
                        }
                    }

                    if (inliers > bestInliers)
                    {
                        bestInliers = inliers;
                        bestAxisDir = axis;
                        bestAxisPt = axisPt;
                        bestRadius = r;
                    }
                }

                if (bestInliers < minInliers) break;

                // 5. Build winning cylinder
                var cylinder = new DetectedCylinder
                {
                    AxisPoint = bestAxisPt,
                    AxisDirection = bestAxisDir,
                    Radius = bestRadius
                };

                var remainingActive = new List<int>(active.Count - bestInliers);
                float minH = float.MaxValue, maxH = float.MinValue;

                for (int a = 0; a < active.Count; a++)
                {
                    int ptIdx = active[a];
                    var pt = cloud.Points[ptIdx];

                    Vector3 w = pt.Position - bestAxisPt;
                    float h = Vector3.Dot(w, bestAxisDir);
                    Vector3 dPerp = w - h * bestAxisDir;
                    float distToAxis = dPerp.Length();

                    if (Math.Abs(distToAxis - bestRadius) <= distanceThreshold && distToAxis > 1e-4f)
                    {
                        Vector3 radialDir = dPerp / distToAxis;
                        if (Math.Abs(Vector3.Dot(pt.Normal, radialDir)) >= cosAngleThresh)
                        {
                            cylinder.InlierIndices.Add(ptIdx);
                            if (h < minH) minH = h;
                            if (h > maxH) maxH = h;
                            continue;
                        }
                    }

                    remainingActive.Add(ptIdx);
                }

                cylinder.Length = Math.Max(0.1f, maxH - minH);
                cylinder.Center = bestAxisPt + bestAxisDir * ((minH + maxH) * 0.5f);

                cylinders.Add(cylinder);
                active = remainingActive;
            }

            return cylinders;
        }
    }
}
