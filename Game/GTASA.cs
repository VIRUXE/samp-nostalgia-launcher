using Microsoft.Win32;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static NostalgiaAnticheat.Game.GameValidation;

namespace NostalgiaAnticheat.Game {
    public class GameInstallation {
        public readonly string Location;

        public GameInstallation(string installationPath) {
            installationPath = !string.IsNullOrEmpty(Path.GetExtension(installationPath)) ? Path.GetDirectoryName(installationPath) : installationPath;

            var validationResult = IsExecutableValid(Path.Combine(installationPath, GTASA.EXECUTABLE_FILENAME));
            if (validationResult.Valid)
                Location = installationPath;
            else {
                if (!string.IsNullOrEmpty(validationResult.Hash))
                    throw new Exception($"Executable Hash is invalid: {validationResult.Hash}");
                else
                    throw new DirectoryNotFoundException();
            }
        }

        public bool HasSAMP => (new[] { "samp.exe", "samp.dll" }).All(file => File.Exists(Path.Combine(Location, file)));

        public bool HasExecutable => File.Exists(Path.Combine(Location, GTASA.EXECUTABLE_FILENAME));

        public bool IsExecutableValid => IsExecutableValid(Path.Combine(Location, GTASA.EXECUTABLE_FILENAME)).Valid;

        public string[] Files => Directory.GetFiles(Location, "*", SearchOption.AllDirectories);

        public List<(string FilePath, List<InvalidFileReason> Reasons)> MatchAgainstManifest() {
            Dictionary<string, List<InvalidFileReason>> invalidFilesDict = new();

            static void ValidateNode(JsonElement node, string currentPath, Dictionary<string, List<InvalidFileReason>> invalidFiles) {
                foreach (var property in node.EnumerateObject()) {
                    string newPath = property.Name.Equals("files", StringComparison.OrdinalIgnoreCase) ? currentPath : Path.Combine(currentPath, property.Name);

                    if (property.Name.Equals("files", StringComparison.OrdinalIgnoreCase)) {
                        foreach (var fileNode in property.Value.EnumerateObject()) {
                            string filePath = Path.Combine(newPath, fileNode.Name);

                            try {
                                if (!File.Exists(filePath)) {
                                    if (!invalidFiles.ContainsKey(filePath)) invalidFiles[filePath] = new List<InvalidFileReason>();
                                    invalidFiles[filePath].Add(InvalidFileReason.DoesNotExist);
                                    continue; // Doesn't exist so we just skip to the next file
                                }

                                FileInfo fileInfo = new(filePath);
                                long expectedSize = fileNode.Value.GetProperty("size").GetInt64();
                                long fileLastModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds();
                                long expectedLastModified = fileNode.Value.GetProperty("last_modified").GetInt64();

                                if (fileInfo.Length != expectedSize) {
                                    if (!invalidFiles.ContainsKey(filePath)) invalidFiles[filePath] = new List<InvalidFileReason>();
                                    invalidFiles[filePath].Add(InvalidFileReason.FileSizeMismatch);
                                }

                                if (fileLastModified != expectedLastModified) {
                                    if (!invalidFiles.ContainsKey(filePath)) invalidFiles[filePath] = new List<InvalidFileReason>();
                                    invalidFiles[filePath].Add(InvalidFileReason.LastModifiedTimeMismatch);
                                }
                            } catch (UnauthorizedAccessException) {
                                if (!invalidFiles.ContainsKey(filePath)) invalidFiles[filePath] = new List<InvalidFileReason>();
                                invalidFiles[filePath].Add(InvalidFileReason.NoAccess);
                            } catch (Exception) {
                                if (!invalidFiles.ContainsKey(filePath)) invalidFiles[filePath] = new List<InvalidFileReason>();
                                invalidFiles[filePath].Add(InvalidFileReason.UnknownError);
                            }
                        }
                    } else if (property.Value.ValueKind == JsonValueKind.Object)
                        ValidateNode(property.Value, newPath, invalidFiles);
                }
            }

            ValidateNode(Manifest.RootElement, Location, invalidFilesDict);

            return invalidFilesDict.Select(kvp => (FilePath: kvp.Key.Replace(Location + "\\", string.Empty), Reasons: kvp.Value)).ToList();
        }

