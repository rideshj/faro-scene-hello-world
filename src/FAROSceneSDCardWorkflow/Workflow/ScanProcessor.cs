using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using FAROSceneSDCardWorkflow.Registration;

namespace FAROSceneSDCardWorkflow.Workflow
{
    /// <summary>
    /// Processes imported raw scans: applies point filters, noise reduction, and loads stations into memory.
    /// </summary>
    public static class ScanProcessor
    {
        public static List<ScanStation> ProcessScans(
            IList<string> scanFilePaths,
            bool applyStrayFilter = true,
            bool applyColorization = true,
            Action<string, int> progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            var stations = new List<ScanStation>();
            if (scanFilePaths == null) return stations;

            for (int i = 0; i < scanFilePaths.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = scanFilePaths[i];
                string name = Path.GetFileNameWithoutExtension(path);

                int basePct = (i * 100) / scanFilePaths.Count;
                progressCallback?.Invoke($"Processing {name} (Filtering & Gridding)...", basePct);

                var station = new ScanStation
                {
                    ScanId = $"Scan_{i + 1:D3}",
                    Name = name,
                    FilePath = path
                };

                // Read points from file
                station.Points = ReadPointsFromFile(path, maxPointsToLoad: 150000);

                if (applyStrayFilter && station.Points.Count > 0)
                {
                    station.Points = ApplyStrayPointFilter(station.Points, minRangeM: 0.5f, maxRangeM: 65.0f);
                }

                stations.Add(station);
            }

            progressCallback?.Invoke($"Processed {stations.Count} scan(s) successfully.", 100);
            return stations;
        }

        private static List<Vector3> ReadPointsFromFile(string filePath, int maxPointsToLoad = 150000)
        {
            var points = new List<Vector3>();
            if (!File.Exists(filePath)) return points;

            char[] sep = new char[] { ' ', '\t', ',', ';' };

            using (var reader = new StreamReader(filePath))
            {
                string line = reader.ReadLine();
                // Check for PTS point count header
                int expected = 0;
                if (line != null && int.TryParse(line.Trim(), out expected))
                {
                    // PTS header line
                }
                else if (line != null)
                {
                    ParseLine(line, sep, points);
                }

                while ((line = reader.ReadLine()) != null && points.Count < maxPointsToLoad)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    ParseLine(line, sep, points);
                }
            }

            return points;
        }

        private static void ParseLine(string line, char[] sep, List<Vector3> points)
        {
            var p = line.Split(sep, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 3)
            {
                if (float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                    float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                {
                    points.Add(new Vector3(x, y, z));
                }
            }
        }

        private static List<Vector3> ApplyStrayPointFilter(List<Vector3> rawPoints, float minRangeM, float maxRangeM)
        {
            float minSq = minRangeM * minRangeM;
            float maxSq = maxRangeM * maxRangeM;
            var filtered = new List<Vector3>(rawPoints.Count);

            for (int i = 0; i < rawPoints.Count; i++)
            {
                float dSq = rawPoints[i].LengthSquared();
                if (dSq >= minSq && dSq <= maxSq)
                {
                    filtered.Add(rawPoints[i]);
                }
            }

            return filtered;
        }
    }
}
