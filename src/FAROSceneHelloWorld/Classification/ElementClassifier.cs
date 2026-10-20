using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using FARO.LS.SDK.AppLogic;
using FARO.LS.SDK.DataLogic;
using FAROSceneHelloWorld.Geometry;

namespace FAROSceneHelloWorld.Classification
{
    /// <summary>
    /// Classification report summary containing detected counts, metrics, and execution time.
    /// </summary>
    public class ClassificationResult
    {
        public int RawPointCount { get; set; }
        public int DownsampledPointCount { get; set; }
        public TimeSpan ExecutionTime { get; set; }

        public List<ClassifiedElement> Floors { get; } = new List<ClassifiedElement>();
        public List<ClassifiedElement> Ceilings { get; } = new List<ClassifiedElement>();
        public List<ClassifiedElement> Walls { get; } = new List<ClassifiedElement>();
        public List<ClassifiedElement> Columns { get; } = new List<ClassifiedElement>();
        public List<ClassifiedElement> Pipes { get; } = new List<ClassifiedElement>();

        public IEnumerable<ClassifiedElement> AllElements =>
            Floors.Concat(Ceilings).Concat(Walls).Concat(Columns).Concat(Pipes);

        public int TotalDetectedCount =>
            Floors.Count + Ceilings.Count + Walls.Count + Columns.Count + Pipes.Count;
    }

    /// <summary>
    /// Core architectural and MEP extraction engine.
    /// Orchestrates Voxel Downsampling -> PCA Normals -> RANSAC Plane Extraction -> RANSAC Cylinder Extraction -> Project Tree Clustering.
    /// </summary>
    public class ElementClassifier
    {
        public event Action<string, int> ProgressChanged; // message, percent

        /// <summary>
        /// Executes automated structural and MEP extraction on the provided point cloud.
        /// </summary>
        public ClassificationResult Execute(
            PointCloud rawCloud,
            IAppHost host = null,
            float voxelSize = 0.035f)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = new ClassificationResult
            {
                RawPointCount = rawCloud.Count
            };

            // -------------------------------------------------------------
            // Stage 1: Spatial Voxel Grid Downsampling
            // -------------------------------------------------------------
            ReportProgress("Downsampling point cloud with spatial voxel grid...", 10);
            var downsampled = VoxelGridDownsampler.Downsample(rawCloud, voxelSize);
            result.DownsampledPointCount = downsampled.Count;

            // -------------------------------------------------------------
            // Stage 2: Normal Vector Estimation via Local Covariance / PCA
            // -------------------------------------------------------------
            ReportProgress($"Estimating normal vectors via PCA ({downsampled.Count:N0} points)...", 25);
            NormalEstimator.EstimateNormals(downsampled, kNeighbors: 20, viewpoint: Vector3.Zero);

            // -------------------------------------------------------------
            // Stage 3: RANSAC Plane Detection (Floors, Ceilings, Walls)
            // -------------------------------------------------------------
            ReportProgress("Fitting planar models with RANSAC...", 45);
            var detectedPlanes = RansacPlaneDetector.DetectPlanes(
                downsampled,
                distanceThreshold: 0.035f,
                normalAngleThresholdDeg: 15.0f,
                maxIterations: 350,
                minInliers: Math.Max(150, downsampled.Count / 100),
                maxPlanes: 12);

            // Separate horizontal and vertical planes
            var horizontalPlanes = new List<DetectedPlane>();
            var verticalPlanes = new List<DetectedPlane>();

            var planarInlierSet = new HashSet<int>();

            foreach (var plane in detectedPlanes)
            {
                foreach (int idx in plane.InlierIndices) planarInlierSet.Add(idx);

                if (plane.IsHorizontal)
                {
                    horizontalPlanes.Add(plane);
                }
                else if (plane.IsVertical)
                {
                    verticalPlanes.Add(plane);
                }
            }

            // Classify Horizontal Planes: Lowest = Floor, Highest = Ceiling/Slab
            if (horizontalPlanes.Count > 0)
            {
                horizontalPlanes.Sort((a, b) => a.Elevation.CompareTo(b.Elevation));

                // Lowest horizontal plane is Floor
                var floorPlane = horizontalPlanes.First();
                var floorElem = CreatePlaneElement(floorPlane, downsampled, StructuralElementType.Floor, "Floors", "Floor (Ground Slab)");
                result.Floors.Add(floorElem);

                // If another horizontal plane exists at higher elevation, classify as Ceiling
                if (horizontalPlanes.Count > 1)
                {
                    var ceilingPlane = horizontalPlanes.Last();
                    // Ensure it is higher than floor
                    if (ceilingPlane.Elevation > floorPlane.Elevation + 1.2f)
                    {
                        var ceilingElem = CreatePlaneElement(ceilingPlane, downsampled, StructuralElementType.Ceiling, "Ceilings", "Ceiling / Upper Slab");
                        result.Ceilings.Add(ceilingElem);
                    }
                }
            }

