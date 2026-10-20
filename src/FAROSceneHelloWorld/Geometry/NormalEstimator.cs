using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace FAROSceneHelloWorld.Geometry
{
    /// <summary>
    /// Surface normal vector estimator utilizing local neighborhood covariance and Principal Component Analysis (PCA).
    /// </summary>
    public static class NormalEstimator
    {
        /// <summary>
        /// Estimates surface normals for all points in the point cloud using k-nearest neighbors and covariance PCA.
        /// </summary>
        /// <param name="cloud">The point cloud to process.</param>
        /// <param name="kNeighbors">Number of local neighbors to evaluate (recommended 15-30).</param>
        /// <param name="viewpoint">Sensor or scanner viewpoint for consistent normal orientation (defaults to (0,0,0)).</param>
        public static void EstimateNormals(PointCloud cloud, int kNeighbors = 20, Vector3? viewpoint = null)
        {
            if (cloud == null || cloud.Count == 0) return;
            if (kNeighbors < 4) kNeighbors = 4;

            Vector3 viewOrigin = viewpoint ?? Vector3.Zero;
            var kdTree = new KdTree3D(cloud);

            int count = cloud.Count;
            var points = cloud.Points;

            // Multi-threaded computation across CPU cores
            Parallel.For(0, count, () => new List<int>(kNeighbors), (i, state, localIndices) =>
            {
                var pt = points[i];
                kdTree.FindKNearest(pt.Position, kNeighbors, localIndices);

                if (localIndices.Count < 3)
                {
                    pt.Normal = VectorMath.UnitZ;
                    points[i] = pt;
                    return localIndices;
                }

                // 1. Compute Centroid
                double cx = 0, cy = 0, cz = 0;
                int k = localIndices.Count;
                for (int n = 0; n < k; n++)
                {
                    var p = points[localIndices[n]].Position;
                    cx += p.X;
                    cy += p.Y;
                    cz += p.Z;
                }
                cx /= k; cy /= k; cz /= k;

                // 2. Compute 3x3 Covariance Matrix
                double cxx = 0, cyy = 0, czz = 0;
                double cxy = 0, cxz = 0, cyz = 0;

                for (int n = 0; n < k; n++)
                {
                    var p = points[localIndices[n]].Position;
                    double dx = p.X - cx;
                    double dy = p.Y - cy;
                    double dz = p.Z - cz;

                    cxx += dx * dx;
                    cyy += dy * dy;
                    czz += dz * dz;
                    cxy += dx * dy;
                    cxz += dx * dz;
                    cyz += dy * dz;
                }

                double invK = 1.0 / k;
                double[,] cov = new double[3, 3]
                {
                    { cxx * invK, cxy * invK, cxz * invK },
                    { cxy * invK, cyy * invK, cyz * invK },
                    { cxz * invK, cyz * invK, czz * invK }
                };

                // 3. Eigendecomposition: smallest eigenvalue corresponds to normal
                VectorMath.SymmetricEigenDecomposition3x3(cov, out double[] eigenvalues, out Vector3[] eigenvectors);

                // Smallest eigenvalue is at index 2 (descending order)
                Vector3 normal = eigenvectors[2];

                // 4. Orient normal towards sensor viewpoint
                Vector3 toView = viewOrigin - pt.Position;
                if (toView.LengthSquared() < 1e-4f)
                {
                    // If at origin, default positive Z
                    if (normal.Z < 0) normal = -normal;
                }
                else
                {
                    if (Vector3.Dot(normal, toView) < 0)
                    {
                        normal = -normal;
                    }
                }

                pt.Normal = Vector3.Normalize(normal);
                points[i] = pt;

                return localIndices;
            },
            (localIndices) => { });
        }
    }
}
