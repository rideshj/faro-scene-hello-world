using System;
using System.Collections.Generic;
using System.Numerics;

namespace FAROSceneHelloWorld.Geometry
{
    /// <summary>
    /// Represents an individual 3D point with spatial position, surface normal, and classification.
    /// </summary>
    public struct Point3D
    {
        public Vector3 Position;
        public Vector3 Normal;
        public float Intensity;
        public int ClassificationId; // 0 = Unclassified, 1 = Floor, 2 = Ceiling, 3 = Wall, 4 = Column, 5 = Pipe

        public Point3D(Vector3 position, float intensity = 1.0f)
        {
            Position = position;
            Normal = Vector3.Zero;
            Intensity = intensity;
            ClassificationId = 0;
        }

        public Point3D(Vector3 position, Vector3 normal, float intensity = 1.0f)
        {
            Position = position;
            Normal = normal;
            Intensity = intensity;
            ClassificationId = 0;
        }
    }

    /// <summary>
    /// Memory-efficient 3D Point Cloud container with spatial bounding metrics.
    /// </summary>
    public class PointCloud
    {
        public List<Point3D> Points { get; }
        public Vector3 BoundingMin { get; private set; }
        public Vector3 BoundingMax { get; private set; }
        public Vector3 Centroid { get; private set; }

        public int Count => Points.Count;

        public PointCloud()
        {
            Points = new List<Point3D>();
            BoundingMin = new Vector3(float.MaxValue);
            BoundingMax = new Vector3(float.MinValue);
            Centroid = Vector3.Zero;
        }

        public PointCloud(int capacity)
        {
            Points = new List<Point3D>(capacity);
            BoundingMin = new Vector3(float.MaxValue);
            BoundingMax = new Vector3(float.MinValue);
            Centroid = Vector3.Zero;
        }

        public void Add(Point3D point)
        {
            Points.Add(point);
            UpdateBoundsWithPoint(point.Position);
        }

        public void AddRange(IEnumerable<Point3D> points)
        {
            foreach (var p in points)
            {
                Add(p);
            }
        }

        public void RecalculateBoundsAndCentroid()
        {
            if (Points.Count == 0)
            {
                BoundingMin = Vector3.Zero;
                BoundingMax = Vector3.Zero;
                Centroid = Vector3.Zero;
                return;
            }

            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);
            double sumX = 0, sumY = 0, sumZ = 0;

            for (int i = 0; i < Points.Count; i++)
            {
                var pos = Points[i].Position;
                min = Vector3.Min(min, pos);
                max = Vector3.Max(max, pos);
                sumX += pos.X;
                sumY += pos.Y;
                sumZ += pos.Z;
            }

            BoundingMin = min;
            BoundingMax = max;
            Centroid = new Vector3(
                (float)(sumX / Points.Count),
                (float)(sumY / Points.Count),
                (float)(sumZ / Points.Count));
        }

        private void UpdateBoundsWithPoint(Vector3 pos)
        {
            BoundingMin = Vector3.Min(BoundingMin, pos);
            BoundingMax = Vector3.Max(BoundingMax, pos);
        }
    }
}
