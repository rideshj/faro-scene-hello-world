using System;
using System.Collections.Generic;
using System.Numerics;

namespace FAROSceneHelloWorld.Geometry
{
    /// <summary>
    /// Geometric plane model defined by unit normal vector and signed distance from origin.
    /// Plane equation: Normal . X + Distance = 0
    /// </summary>
    public class DetectedPlane
    {
        public Vector3 Normal { get; set; }
        public float Distance { get; set; }
        public Vector3 Center { get; set; }
        public Vector3 UAxis { get; set; }
        public Vector3 VAxis { get; set; }
        public float Width { get; set; }
        public float Length { get; set; }
        public List<int> InlierIndices { get; } = new List<int>();

        /// <summary>
        /// True if plane normal is substantially horizontal (i.e. plane is vertical, like a wall).
        /// </summary>
        public bool IsVertical => Math.Abs(Normal.Z) <= 0.25f;

        /// <summary>
        /// True if plane normal is substantially vertical (i.e. plane is horizontal, like a floor or ceiling).
        /// </summary>
        public bool IsHorizontal => Math.Abs(Normal.Z) >= 0.85f;

        /// <summary>
        /// Signed elevation Z of the plane center.
        /// </summary>
        public float Elevation => Center.Z;
    }

    /// <summary>
    /// RANSAC (Random Sample Consensus) planar segmentation engine with normal-consistency verification
    /// and PCA least-squares refinement.
    /// </summary>
    public static class RansacPlaneDetector
    {
        /// <summary>
        /// Extracts multiple dominant planar surfaces sequentially from the point cloud.
        /// </summary>
        public static List<DetectedPlane> DetectPlanes(
            PointCloud cloud,
            float distanceThreshold = 0.035f,
            float normalAngleThresholdDeg = 15.0f,
            int maxIterations = 300,
            int minInliers = 200,
            int maxPlanes = 15)
        {
            var planes = new List<DetectedPlane>();
            if (cloud == null || cloud.Count < minInliers) return planes;

            int n = cloud.Count;
            bool[] isExtracted = new bool[n];
            int remainingCount = n;

            float cosAngleThresh = (float)Math.Cos(normalAngleThresholdDeg * Math.PI / 180.0);
            var rng = new Random(42);

            for (int planeIdx = 0; planeIdx < maxPlanes && remainingCount >= minInliers; planeIdx++)
            {
                int bestInlierCount = 0;
                Vector3 bestNormal = Vector3.Zero;
                float bestDist = 0f;

                // Build active list for fast random sampling
                var activeIndices = new List<int>(remainingCount);
                for (int i = 0; i < n; i++)
                {
                    if (!isExtracted[i]) activeIndices.Add(i);
                }

                if (activeIndices.Count < minInliers) break;

                // RANSAC Iterations
                for (int iter = 0; iter < maxIterations; iter++)
                {
                    // 1. Pick 3 distinct random points
                    int idx1 = activeIndices[rng.Next(activeIndices.Count)];
                    int idx2 = activeIndices[rng.Next(activeIndices.Count)];
                    int idx3 = activeIndices[rng.Next(activeIndices.Count)];
                    if (idx1 == idx2 || idx2 == idx3 || idx1 == idx3) continue;

                    Vector3 p1 = cloud.Points[idx1].Position;
                    Vector3 p2 = cloud.Points[idx2].Position;
                    Vector3 p3 = cloud.Points[idx3].Position;

                    Vector3 v12 = p2 - p1;
                    Vector3 v13 = p3 - p1;
                    Vector3 normal = Vector3.Cross(v12, v13);
                    float normLen = normal.Length();
                    if (normLen < 1e-5f) continue; // Collinear sample

                    normal /= normLen;
                    float d = -Vector3.Dot(normal, p1);

                    // 2. Count inliers with distance and normal constraints
                    int inliers = 0;
                    for (int a = 0; a < activeIndices.Count; a++)
                    {
                        int ptIdx = activeIndices[a];
                        var pt = cloud.Points[ptIdx];

                        float dist = Math.Abs(Vector3.Dot(normal, pt.Position) + d);
                        if (dist <= distanceThreshold)
                        {
                            // Normal consistency check
                            float normalDot = Math.Abs(Vector3.Dot(normal, pt.Normal));
                            if (normalDot >= cosAngleThresh)
                            {
                                inliers++;
                            }
                        }
                    }

                    if (inliers > bestInlierCount)
                    {
                        bestInlierCount = inliers;
                        bestNormal = normal;
                        bestDist = d;

                        // Early exit if dominant plane captures > 60% of active points
                        if (inliers > activeIndices.Count * 0.6) break;
                    }
                }

                if (bestInlierCount < minInliers)
                {
                    // No further planes meeting inlier criteria
                    break;
                }

                // 3. Gather winning inliers
                var plane = new DetectedPlane
                {
                    Normal = bestNormal,
                    Distance = bestDist
                };

                for (int a = 0; a < activeIndices.Count; a++)
                {
                    int ptIdx = activeIndices[a];
                    var pt = cloud.Points[ptIdx];

                    float dist = Math.Abs(Vector3.Dot(bestNormal, pt.Position) + bestDist);
                    if (dist <= distanceThreshold && Math.Abs(Vector3.Dot(bestNormal, pt.Normal)) >= cosAngleThresh)
                    {
                        plane.InlierIndices.Add(ptIdx);
                        isExtracted[ptIdx] = true;
                    }
                }

                // 4. Least-squares plane refinement via PCA on inliers
                RefinePlaneModel(cloud, plane);

                planes.Add(plane);
                remainingCount -= plane.InlierIndices.Count;
            }

            return planes;
        }

