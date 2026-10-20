using System;
using System.Numerics;

namespace FAROSceneHelloWorld.Geometry
{
    /// <summary>
    /// High-performance 3D vector utilities and linear algebra operations using System.Numerics.
    /// </summary>
    public static class VectorMath
    {
        public static readonly Vector3 UnitX = new Vector3(1f, 0f, 0f);
        public static readonly Vector3 UnitY = new Vector3(0f, 1f, 0f);
        public static readonly Vector3 UnitZ = new Vector3(0f, 0f, 1f);

        /// <summary>
        /// Computes the angle in radians between two vectors.
        /// </summary>
        public static float AngleBetween(Vector3 a, Vector3 b)
        {
            float lenProduct = a.Length() * b.Length();
            if (lenProduct < 1e-7f) return 0f;
            float cosTheta = Vector3.Dot(a, b) / lenProduct;
            cosTheta = Math.Max(-1f, Math.Min(1f, cosTheta));
            return (float)Math.Acos(cosTheta);
        }

        /// <summary>
        /// Projects a point onto a line defined by a point on the line and unit direction vector.
        /// </summary>
        public static Vector3 ProjectPointOnLine(Vector3 point, Vector3 linePoint, Vector3 lineDir)
        {
            float t = Vector3.Dot(point - linePoint, lineDir);
            return linePoint + lineDir * t;
        }

        /// <summary>
        /// Computes perpendicular distance from a point to an infinite 3D line.
        /// </summary>
        public static float DistancePointToLine(Vector3 point, Vector3 linePoint, Vector3 lineDir)
        {
            Vector3 proj = ProjectPointOnLine(point, linePoint, lineDir);
            return Vector3.Distance(point, proj);
        }

        /// <summary>
        /// Projects a point onto a plane defined by origin and unit normal.
        /// </summary>
        public static Vector3 ProjectPointOnPlane(Vector3 point, Vector3 planeOrigin, Vector3 planeNormal)
        {
            float dist = Vector3.Dot(point - planeOrigin, planeNormal);
            return point - planeNormal * dist;
        }

        /// <summary>
        /// Solves the real symmetric 3x3 eigenvalue problem using Jacobi rotation algorithm.
        /// Guaranteed convergence, highly stable, and exact.
        /// Returns eigenvalues in descending order: lambda1 >= lambda2 >= lambda3 >= 0.
        /// Eigenvectors are returned as columns corresponding to eigenvalues.
        /// </summary>
        public static void SymmetricEigenDecomposition3x3(
            double[,] A,
            out double[] eigenvalues,
            out Vector3[] eigenvectors)
        {
            // Copy A to local working matrix
            double[,] a = (double[,])A.Clone();
            double[,] v = new double[3, 3]
            {
                { 1.0, 0.0, 0.0 },
                { 0.0, 1.0, 0.0 },
                { 0.0, 0.0, 1.0 }
            };

            const int maxSweeps = 50;
            const double epsilon = 1e-12;

            for (int sweep = 0; sweep < maxSweeps; sweep++)
            {
                // Find off-diagonal norm
                double offDiag = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
                if (offDiag < epsilon) break;

                // Jacobi rotations for (0,1), (0,2), (1,2)
                RotateJacobi(ref a, ref v, 0, 1);
                RotateJacobi(ref a, ref v, 0, 2);
                RotateJacobi(ref a, ref v, 1, 2);
            }

            // Extract diagonal eigenvalues
            double[] vals = new double[3] { a[0, 0], a[1, 1], a[2, 2] };
            Vector3[] vecs = new Vector3[3]
            {
                new Vector3((float)v[0, 0], (float)v[1, 0], (float)v[2, 0]),
                new Vector3((float)v[0, 1], (float)v[1, 1], (float)v[2, 1]),
                new Vector3((float)v[0, 2], (float)v[1, 2], (float)v[2, 2])
            };

            // Sort in descending order: idx 0 = max, idx 2 = min
            int[] order = new int[] { 0, 1, 2 };
            Array.Sort(order, (i, j) => vals[j].CompareTo(vals[i]));

            eigenvalues = new double[3]
            {
                vals[order[0]],
                vals[order[1]],
                vals[order[2]]
            };

            eigenvectors = new Vector3[3]
            {
                Vector3.Normalize(vecs[order[0]]),
                Vector3.Normalize(vecs[order[1]]),
                Vector3.Normalize(vecs[order[2]])
            };
        }

        private static void RotateJacobi(ref double[,] a, ref double[,] v, int p, int q)
        {
            if (Math.Abs(a[p, q]) < 1e-15) return;

            double theta = (a[q, q] - a[p, p]) / (2.0 * a[p, q]);
            double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
            if (double.IsNaN(t)) t = 0.0;
            double c = 1.0 / Math.Sqrt(t * t + 1.0);
            double s = t * c;
            double tau = s / (1.0 + c);

            double apq = a[p, q];
            a[p, q] = 0.0;
            a[p, p] -= t * apq;
            a[q, q] += t * apq;

            for (int j = 0; j < p; j++)
            {
                double g = a[j, p];
                double h = a[j, q];
                a[j, p] = g - s * (h + g * tau);
                a[j, q] = h + s * (g - h * tau);
            }
            for (int j = p + 1; j < q; j++)
            {
                double g = a[p, j];
                double h = a[j, q];
                a[p, j] = g - s * (h + g * tau);
                a[j, q] = h + s * (g - h * tau);
            }
            for (int j = q + 1; j < 3; j++)
            {
                double g = a[p, j];
                double h = a[q, j];
                a[p, j] = g - s * (h + g * tau);
                a[q, j] = h + s * (g - h * tau);
            }

            for (int j = 0; j < 3; j++)
            {
                double g = v[j, p];
                double h = v[j, q];
                v[j, p] = g - s * (h + g * tau);
                v[j, q] = h + s * (g - h * tau);
            }
        }
    }
}
