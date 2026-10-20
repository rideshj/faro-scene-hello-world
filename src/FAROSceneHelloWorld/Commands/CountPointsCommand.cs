using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using FARO.LS.SDK.AppLogic;
using FARO.LS.SDK.DataLogic;
using FAROSceneHelloWorld.UI;

namespace FAROSceneHelloWorld.Commands
{
    /// <summary>
    /// Command handler responsible for calculating and displaying workspace scan statistics.
    /// Triggered by the FARO SCENE Ribbon button or declarative command event.
    /// </summary>
    public class CountPointsCommand
    {
        private readonly IAppHost _host;

        public CountPointsCommand(IAppHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        /// <summary>
        /// Main execution handler invoked when the Ribbon button is clicked.
        /// </summary>
        public void Execute(object sender = null, EventArgs e = null)
        {
            try
            {
                _host.Logger?.LogInfo("[FAROSceneHelloWorld] CountPointsCommand invoked.");

                // 1. Verify workspace availability
                var workspaceHost = _host.WorkspaceHost;
                if (workspaceHost == null || !workspaceHost.HasActiveWorkspace || workspaceHost.ActiveWorkspace == null)
                {
                    _host.Logger?.LogWarning("[FAROSceneHelloWorld] No active workspace detected.");
                    MessageBox.Show(
                        "No active workspace is currently open in FARO SCENE.\n\n" +
                        "Please open or create a project (.lsproj or .fls scan project) before running this command.",
                        "FARO SCENE - No Workspace Open",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                var workspace = workspaceHost.ActiveWorkspace;
                string workspaceName = workspace.Name ?? "Untitled Workspace";
                string workspacePath = workspace.FilePath ?? "Not saved";

                // 2. Enumerate scans and calculate statistics
                var scanList = new List<ScanItemInfo>();
                long totalActivePoints = 0;
                int loadedScanCount = 0;

                var scans = workspace.Scans;
                if (scans != null)
                {
                    foreach (var scan in scans)
                    {
                        if (scan == null) continue;

                        string scanName = scan.Name ?? "Unnamed Scan";
                        bool isLoaded = scan.IsLoaded;
                        long pointCount = 0;

                        if (isLoaded)
                        {
                            loadedScanCount++;
                            pointCount = GetScanPointCount(scan);
                            totalActivePoints += pointCount;
                        }

                        scanList.Add(new ScanItemInfo
                        {
                            Name = scanName,
                            IsLoaded = isLoaded,
                            PointCount = pointCount
                        });
                    }
                }

                _host.Logger?.LogInfo(
                    $"[FAROSceneHelloWorld] Statistics calculated: {scanList.Count} total scans, " +
                    $"{loadedScanCount} open scans, {totalActivePoints:N0} total points.");

                // 3. Display the modal statistics dialog
                using (var dialog = new StatsDialog(workspaceName, workspacePath, scanList, totalActivePoints, loadedScanCount))
                {
                    dialog.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                _host.Logger?.LogError($"[FAROSceneHelloWorld] Error calculating scan statistics: {ex.Message}", ex);
                MessageBox.Show(
                    $"An error occurred while inspecting workspace scans:\n\n{ex.Message}",
                    "FARO SCENE - Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Resilient helper to retrieve point count from an IScan instance.
        /// Primarily reads NumberOfPoints, with defensive reflection fallbacks
        /// across minor SCENE SDK version differences.
        /// </summary>
        private static long GetScanPointCount(IScan scan)
        {
            if (scan == null) return 0;

            try
            {
                // Primary SDK contract
                return scan.NumberOfPoints;
            }
            catch
            {
                // Defensive fallback for legacy / alternate SDK builds
                try
                {
                    var prop = scan.GetType().GetProperty("PointCount", BindingFlags.Public | BindingFlags.Instance)
                               ?? scan.GetType().GetProperty("NumPoints", BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null)
                    {
                        object val = prop.GetValue(scan, null);
                        if (val != null) return Convert.ToInt64(val);
                    }
                }
                catch
                {
                    // Ignore reflection errors and return 0
                }

                return 0;
            }
        }
    }
}
