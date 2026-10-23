using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using FAROSceneSDCardWorkflow.Hardware;

namespace FAROSceneSDCardWorkflow.Workflow
{
    /// <summary>
    /// Generates a realistic simulated FARO SD card directory structure with multiple overlapping
    /// scans containing shared 140mm spherical reference targets for instant verification.
    /// </summary>
    public static class SyntheticSDCardGenerator
    {
        public static SDCardInfo GenerateSyntheticSDCard(string targetFolder = null, int scanCount = 3)
        {
            string folder = GenerateSimulatedSDCard(targetFolder);
            var scanFiles = SDCardWatcher.FindScanFilesInDirectory(folder);
            long totalBytes = 0;
            foreach (var f in scanFiles)
            {
                try { totalBytes += new FileInfo(f).Length; } catch { }
            }

            return new SDCardInfo
            {
                DriveLetter = folder,
                VolumeLabel = "FARO_SD_SIM",
                DriveType = DriveType.Removable,
                TotalSizeBytes = 64L * 1024 * 1024 * 1024,
                AvailableFreeBytes = 48L * 1024 * 1024 * 1024,
                ScanFiles = scanFiles,
                TotalScanSizeBytes = totalBytes,
                ScannerSerial = "Focus-S350-SIM",
                DetectedTime = DateTime.Now,
                IsSimulated = true
            };
        }

        public static string GenerateSimulatedSDCard(string targetFolder = null)
        {
            if (string.IsNullOrEmpty(targetFolder))
            {
                targetFolder = Path.Combine(Path.GetTempPath(), "FARO_SD_SIMULATED_" + DateTime.Now.ToString("HHmmss"));
            }

            string rawScansDir = Path.Combine(targetFolder, "RawScans");
            Directory.CreateDirectory(rawScansDir);

            // Create Focus scanner metadata file
            File.WriteAllText(Path.Combine(targetFolder, "Focus.ini"),
                "[Scanner]\r\nModel=Focus Premium S350\r\nSerial=LLS-082405912\r\nFirmware=7.2.1.250\r\nOperator=FieldTech\r\n");

            // 4 Fixed World Reference Spheres (Radius = 0.070m = 140mm diameter)
            Vector3[] worldSpheres = new Vector3[]
            {
                new Vector3( 2.0f,  3.5f, 1.20f), // Target 1
                new Vector3( 5.5f,  1.5f, 1.55f), // Target 2
                new Vector3(-2.2f,  2.8f, 0.95f), // Target 3
                new Vector3( 1.0f,  6.0f, 1.40f)  // Target 4
            };

            // 3 Scanner Stations (World positions & Yaw rotations)
            var stations = new[]
            {
                new { Id = "Scan_001", Pos = new Vector3( 0.0f, 0.0f, 1.6f), YawDeg =   0.0f },
                new { Id = "Scan_002", Pos = new Vector3( 4.0f, 1.5f, 1.6f), YawDeg =  35.0f },
                new { Id = "Scan_003", Pos = new Vector3(-2.5f, 2.5f, 1.6f), YawDeg = -30.0f }
            };

            var rng = new Random(2026);

            for (int sIdx = 0; sIdx < stations.Length; sIdx++)
            {
                var station = stations[sIdx];
                string filePath = Path.Combine(rawScansDir, $"{station.Id}.pts");

                float yawRad = station.YawDeg * (float)Math.PI / 180f;
                var R_world = Matrix4x4.CreateRotationZ(yawRad);
                var T_world = Matrix4x4.CreateTranslation(station.Pos);
                var M_world = R_world * T_world; // Local to World

                // Inverse matrix: World to Local
                Matrix4x4.Invert(M_world, out Matrix4x4 M_local);

                var localPoints = new List<Vector3>(40000);

                // 1. Generate room environment (Floor Z=0, Walls at X=±8, Y=±8, Ceiling Z=3.5)
                for (int i = 0; i < 15000; i++)
                {
                    float wx = (float)(rng.NextDouble() * 16.0 - 8.0);
                    float wy = (float)(rng.NextDouble() * 16.0 - 8.0);
                    float wz = (float)(rng.NextDouble() * 0.05); // Floor
                    localPoints.Add(Vector3.Transform(new Vector3(wx, wy, wz), M_local));
                }

                // Walls
                for (int i = 0; i < 10000; i++)
                {
                    float wx = (float)(rng.NextDouble() * 16.0 - 8.0);
                    float wz = (float)(rng.NextDouble() * 3.5);
                    localPoints.Add(Vector3.Transform(new Vector3(wx, 8.0f, wz), M_local));
                    localPoints.Add(Vector3.Transform(new Vector3(wx, -8.0f, wz), M_local));
                }

                // 2. Synthesize Reference Target Spheres in local frame
                float rSphere = 0.070f; // 140mm sphere
                for (int tIdx = 0; tIdx < worldSpheres.Length; tIdx++)
                {
                    Vector3 worldCenter = worldSpheres[tIdx];
                    Vector3 localCenter = Vector3.Transform(worldCenter, M_local);

                    // Add 150 points on the surface of the sphere
                    for (int p = 0; p < 150; p++)
                    {
                        double u = rng.NextDouble();
                        double v = rng.NextDouble();
                        double theta = u * 2.0 * Math.PI;
                        double phi = Math.Acos(2.0 * v - 1.0);

                        float sx = (float)(Math.Sin(phi) * Math.Cos(theta)) * rSphere;
                        float sy = (float)(Math.Sin(phi) * Math.Sin(theta)) * rSphere;
                        float sz = (float)(Math.Cos(phi)) * rSphere;

                        localPoints.Add(localCenter + new Vector3(sx, sy, sz));
                    }
                }

                // Write PTS file
                using (var sw = new StreamWriter(filePath))
                {
                    sw.WriteLine(localPoints.Count);
                    foreach (var pt in localPoints)
                    {
                        sw.WriteLine($"{pt.X:F4} {pt.Y:F4} {pt.Z:F4} 2048");
                    }
                }
            }

            return targetFolder;
        }
    }
}