            // Classify Vertical Planes: Walls
            int wallCounter = 1;
            foreach (var wallPlane in verticalPlanes)
            {
                var wallElem = CreatePlaneElement(wallPlane, downsampled, StructuralElementType.Wall, "Walls", $"Wall {wallCounter++}");
                result.Walls.Add(wallElem);
            }

            // -------------------------------------------------------------
            // Stage 4: Non-Planar Residual Filtering & Cylinder RANSAC
            // -------------------------------------------------------------
            ReportProgress("Extracting non-planar points for cylinder fitting...", 65);
            var cylinderCandidateIndices = new List<int>(downsampled.Count - planarInlierSet.Count);
            for (int i = 0; i < downsampled.Count; i++)
            {
                if (!planarInlierSet.Contains(i))
                {
                    cylinderCandidateIndices.Add(i);
                }
            }

            ReportProgress($"Fitting cylindrical models (Columns & Pipes) on {cylinderCandidateIndices.Count:N0} points...", 75);
            var detectedCylinders = RansacCylinderDetector.DetectCylinders(
                downsampled,
                cylinderCandidateIndices,
                distanceThreshold: 0.035f,
                minRadius: 0.015f,
                maxRadius: 1.2f,
                maxIterations: 800,
                minInliers: Math.Max(300, cylinderCandidateIndices.Count / 50),
                maxCylinders: 8);

            int colCounter = 1;
            int pipeCounter = 1;

            foreach (var cyl in detectedCylinders)
            {
                if (cyl.IsColumn)
                {
                    var colElem = CreateCylinderElement(cyl, downsampled, StructuralElementType.Column, "Columns", $"Column {colCounter++}");
                    result.Columns.Add(colElem);
                }
                else if (cyl.IsPipe)
                {
                    var pipeElem = CreateCylinderElement(cyl, downsampled, StructuralElementType.Pipe, "MEP Pipes", $"MEP Pipe {pipeCounter++}");
                    result.Pipes.Add(pipeElem);
                }
            }

            // -------------------------------------------------------------
            // Stage 5: FARO SCENE Project Tree Integration
            // -------------------------------------------------------------
            ReportProgress("Creating clusters in SCENE Project Tree...", 90);
            InsertIntoSceneProjectTree(host, result);

            sw.Stop();
            result.ExecutionTime = sw.Elapsed;
            ReportProgress($"Classification complete! {result.TotalDetectedCount} elements extracted in {result.ExecutionTime.TotalSeconds:F2}s.", 100);

            return result;
        }

        private static ClassifiedElement CreatePlaneElement(DetectedPlane plane, PointCloud cloud, StructuralElementType type, string cluster, string name)
        {
            var elem = new ClassifiedElement
            {
                Name = name,
                Type = type,
                ClusterName = cluster,
                Center = plane.Center,
                Orientation = plane.Normal,
                PrimaryDimension = plane.Width,
                SecondaryDimension = plane.Length,
                PlaneGeometry = plane
            };
            foreach (int idx in plane.InlierIndices)
            {
                elem.InlierPoints.Add(cloud.Points[idx].Position);
            }
            return elem;
        }

        private static ClassifiedElement CreateCylinderElement(DetectedCylinder cyl, PointCloud cloud, StructuralElementType type, string cluster, string name)
        {
            var elem = new ClassifiedElement
            {
                Name = name,
                Type = type,
                ClusterName = cluster,
                Center = cyl.Center,
                Orientation = cyl.AxisDirection,
                PrimaryDimension = cyl.Radius,
                SecondaryDimension = cyl.Length,
                CylinderGeometry = cyl
            };
            foreach (int idx in cyl.InlierIndices)
            {
                elem.InlierPoints.Add(cloud.Points[idx].Position);
            }
            return elem;
        }