        private static void RefinePlaneModel(PointCloud cloud, DetectedPlane plane)
        {
            int count = plane.InlierIndices.Count;
            if (count < 3) return;

            // Compute inlier centroid
            double cx = 0, cy = 0, cz = 0;
            for (int i = 0; i < count; i++)
            {
                var p = cloud.Points[plane.InlierIndices[i]].Position;
                cx += p.X; cy += p.Y; cz += p.Z;
            }
            cx /= count; cy /= count; cz /= count;
            Vector3 center = new Vector3((float)cx, (float)cy, (float)cz);

            // Compute covariance matrix
            double cxx = 0, cyy = 0, czz = 0, cxy = 0, cxz = 0, cyz = 0;
            for (int i = 0; i < count; i++)
            {
                var p = cloud.Points[plane.InlierIndices[i]].Position;
                double dx = p.X - cx;
                double dy = p.Y - cy;
                double dz = p.Z - cz;

                cxx += dx * dx; cyy += dy * dy; czz += dz * dz;
                cxy += dx * dy; cxz += dx * dz; cyz += dy * dz;
            }

            double inv = 1.0 / count;
            double[,] cov = new double[3, 3]
            {
                { cxx * inv, cxy * inv, cxz * inv },
                { cxy * inv, cyy * inv, cyz * inv },
                { cxz * inv, cyz * inv, czz * inv }
            };

            VectorMath.SymmetricEigenDecomposition3x3(cov, out double[] eigenvalues, out Vector3[] eigenvectors);

            // Eigenvector 2 is refined normal (minimum variance direction)
            Vector3 refinedNormal = eigenvectors[2];
            // Ensure orientation doesn't flip 180 degrees from RANSAC normal
            if (Vector3.Dot(refinedNormal, plane.Normal) < 0) refinedNormal = -refinedNormal;

            plane.Normal = refinedNormal;
            plane.Distance = -Vector3.Dot(refinedNormal, center);
            plane.Center = center;

            // In-plane coordinate frame: eigenvectors 0 and 1 are orthogonal in-plane principal axes
            plane.UAxis = eigenvectors[0];
            plane.VAxis = eigenvectors[1];

            // Compute in-plane 2D bounding extents
            float minU = float.MaxValue, maxU = float.MinValue;
            float minV = float.MaxValue, maxV = float.MinValue;

            for (int i = 0; i < count; i++)
            {
                var p = cloud.Points[plane.InlierIndices[i]].Position;
                Vector3 rel = p - center;
                float u = Vector3.Dot(rel, plane.UAxis);
                float v = Vector3.Dot(rel, plane.VAxis);

                if (u < minU) minU = u;
                if (u > maxU) maxU = u;
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }

            plane.Width = maxU - minU;
            plane.Length = maxV - minV;
        }
    }
}
