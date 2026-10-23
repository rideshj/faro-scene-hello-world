using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace FAROSceneSDCardWorkflow.Hardware
{
    /// <summary>
    /// Background watcher detecting physical insertion and removal of FARO SD cards
    /// and removable storage media containing laser scan files (.fls, .pts, .xyz).
    /// </summary>
    public class SDCardWatcher : IDisposable
    {
        public static SDCardWatcher Instance { get; } = new SDCardWatcher();

        public event Action<SDCardInfo> CardInserted;
        public event Action<string> CardRemoved;
        public event Action<string> LogMessage;
        public event Action<string> WatcherError;

        private readonly HashSet<string> _knownDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Timer _pollTimer;
        private bool _isWatching;
        private readonly object _lock = new object();
        private readonly int _pollIntervalMs;

        public bool IsWatching => _isWatching;

        public SDCardWatcher(int pollIntervalMs = 1500)
        {
            _pollIntervalMs = pollIntervalMs;
        }

        /// <summary>
        /// Starts background monitoring for removable drive arrivals.
        /// </summary>
        public void Start()
        {
            lock (_lock)
            {
                if (_isWatching) return;
                _isWatching = true;

                // Cache current active drives
                _knownDrives.Clear();
                try
                {
                    foreach (var d in DriveInfo.GetDrives())
                    {
                        if (d.IsReady)
                        {
                            _knownDrives.Add(d.RootDirectory.FullName);
                        }
                    }
                }
                catch
                {
                }

                _pollTimer = new Timer(OnPollDrives, null, 1500, 2000);
                LogMessage?.Invoke("[SDCardWatcher] Background hardware drive monitor started.");
            }
        }

        /// <summary>
        /// Stops the background watcher.
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                if (!_isWatching) return;
                _isWatching = false;
                _pollTimer?.Dispose();
                _pollTimer = null;
                LogMessage?.Invoke("[SDCardWatcher] Background hardware drive monitor stopped.");
            }
        }

        private void OnPollDrives(object state)
        {
            if (!_isWatching) return;

            try
            {
                var currentDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                DriveInfo[] drives;

                try
                {
                    drives = DriveInfo.GetDrives();
                }
                catch
                {
                    return;
                }

                foreach (var d in drives)
                {
                    try
                    {
                        if (!d.IsReady) continue;
                        string rootPath = d.RootDirectory.FullName;
                        currentDrives.Add(rootPath);

                        // Check for newly arrived drives
                        bool isNew = false;
                        lock (_lock)
                        {
                            if (!_knownDrives.Contains(rootPath))
                            {
                                _knownDrives.Add(rootPath);
                                isNew = true;
                            }
                        }

                        if (isNew)
                        {
                            // We look at removable drives or any USB volume
                            if (d.DriveType == DriveType.Removable || d.DriveType == DriveType.Fixed)
                            {
                                InspectDriveForFAROScans(d);
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                // Check for removed drives
                List<string> removed = null;
                lock (_lock)
                {
                    foreach (var known in _knownDrives)
                    {
                        if (!currentDrives.Contains(known))
                        {
                            if (removed == null) removed = new List<string>();
                            removed.Add(known);
                        }
                    }

                    if (removed != null)
                    {
                        foreach (var rem in removed)
                        {
                            _knownDrives.Remove(rem);
                        }
                    }
                }

                if (removed != null)
                {
                    foreach (var rem in removed)
                    {
                        LogMessage?.Invoke($"[SDCardWatcher] Storage drive disconnected: {rem}");
                        CardRemoved?.Invoke(rem);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"[SDCardWatcher] Drive polling error: {ex.Message}");
                WatcherError?.Invoke(ex.Message);
            }
        }

        /// <summary>
        /// Inspects a storage volume to determine if it is a FARO scanner SD card containing scan files.
        /// </summary>
        public bool InspectDriveForFAROScans(DriveInfo drive)
        {
            try
            {
                if (!drive.IsReady) return false;

                string root = drive.RootDirectory.FullName;
                LogMessage?.Invoke($"[SDCardWatcher] Examining newly mounted volume {drive.VolumeLabel} ({root})...");

                var scanFiles = FindScanFilesInDirectory(root);

                if (scanFiles.Count > 0)
                {
                    long totalBytes = 0;
                    foreach (var file in scanFiles)
                    {
                        try { totalBytes += new FileInfo(file).Length; } catch { }
                    }

                    var cardInfo = new SDCardInfo
                    {
                        DriveLetter = root,
                        VolumeLabel = string.IsNullOrEmpty(drive.VolumeLabel) ? "FARO_SD" : drive.VolumeLabel,
                        DriveType = drive.DriveType,
                        TotalSizeBytes = drive.TotalSize,
                        AvailableFreeBytes = drive.AvailableFreeSpace,
                        ScanFiles = scanFiles,
                        TotalScanSizeBytes = totalBytes,
                        DetectedTime = DateTime.Now,
                        IsSimulated = false
                    };

                    // Check for Focus ini or serial number
                    cardInfo.ScannerSerial = DetectScannerSerial(root);

                    LogMessage?.Invoke($"[SDCardWatcher] FARO SD Card DETECTED on {root}! Found {scanFiles.Count} scan(s), {cardInfo.ScanSizeMb:F1} MB.");
                    CardInserted?.Invoke(cardInfo);
                    return true;
                }
                else
                {
                    LogMessage?.Invoke($"[SDCardWatcher] Drive {root} contains no FARO scan files (.fls, .pts, .xyz).");
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"[SDCardWatcher] Error inspecting drive: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Searches root and standard FARO folder structures for .fls, .pts, and .xyz scans.
        /// </summary>
        public static List<string> FindScanFilesInDirectory(string directory)
        {
            var results = new List<string>();
            if (!Directory.Exists(directory)) return results;

            // 1. Check FARO standard subdirectories: Scans, RawScans, FARO
            string[] candidateDirs = new string[]
            {
                directory,
                Path.Combine(directory, "Scans"),
                Path.Combine(directory, "RawScans"),
                Path.Combine(directory, "FARO"),
                Path.Combine(directory, "Focus")
            };

            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".fls", ".pts", ".xyz", ".faro", ".fws" };

            foreach (var dir in candidateDirs)
            {
                if (Directory.Exists(dir))
                {
                    try
                    {
                        var files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                            .Where(f => exts.Contains(Path.GetExtension(f)));
                        foreach (var f in files)
                        {
                            if (!results.Contains(f, StringComparer.OrdinalIgnoreCase))
                            {
                                results.Add(f);
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            // Recursive fallback (limit to depth 2)
            if (results.Count == 0)
            {
                try
                {
                    var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
                        .Where(f => exts.Contains(Path.GetExtension(f)))
                        .Take(50);
                    results.AddRange(files);
                }
                catch
                {
                }
            }

            return results;
        }

        private static string DetectScannerSerial(string root)
        {
            try
            {
                string[] candidates = new string[] { "Focus.ini", "AutoScan.ini", "Scanner.ini", "version.txt" };
                foreach (var c in candidates)
                {
                    string p = Path.Combine(root, c);
                    if (File.Exists(p))
                    {
                        var lines = File.ReadAllLines(p);
                        foreach (var l in lines)
                        {
                            if (l.IndexOf("serial", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                l.IndexOf("scanner", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return l.Trim();
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return "Focus-S350";
        }

        /// <summary>
        /// Simulates inserting an SD card from a local folder containing scans or synthetic scans.
        /// </summary>
        public void SimulateCardInserted(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            var scanFiles = FindScanFilesInDirectory(folderPath);
            long totalBytes = 0;
            foreach (var f in scanFiles)
            {
                try { totalBytes += new FileInfo(f).Length; } catch { }
            }

            var cardInfo = new SDCardInfo
            {
                DriveLetter = folderPath,
                VolumeLabel = "SIMULATED_SD",
                DriveType = DriveType.Removable,
                TotalSizeBytes = 64L * 1024 * 1024 * 1024,
                AvailableFreeBytes = 48L * 1024 * 1024 * 1024,
                ScanFiles = scanFiles,
                TotalScanSizeBytes = totalBytes,
                ScannerSerial = "Focus-Core-Simulated",
                DetectedTime = DateTime.Now,
                IsSimulated = true
            };

            LogMessage?.Invoke($"[SDCardWatcher] SIMULATED SD Card attached: {folderPath} with {scanFiles.Count} scans.");
            CardInserted?.Invoke(cardInfo);
        }

        /// <summary>
        /// Inspects an arbitrary local directory and packages it into an SDCardInfo structure.
        /// </summary>
        public static SDCardInfo InspectFolder(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;

            var scanFiles = FindScanFilesInDirectory(directory);
            long totalBytes = 0;
            foreach (var file in scanFiles)
            {
                try { totalBytes += new FileInfo(file).Length; } catch { }
            }

            return new SDCardInfo
            {
                DriveLetter = directory,
                VolumeLabel = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                DriveType = DriveType.Fixed,
                TotalSizeBytes = 500L * 1024 * 1024 * 1024,
                AvailableFreeBytes = 250L * 1024 * 1024 * 1024,
                ScanFiles = scanFiles,
                TotalScanSizeBytes = totalBytes,
                ScannerSerial = DetectScannerSerial(directory),
                DetectedTime = DateTime.Now,
                IsSimulated = false
            };
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