        public ValidationResult Validate(out List<(string FilePath, List<InvalidFileReason> Reasons)> invalidFiles) {
            invalidFiles = new();

            if (!HasExecutable) return ValidationResult.MissingExecutable;

            if (!IsExecutableValid) return ValidationResult.InvalidExecutable;

            if (Manifest == null) return ValidationResult.ManifestNotFetched;

            if (Files.Length < CountFilesInManifest()) return ValidationResult.MissingRequiredFiles;

            invalidFiles = MatchAgainstManifest();

            return invalidFiles.Count == 0 ? ValidationResult.Valid : ValidationResult.InvalidFiles;
        }

        public ValidationResult Validate() => Validate(out _);

        public async Task<bool> Repair() {
            bool success = true;

            var invalidFiles = MatchAgainstManifest();

            // Calculate total size
            long totalSizeToDownload = 0;
            long totalDownloaded = 0;

            foreach (var (FilePath, Reason) in invalidFiles) {
                try {
                    totalSizeToDownload += GetFileInfoFromManifest(FilePath).Size;
                } catch (Exception ex) {
                    Console.WriteLine($"Failed to get file info from manifest for {FilePath}: {ex.Message}");
                }
            }

            double totalSizeMB = totalSizeToDownload / (1024.0 * 1024.0); // Convert to MB
            Console.WriteLine($"Starting Repair... Total size to download: {totalSizeMB:F2} MB");

            int fileIndex = 0;
            foreach (var (FilePath, Reason) in invalidFiles) {
                fileIndex++;
                Console.WriteLine($"\nDownloading {fileIndex} of {invalidFiles.Count}: {FilePath}...");

                string downloadUrl = Program.API_ADDR + $"/game/download/{FilePath}";

                using (HttpResponseMessage response = await new HttpClient().GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                using (Stream streamToReadFrom = await response.Content.ReadAsStreamAsync()) {
                    string fileToWriteTo = Path.Combine(Location, FilePath);

                    long totalFileSize = response.Content.Headers.ContentLength ?? 0;
                    double totalFileSizeMB = totalFileSize / (1024.0 * 1024.0);  // Convert to MB
                    DateTimeOffset? lastModified = response.Content.Headers.LastModified;  // Get "Last-Modified" header

                    using (Stream streamToWriteTo = File.Open(fileToWriteTo, FileMode.Create)) {
                        byte[] buffer = new byte[81920];  // 80 KB buffer
                        int bytesRead;
                        long totalBytes = 0;

                        DateTime startTime = DateTime.Now;

                        while ((bytesRead = await streamToReadFrom.ReadAsync(buffer, 0, buffer.Length)) != 0) {
                            await streamToWriteTo.WriteAsync(buffer, 0, bytesRead);

                            totalBytes += bytesRead;
                            TimeSpan timeTaken = DateTime.Now - startTime;

                            double bytesPerSecond = totalBytes / timeTaken.TotalSeconds;

                            // Convert to megabytes and display on the same line
                            double mbytesPerSecond = (bytesPerSecond / (1024 * 1024));
                            double mbytesTotal = (totalBytes / (1024.0 * 1024.0));

                            // Estimate remaining time
                            double remainingTime = (totalFileSize - totalBytes) / bytesPerSecond;
                            string remainingTimeStr = remainingTime > 3600 ? $"{remainingTime / 3600:F2} hrs" : remainingTime > 60 ? $"{remainingTime / 60:F2} mins" : $"{remainingTime:F2} sec";

                            // Update totalDownloaded during or after each file download
                            totalDownloaded += bytesRead; // totalFileSize from your current code
                            double totalDownloadedMB = totalDownloaded / (1024.0 * 1024.0); // Convert to MB

                            string output = $"\r{mbytesTotal:F2} MB of {totalFileSizeMB:F2} MB at {mbytesPerSecond:F2} MB/sec. Estimated time remaining: {remainingTimeStr}. Total downloaded: {totalDownloadedMB:F2}/{totalSizeMB:F2} MB";

                            Console.Write(output.PadRight(100));
                        }

                        // Set "Last-Modified" time after download
                        if (lastModified.HasValue) File.SetLastWriteTime(fileToWriteTo, lastModified.Value.DateTime); 
                    }
                }
            }

            Console.WriteLine("\nRepair Complete!");
            return success;
        }

        public override string ToString() => Location;
    }

    public static class GTASA {
        public const string EXECUTABLE_FILENAME = "gta_sa.exe";

        public static Process Process;
        public static bool Monitoring;
        public static event Action GameStarted;
        public static event Action GameExited;

        public static string CurrentInstallationPath { get; private set; }

        public static bool Playing { get; internal set; }

        public static bool IsRunning => Process != null && Process.MainWindowHandle != IntPtr.Zero;

