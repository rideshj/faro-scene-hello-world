using System;
using System.Collections.Generic;
using System.IO;

namespace FAROSceneSDCardWorkflow.Hardware
{
    /// <summary>
    /// Encapsulates metadata, volume statistics, and scan file inventory
    /// detected on a FARO scanner SD card or removable volume.
    /// </summary>
    public class SDCardInfo
    {
        public string DriveLetter { get; set; }
        public string VolumeLabel { get; set; }
        public DriveType DriveType { get; set; }
        public long TotalSizeBytes { get; set; }
        public long AvailableFreeBytes { get; set; }
        public List<string> ScanFiles { get; set; } = new List<string>();
        public long TotalScanSizeBytes { get; set; }
        public string ScannerSerial { get; set; } = "Focus-Unknown";
        public DateTime DetectedTime { get; set; } = DateTime.Now;
        public bool IsSimulated { get; set; }

        public int ScanCount => ScanFiles?.Count ?? 0;

        public double TotalSizeGb => TotalSizeBytes / (1024.0 * 1024.0 * 1024.0);
        public double ScanSizeMb => TotalScanSizeBytes / (1024.0 * 1024.0);
        public double TotalScanSizeMB => ScanSizeMb;

        public override string ToString()
        {
            return $"{VolumeLabel} ({DriveLetter}) - {ScanCount} scans ({ScanSizeMb:F1} MB)";
        }
    }
}
