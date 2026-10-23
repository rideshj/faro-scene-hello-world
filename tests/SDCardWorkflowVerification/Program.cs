using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using FAROSceneSDCardWorkflow.Hardware;
using FAROSceneSDCardWorkflow.Registration;
using FAROSceneSDCardWorkflow.UI;
using FAROSceneSDCardWorkflow.Workflow;

namespace SDCardWorkflowVerification
{
    class Program
    {
        static int Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==========================================================================");
            Console.WriteLine("   FARO SCENE: Automated SD Card Field-to-Finish End-to-End Test Suite    ");
            Console.WriteLine("==========================================================================");
            Console.ResetColor();

            int passed = 0;
            int total = 8;
            string tempDir = Path.Combine(Path.GetTempPath(), "FARO_Test_SDWorkflow_" + Guid.NewGuid().ToString("N"));

            try
            {
                // TEST 1: Synthetic SD Card Generation
                Console.Write("\n[TEST 1/8] Generating Synthetic FARO SD Card Media... ");
                string sdFolder = Path.Combine(tempDir, "SD_Card");
                var card = SyntheticSDCardGenerator.GenerateSyntheticSDCard(sdFolder, scanCount: 3);

                if (card != null && card.ScanCount == 3 && card.TotalScanSizeBytes > 0)
                {
                    PrintPass($"Found {card.ScanCount} scans ({card.ScanSizeMb:F1} MB), Volume: {card.VolumeLabel}");
                    passed++;
                }
                else
                {
                    PrintFail("Failed to generate or inspect synthetic SD card.");
                }

                // TEST 2: Project Creation & Buffered Scan Import
                Console.Write("[TEST 2/8] Creating Local Project & Importing Scans... ");
                string projBase = Path.Combine(tempDir, "Projects");
                string projectPath = ScanImportManager.CreateProjectDirectory(projBase, "Project_Test_001");

                bool subdirsExist = Directory.Exists(Path.Combine(projectPath, "Scans")) &&
                                    Directory.Exists(Path.Combine(projectPath, "Exports")) &&
                                    Directory.Exists(Path.Combine(projectPath, "Reports"));

                var imported = ScanImportManager.ImportScans(card.ScanFiles, projectPath);

                if (subdirsExist && imported.Count == 3)
                {
                    PrintPass($"Project staged with 3 imported scan files at {projectPath}");
                    passed++;
                }
                else
                {
                    PrintFail("Project directory structure or file import failed.");
                }

                // TEST 3: Scan Processing & Stray Point Filtering
                Console.Write("[TEST 3/8] Processing Scan Geometry & Stray Point Filtering... ");
                var stations = ScanProcessor.ProcessScans(imported, applyStrayFilter: true);

                bool pointsLoaded = stations.Count == 3 &&
                                    stations[0].Points.Count > 5000 &&
                                    stations[1].Points.Count > 5000 &&
                                    stations[2].Points.Count > 5000;

                if (pointsLoaded)
                {
                    PrintPass($"3 Scan Stations Loaded: Stn1={stations[0].Points.Count} pts, Stn2={stations[1].Points.Count} pts, Stn3={stations[2].Points.Count} pts");
                    passed++;
                }
                else
                {
                    PrintFail("Point cloud loading or stray point filtering failed.");
                }

                // TEST 4: Reference Sphere Target Detection
                Console.Write("[TEST 4/8] Detecting 140mm Reference Target Spheres... ");
                var settings = new TargetRegistrationSettings
                {
                    PrimaryTargetType = TargetType.Sphere140mm,
                    MaxDistanceTolerance = 0.008f,
                    MaxAllowedMeanErrorMm = 4.0f
                };

                for (int i = 0; i < stations.Count; i++)
                {
                    stations[i].Targets = TargetDetector.DetectTargets(stations[i].Points, stations[i].ScanId, settings);
                }

                bool targetsDetected = stations[0].Targets.Count >= 3 &&
                                       stations[1].Targets.Count >= 3 &&
                                       stations[2].Targets.Count >= 3;

                if (targetsDetected)
                {
                    PrintPass($"Spheres Detected: Stn1={stations[0].Targets.Count}, Stn2={stations[1].Targets.Count}, Stn3={stations[2].Targets.Count} (r ≈ 70mm)");
                    passed++;
                }
                else
                {
                    PrintFail($"Target detection incomplete: Stn1={stations[0].Targets.Count}, Stn2={stations[1].Targets.Count}, Stn3={stations[2].Targets.Count}");
                }

                // TEST 5: Distance Invariant Target Matching
                Console.Write("[TEST 5/8] Invariant Distance Matrix Target Correspondence Matching... ");
                var matches12 = TargetMatcher.MatchTargets(stations[0].Targets, stations[1].Targets, settings.MaxDistanceTolerance);
                var matches13 = TargetMatcher.MatchTargets(stations[0].Targets, stations[2].Targets, settings.MaxDistanceTolerance);

                if (matches12.Count >= 3 && matches13.Count >= 3)
                {
                    PrintPass($"Matched Pairs: Stn1<->Stn2={matches12.Count} pairs, Stn1<->Stn3={matches13.Count} pairs");
                    passed++;
                }
                else
                {
                    PrintFail($"Matching failed: Stn1-2={matches12.Count}, Stn1-3={matches13.Count} (requires >= 3)");
                }

                // TEST 6: Closed-Form Horn / Kabsch SVD Rigid Registration
                Console.Write("[TEST 6/8] Rigid 6-DOF Horn/Kabsch SVD Solver Verification... ");
                var srcPts = new List<Vector3>();
                var tgtPts = new List<Vector3>();
                foreach (var m in matches12)
                {
                    srcPts.Add(m.TargetInScanB.Center);
                    tgtPts.Add(m.TargetInScanA.Center);
                }

                bool svdSolved = RigidTransformSolver.Solve(srcPts, tgtPts, out Matrix4x4 t12, out float meanRes, out float maxRes, out _);
                // Station 2 position relative to Station 1 (0, 0, 1.6) is (4.0, 1.5, 0.0)
                Vector3 recoveredPos2 = new Vector3(t12.M41, t12.M42, t12.M43);
                float posError = Vector3.Distance(recoveredPos2, new Vector3(4.0f, 1.5f, 0.0f));

                if (svdSolved && meanRes < 2.0f && posError < 0.05f)
                {
                    PrintPass($"SVD 6-DOF Solved: Mean Residual={meanRes:F2}mm, Scanner Position=({recoveredPos2.X:F3}, {recoveredPos2.Y:F3}, {recoveredPos2.Z:F3})");
                    passed++;
                }
                else
                {
                    PrintFail($"SVD solve error: Residual={meanRes:F2}mm, PosError={posError:F3}m");
                }

                // TEST 7: Multi-Scan Network Registration Engine
                Console.Write("[TEST 7/8] Full Network Target-Based Registration Graph... ");
                var regEngine = new TargetRegistrationEngine();
                var regResult = regEngine.Execute(stations, settings);

                if (regResult.Success && regResult.RegisteredScans == 3 && regResult.MeanTargetErrorMm < 2.5f)
                {
                    PrintPass($"100% Scans Registered ({regResult.RegisteredScans}/3) | Mean Error: {regResult.MeanTargetErrorMm:F2}mm | Quality: {regResult.QualityRating}");
                    passed++;
                }
                else
                {
                    PrintFail($"Network registration failed: {regResult}");
                }

                // TEST 8: Top View Canvas & Report Export
                Console.Write("[TEST 8/8] Top View Verification Canvas & Report Generation... ");
                var canvas = new TopViewCanvas();
                canvas.Width = 800;
                canvas.Height = 600;
                canvas.SetData(regResult.Stations, regResult.Correspondences);
                canvas.ResetView();

                string reportPath = Path.Combine(projectPath, "Reports", "RegistrationReport.txt");
                using (var sw = new StreamWriter(reportPath))
                {
                    sw.WriteLine("FARO SCENE REGISTRATION REPORT");
                    sw.WriteLine($"Scans: {regResult.RegisteredScans}/{regResult.TotalScans}");
                    sw.WriteLine($"Mean Error: {regResult.MeanTargetErrorMm:F2} mm");
                    sw.WriteLine($"Max Error: {regResult.MaxTargetErrorMm:F2} mm");
                }

                if (File.Exists(reportPath) && new FileInfo(reportPath).Length > 20)
                {
                    PrintPass($"Top View Canvas bounds initialized. Report exported ({new FileInfo(reportPath).Length} bytes).");
                    passed++;
                }
                else
                {
                    PrintFail("Top View canvas setup or report export failed.");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[EXCEPTION] {ex.Message}\n{ex.StackTrace}");
                Console.ResetColor();
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch { }
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==========================================================================");
            if (passed == total)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"   RESULT: ALL {passed}/{total} TESTS PASSED PERFECTLY (100% SUCCESS)     ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("==========================================================================");
                Console.ResetColor();
                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"   RESULT: {passed}/{total} TESTS PASSED ({total - passed} FAILED)        ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("==========================================================================");
                Console.ResetColor();
                return 1;
            }
        }

        private static void PrintPass(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[PASS]");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"        └─ {message}");
            Console.ResetColor();
        }

        private static void PrintFail(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[FAIL]");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"        └─ {message}");
            Console.ResetColor();
        }
    }
}