        public static bool IsResponding => IsRunning && Process?.Responding == true;

        public static List<string> Modules => IsRunning ? Process.Modules.Cast<ProcessModule>().Select(m => m.ModuleName).ToList() : null;

        public static bool IsFocused {
            get {
                if (Process == null || Process.HasExited) return false;

                IntPtr windowHandle = Process.MainWindowHandle;
                return windowHandle != IntPtr.Zero && windowHandle == GetForegroundWindow();
            }
        }

        public static string InstallPath => (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Rockstar Games\GTA San Andreas\Installation", "ExePath", null) as string)?.Replace("\"", "");
        public static bool IsInstalled => InstallPath != null;

        public static bool Launch() {
            if (IsRunning) {
                Focus();

                return false;
            }

            Process = Process.Start(new ProcessStartInfo() {
                FileName  = CurrentInstallationPath + "\\samp.exe",
                Arguments = "sv.scavengenostalgia.fun:7777"
            });

            return true;
        }

        public static async Task<bool> Close() {
            if (!IsRunning) return false;

            Process.Kill();
            await Process.WaitForExitAsync();
            Process = null;

            return true;
        }

        public static bool Focus() {
            if (!IsRunning || IsFocused) return false;

            IntPtr windowHandle = Process.MainWindowHandle;

            if (windowHandle == IntPtr.Zero) return false;

            ShowWindow(windowHandle, 1);
            SetForegroundWindow(windowHandle);

            return true;
        }

        private static IEnumerable<Process> GetAllGTAProcesses() {
            return Process.GetProcesses()
                .Where(p => !p.ProcessName.StartsWith("System") && !p.ProcessName.StartsWith("Idle"))
                .Where(p => {
                    try {
                        string windowTitle = p.MainWindowTitle.ToLower();
                        return windowTitle != string.Empty &&
                               (windowTitle.Contains("gta san andreas") || windowTitle.Contains("gta:sa:mp")) &&
                               p.MainModule.ModuleName.ToLower() == EXECUTABLE_FILENAME;
                    } catch {
                        return false;
                    }
                });
        }

        /// <summary>
        /// Starts monitoring for the GTA SA processes. Kills any non-legitimate instances of the game and raises events when the game starts or exits.
        /// </summary>
        public static async void MonitorGameProcesses() {
            if(Monitoring) return;

            Monitoring = true;

            await Task.Run(() => {
                while (true) {
                    if (Settings.SelectedGameInstallation == null) continue;

                    // Get a list of all currently running GTA processes
                    // Loop through each GTA process found
                    foreach (var process in GetAllGTAProcesses()) {
                        // If the process path doesn't match our installation path, it's not a legitimate process and we should kill it
                        if (process.MainModule.FileName != Settings.SelectedGameInstallation.Location) { 
                            process.Kill();
                            continue;
                        }

                        // If our currently tracked process is null, has exited, or is different from the current process in loop
                        if (Process == null || Process.HasExited || Process.Id != process.Id) {
                            // If we currently don't have a process tracked or it has exited, raise the GameStarted event
                            if (Process == null || Process.HasExited) GameStarted?.Invoke();

                            // Assign the current process in loop as our main GTA process to track
                            Process = process;
                        }
                    }

                    // If we're tracking a process but it has exited, raise the GameExited event and reset the tracked process
                    if (IsRunning && Process.HasExited) {
                        Process = null;
                        GameExited?.Invoke();
                    }

                    // Pause the loop for 1 second before checking again
                    Thread.Sleep(1000);
                }
            });
        }

        public static async Task<bool> DownloadGameArchive() {
            var archivePath = Path.Combine(Directory.GetCurrentDirectory(), "gtasa.7z");

            try {
                using HttpClient client = new();

                // Get the total size of the file to be downloaded
                var responseHead = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "http://scavengenostalgia.fun/baixar/gtasa.7z"));
                var totalBytes = responseHead.Content.Headers.ContentLength.GetValueOrDefault();

                var fileInfo = new FileInfo(archivePath);
                long startRange = 0;
                if (fileInfo.Exists) {
                    startRange = fileInfo.Length;

                    if (startRange >= totalBytes) {
                        Console.WriteLine("Game archive already downloaded");
                        return true;
                    }
                }

                CancellationTokenSource cts = new();

                Task keyListenerTask = Task.Run(() => {
                    while (true) {
                        if (Console.KeyAvailable) {
                            var key = Console.ReadKey(true);
                            if (key.Key == ConsoleKey.Escape) {
                                cts.Cancel();
                                break;
                            }
                        }
                    }
                });

