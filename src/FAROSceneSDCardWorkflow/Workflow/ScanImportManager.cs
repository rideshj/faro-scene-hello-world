using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FARO.LS.SDK.AppLogic;
using FARO.LS.SDK.DataLogic;

namespace FAROSceneSDCardWorkflow.Workflow
{
    /// <summary>
    /// Manages high-throughput copying and project directory ingestion of scan files from the SD card.
    /// </summary>
    public static class ScanImportManager
    {
        public static string CreateProjectDirectory(string baseDir, string projectName)
        {
            if (string.IsNullOrWhiteSpace(baseDir))
            {
                baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FARO Projects");
            }

            string projectPath = Path.Combine(baseDir, projectName);
            Directory.CreateDirectory(projectPath);
            Directory.CreateDirectory(Path.Combine(projectPath, "Scans"));
            Directory.CreateDirectory(Path.Combine(projectPath, "Exports"));
            Directory.CreateDirectory(Path.Combine(projectPath, "Reports"));

            return projectPath;
        }

        public static List<string> ImportScans(
            IList<string> sourceFiles,
            string projectPath,
            Action<string, int> progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            var destinationFiles = new List<string>();
            string scanDestFolder = Path.Combine(projectPath, "Scans");

            if (sourceFiles == null || sourceFiles.Count == 0) return destinationFiles;

            long totalBytesToCopy = 0;
            foreach (var f in sourceFiles)
            {
                if (File.Exists(f)) totalBytesToCopy += new FileInfo(f).Length;
            }

            long totalBytesCopied = 0;

            for (int i = 0; i < sourceFiles.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string src = sourceFiles[i];
                string filename = Path.GetFileName(src);
                string dest = Path.Combine(scanDestFolder, filename);

                progressCallback?.Invoke($"Copying {filename} ({i + 1}/{sourceFiles.Count})...",
                    totalBytesToCopy > 0 ? (int)((totalBytesCopied * 100) / totalBytesToCopy) : (i * 100 / sourceFiles.Count));

                CopyFileWithProgress(src, dest, (bytesCopiedInFile) =>
                {
                    long currentTotal = totalBytesCopied + bytesCopiedInFile;
                    if (totalBytesToCopy > 0)
                    {
                        int pct = (int)((currentTotal * 100) / totalBytesToCopy);
                        progressCallback?.Invoke($"Copying {filename} ({pct}% total)...", Math.Min(99, pct));
                    }
                }, cancellationToken);

                if (File.Exists(src))
                {
                    totalBytesCopied += new FileInfo(src).Length;
                }

                destinationFiles.Add(dest);
            }

            progressCallback?.Invoke($"Imported {destinationFiles.Count} scan(s) successfully.", 100);
            return destinationFiles;
        }

        private static void CopyFileWithProgress(
            string source,
            string destination,
            Action<long> progress,
            CancellationToken token)
        {
            const int bufferSize = 1024 * 1024; // 1MB buffer
            byte[] buffer = new byte[bufferSize];

            using (var srcStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var destStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                long totalCopied = 0;
                int bytesRead;

                while ((bytesRead = srcStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    destStream.Write(buffer, 0, bytesRead);
                    totalCopied += bytesRead;
                    progress?.Invoke(totalCopied);
                }
            }
        }
    }
}