        /// <summary>
        /// Creates structured clusters in the FARO SCENE Project Tree:
        /// "Floors", "Ceilings", "Walls", "Columns", "MEP Pipes"
        /// and inserts the detected primitives and classified point subsets.
        /// </summary>
        public static void InsertIntoSceneProjectTree(IAppHost host, ClassificationResult result)
        {
            if (result == null) return;

            try
            {
                var ws = host?.WorkspaceHost?.ActiveWorkspace;
                if (ws != null)
                {
                    string[] clusterNames = new string[] { "Floors", "Ceilings", "Walls", "Columns", "MEP Pipes" };
                    var clusters = new Dictionary<string, ICluster>();

                    foreach (var cName in clusterNames)
                    {
                        try
                        {
                            var cluster = ws.CreateCluster(cName);
                            if (cluster != null)
                            {
                                clusters[cName] = cluster;
                            }
                        }
                        catch
                        {
                            // Host may not support direct dynamic cluster creation
                        }
                    }

                    // Insert elements into respective clusters
                    foreach (var elem in result.AllElements)
                    {
                        if (clusters.TryGetValue(elem.ClusterName, out var targetCluster))
                        {
                            try
                            {
                                targetCluster.AddItem(elem.Name, elem);
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                host?.Logger?.LogWarning($"[FAROSceneHelloWorld] Notice: Project tree cluster insertion: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates a layered 3D DXF file with classified geometric primitives
        /// and writes a text summary report.
        /// </summary>
        public static string ExportToDxf(ClassificationResult result, string outputFilePath)
        {
            var sb = new StringBuilder();

            // DXF Header
            sb.AppendLine("0\nSECTION\n2\nHEADER\n0\nENDSEC");

            // DXF Tables & Layers
            sb.AppendLine("0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n70\n5");
            AddDxfLayer(sb, "Floors", 3);    // Green
            AddDxfLayer(sb, "Ceilings", 1);  // Red
            AddDxfLayer(sb, "Walls", 4);     // Cyan
            AddDxfLayer(sb, "Columns", 5);   // Blue
            AddDxfLayer(sb, "MEP_Pipes", 2); // Yellow
            sb.AppendLine("0\nENDTAB\n0\nENDSEC");

            // DXF Entities
            sb.AppendLine("0\nSECTION\n2\nENTITIES");

            // Export Planes as 3DFACE
            foreach (var elem in result.Floors.Concat(result.Ceilings).Concat(result.Walls))
            {
                var p = elem.PlaneGeometry;
                if (p == null) continue;

                float hw = p.Width * 0.5f;
                float hl = p.Length * 0.5f;

                Vector3 c1 = p.Center - p.UAxis * hw - p.VAxis * hl;
                Vector3 c2 = p.Center + p.UAxis * hw - p.VAxis * hl;
                Vector3 c3 = p.Center + p.UAxis * hw + p.VAxis * hl;
                Vector3 c4 = p.Center - p.UAxis * hw + p.VAxis * hl;

                string layer = elem.ClusterName.Replace(" ", "_");

                sb.AppendLine("0\n3DFACE");
                sb.AppendLine($"8\n{layer}");
                sb.AppendLine($"10\n{c1.X:F4}\n20\n{c1.Y:F4}\n30\n{c1.Z:F4}");
                sb.AppendLine($"11\n{c2.X:F4}\n21\n{c2.Y:F4}\n31\n{c2.Z:F4}");
                sb.AppendLine($"12\n{c3.X:F4}\n22\n{c3.Y:F4}\n32\n{c3.Z:F4}");
                sb.AppendLine($"13\n{c4.X:F4}\n23\n{c4.Y:F4}\n33\n{c4.Z:F4}");
            }

            // Export Cylinders as 3D faceted facets
            foreach (var elem in result.Columns.Concat(result.Pipes))
            {
                var cyl = elem.CylinderGeometry;
                if (cyl == null) continue;

                string layer = elem.ClusterName.Replace(" ", "_");
                Vector3 axis = cyl.AxisDirection;
                float r = cyl.Radius;
                float halfLen = cyl.Length * 0.5f;

                Vector3 start = cyl.Center - axis * halfLen;
                Vector3 end = cyl.Center + axis * halfLen;

                // Create orthogonal circle basis
                Vector3 u = Math.Abs(axis.Z) < 0.9f
                    ? Vector3.Normalize(Vector3.Cross(axis, VectorMath.UnitZ))
                    : Vector3.Normalize(Vector3.Cross(axis, VectorMath.UnitX));
                Vector3 v = Vector3.Normalize(Vector3.Cross(axis, u));

                const int segments = 12;
                for (int s = 0; s < segments; s++)
                {
                    double th1 = s * 2.0 * Math.PI / segments;
                    double th2 = (s + 1) * 2.0 * Math.PI / segments;

                    Vector3 r1 = u * (float)(Math.Cos(th1) * r) + v * (float)(Math.Sin(th1) * r);
                    Vector3 r2 = u * (float)(Math.Cos(th2) * r) + v * (float)(Math.Sin(th2) * r);

                    Vector3 p1 = start + r1;
                    Vector3 p2 = start + r2;
                    Vector3 p3 = end + r2;
                    Vector3 p4 = end + r1;

                    sb.AppendLine("0\n3DFACE");
                    sb.AppendLine($"8\n{layer}");
                    sb.AppendLine($"10\n{p1.X:F4}\n20\n{p1.Y:F4}\n30\n{p1.Z:F4}");
                    sb.AppendLine($"11\n{p2.X:F4}\n21\n{p2.Y:F4}\n31\n{p2.Z:F4}");
                    sb.AppendLine($"12\n{p3.X:F4}\n22\n{p3.Y:F4}\n32\n{p3.Z:F4}");
                    sb.AppendLine($"13\n{p4.X:F4}\n23\n{p4.Y:F4}\n33\n{p4.Z:F4}");
                }
            }

            sb.AppendLine("0\nENDSEC\n0\nEOF");

            File.WriteAllText(outputFilePath, sb.ToString());
            return outputFilePath;
        }

        private static void AddDxfLayer(StringBuilder sb, string name, int colorCode)
        {
            sb.AppendLine("0\nLAYER");
            sb.AppendLine($"2\n{name}");
            sb.AppendLine("70\n0");
            sb.AppendLine($"62\n{colorCode}");
            sb.AppendLine("6\nCONTINUOUS");
        }

        private void ReportProgress(string message, int percent)
        {
            ProgressChanged?.Invoke(message, percent);
        }
    }
}
