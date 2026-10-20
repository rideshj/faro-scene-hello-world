using System;
using System.Diagnostics;
using FAROSceneHelloWorld.Classification;

namespace AlgorithmVerification
{
    class Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine(" 3D Point Cloud Structural & MEP Verification");
            Console.WriteLine("=================================================");

            Console.WriteLine("\n[1] Generating 150,000 pt Synthetic TLS Benchmark Scan...");
            var sw = Stopwatch.StartNew();
            var cloud = ScanDataLoader.GenerateBenchmarkScanCloud();
            sw.Stop();
            Console.WriteLine($"    Generated {cloud.Count:N0} points in {sw.ElapsedMilliseconds} ms.");
            Console.WriteLine($"    Bounds: Min={cloud.BoundingMin}, Max={cloud.BoundingMax}");

            Console.WriteLine("\n[2] Executing Classification Pipeline (Voxel -> PCA -> RANSAC Planes -> RANSAC Cylinders)...");
            var classifier = new ElementClassifier();
            classifier.ProgressChanged += (stage, pct) =>
            {
                Console.WriteLine($"    [{pct,3}%] {stage}");
            };

            var result = classifier.Execute(cloud, host: null, voxelSize: 0.035f);

            Console.WriteLine("\n=================================================");
            Console.WriteLine(" Classification Results Summary");
            Console.WriteLine("=================================================");
            Console.WriteLine($"Raw Points        : {result.RawPointCount:N0}");
            Console.WriteLine($"Downsampled Points: {result.DownsampledPointCount:N0}");
            Console.WriteLine($"Processing Time   : {result.ExecutionTime.TotalSeconds:F2} seconds");
            Console.WriteLine($"Total Elements    : {result.TotalDetectedCount}");
            Console.WriteLine($"Floors Detected   : {result.Floors.Count}");
            Console.WriteLine($"Ceilings Detected : {result.Ceilings.Count}");
            Console.WriteLine($"Walls Detected    : {result.Walls.Count}");
            Console.WriteLine($"Columns Detected  : {result.Columns.Count}");
            Console.WriteLine($"MEP Pipes Detected: {result.Pipes.Count}");

            foreach (var elem in result.AllElements)
            {
                Console.WriteLine($"  - [{elem.ClusterName}] {elem.Name}: {elem.DimensionSummary} (Center: {elem.Center.X:F2}, {elem.Center.Y:F2}, {elem.Center.Z:F2}) [Inliers: {elem.InlierCount:N0} pts]");
            }

            bool passed = true;
            if (result.Floors.Count != 1)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAIL: Expected 1 Floor, got {result.Floors.Count}");
                passed = false;
            }
            else
            {
                var floor = result.Floors[0];
                if (Math.Abs(floor.Center.Z) > 0.15f)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"FAIL: Floor elevation should be ~0.0m, got {floor.Center.Z:F3}m");
                    passed = false;
                }
            }

            if (result.Ceilings.Count != 1)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAIL: Expected 1 Ceiling, got {result.Ceilings.Count}");
                passed = false;
            }
            else
            {
                var ceiling = result.Ceilings[0];
                if (Math.Abs(ceiling.Center.Z - 3.2f) > 0.25f)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"FAIL: Ceiling elevation should be ~3.2m, got {ceiling.Center.Z:F3}m");
                    passed = false;
                }
            }

            if (result.Walls.Count < 3)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAIL: Expected at least 3-4 Walls, got {result.Walls.Count}");
                passed = false;
            }

            if (result.Columns.Count < 1)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAIL: Expected at least 1-2 Columns, got {result.Columns.Count}");
                passed = false;
            }

            if (result.Pipes.Count < 1)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAIL: Expected at least 1-2 MEP Pipes, got {result.Pipes.Count}");
                passed = false;
            }

            if (passed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n>>> ALL CLASSIFICATION ALGORITHM TESTS PASSED! <<<");
                Console.ResetColor();
                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n>>> TEST SUITE FAILED <<<");
                Console.ResetColor();
                return 1;
            }
        }
    }
}
