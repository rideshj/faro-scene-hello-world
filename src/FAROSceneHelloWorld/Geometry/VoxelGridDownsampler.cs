using System;
using System.Collections.Generic;
using System.Numerics;

namespace FAROSceneHelloWorld.Geometry
{
    /// <summary>
    /// High-performance spatial voxel grid filter for downsampling massive TLS point clouds.
    /// Reduces millions of points into a uniform spatial distribution in O(N) time.
    /// </summary>
    public static class VoxelGridDownsampler
    {
        private struct VoxelCoord : IEquatable<VoxelCoord>
        {
            public readonly int X;
            public readonly int Y;
            public readonly int Z;

            public VoxelCoord(int x, int y, int z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public bool Equals(VoxelCoord other)
            {
                return X == other.X && Y == other.Y && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is VoxelCoord other && Equals(other);
            }

            public override int GetHashCode()
            {
                // Large prime multiplication for high dispersion spatial hashing
                unchecked
                {
                    int hash = 17;
                    hash = hash * 73856093 ^ X;
                    hash = hash * 19349663 ^ Y;
                    hash = hash * 83492791 ^ Z;
                    return hash;
                }
            }
        }

        private class VoxelAccumulator
        {
            public double SumX;
            public double SumY;
            public double SumZ;
            public double SumIntensity;
            public int Count;

            public void Add(Vector3 point, float intensity)
            {
                SumX += point.X;
                SumY += point.Y;
                SumZ += point.Z;
                SumIntensity += intensity;
                Count++;
            }

            public Point3D ToCentroidPoint()
            {
                float inv = 1.0f / Count;
                return new Point3D(
                    new Vector3((float)(SumX * inv), (float)(SumY * inv), (float)(SumZ * inv)),
                    (float)(SumIntensity * inv));
            }
        }

        /// <summary>
        /// Downsamples the input point cloud into a regular 3D voxel grid.
        /// </summary>
        /// <param name="input">Source point cloud (millions of raw points).</param>
        /// <param name="voxelSize">Voxel cube edge size in meters (e.g. 0.04m to 0.08m).</param>
        /// <returns>Uniformly distributed, centroid-downsampled point cloud.</returns>
        public static PointCloud Downsample(PointCloud input, float voxelSize)
        {
            if (input == null || input.Count == 0)
            {
                return new PointCloud();
            }

            if (voxelSize <= 0f)
            {
                throw new ArgumentException("Voxel size must be greater than zero.", nameof(voxelSize));
            }

            float invVoxel = 1.0f / voxelSize;
            var origin = input.BoundingMin;

            var grid = new Dictionary<VoxelCoord, VoxelAccumulator>(Math.Min(input.Count / 4, 500000));

            for (int i = 0; i < input.Count; i++)
            {
                var pt = input.Points[i];
                var rel = pt.Position - origin;

                int vx = (int)Math.Floor(rel.X * invVoxel);
                int vy = (int)Math.Floor(rel.Y * invVoxel);
                int vz = (int)Math.Floor(rel.Z * invVoxel);

                var key = new VoxelCoord(vx, vy, vz);

                if (!grid.TryGetValue(key, out var acc))
                {
                    acc = new VoxelAccumulator();
                    grid[key] = acc;
                }

                acc.Add(pt.Position, pt.Intensity);
            }

            var output = new PointCloud(grid.Count);
            foreach (var kvp in grid)
            {
                output.Add(kvp.Value.ToCentroidPoint());
            }

            output.RecalculateBoundsAndCentroid();
            return output;
        }
    }
}
