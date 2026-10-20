using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using FARO.LS.SDK.AppLogic;
using FARO.LS.SDK.DataLogic;
using FAROSceneHelloWorld.Geometry;

namespace FAROSceneHelloWorld.Classification
{
    /// <summary>
    /// Loads point cloud data from an external file (.pts, .xyz, .txt), active SCENE workspace,
    /// or synthesizes a benchmark scan for verification.
    /// </summary>
    public static class ScanDataLoader
    {
        /// <summary>
        /// Reads point cloud data from an external point cloud file (.pts, .xyz, .txt, .csv, .asc).
        /// </summary>
        public static PointCloud LoadFromFile(string filePath, Action<string, int> progressCallback = null)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException("Scan file not found.", filePath);

            var cloud = new PointCloud(100000);
            char[] splitChars = new char[] { ' ', '\t', ',', ';' };

            using (var reader = new StreamReader(filePath, System.Text.Encoding.ASCII, true, 65536))
            {
                string line = reader.ReadLine();
                // Check if line 1 is point count (standard PTS format)
                if (line != null && int.TryParse(line.Trim(), out int expectedCount) && expectedCount > 0)
                {
                    // PTS header line
                }
                else if (line != null)
                {
                    ParseLineAndAdd(line, splitChars, cloud);
                }

                int lineCounter = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    lineCounter++;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    ParseLineAndAdd(line, splitChars, cloud);

                    if (lineCounter % 50000 == 0 && progressCallback != null)
                    {
                        progressCallback($"Loaded {cloud.Count:N0} points from file...", 10);
                    }
                }
            }

