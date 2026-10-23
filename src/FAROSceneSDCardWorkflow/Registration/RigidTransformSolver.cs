using System;
using System.Collections.Generic;
using System.Numerics;

namespace FAROSceneSDCardWorkflow.Registration
{
    /// <summary>
    /// Pure C# closed-form Kabsch / Horn rigid transformation solver.
    /// Finds optimal 6-DOF Rotation matrix R and Translation vector t
    /// minimizing sum of squared Euclidean target alignment errors: min || R*p_i + t - q_i ||^2.
    /// </summary>
    public static class RigidTransformSolver
    {
        /// <summary>
        /// Solves for the optimal 4x4 rigid transformation matrix from source points (p) to target points (q).
        /// Returns Matrix4x4, mean residual error in meters, and individual residuals.
        /// </summary>
        public static bool Solve(
            IList<Vector3> sourcePoints,
            IList<Vector3> targetPoints,
            out Matrix4x4 transform,
            out float meanResidualMm,
            out float maxResidualMm,
            out float[] residualsMm)
        {
            transform = Matrix4x4.Identity;
            meanResidualMm = 0f;
            maxResidualMm = 0f;
            residualsMm = null;

            if (sourcePoints == null || targetPoints == null ||
                sourcePoints.Count < 3 || targetPoints.Count < 3 ||
                sourcePoints.Count != targetPoints.Count)
            {
                return false;
            }

            int n = sourcePoints.Count;

            // 1. Calculate Centroids
            Vector3 pCentroid = Vector3.Zero;
            Vector3 qCentroid = Vector3.Zero;

            for (int i = 0; i < n; i++)
            {
                pCentroid += sourcePoints[i];
                qCentroid += targetPoints[i];
            }

            pCentroid /= n;
            qCentroid /= n;

            // 2. Compute 3x3 Cross-Covariance Matrix H = Sum((p_i - pC) * (q_i - qC)^T)
            double[,] H = new double[3, 3];

            for (int i = 0; i < n; i++)
            {
                Vector3 p = sourcePoints[i] - pCentroid;
                Vector3 q = targetPoints[i] - qCentroid;

                H[0, 0] += p.X * q.X; H[0, 1] += p.X * q.Y; H[0, 2] += p.X * q.Z;
                H[1, 0] += p.Y * q.X; H[1, 1] += p.Y * q.Y; H[1, 2] += p.Y * q.Z;
                H[2, 0] += p.Z * q.X; H[2, 1] += p.Z * q.Y; H[2, 2] += p.Z * q.Z;
            }

            // 3. Singular Value Decomposition of H via H^T * H
            // H^T * H is 3x3 symmetric positive semi-definite
            double[,] HtH = new double[3, 3];
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    double sum = 0;
                    for (int k = 0; k < 3; k++) sum += H[k, r] * H[k, c];
                    HtH[r, c] = sum;
                }
            }

            JacobiSymmetric3x3(HtH, out double[] eigenValues, out double[,] V);

            // Singular values of H are sqrt(eigenValues)
            double[] sigma = new double[3];
            for (int i = 0; i < 3; i++)
            {
                sigma[i] = eigenValues[i] > 1e-12 ? Math.Sqrt(Math.Max(0.0, eigenValues[i])) : 0.0;
            }

            // Compute U columns: u_i = H * v_i / sigma_i
            double[,] U = new double[3, 3];
            for (int i = 0; i < 3; i++)
            {
                if (sigma[i] > 1e-8)
                {
                    double invSig = 1.0 / sigma[i];
                    U[0, i] = (H[0, 0] * V[0, i] + H[0, 1] * V[1, i] + H[0, 2] * V[2, i]) * invSig;
                    U[1, i] = (H[1, 0] * V[0, i] + H[1, 1] * V[1, i] + H[1, 2] * V[2, i]) * invSig;
                    U[2, i] = (H[2, 0] * V[0, i] + H[2, 1] * V[1, i] + H[2, 2] * V[2, i]) * invSig;
                }
                else
                {
                    // Fallback to cross product of other columns
                    int prev = (i + 1) % 3;
                    int next = (i + 2) % 3;
                    U[0, i] = U[1, prev] * U[2, next] - U[2, prev] * U[1, next];
                    U[1, i] = U[2, prev] * U[0, next] - U[0, prev] * U[2, next];
                    U[2, i] = U[0, prev] * U[1, next] - U[1, prev] * U[0, next];
                }
            }

            // Optimal Rotation R = U * S * V^T where S = diag(1, 1, det(U * V^T))
            // Compute det(U * V^T)
            double[,] UVt = Multiply3x3(U, Transpose3x3(V));
            double det = Determinant3x3(UVt);
            double sign = det >= 0 ? 1.0 : -1.0;

