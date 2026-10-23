using System;
using System.Collections.Generic;
using System.Numerics;

namespace FAROSceneSDCardWorkflow.Registration
{
    /// <summary>
    /// Invariant distance-matrix target correspondence matcher.
    /// Matches reference targets across overlapping scans without requiring manual user labeling.
    /// </summary>
    public static class TargetMatcher
    {
        /// <summary>
        /// Matches corresponding targets between two scans based on pairwise Euclidean distance invariance.
        /// </summary>
        public static List<TargetCorrespondence> MatchTargets(
            IList<DetectedTarget> targetsA,
            IList<DetectedTarget> targetsB,
            float distanceToleranceM = 0.008f) // 8mm distance tolerance
        {
            var matches = new List<TargetCorrespondence>();
            if (targetsA == null || targetsB == null || targetsA.Count < 3 || targetsB.Count < 3)
            {
                return matches;
            }

            int nA = targetsA.Count;
            int nB = targetsB.Count;

            // 1. Compute Pairwise Distance Matrices
            float[,] distA = new float[nA, nA];
            for (int i = 0; i < nA; i++)
            {
                for (int j = 0; j < nA; j++)
                {
                    distA[i, j] = Vector3.Distance(targetsA[i].Center, targetsA[j].Center);
                }
            }

            float[,] distB = new float[nB, nB];
            for (int i = 0; i < nB; i++)
            {
                for (int j = 0; j < nB; j++)
                {
                    distB[i, j] = Vector3.Distance(targetsB[i].Center, targetsB[j].Center);
                }
            }

            // 2. Score potential pairings (i in A, u in B)
            // A pair (i, u) is valid if the distances from i to all other targets j match distances from u to some v
            int[,] matchScores = new int[nA, nB];

            for (int i = 0; i < nA; i++)
            {
                for (int u = 0; u < nB; u++)
                {
                    int score = 0;
                    for (int j = 0; j < nA; j++)
                    {
                        if (i == j) continue;
                        float dAi = distA[i, j];

                        for (int v = 0; v < nB; v++)
                        {
                            if (u == v) continue;
                            float dBu = distB[u, v];

                            if (Math.Abs(dAi - dBu) <= distanceToleranceM)
                            {
                                score++;
                                break;
                            }
                        }
                    }

                    matchScores[i, u] = score;
                }
            }

            // 3. Find unique mutually highest correspondences (greedy assignment)
            var assignedA = new HashSet<int>();
            var assignedB = new HashSet<int>();

            // Sorted candidates by score descending
            var candidates = new List<Tuple<int, int, int>>(); // i, u, score
            for (int i = 0; i < nA; i++)
            {
                for (int u = 0; u < nB; u++)
                {
                    if (matchScores[i, u] >= 2) // Must match at least 2 other targets (triangle)
                    {
                        candidates.Add(Tuple.Create(i, u, matchScores[i, u]));
                    }
                }
            }

            candidates.Sort((x, y) => y.Item3.CompareTo(x.Item3));

            int labelIndex = 1;
            foreach (var cand in candidates)
            {
                int i = cand.Item1;
                int u = cand.Item2;

                if (!assignedA.Contains(i) && !assignedB.Contains(u))
                {
                    assignedA.Add(i);
                    assignedB.Add(u);

                    string globalLabel = targetsA[i].GlobalLabel ?? targetsB[u].GlobalLabel ?? $"Target_{labelIndex++}";
                    targetsA[i].GlobalLabel = globalLabel;
                    targetsB[u].GlobalLabel = globalLabel;

                    matches.Add(new TargetCorrespondence
                    {
                        TargetLabel = globalLabel,
                        TargetInScanA = targetsA[i],
                        TargetInScanB = targetsB[u]
                    });
                }
            }

            return matches;
        }
    }
}