            cloud.RecalculateBoundsAndCentroid();
            return cloud;
        }

        private static void ParseLineAndAdd(string line, char[] splitChars, PointCloud cloud)
        {
            var parts = line.Split(splitChars, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                if (float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                    float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                {
                    float intensity = 1.0f;
                    if (parts.Length >= 4 && float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float inten))
                    {
                        intensity = inten;
                    }
                    cloud.Add(new Point3D(new Vector3(x, y, z), intensity));
                }
            }
        }

        /// <summary>
        /// Attempts to acquire raw point coordinates from the active workspace scan.
        /// Returns null if no active loaded scan is available.
        /// </summary>
        public static PointCloud TryLoadActiveScanPoints(IAppHost host, out string sourceDescription, out string diagnosticMessage)
        {
            diagnosticMessage = string.Empty;
            sourceDescription = string.Empty;

            try
            {
                var wsHost = host?.WorkspaceHost;
                if (wsHost == null)
                {
                    diagnosticMessage = "SCENE WorkspaceHost interface is not attached in the current process context.";
                    return null;
                }

                if (!wsHost.HasActiveWorkspace || wsHost.ActiveWorkspace == null)
                {
                    diagnosticMessage = "No active workspace project (.lsproj) is currently open in SCENE.";
                    return null;
                }

                var ws = wsHost.ActiveWorkspace;
                if (ws.Scans == null)
                {
                    diagnosticMessage = "The active workspace project does not contain any scans.";
                    return null;
                }

                int totalScans = 0;
                int loadedScans = 0;

                foreach (var scan in ws.Scans)
                {
                    if (scan == null) continue;
                    totalScans++;

                    if (!scan.IsLoaded) continue;
                    loadedScans++;

                    var cloud = TryReadScanPointsFromSDK(scan);
                    if (cloud != null && cloud.Count > 100)
                    {
                        sourceDescription = $"Active SCENE Scan: '{scan.Name}' ({cloud.Count:N0} points)";
                        return cloud;
                    }
                }

                if (totalScans == 0)
                {
                    diagnosticMessage = "The active project workspace contains 0 scans.";
                }
                else if (loadedScans == 0)
                {
                    diagnosticMessage = $"Workspace has {totalScans} scan(s), but none are loaded into memory. In SCENE, right-click the scan in the Structure Tree and select 'Load Points'.";
                }
                else
                {
                    diagnosticMessage = $"{loadedScans} scan(s) are loaded, but direct SDK raw point access was not available for this scan format.";
                }
            }
            catch (Exception ex)
            {
                diagnosticMessage = $"Workspace scan query exception: {ex.Message}";
                host?.Logger?.LogWarning($"[FAROSceneHelloWorld] Could not read raw points from active scan: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Attempts to read active scan points, or falls back to synthetic benchmark scan if not available.
        /// </summary>
        public static PointCloud LoadActiveScanPoints(IAppHost host, out string sourceDescription)
        {
            var cloud = TryLoadActiveScanPoints(host, out sourceDescription, out string diagnostic);
            if (cloud != null)
            {
                return cloud;
            }

            sourceDescription = "Synthetic TLS Indoor Scan Benchmark (Floors, Ceilings, Walls, Columns, MEP Pipes)";
            return GenerateBenchmarkScanCloud();
        }

        private static PointCloud TryReadScanPointsFromSDK(IScan scan)
        {
            try
            {
                // 1. Direct GetPoints() interface contract
                var pts = scan.GetPoints();
                if (pts != null)
                {
                    var cloud = new PointCloud();
                    foreach (var p in pts)
                    {
                        cloud.Add(new Point3D(new Vector3((float)p.X, (float)p.Y, (float)p.Z), p.Intensity));
                    }
                    cloud.RecalculateBoundsAndCentroid();
                    return cloud;
                }
            }
            catch
            {
            }

            try
            {
                var method = scan.GetType().GetMethod("GetPointBuffer", BindingFlags.Public | BindingFlags.Instance)
                             ?? scan.GetType().GetMethod("ReadPoints", BindingFlags.Public | BindingFlags.Instance);
                if (method != null)
                {
                    object result = method.Invoke(scan, null);
                    if (result is IEnumerable<Vector3> vecEnum)
                    {
                        var cloud = new PointCloud();
                        foreach (var v in vecEnum) cloud.Add(new Point3D(v));
                        cloud.RecalculateBoundsAndCentroid();
                        return cloud;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>
        /// Generates a realistic 12m x 10m x 3.2m indoor building scan with:
        /// - 1 Floor (Z=0.0m)
        /// - 1 Ceiling / Slab (Z=3.2m)
        /// - 4 Vertical Walls (X=±6m, Y=±5m)
        /// - 2 Vertical Structural Columns (r=0.25m, h=3.2m at (2.5, 1.5) and (-2.5, -1.5))
        /// - 2 MEP Utility Pipes (r=0.05m at Z=2.8m, and r=0.08m at Z=2.5m)
        /// - Gaussian scanner surface noise (sigma = 2.5mm)
        /// </summary>
        public static PointCloud GenerateBenchmarkScanCloud()
        {
            var cloud = new PointCloud(150000);
            var rng = new Random(1337);

            Func<float, float> noise = (sigma) =>
            {
                // Box-Muller Gaussian
                double u1 = 1.0 - rng.NextDouble();
                double u2 = 1.0 - rng.NextDouble();
                double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
                return (float)(randStdNormal * sigma);
            };

            const float sigmaNoise = 0.0025f; // 2.5mm scanner jitter

            // 1. Floor: 12m x 10m at Z = 0.0m
            for (int i = 0; i < 35000; i++)
            {
                float x = (float)(rng.NextDouble() * 12.0 - 6.0);
                float y = (float)(rng.NextDouble() * 10.0 - 5.0);
                float z = 0.0f + noise(sigmaNoise);
                cloud.Add(new Point3D(new Vector3(x, y, z), 0.8f));
            }

            // 2. Ceiling / Slab: 12m x 10m at Z = 3.2m
            for (int i = 0; i < 30000; i++)
            {
                float x = (float)(rng.NextDouble() * 12.0 - 6.0);
                float y = (float)(rng.NextDouble() * 10.0 - 5.0);
                float z = 3.2f + noise(sigmaNoise);
                cloud.Add(new Point3D(new Vector3(x, y, z), 0.7f));
            }

            // 3. Walls: 4 perimeter walls
            // Wall North (+Y = +5m) & South (-Y = -5m)
            for (int i = 0; i < 15000; i++)
            {
                float x = (float)(rng.NextDouble() * 12.0 - 6.0);
                float z = (float)(rng.NextDouble() * 3.2);
                cloud.Add(new Point3D(new Vector3(x, 5.0f + noise(sigmaNoise), z), 0.6f));
                cloud.Add(new Point3D(new Vector3(x, -5.0f + noise(sigmaNoise), z), 0.6f));
            }

            // Wall East (+X = +6m) & West (-X = -6m)
            for (int i = 0; i < 12000; i++)
            {
                float y = (float)(rng.NextDouble() * 10.0 - 5.0);
                float z = (float)(rng.NextDouble() * 3.2);
                cloud.Add(new Point3D(new Vector3(6.0f + noise(sigmaNoise), y, z), 0.6f));
                cloud.Add(new Point3D(new Vector3(-6.0f + noise(sigmaNoise), y, z), 0.6f));
            }

            // 4. Columns: 2 Vertical Cylinders (Radius = 0.25m, Height = 3.2m)
            Vector3[] columnLocations = new Vector3[]
            {
                new Vector3(2.5f, 1.5f, 0f),
                new Vector3(-2.5f, -1.5f, 0f)
            };

            foreach (var colLoc in columnLocations)
            {
                float r = 0.25f;
                for (int i = 0; i < 9000; i++)
                {
                    float theta = (float)(rng.NextDouble() * Math.PI * 2.0);
                    float z = (float)(rng.NextDouble() * 3.2);
                    float x = colLoc.X + (r + noise(sigmaNoise)) * (float)Math.Cos(theta);
                    float y = colLoc.Y + (r + noise(sigmaNoise)) * (float)Math.Sin(theta);
                    cloud.Add(new Point3D(new Vector3(x, y, z), 0.85f));
                }
            }

            // 5. MEP Pipes: 2 Overhead Cylinders (Radius < 0.15m)
            // Pipe 1: Diameter 100mm (Radius = 0.05m), running along X at Y = 2.0m, Z = 2.8m
            for (int i = 0; i < 6000; i++)
            {
                float x = (float)(rng.NextDouble() * 10.0 - 5.0);
                float theta = (float)(rng.NextDouble() * Math.PI * 2.0);
                float r = 0.05f;
                float y = 2.0f + (r + noise(sigmaNoise)) * (float)Math.Cos(theta);
                float z = 2.8f + (r + noise(sigmaNoise)) * (float)Math.Sin(theta);
                cloud.Add(new Point3D(new Vector3(x, y, z), 0.9f));
            }

            // Pipe 2: Diameter 160mm (Radius = 0.08m), running along Y at X = -1.0m, Z = 2.5m
            for (int i = 0; i < 6000; i++)
            {
                float y = (float)(rng.NextDouble() * 8.0 - 4.0);
                float theta = (float)(rng.NextDouble() * Math.PI * 2.0);
                float r = 0.08f;
                float x = -1.0f + (r + noise(sigmaNoise)) * (float)Math.Cos(theta);
                float z = 2.5f + (r + noise(sigmaNoise)) * (float)Math.Sin(theta);
                cloud.Add(new Point3D(new Vector3(x, y, z), 0.9f));
            }

            cloud.RecalculateBoundsAndCentroid();
            return cloud;
        }
    }
}