            double[,] S = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, sign } };
            double[,] R = Multiply3x3(Multiply3x3(U, S), Transpose3x3(V));

            // Transpose if mapping from p to q: R_opt = V * S * U^T
            double[,] Ropt = Transpose3x3(R); // maps p to q

            // 4. Compute Translation vector t = qCentroid - Ropt * pCentroid
            Vector3 rotP = MultiplyMatrixVector(Ropt, pCentroid);
            Vector3 t = qCentroid - rotP;

            // 5. Construct 4x4 Transformation Matrix
            transform = new Matrix4x4(
                (float)Ropt[0, 0], (float)Ropt[1, 0], (float)Ropt[2, 0], 0f,
                (float)Ropt[0, 1], (float)Ropt[1, 1], (float)Ropt[2, 1], 0f,
                (float)Ropt[0, 2], (float)Ropt[1, 2], (float)Ropt[2, 2], 0f,
                t.X, t.Y, t.Z, 1f);

            // 6. Compute Residuals (in mm)
            residualsMm = new float[n];
            float totalError = 0f;
            float maxError = 0f;

            for (int i = 0; i < n; i++)
            {
                Vector3 pTrans = Vector3.Transform(sourcePoints[i], transform);
                float errM = Vector3.Distance(pTrans, targetPoints[i]);
                float errMm = errM * 1000f;
                residualsMm[i] = errMm;
                totalError += errMm;
                if (errMm > maxError) maxError = errMm;
            }

            meanResidualMm = totalError / n;
            maxResidualMm = maxError;

            return true;
        }

        private static Vector3 MultiplyMatrixVector(double[,] m, Vector3 v)
        {
            return new Vector3(
                (float)(m[0, 0] * v.X + m[0, 1] * v.Y + m[0, 2] * v.Z),
                (float)(m[1, 0] * v.X + m[1, 1] * v.Y + m[1, 2] * v.Z),
                (float)(m[2, 0] * v.X + m[2, 1] * v.Y + m[2, 2] * v.Z));
        }

        private static double[,] Multiply3x3(double[,] a, double[,] b)
        {
            var res = new double[3, 3];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    res[r, c] = a[r, 0] * b[0, c] + a[r, 1] * b[1, c] + a[r, 2] * b[2, c];
            return res;
        }

        private static double[,] Transpose3x3(double[,] a)
        {
            var res = new double[3, 3];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    res[r, c] = a[c, r];
            return res;
        }

        private static double Determinant3x3(double[,] m)
        {
            return m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
                 - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
                 + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
        }

        private static void JacobiSymmetric3x3(double[,] a, out double[] eigenvalues, out double[,] v)
        {
            double[,] A = (double[,])a.Clone();
            v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            eigenvalues = new double[3];

            for (int iter = 0; iter < 50; iter++)
            {
                double maxOff = 0;
                int p = 0, q = 1;
                for (int i = 0; i < 3; i++)
                {
                    for (int j = i + 1; j < 3; j++)
                    {
                        double val = Math.Abs(A[i, j]);
                        if (val > maxOff) { maxOff = val; p = i; q = j; }
                    }
                }

                if (maxOff < 1e-15) break;

                double theta = (A[q, q] - A[p, p]) / (2.0 * A[p, q]);
                double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                double c = 1.0 / Math.Sqrt(t * t + 1.0);
                double s = c * t;

                double App = A[p, p], Aqq = A[q, q], Apq = A[p, q];
                A[p, p] = c * c * App - 2.0 * s * c * Apq + s * s * Aqq;
                A[q, q] = s * s * App + 2.0 * s * c * Apq + c * c * Aqq;
                A[p, q] = 0.0;
                A[q, p] = 0.0;

                int k = 3 - p - q;
                double Apk = A[p, k], Aqk = A[q, k];
                A[p, k] = c * Apk - s * Aqk;
                A[k, p] = A[p, k];
                A[q, k] = s * Apk + c * Aqk;
                A[k, q] = A[q, k];

                for (int i = 0; i < 3; i++)
                {
                    double vip = v[i, p], viq = v[i, q];
                    v[i, p] = c * vip - s * viq;
                    v[i, q] = s * vip + c * viq;
                }
            }

            eigenvalues[0] = A[0, 0];
            eigenvalues[1] = A[1, 1];
            eigenvalues[2] = A[2, 2];

            // Sort eigenvalues descending
            for (int i = 0; i < 2; i++)
            {
                for (int j = i + 1; j < 3; j++)
                {
                    if (eigenvalues[j] > eigenvalues[i])
                    {
                        double te = eigenvalues[i]; eigenvalues[i] = eigenvalues[j]; eigenvalues[j] = te;
                        for (int r = 0; r < 3; r++)
                        {
                            double tv = v[r, i]; v[r, i] = v[r, j]; v[r, j] = tv;
                        }
                    }
                }
            }
        }
    }
}