                // Specify the starting range in the header
                client.DefaultRequestHeaders.Range = new System.Net.Http.Headers.RangeHeaderValue(startRange, null);

                using var response = await client.GetAsync("http://scavengenostalgia.fun/baixar/gtasa.7z", HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if (!response.IsSuccessStatusCode) {
                    Console.WriteLine("Error: Unable to download the Game archive");
                    return false;
                }

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(archivePath, FileMode.Append, FileAccess.Write, FileShare.None, 8192, true);
                var totalReadBytes = startRange;
                var buffer = new byte[8192];
                var isMoreToRead = true;

                do {
                    var read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    if (read == 0) {
                        isMoreToRead = false;
                    } else {
                        await fileStream.WriteAsync(buffer, 0, read, cts.Token);

                        totalReadBytes += read;
                        var percentage = totalReadBytes * 100 / totalBytes;

                        Console.SetCursorPosition(0, Console.CursorTop);
                        Console.Write($"{percentage}% downloaded");
                    }
                }
                while (isMoreToRead && !cts.Token.IsCancellationRequested);

                if (cts.Token.IsCancellationRequested) {
                    Console.WriteLine("Download cancelled.");
                    return false;
                }

                return true;
            } catch (OperationCanceledException) {
                Console.WriteLine("Download cancelled by user.");
                return false;
            } catch (Exception) {
                Console.WriteLine("An error occurred while downloading the game archive.");
                return false;
            }
        }

        public static bool DecompressGameArchive(string installPath) {
            var archivePath = Path.Combine(Directory.GetCurrentDirectory(), "gtasa.7z");

            if (!File.Exists(archivePath)) {
                Console.WriteLine("The archive file does not exist.");
                return false;
            }

            try {
                if (!Directory.Exists(installPath)) Directory.CreateDirectory(installPath);

                Console.WriteLine("Decompressing GTASA:");

                if (OS.Is7ZipInstalled()) {
                    var processStartInfo = new ProcessStartInfo {
                        FileName = "7z",
                        Arguments = $"x {archivePath} -o{installPath} -y",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = false,
                    };

                    var process = Process.Start(processStartInfo);

                    process.OutputDataReceived += (sender, e) => Console.WriteLine(e.Data);

                    process.BeginOutputReadLine(); // Start reading from the standard output
                    process.WaitForExit();

                    Console.WriteLine(process.ExitCode == 0 ? "\nDecompression completed." : "\nDecompression failed.");

                    return process.ExitCode == 0;
                } else {
                    using var archive = SharpCompress.Archives.SevenZip.SevenZipArchive.Open(archivePath);
                    int totalEntries = archive.Entries.Count;
                    int entryIndex = 0;
                    int dotCount = 0;
                    string currentFileName = string.Empty;

                    Timer dotTimer = new(_ => {
                        dotCount = (dotCount + 1) % 4;
                        Console.SetCursorPosition(0, Console.CursorTop);
                        Console.Write(new string(' ', Console.WindowWidth - 1));
                        Console.SetCursorPosition(0, Console.CursorTop);

                        double progress = ((double)entryIndex / totalEntries) * 100;
                        Console.Write($"Decompressing: {currentFileName} ({progress:0.00}%) {new string('.', dotCount)}");
                    }, null, 0, 500);

                    foreach (var entry in archive.Entries.Where(e => !e.IsDirectory)) {
                        currentFileName = entry.Key;
                        entry.WriteToDirectory(installPath, new ExtractionOptions() { ExtractFullPath = true, Overwrite = true });

                        entryIndex++;
                    }

                    dotTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    dotTimer.Dispose();

                    Console.WriteLine("\nDecompression completed.");
                    return true;
                }
            } catch (OperationCanceledException) {
                Console.WriteLine("\nDecompression was cancelled.");
                return false;
            } catch (Exception ex) {
                Console.WriteLine($"\nDecompression failed: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> Install() {
            Console.Write("Enter the path where you want to install the game: ");
            string installPath = Console.ReadLine();

            if (await DownloadGameArchive()) {
                if (DecompressGameArchive(installPath)) {
                    Console.WriteLine("Game downloaded and installed successfully.");
                } else {
                    Console.WriteLine("Failed to decompress the game archive.");
                    return false;
                }
            } else {
                Console.WriteLine("Failed to download the game archive.");
                return false;
            }

            return true;
        }

        [DllImport("User32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("User32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("User32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}
